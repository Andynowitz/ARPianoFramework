using UnityEngine;
using OpenCvSharp;

namespace ARPIANO.Tests
{
    public class OpenCVTest : MonoBehaviour
    {
        void Start()
        {
            Debug.Log("OpenCV Test: Starting OpenCV version check...");
            Debug.Log(Cv2.GetVersionString());
        }
    }
}