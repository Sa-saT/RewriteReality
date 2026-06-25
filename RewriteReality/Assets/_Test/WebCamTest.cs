using UnityEngine;
using UnityEngine.UI;

public class WebCamTest : MonoBehaviour {
    public RawImage target;
    void Start() {
        var cam = new WebCamTexture();
        target.texture = cam;
        cam.Play();
    }
}