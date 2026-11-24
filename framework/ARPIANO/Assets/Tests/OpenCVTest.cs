using UnityEngine;
using OpenCvSharp;
using ARPIANO.Scripts;

namespace ARPIANO.Tests
{
    public class OpenCVTest : MonoBehaviour
    {
        void Start()
        {
            if (!RuntimeFeatures.OpenCVTrackingEnabled)
            {
                enabled = false;
                return;
            }

            Debug.Log("OpenCV Test: Starting OpenCV version check...");
            Debug.Log(Cv2.GetVersionString());
        }
    }
}