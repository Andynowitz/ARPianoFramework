// OpenCVKeyboardTracker.cs
// Purpose: Tracks the position of a physical piano keyboard using OpenCV and provides the position data for virtual piano rendering.

using System;
using System.Collections.Generic;
using UnityEngine;
using OpenCvSharp;
using ARPIANO.Scripts.Piano;

namespace ARPIANO.Scripts.Tracking
{
    public class OpenCVKeyboardTracker : MonoBehaviour
    {
        [SerializeField] private Texture2D pianoImage;
        [SerializeField] private Renderer pianoRenderer;

        private void Start()
        {
            Texture2D image = TryGetTexture();

            if (image == null)
            {
                return;
            }

            Debug.Log($"Source texture: {image.name} format={image.format} readable={image.isReadable}");

            Texture2D readable = GetReadableTexture(image);
            if (readable == null)
            {
                Debug.LogError("Unable to obtain a readable texture for OpenCV processing.");
                return;
            }

            Debug.Log($"Readable texture: {readable.name} format={readable.format} readable={readable.isReadable}");


            Mat mat = TextureToMat(readable);
            if (mat == null)
                return;

            Debug.Log($"Loaded image: {mat.Width}x{mat.Height}");

            var outline = DetectKeyboardOutline(mat);
            if (outline.Width > 0 && outline.Height > 0)
            {
                Debug.Log($"Found keyboard outline: x={outline.X} y={outline.Y} w={outline.Width} h={outline.Height}");

                if (outline.Width < mat.Width * 0.2f || outline.Height < mat.Height * 0.1f || outline.Width / (float)outline.Height < 2.5f)
                {
                    Debug.LogWarning("Detected outline is too small or has an invalid aspect ratio; this is likely a false positive.");
                }

                if (TryDetectWhiteKeyLines(mat, outline, out float leftEdge, out float rightEdge, out float keySpacing, out int lineCount))
                {
                    Debug.Log($"Detected {lineCount} vertical white-key lines; left={leftEdge:F1} right={rightEdge:F1} spacing={keySpacing:F1}");
                }
                else
                {
                    Debug.LogWarning("White key line detection failed inside the detected keyboard outline.");
                }

                if (TryDetectBlackKeys(mat, outline, out int blackKeyCount, out float averageAspect, out List<OpenCvSharp.Rect> blackKeyRects))
                {
                    Debug.Log($"Detected {blackKeyCount} black-key candidates; avg aspect ratio={averageAspect:F2}");

                    // attempt refined corner detection first
                    if (TryDetectOutlineCorners(mat, out OpenCvSharp.Point2f[] corners))
                    {
                        Debug.Log($"Detected outline corners: {corners.Length}");
                    }

                    if (TryVerifyPianoPattern(blackKeyRects, out string patternDescription))
                    {
                        Debug.Log($"Piano pattern verified: {patternDescription}");

                        var vp = FindObjectOfType<VirtualPiano>();
                        if (vp != null)
                        {
                            // prefer corner-based alignment when available
                            bool applied = false;
                            if (TryDetectOutlineCorners(mat, out OpenCvSharp.Point2f[] quadCorners) && quadCorners.Length == 4)
                            {
                                applied = EstimateAndApplyPoseUsingCorners(mat, quadCorners, vp);
                            }
                            if (!applied)
                            {
                                applied = EstimateAndApplyPose(mat, outline, blackKeyRects, vp);
                            }

                            if (applied)
                            {
                                vp.transform.SetParent(pianoRenderer.transform, true);
                                Debug.Log("Applied estimated pose to VirtualPiano and parented it to the reference plane.");
                            }
                            else
                            {
                                Debug.LogWarning("Pose estimation failed.");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("VirtualPiano instance not found in scene.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Piano pattern verification failed: {patternDescription}");
                    }
                }
                else
                {
                    Debug.LogWarning("Black key detection failed inside the detected keyboard outline.");
                }
            }
            else
            {
                Debug.LogWarning("Keyboard outline detection did not find a valid rectangle.");
            }
        }

        private Texture2D TryGetTexture()
        {
            if (pianoImage != null)
            {
                return pianoImage;
            }

            if (pianoRenderer != null && pianoRenderer.material != null)
            {
                if (pianoRenderer.material.mainTexture is Texture2D textureFromRenderer)
                {
                    return textureFromRenderer;
                }
            }

            Debug.LogError("No piano image found. Assign a Texture2D or drag the Piano88 plane into the renderer field.");
            return null;
        }

        private Texture2D GetReadableTexture(Texture2D source)
        {
            if (source == null)
                return null;

            if (source.isReadable && !IsCompressedFormat(source.format))
                return source;

            Debug.LogWarning("Creating a readable RGBA32 copy of the texture because the source is either not readable or compressed.");
            return CopyToReadableRGBA32(source);
        }

        private bool IsCompressedFormat(TextureFormat format)
        {
            return format switch
            {
                TextureFormat.DXT1 or TextureFormat.DXT5 or TextureFormat.BC4 or TextureFormat.BC5 or TextureFormat.BC6H or TextureFormat.BC7 or TextureFormat.ETC_RGB4 or TextureFormat.ETC2_RGBA8 or TextureFormat.ETC2_RGBA1 or TextureFormat.ASTC_4x4 or TextureFormat.ASTC_5x5 or TextureFormat.ASTC_6x6 or TextureFormat.ASTC_8x8 or TextureFormat.ASTC_10x10 or TextureFormat.ASTC_12x12 => true,
                _ => false,
            };
        }

        private Texture2D CopyToReadableRGBA32(Texture2D source)
        {
            RenderTexture tempRT = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, tempRT);
            RenderTexture.active = tempRT;

            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new UnityEngine.Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tempRT);
            return copy;
        }

