using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.CoordinateSystem;
using System.IO;

public class HandsMediaPipe : MonoBehaviour
{
    [SerializeField] private TextAsset _configAssetCPU;
    [SerializeField] private TextAsset _configAssetGPU;
    [SerializeField] private RawImage _screen;
    [SerializeField] private int _width;
    [SerializeField] private int _height;
    [SerializeField] private int _fps;
    [SerializeField] private MultiHandLandmarkListAnnotationController _multiHandLandmarksAnnotationController;


    [SerializeField] private bool enableCoordinateDebugging;
    [SerializeField] private Text coordinateDebugger;

#if UNITY_EDITOR
    private bool useGPU = false;
#else
    private bool useGPU = true;
#endif

    private CalculatorGraph _graph;

    private WebCamTexture _webCamTexture;
    private Texture2D _inputTexture;
    private Color32[] _inputPixelData;

    void Awake()
    {
        if(UnityEngine.Screen.height >= 2300)
            _screen.gameObject.transform.localScale = new Vector3(0.34f, 0.34f, 0.34f);

    }

    private IEnumerator Start()
    {
        if (WebCamTexture.devices.Length == 0)
        {
            throw new System.Exception("Web Camera devices are not found");
        }

        int defaultSource = 0;

        for (int i = 0; i < WebCamTexture.devices.Length; i++)
        {
            if (WebCamTexture.devices[i].isFrontFacing == true)
            {
                defaultSource = i;
                break;
            }
        }

        var webcamDevice = WebCamTexture.devices[defaultSource];

#if UNITY_EDITOR
        Debug.LogWarning("HandsMediaPipe: DOWNGRADING CAMERA IN EDITOR!");
        _width = 1280;
        _height = 720;

#else
        float maxres = 0f;
        foreach (var res in webcamDevice.availableResolutions)
        {
            if(res.height > maxres)
            {
                Debug.Log("Res Widths " + res.width + " Res h " + res.height);
                maxres = res.height;
            }
        }
        if (maxres < _height)
        {
            Debug.LogWarning("HandsMediaPipe: CAMERA TOO LOW RES! DOWNGRADING TO 720P!");
            _width = 1280;
            _height = 720;
        }
#endif

        _webCamTexture = new WebCamTexture(webcamDevice.name, _width, _height, _fps);
        _webCamTexture.Play();

        yield return new WaitUntil(() => _webCamTexture.width > 16);
        if (useGPU)
        {
            yield return GpuManager.Initialize();

            if (!GpuManager.IsInitialized)
            {
                throw new System.Exception("Failed to initialize GPU resources");
            }
        }

        _screen.rectTransform.sizeDelta = new Vector2(_width, _height);

        _inputTexture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
        _inputPixelData = new Color32[_width * _height];

        _screen.texture = _webCamTexture;

        yield return MediapipeResourceManager.Instance.resourceManager.PrepareAssetAsync("hand_landmark_full.bytes");
        yield return MediapipeResourceManager.Instance.resourceManager.PrepareAssetAsync("palm_detection_full.bytes");
        yield return MediapipeResourceManager.Instance.resourceManager.PrepareAssetAsync("hand_recrop.bytes");
        yield return MediapipeResourceManager.Instance.resourceManager.PrepareAssetAsync("handedness.txt");

        var stopwatch = new System.Diagnostics.Stopwatch();

        if (useGPU)
        {
            _graph = new CalculatorGraph(_configAssetGPU.text);
            _graph.SetGpuResources(GpuManager.GpuResources).AssertOk();
        }
        else
        {
            _graph = new CalculatorGraph(_configAssetCPU.text);
        }

        var handLandmarksStream = new OutputStream<NormalizedLandmarkListVectorPacket, List<NormalizedLandmarkList>>(_graph, "hand_landmarks");
        var handednessStream = new OutputStream<ClassificationListVectorPacket, List<ClassificationList>>(_graph, "handedness");
        handLandmarksStream.StartPolling().AssertOk();
        handednessStream.StartPolling().AssertOk();

        var sidePacket = new SidePacket();
        sidePacket.Emplace("input_horizontally_flipped", new BoolPacket(false));
        sidePacket.Emplace("input_rotation", new IntPacket(0));
#if UNITY_IOS
        sidePacket.Emplace("input_vertically_flipped", new BoolPacket(false));
#else
        sidePacket.Emplace("input_vertically_flipped", new BoolPacket(true));
#endif
        sidePacket.Emplace("num_hands", new IntPacket(1));

        _graph.StartRun(sidePacket).AssertOk();
        stopwatch.Start();

        var screenRect = _screen.GetComponent<RectTransform>().rect;

        while (true)
        {
            _inputTexture.SetPixels32(_webCamTexture.GetPixels32(_inputPixelData));
            var imageFrame = new ImageFrame(ImageFormat.Types.Format.Srgba, _width, _height, _width * 4, _inputTexture.GetRawTextureData<byte>());
            var currentTimestamp = stopwatch.ElapsedTicks / (System.TimeSpan.TicksPerMillisecond / 1000);
            _graph.AddPacketToInputStream("input_video", new ImageFramePacket(imageFrame, new Timestamp(currentTimestamp))).AssertOk();

            yield return new WaitForEndOfFrame();

            handednessStream.TryGetNext(out var handedness);

            if (handLandmarksStream.TryGetNext(out var handLandmarks))
            {
                if (TfLiteManager.Instance.IsRecording() && !GamePlay.Instance.InGamePauseTriggered)
                {
                    if (handLandmarks != null && handLandmarks.Count > 0)
                    {
                        foreach (var landmarks in handLandmarks)
                        {

                            float[] currentFrame;

                            currentFrame = new float[42];

                            for (int i = 0; i < landmarks.Landmark.Count; i++)
                            {
                                if (handedness[0].Classification[0].Label.Contains("Left"))
                                {
                                    currentFrame[i * 2] = 1.0f - landmarks.Landmark[i].Y;
                                }
                                else
                                {
                                    currentFrame[i * 2] = landmarks.Landmark[i].Y;
                                }

                                currentFrame[i * 2 + 1] = 1.0f - landmarks.Landmark[i].X;

                                //We flip x and y for iOS
#if UNITY_IOS
                                currentFrame[i * 2] = 1.0f - currentFrame[i * 2];
                                currentFrame[i * 2 + 1] = 1.0f - currentFrame[i * 2 + 1];
#endif
                            }


                            if (enableCoordinateDebugging && coordinateDebugger != null)
                            {
                                coordinateDebugger.text = currentFrame[0].ToString("0.00") + ", " + currentFrame[1].ToString("0.00");
                            }

                            TfLiteManager.Instance.AddDataToList(currentFrame);
                            TfLiteManager.Instance.SaveToFile(landmarks.ToString());
                        }
                    }
                }
                _multiHandLandmarksAnnotationController.DrawNow(handLandmarks);
            }
            else
            {
                _multiHandLandmarksAnnotationController.DrawNow(null);
            }
        }
    }

    private void OnDestroy()
    {
        if (_webCamTexture != null)
        {
            _webCamTexture.Stop();
        }
        if (_graph != null)
        {
            try
            {
                _graph.CloseInputStream("input_video").AssertOk();
                _graph.WaitUntilDone().AssertOk();
            }
            finally
            {
                _graph.Dispose();
            }
        }
        if (useGPU)
        {
            GpuManager.Shutdown();
        }
    }
}
