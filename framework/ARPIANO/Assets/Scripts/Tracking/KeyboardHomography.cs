using UnityEngine;
using OpenCvSharp;

namespace ARPIANO.Scripts.Tracking
{
    public class KeyboardHomography
    {
        private readonly Mat homography;

        public KeyboardHomography(Vector2 topLeft,
                                  Vector2 topRight,
                                  Vector2 bottomRight,
                                  Vector2 bottomLeft)
        {
            var source = new[]
            {
                new Point2f(topLeft.x, topLeft.y),
                new Point2f(topRight.x, topRight.y),
                new Point2f(bottomRight.x, bottomRight.y),
                new Point2f(bottomLeft.x, bottomLeft.y)
            };

            var destination = new[]
            {
                new Point2f(0f, 0f),
                new Point2f(1f, 0f),
                new Point2f(1f, 1f),
                new Point2f(0f, 1f)
            };

            homography = Cv2.GetPerspectiveTransform(source, destination);
        }

        public Vector2 ImageToNormalized(Vector2 imagePoint)
        {
            using var source = new Mat(1, 1, MatType.CV_32FC2);

            source.Set(
                0,
                0,
                new Point2f(imagePoint.x, imagePoint.y));

            using var destination = new Mat();

            Cv2.PerspectiveTransform(source, destination, homography);

            Point2f result = destination.At<Point2f>(0, 0);

            return new Vector2(result.X, result.Y);
        }

        public Vector2 NormalizedToImage(Vector2 normalizedPoint)
        {
            using var inverse = new Mat();
            Cv2.Invert(homography, inverse);

            using var source = new Mat(1, 1, MatType.CV_32FC2);

            source.Set(
                0,
                0,
                new Point2f(normalizedPoint.x, normalizedPoint.y));

            using var destination = new Mat();

            Cv2.PerspectiveTransform(source, destination, inverse);

            Point2f result = destination.At<Point2f>(0, 0);

            return new Vector2(result.X, result.Y);
        }

        public void Dispose()
        {
            homography.Dispose();
        }
    }
}