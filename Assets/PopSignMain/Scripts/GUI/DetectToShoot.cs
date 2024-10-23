using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
public class DetectToShoot : MonoBehaviour
{
    public bool isShot = false;
    public bool isPressed = false;
    public bool inFlight = false;
    public bool entered = false;
    public string ans;
    [SerializeField] private GameObject hands;

    void Start(){
        GetComponent<Image>().color = new Color32(97, 97, 97,255);
    }

    // Update is called once per frame
    void Update()
    {
        if(isShot && !Input.GetMouseButton(0)){
            hands.GetComponent<HandsMediaPipe>().lockOutTimeLeft -= Time.deltaTime; 
        }
        if (!hands.GetComponent<HandsMediaPipe>().handInFrame && entered) {
            ans = TfLiteManager.Instance.StopRecording();
            if (ans != "") {
                isShot = true;
            } else {
                TfLiteManager.Instance.StartRecording();
            }
            entered = false;
        }
        if (hands.GetComponent<HandsMediaPipe>().handInFrame && !entered) {
            entered = true;
        }
        if (isShot && !inFlight && !entered) {
                //label.SetText("Shoot");
                GetComponent<Image>().color = new Color32(170,255,182,255);
                //170, 255, 182
            }
            else
            {
                //label.SetText("Hold\nTo\nSign");
                GetComponent<Image>().color = new Color32(97, 97, 97,255);
                //97, 97, 97
            }
    }
}
