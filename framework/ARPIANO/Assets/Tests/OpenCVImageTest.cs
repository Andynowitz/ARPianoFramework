using UnityEngine;
using OpenCvSharp;

namespace ARPIANO.Tests
{
    public class OpenCVImageTest : MonoBehaviour
    {
        private void Start()
        {
            Mat image = new Mat(480, 640, MatType.CV_8UC3);

            image.SetTo(new Scalar(255, 0, 0));

            Debug.Log($"OpenCVImageTest - Image size: {image.Width}x{image.Height}");
        }
    }
}