        private Mat TextureToMat(Texture2D texture)
        {
            byte[] data = texture.EncodeToPNG();
            if (data == null || data.Length == 0)
            {
                Debug.LogError("Failed to encode texture to PNG. The texture may still be in an unsupported format.");
                return null;
            }

            return Cv2.ImDecode(data, ImreadModes.Color);
        }

        private bool TryDetectWhiteKeyLines(Mat src, OpenCvSharp.Rect outline, out float leftEdge, out float rightEdge, out float keySpacing, out int lineCount)
        {
            leftEdge = 0f;
            rightEdge = 0f;
            keySpacing = 0f;
            lineCount = 0;

            if (src == null || src.Empty() || outline.Width <= 0 || outline.Height <= 0)
                return false;

            using var roi = new Mat(src, outline);
            using var gray = new Mat();
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 20, 80);

            LineSegmentPoint[] lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180.0, 40, outline.Height * 0.2, 6);
            Debug.Log($"HoughLinesP found {(lines?.Length ?? 0)} raw lines in the keyboard ROI.");
            if (lines == null || lines.Length == 0)
                return false;

            var verticalX = new List<float>();
            foreach (var line in lines)
            {
                float dx = Math.Abs(line.P1.X - line.P2.X);
                float dy = Math.Abs(line.P1.Y - line.P2.Y);
                if (dy < outline.Height * 0.12f)
                    continue;

                float slope = dx / (dy + 1e-6f);
                if (slope < 0.5f)
                {
                    verticalX.Add((line.P1.X + line.P2.X) * 0.5f);
                }
            }

            Debug.Log($"White key line candidates after vertical filter: {verticalX.Count}");
            if (verticalX.Count == 0)
                return false;

            verticalX.Sort();
            var uniqueX = new List<float>();
            foreach (var x in verticalX)
            {
                if (uniqueX.Count == 0 || Math.Abs(x - uniqueX[^1]) > 10)
                {
                    uniqueX.Add(x);
                }
            }

            Debug.Log($"White key unique x positions: {uniqueX.Count}");
            if (uniqueX.Count < 2)
                return false;

            leftEdge = uniqueX[0] + outline.X;
            rightEdge = uniqueX[^1] + outline.X;
            lineCount = uniqueX.Count;

            float totalSpacing = 0f;
            for (int i = 1; i < uniqueX.Count; i++)
            {
                totalSpacing += uniqueX[i] - uniqueX[i - 1];
            }

