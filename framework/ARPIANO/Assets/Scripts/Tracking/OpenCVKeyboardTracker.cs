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
                                Debug.Log("Applied estimated pose to VirtualPiano.");
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
                double contourArea = Math.Abs(Cv2.ContourArea(contour));
                if (contourArea < imageArea * 0.01)
                    continue;

                var approx = Cv2.ApproxPolyDP(contour, 0.02 * Cv2.ArcLength(contour, true), true);
                if (approx.Length == 4 && Cv2.IsContourConvex(approx))
                {
                    corners = new OpenCvSharp.Point2f[4];
                    for (int i = 0; i < 4; i++)
                        corners[i] = new OpenCvSharp.Point2f(approx[i].X, approx[i].Y);

                    // order corners CCW around centroid
                    var cx = 0.0; var cy = 0.0;
                    foreach (var p in corners) { cx += p.X; cy += p.Y; }
                    cx /= 4.0; cy /= 4.0;
                    Array.Sort(corners, (a, b) => Math.Atan2(a.Y - cy, a.X - cx).CompareTo(Math.Atan2(b.Y - cy, b.X - cx)));
                    return true;
                }
            }

            return false;
        }

        private bool EstimateAndApplyPoseUsingCorners(Mat mat, OpenCvSharp.Point2f[] corners, VirtualPiano vp)
        {
            if (mat == null || mat.Empty() || pianoRenderer == null || vp == null)
                return false;

            Bounds b = pianoRenderer.bounds;
            // map image corner -> normalized UV
            var worldTargets = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float u = corners[i].X / (float)mat.Width;
                float v = 1.0f - (corners[i].Y / (float)mat.Height);
                worldTargets[i] = new Vector3(b.min.x + u * b.size.x, b.min.y + v * b.size.y, b.center.z);
            }

            // plane basis
            Vector3 planeNormal = pianoRenderer.transform.forward;
            Vector3 planeRight = pianoRenderer.transform.right;
            planeRight -= Vector3.Dot(planeRight, planeNormal) * planeNormal;
            planeRight.Normalize();
            Vector3 planeUp = pianoRenderer.transform.up;
            planeUp -= Vector3.Dot(planeUp, planeNormal) * planeNormal;
            planeUp.Normalize();

            Vector3 worldOrigin = new Vector3(b.min.x, b.min.y, b.center.z);

            // target in 2D plane coords
            var target2 = new System.Numerics.Complex[4];
            for (int i = 0; i < 4; i++)
            {
                var d = worldTargets[i] - worldOrigin;
                double _tx = Vector3.Dot(d, planeRight);
                double _ty = Vector3.Dot(d, planeUp);
                target2[i] = new System.Numerics.Complex(_tx, _ty);
            }

            // model points from VirtualPiano keys projected into same plane coords
            var keyPts = new List<System.Numerics.Complex>();
            foreach (var kv in vp.Keys)
            {
                Vector3 wp = kv.Value.transform.position;
                var d = wp - worldOrigin;
                double mx = Vector3.Dot(d, planeRight);
                double my = Vector3.Dot(d, planeUp);
                keyPts.Add(new System.Numerics.Complex(mx, my));
            }
            if (keyPts.Count == 0)
                return false;

            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            foreach (var c in keyPts)
            {
                if (c.Real < minX) minX = c.Real;
                if (c.Real > maxX) maxX = c.Real;
                if (c.Imaginary < minY) minY = c.Imaginary;
                if (c.Imaginary > maxY) maxY = c.Imaginary;
            }

            var model2 = new System.Numerics.Complex[4];
            model2[0] = new System.Numerics.Complex(minX, maxY); // TL
            model2[1] = new System.Numerics.Complex(maxX, maxY); // TR
            model2[2] = new System.Numerics.Complex(maxX, minY); // BR
            model2[3] = new System.Numerics.Complex(minX, minY); // BL

            // ensure ordering matches target (both CCW). Target was sorted CCW.

            // compute centroids
            System.Numerics.Complex cz = System.Numerics.Complex.Zero, cw = System.Numerics.Complex.Zero;
            for (int i = 0; i < 4; i++) { cz += model2[i]; cw += target2[i]; }
            cz /= 4.0; cw /= 4.0;

            // centered
            System.Numerics.Complex num = System.Numerics.Complex.Zero; double denom = 0;
            for (int i = 0; i < 4; i++)
            {
                var zc = model2[i] - cz;
                var wc = target2[i] - cw;
                num += wc * System.Numerics.Complex.Conjugate(zc);
                denom += (zc.Real * zc.Real + zc.Imaginary * zc.Imaginary);
            }
            if (denom == 0)
                return false;
            var a = num / denom; // scale*rot as complex
            double scale = a.Magnitude;
            double angleRad = Math.Atan2(a.Imaginary, a.Real);

            // compute target centroid world
            double tgtX = cw.Real - (a.Real * cz.Real - a.Imaginary * cz.Imaginary);
            double tgtY = cw.Imaginary - (a.Imaginary * cz.Real + a.Real * cz.Imaginary);

            Vector3 targetCentroidWorld = worldOrigin + planeRight * (float)tgtX + planeUp * (float)tgtY;

            // apply
            // scale
            vp.transform.localScale = new Vector3(vp.transform.localScale.x * (float)scale, vp.transform.localScale.y, vp.transform.localScale.z * (float)scale);

            // rotation around plane normal
            float angleDeg = (float)(angleRad * 180.0 / Math.PI);
            Quaternion rot = Quaternion.AngleAxis(angleDeg, planeNormal);
            vp.transform.rotation = rot * pianoRenderer.transform.rotation;

            // translate centroid
            // current centroid from key world positions
            Vector3 currentCentroid = Vector3.zero;
            foreach (var kv in vp.Keys) currentCentroid += kv.Value.transform.position;
            currentCentroid /= vp.Keys.Count;

            Vector3 delta = targetCentroidWorld - currentCentroid;
            vp.transform.position += delta;

            return true;
        }

        private bool EstimateAndApplyPose(Mat mat, OpenCvSharp.Rect outline, List<OpenCvSharp.Rect> blackKeyRects, VirtualPiano vp)
        {
            if (pianoRenderer == null || vp == null || mat == null || mat.Empty())
                return false;

            // Normalized center of detected outline in texture space
            float normX = (outline.X + outline.Width * 0.5f) / (float)mat.Width;
            float normY = 1.0f - ((outline.Y + outline.Height * 0.5f) / (float)mat.Height); // flip Y because texture origin

            // Use renderer bounds to compute world position
            Bounds b = pianoRenderer.bounds;
            Vector3 worldCenter = new Vector3(
                b.min.x + normX * b.size.x,
                b.min.y + normY * b.size.y,
                b.center.z);

            // base rotation aligned to the plane
            Quaternion worldRotation = pianoRenderer.transform.rotation;

            // compute target keyboard width in world units (portion of plane width)
            float targetWidthWorld = b.size.x * (outline.Width / (float)mat.Width);

            // find current virtual piano width from keys
            float currentMinX = float.MaxValue, currentMaxX = float.MinValue;
            foreach (var kv in vp.Keys)
            {
                var keyObj = kv.Value.gameObject;
                float x = keyObj.transform.position.x;
                if (x < currentMinX) currentMinX = x;
                if (x > currentMaxX) currentMaxX = x;
            }

            if (currentMinX == float.MaxValue || currentMaxX == float.MinValue)
                return false;

            float currentWidthWorld = currentMaxX - currentMinX;
            if (currentWidthWorld <= 0.0001f)
                return false;

            float scaleFactor = targetWidthWorld / currentWidthWorld;

            // apply uniform scale on X and Z, keep Y unchanged
            Vector3 newScale = vp.transform.localScale;
            newScale = new Vector3(newScale.x * scaleFactor, newScale.y, newScale.z * scaleFactor);

            // move piano so its center aligns with detected center
            Vector3 currentCenter = new Vector3((currentMinX + currentMaxX) * 0.5f, vp.transform.position.y, vp.transform.position.z);
            Vector3 delta = worldCenter - currentCenter;

            // refine rotation using black-key center orientation (PCA)
            if (blackKeyRects != null && blackKeyRects.Count >= 2)
            {
                // compute principal axis angle in image space, flipping Y so increasing Y is up
                double meanX = 0, meanY = 0;
                int n = blackKeyRects.Count;
                var ptsX = new double[n];
                var ptsY = new double[n];
                for (int i = 0; i < n; i++)
                {
                    ptsX[i] = blackKeyRects[i].X + blackKeyRects[i].Width * 0.5;
                    ptsY[i] = -(blackKeyRects[i].Y + blackKeyRects[i].Height * 0.5); // flip Y
                    meanX += ptsX[i];
                    meanY += ptsY[i];
                }
                meanX /= n; meanY /= n;

                double covXX = 0, covXY = 0, covYY = 0;
                for (int i = 0; i < n; i++)
                {
                    double dx = ptsX[i] - meanX;
                    double dy = ptsY[i] - meanY;
                    covXX += dx * dx;
                    covXY += dx * dy;
                    covYY += dy * dy;
                }
                covXX /= n; covXY /= n; covYY /= n;

                // principal angle (radians)
                double theta = 0.5 * Math.Atan2(2.0 * covXY, covXX - covYY);
                float vx = (float)Math.Cos(theta);
                float vy = (float)Math.Sin(theta);

                // map 2D image direction to world-space direction on the plane
                Vector3 imgDirWorld = pianoRenderer.transform.right * vx + pianoRenderer.transform.up * vy;
                Vector3 planeNormal = pianoRenderer.transform.forward;
                // project onto plane (remove normal component)
                imgDirWorld -= Vector3.Dot(imgDirWorld, planeNormal) * planeNormal;
                if (imgDirWorld.sqrMagnitude > 1e-6f)
                    imgDirWorld.Normalize();

                Vector3 planeRight = pianoRenderer.transform.right;
                planeRight -= Vector3.Dot(planeRight, planeNormal) * planeNormal;
                if (planeRight.sqrMagnitude > 1e-6f)
                    planeRight.Normalize();

                float signedAngle = Vector3.SignedAngle(planeRight, imgDirWorld, planeNormal);
                Quaternion rotAroundNormal = Quaternion.AngleAxis(signedAngle, planeNormal);
                vp.transform.rotation = rotAroundNormal * worldRotation;
            }
            else
            {
                vp.transform.rotation = worldRotation;
            }
            vp.transform.localScale = newScale;
            vp.transform.position += delta;

            return true;
        }
    }
}