            keySpacing = totalSpacing / (uniqueX.Count - 1);
            return true;
        }

        private bool TryDetectBlackKeys(Mat src, OpenCvSharp.Rect outline, out int blackKeyCount, out float averageAspect, out List<OpenCvSharp.Rect> blackKeyRects)
        {
            blackKeyCount = 0;
            averageAspect = 0f;
            blackKeyRects = new List<OpenCvSharp.Rect>();

            if (src == null || src.Empty() || outline.Width <= 0 || outline.Height <= 0)
                return false;

            using var roi = new Mat(src, outline);
            using var gray = new Mat();
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            using var thresh = new Mat();
            Cv2.Threshold(blurred, thresh, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.MorphologyEx(thresh, thresh, MorphTypes.Open, kernel);
            Cv2.MorphologyEx(thresh, thresh, MorphTypes.Close, kernel);

            Cv2.FindContours(thresh, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            Debug.Log($"Black key contours found: {contours.Length}");

            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                if (rect.Width < 5 || rect.Height < 10)
                    continue;

                float aspect = rect.Width / (float)rect.Height;
                if (aspect > 0.7f || aspect < 0.08f)
                    continue;

                if (rect.Height < outline.Height * 0.2 || rect.Height > outline.Height * 0.9)
                    continue;

                if (rect.Width > outline.Width * 0.12)
                    continue;

                blackKeyRects.Add(rect);
            }

            Debug.Log($"Black key rectangles after filtering: {blackKeyRects.Count}");
            if (blackKeyRects.Count == 0)
                return false;

            blackKeyCount = blackKeyRects.Count;
            float sumAspect = 0f;
            foreach (var rect in blackKeyRects)
            {
                sumAspect += rect.Width / (float)rect.Height;
            }

            averageAspect = sumAspect / blackKeyCount;
            return true;
        }

        private bool TryVerifyPianoPattern(List<OpenCvSharp.Rect> blackKeyRects, out string patternDescription)
        {
            patternDescription = string.Empty;
            if (blackKeyRects == null || blackKeyRects.Count < 7)
            {
                patternDescription = "Not enough black-key candidates for pattern verification.";
                return false;
            }

            blackKeyRects.Sort((a, b) => a.X.CompareTo(b.X));
            var centers = new List<float>();
            foreach (var rect in blackKeyRects)
            {
                centers.Add(rect.X + rect.Width * 0.5f);
            }

            var gaps = new List<float>();
            for (int i = 1; i < centers.Count; i++)
            {
                gaps.Add(centers[i] - centers[i - 1]);
            }

            if (gaps.Count == 0)
            {
                patternDescription = "No horizontal gaps between black keys.";
                return false;
            }

            float medianGap = GetMedian(gaps);
            if (medianGap <= 0)
            {
                patternDescription = "Invalid key spacing.";
                return false;
            }

            var groups = new List<int>();
            int currentGroup = 1;
            for (int i = 1; i < centers.Count; i++)
            {
                if (gaps[i - 1] > medianGap * 1.8f)
                {
                    groups.Add(currentGroup);
                    currentGroup = 1;
                }
                else
                {
                    currentGroup++;
                }
            }
            groups.Add(currentGroup);

            if (groups.Count < 3)
            {
                patternDescription = "Too few black-key groups detected.";
                return false;
            }

            var expected = new[] { 2, 3, 2, 3 };
            bool match = false;
            for (int i = 0; i + expected.Length <= groups.Count; i++)
            {
                bool ok = true;
                for (int j = 0; j < expected.Length; j++)
                {
                    if (Math.Abs(groups[i + j] - expected[j]) > 0)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    match = true;
                    break;
                }
            }

            patternDescription = "groups=" + string.Join(",", groups);
            return match;
        }

        private float GetMedian(List<float> values)
        {
            var temp = new List<float>(values);
            temp.Sort();
            int mid = temp.Count / 2;
            if (temp.Count % 2 == 1)
                return temp[mid];
            return (temp[mid - 1] + temp[mid]) * 0.5f;
        }

        private OpenCvSharp.Rect DetectKeyboardOutline(Mat src)
        {
            if (src == null || src.Empty())
                return new OpenCvSharp.Rect();

            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 50, 150);

            Cv2.FindContours(edges, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            double imageArea = src.Width * src.Height;
            double bestArea = 0;
            var bestRect = new OpenCvSharp.Rect();
            double largestAnyArea = 0;
            var largestAnyRect = new OpenCvSharp.Rect();

            foreach (var contour in contours)
            {
                double contourArea = Math.Abs(Cv2.ContourArea(contour));
                var rectAny = Cv2.BoundingRect(contour);
                if (rectAny.Width * rectAny.Height > largestAnyArea)
                {
                    largestAnyArea = rectAny.Width * rectAny.Height;
                    largestAnyRect = rectAny;
                }

                if (contourArea < imageArea * 0.02)
                    continue;

                var approx = Cv2.ApproxPolyDP(contour, 0.02 * Cv2.ArcLength(contour, true), true);
                if (approx.Length != 4 || !Cv2.IsContourConvex(approx))
                    continue;

                var rect = Cv2.BoundingRect(approx);
                if (rect.Width < src.Width * 0.2 || rect.Height < src.Height * 0.12)
                    continue;

                float aspect = rect.Width / (float)rect.Height;
                if (aspect < 2.5f)
                    continue;

                double area = rect.Width * rect.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestRect = rect;
                }
            }

            if (bestRect.Width == 0 && largestAnyRect.Width > src.Width * 0.3 && largestAnyRect.Height > src.Height * 0.15 && largestAnyRect.Width / (float)largestAnyRect.Height > 2f)
            {
                bestRect = largestAnyRect;
            }

            return bestRect;
        }

        private bool TryDetectOutlineCorners(Mat src, out OpenCvSharp.Point2f[] corners)
        {
            corners = Array.Empty<OpenCvSharp.Point2f>();
            if (src == null || src.Empty())
                return false;

            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 50, 150);
            Cv2.FindContours(edges, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            double imageArea = src.Width * src.Height;
            foreach (var contour in contours)
            {
                if (Math.Abs(Cv2.ContourArea(contour)) < imageArea * 0.01)
                    continue;

                var approx = Cv2.ApproxPolyDP(contour, 0.02 * Cv2.ArcLength(contour, true), true);
                if (approx.Length != 4 || !Cv2.IsContourConvex(approx))
                    continue;

                var detectedCorners = new OpenCvSharp.Point2f[4];
                for (int i = 0; i < 4; i++)
                    detectedCorners[i] = new OpenCvSharp.Point2f(approx[i].X, approx[i].Y);

                // OpenCV uses top-left origin with Y increasing downward.
                // Canonicalize the quadrilateral to TL, TR, BR, BL before pose matching.
                corners = OrderCornersTopLeftClockwise(detectedCorners);
                return true;
            }

            return false;
        }

        private OpenCvSharp.Point2f[] OrderCornersTopLeftClockwise(OpenCvSharp.Point2f[] points)
        {
            var topLeft = points[0];
            var topRight = points[0];
            var bottomRight = points[0];
            var bottomLeft = points[0];
            float minSum = float.PositiveInfinity;
            float maxSum = float.NegativeInfinity;
            float minDifference = float.PositiveInfinity;
            float maxDifference = float.NegativeInfinity;

            foreach (var point in points)
            {
                float sum = point.X + point.Y;
                float difference = point.X - point.Y;
                if (sum < minSum) { minSum = sum; topLeft = point; }
                if (sum > maxSum) { maxSum = sum; bottomRight = point; }
                if (difference > maxDifference) { maxDifference = difference; topRight = point; }
                if (difference < minDifference) { minDifference = difference; bottomLeft = point; }
            }

            return new[] { topLeft, topRight, bottomRight, bottomLeft };
        }
        private bool EstimateAndApplyPoseUsingCorners(Mat mat, OpenCvSharp.Point2f[] corners, VirtualPiano vp)
        {
            if (mat == null || mat.Empty() || pianoRenderer == null || vp == null || corners == null || corners.Length != 4)
                return false;

            var worldTargets = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float u = corners[i].X / mat.Width;
                float v = 1.0f - (corners[i].Y / mat.Height);
                if (!TryMapNormalizedImageToPlaneWorld(u, v, out worldTargets[i]))
                    return false;
            }

            Vector3 planeNormal = pianoRenderer.transform.up.normalized;
            Vector3 planeRight = pianoRenderer.transform.right.normalized;
            Vector3 planeImageUp = pianoRenderer.transform.forward.normalized;
            if (!TryMapNormalizedImageToPlaneWorld(0f, 0f, out Vector3 worldOrigin))
                return false;

            var target2 = new System.Numerics.Complex[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 d = worldTargets[i] - worldOrigin;
                target2[i] = new System.Numerics.Complex(Vector3.Dot(d, planeRight), Vector3.Dot(d, planeImageUp));
            }

            var keyPoints = new List<System.Numerics.Complex>();
            foreach (var kv in vp.Keys)
            {
                Vector3 d = kv.Value.transform.position - worldOrigin;
                keyPoints.Add(new System.Numerics.Complex(Vector3.Dot(d, planeRight), Vector3.Dot(d, planeImageUp)));
            }
            if (keyPoints.Count == 0)
                return false;

            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            foreach (var point in keyPoints)
            {
                if (point.Real < minX) minX = point.Real;
                if (point.Real > maxX) maxX = point.Real;
                if (point.Imaginary < minY) minY = point.Imaginary;
                if (point.Imaginary > maxY) maxY = point.Imaginary;
            }

            // Target and model points both use deterministic TL, TR, BR, BL ordering.
            var model2 = new[]
            {
                new System.Numerics.Complex(minX, maxY),
                new System.Numerics.Complex(maxX, maxY),
                new System.Numerics.Complex(maxX, minY),
                new System.Numerics.Complex(minX, minY)
            };

            System.Numerics.Complex modelCentroid = System.Numerics.Complex.Zero;
            System.Numerics.Complex targetCentroid = System.Numerics.Complex.Zero;
            for (int i = 0; i < 4; i++)
            {
                modelCentroid += model2[i];
                targetCentroid += target2[i];
            }
            modelCentroid /= 4.0;
            targetCentroid /= 4.0;

            System.Numerics.Complex numerator = System.Numerics.Complex.Zero;
            double denominator = 0;
            for (int i = 0; i < 4; i++)
            {
                var modelCentered = model2[i] - modelCentroid;
                var targetCentered = target2[i] - targetCentroid;
                numerator += targetCentered * System.Numerics.Complex.Conjugate(modelCentered);
                denominator += modelCentered.Real * modelCentered.Real + modelCentered.Imaginary * modelCentered.Imaginary;
            }
            if (denominator == 0)
                return false;

            var transform = numerator / denominator;
            float scale = (float)transform.Magnitude;
            float angleDegrees = (float)(Math.Atan2(transform.Imaginary, transform.Real) * 180.0 / Math.PI);
            double targetX = targetCentroid.Real - (transform.Real * modelCentroid.Real - transform.Imaginary * modelCentroid.Imaginary);
            double targetY = targetCentroid.Imaginary - (transform.Imaginary * modelCentroid.Real + transform.Real * modelCentroid.Imaginary);
            Vector3 targetCentroidWorld = worldOrigin + planeRight * (float)targetX + planeImageUp * (float)targetY;

            vp.transform.localScale = new Vector3(vp.transform.localScale.x * scale, vp.transform.localScale.y, vp.transform.localScale.z * scale);
            vp.transform.rotation = Quaternion.AngleAxis(angleDegrees, planeNormal) * pianoRenderer.transform.rotation;

            Vector3 currentCentroid = Vector3.zero;
            foreach (var kv in vp.Keys)
                currentCentroid += kv.Value.transform.position;
            currentCentroid /= vp.Keys.Count;

            vp.transform.position += targetCentroidWorld - currentCentroid;
            return true;
        }
        private bool EstimateAndApplyPose(
            Mat mat,
            OpenCvSharp.Rect outline,
            List<OpenCvSharp.Rect> blackKeyRects,
            VirtualPiano vp)
        {
            if (pianoRenderer == null || vp == null || mat == null || mat.Empty())
                return false;

            // Normalize the center of the detected keyboard outline.
            float normX =
                (outline.X + outline.Width * 0.5f) / (float)mat.Width;

            float normY =
                1.0f - ((outline.Y + outline.Height * 0.5f) / (float)mat.Height);

            if (!TryMapNormalizedImageToPlaneWorld(
                    normX,
                    normY,
                    out Vector3 worldCenter))
            {
                return false;
            }

            // The Unity Plane uses local X/Z as its surface axes.
            Vector3 planeNormal = pianoRenderer.transform.up.normalized;
            Vector3 planeRight = pianoRenderer.transform.right.normalized;
            Vector3 planeImageUp = pianoRenderer.transform.forward.normalized;

            Quaternion worldRotation = pianoRenderer.transform.rotation;

            // Determine the physical width of the reference plane.
            MeshFilter meshFilter = pianoRenderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return false;

            Bounds localBounds = meshFilter.sharedMesh.bounds;

            float planeWidthWorld =
                localBounds.size.x * pianoRenderer.transform.lossyScale.x;

            float targetWidthWorld =
                planeWidthWorld * (outline.Width / (float)mat.Width);

            if (targetWidthWorld <= 0.0001f)
                return false;

            // Find the current virtual piano width along the plane's horizontal axis.
            float currentMinX = float.PositiveInfinity;
            float currentMaxX = float.NegativeInfinity;

            Vector3 currentCenter = Vector3.zero;
            int keyCount = 0;

            foreach (var kv in vp.Keys)
            {
                Vector3 keyPosition = kv.Value.transform.position;

                float projectedX =
                    Vector3.Dot(keyPosition, planeRight);

                if (projectedX < currentMinX)
                    currentMinX = projectedX;

                if (projectedX > currentMaxX)
                    currentMaxX = projectedX;

                currentCenter += keyPosition;
                keyCount++;
            }

            if (keyCount == 0 ||
                float.IsInfinity(currentMinX) ||
                float.IsInfinity(currentMaxX))
            {
                return false;
            }

            currentCenter /= keyCount;

            float currentWidthWorld = currentMaxX - currentMinX;

            if (currentWidthWorld <= 0.0001f)
                return false;

            float scaleFactor = targetWidthWorld / currentWidthWorld;

            // Apply uniform scaling in the plane.
            Vector3 newScale = vp.transform.localScale;

            newScale = new Vector3(
                newScale.x * scaleFactor,
                newScale.y,
                newScale.z * scaleFactor
            );

            // Refine rotation using the detected black-key orientation.
            if (blackKeyRects != null && blackKeyRects.Count >= 2)
            {
                // Compute principal axis angle in image space.
                double meanX = 0;
                double meanY = 0;

                int n = blackKeyRects.Count;

                var ptsX = new double[n];
                var ptsY = new double[n];

                for (int i = 0; i < n; i++)
                {
                    ptsX[i] =
                        blackKeyRects[i].X +
                        blackKeyRects[i].Width * 0.5;

                    // OpenCV Y points downward, so invert it.
                    ptsY[i] =
                        -(blackKeyRects[i].Y +
                        blackKeyRects[i].Height * 0.5);

                    meanX += ptsX[i];
                    meanY += ptsY[i];
                }

                meanX /= n;
                meanY /= n;

                double covXX = 0;
                double covXY = 0;
                double covYY = 0;

                for (int i = 0; i < n; i++)
                {
                    double dx = ptsX[i] - meanX;
                    double dy = ptsY[i] - meanY;

                    covXX += dx * dx;
                    covXY += dx * dy;
                    covYY += dy * dy;
                }

                covXX /= n;
                covXY /= n;
                covYY /= n;

                double theta =
                    0.5 * Math.Atan2(
                        2.0 * covXY,
                        covXX - covYY);

                float vx = (float)Math.Cos(theta);
                float vy = (float)Math.Sin(theta);

                // Convert the detected image direction onto the Plane.
                Vector3 imgDirWorld =
                    planeRight * vx +
                    planeImageUp * vy;

                imgDirWorld -=
                    Vector3.Dot(imgDirWorld, planeNormal) *
                    planeNormal;

                if (imgDirWorld.sqrMagnitude > 1e-6f)
                {
                    imgDirWorld.Normalize();

                    float signedAngle =
                        Vector3.SignedAngle(
                            planeRight,
                            imgDirWorld,
                            planeNormal);

                    Quaternion rotAroundNormal =
                        Quaternion.AngleAxis(
                            signedAngle,
                            planeNormal);

                    vp.transform.rotation =
                        Quaternion.AngleAxis(180f, planeNormal) *
                        rotAroundNormal *
                        worldRotation;
                }
                else
                {
                    vp.transform.rotation =
                        Quaternion.AngleAxis(180f, planeNormal) *
                        worldRotation;
                }
            }
            else
            {
                vp.transform.rotation = worldRotation;
            }

            vp.transform.localScale = newScale;

            // Recalculate the center after changing rotation/scale.
            Vector3 transformedCenter = Vector3.zero;
            int transformedCount = 0;

            foreach (var kv in vp.Keys)
            {
                transformedCenter += kv.Value.transform.position;
                transformedCount++;
            }

            if (transformedCount > 0)
                transformedCenter /= transformedCount;

            // Move the virtual piano onto the detected keyboard center.
            vp.transform.position +=
                worldCenter - transformedCenter;

            return true;
        }

        private bool TryMapNormalizedImageToPlaneWorld(
            float u,
            float v,
            out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;

            if (pianoRenderer == null)
                return false;

            Transform planeTransform = pianoRenderer.transform;

            // Use the actual local-space size of the Unity Plane.
            MeshFilter meshFilter = pianoRenderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return false;

            Bounds localBounds = meshFilter.sharedMesh.bounds;

            // The Unity Plane lies in its local XZ plane.
            // X = image horizontal
            // Z = image vertical/depth
            // Y = plane normal
            float localX = Mathf.Lerp(localBounds.min.x, localBounds.max.x, u);
            float localZ = Mathf.Lerp(localBounds.min.z, localBounds.max.z, v);

            Vector3 localPoint = new Vector3(
                localX,
                localBounds.center.y,
                localZ
            );

            worldPoint = planeTransform.TransformPoint(localPoint);
            return true;
        }

    }
}