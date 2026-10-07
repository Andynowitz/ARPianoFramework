// OpenCVKeyboardTracker.cs
// Purpose: Tracks the position of a physical piano keyboard using OpenCV and provides the position data for virtual piano rendering.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using OpenCvSharp;
using ARPIANO.Scripts.Piano;

namespace ARPIANO.Scripts.Tracking
{
    public class OpenCVKeyboardTracker : MonoBehaviour
    {
        [SerializeField] private Texture2D pianoImage;
        [SerializeField] private Renderer pianoRenderer;
        [SerializeField] private WebcamCapture webcamCapture;
        [SerializeField, Range(21, 108)] private int firstVisibleMidiNote = 21;
        [Header("Calibration marker diagnostics")]
        [SerializeField, Range(0, 179)] private int yellowMarkerHueMin = 15;
        [SerializeField, Range(0, 179)] private int yellowMarkerHueMax = 40;
        [SerializeField, Range(0, 255)] private int markerSaturationMin = 100;
        [SerializeField, Range(0, 255)] private int markerValueMin = 100;
        [SerializeField, Min(1)] private int markerMinimumArea = 40;
        [SerializeField, Min(0)] private int calibrationMarkerBottomExpansionPixels = 220;
        [Header("Keyboard geometry diagnostics")]
        [SerializeField] private int geometryMeasurementYOffsetPixels = -80;
        [SerializeField, Range(40, 60)] private int geometryMeasurementBandHeightPixels = 50;
        [SerializeField] private bool verboseLogging = false;
        private KeyboardGeometryDetector geometryDetector;

        private IEnumerator Start()
        {
            int finalBlackKeyCount = 0;
            int finalWhiteLineCount = 0;
            string finalPattern = "FAILED";
            if (webcamCapture == null)
                webcamCapture = FindAnyObjectByType<WebcamCapture>();

            Mat mat = null;
            if (webcamCapture != null && webcamCapture.ActiveTexture != null)
            {
                WebCamTexture webcam = webcamCapture.ActiveTexture;
                int waitFrames = 300;
                while (webcam.isPlaying &&
                       (webcam.width <= 16 || webcam.height <= 16 || !webcam.didUpdateThisFrame) &&
                       waitFrames-- > 0)
                    yield return null;

                if (webcam.isPlaying &&
                    webcam.width > 16 &&
                    webcam.height > 16 &&
                    webcam.didUpdateThisFrame)
                {
                    for (int sample = 1; sample <= 6; sample++)
                    {
                        yield return new WaitForSeconds(0.5f);
                        LogWebcamPixelStatistics(webcam);
                    }

                    mat = WebCamTextureToMat(webcam);
                    if (mat != null)
                        VerboseLog($"Loaded webcam frame: {mat.Width}x{mat.Height}");
                }
                else
                {
                    VerboseWarning(
                        $"Webcam did not provide a valid updated frame before timeout: " +
                        $"isPlaying={webcam.isPlaying}, didUpdateThisFrame={webcam.didUpdateThisFrame}, " +
                        $"width={webcam.width}, height={webcam.height}");
                }
            }

            if (mat == null)
            {
                Texture2D image = TryGetTexture();
                if (image == null)
                    yield break;

                VerboseLog($"Source texture: {image.name} format={image.format} readable={image.isReadable}");

                Texture2D readable = GetReadableTexture(image);
                if (readable == null)
                {
                    Debug.LogError("Unable to obtain a readable texture for OpenCV processing.");
                    yield break;
                }

                VerboseLog($"Readable texture: {readable.name} format={readable.format} readable={readable.isReadable}");
                mat = TextureToMat(readable);
            }

            if (mat == null)
                yield break;

            LogMatStatistics(mat);
            VerboseLog($"Loaded image: {mat.Width}x{mat.Height}");

            if (webcamCapture != null &&
                webcamCapture.ActiveTexture != null &&
                IsMatBlack(mat))
            {
                VerboseWarning("Webcam remained black after the 3-second startup delay; stopping before keyboard detection.");
                yield break;
            }

            var outline = DetectKeyboardOutline(mat);
            OpenCvSharp.Rect calibrationRegion = GetCalibrationMarkerRegion(mat, outline);
            bool markerCalibrationAvailable = DetectCalibrationMarkers(
                mat,
                calibrationRegion,
                out List<CalibrationMarkerCandidate> calibrationMarkers);
            bool markerCalibrationApplied = markerCalibrationAvailable &&
                TryApplyMarkerCalibration(mat, calibrationMarkers);
            if (markerCalibrationAvailable && !markerCalibrationApplied)
                EssentialLog("Calibration anchors detected but marker-based alignment could not be applied.");
            if (markerCalibrationAvailable)
            {
                geometryDetector = new KeyboardGeometryDetector(
                    geometryMeasurementYOffsetPixels,
                    geometryMeasurementBandHeightPixels);
                DetectKeyboardGeometry(mat, calibrationRegion, calibrationMarkers);
            }
            if (outline.Width > 0 && outline.Height > 0)
            {
                VerboseLog($"Found keyboard outline: x={outline.X} y={outline.Y} w={outline.Width} h={outline.Height}");

                bool validOutline =
                    outline.Width >= mat.Width * 0.2f &&
                    outline.Height >= mat.Height * 0.1f &&
                    outline.Width / (float)outline.Height >= 2.5f;
                OpenCvSharp.Rect whiteKeyRegion = outline;
                if (!validOutline)
                {
                    VerboseWarning("Detected outline is too small or has an invalid aspect ratio; this is likely a false positive.");
                    whiteKeyRegion = new OpenCvSharp.Rect(0, 0, mat.Width, mat.Height);
                    VerboseLog("Invalid keyboard outline; using full webcam frame for white-key structure.");
                }

                bool hasWhiteKeyStructure = TryDetectWhiteKeyLines(
                    mat,
                    whiteKeyRegion,
                    out float leftEdge,
                    out float rightEdge,
                    out float keySpacing,
                    out int lineCount);
                int visibleWhiteKeyCount = lineCount - 1;
                finalWhiteLineCount = lineCount;
                if (hasWhiteKeyStructure)
                {
                    VerboseLog($"Detected {lineCount} vertical white-key lines; visible white keys={visibleWhiteKeyCount}; left={leftEdge:F1} right={rightEdge:F1} spacing={keySpacing:F1}");
                }
                else
                {
                    VerboseWarning("White key line detection failed inside the detected keyboard outline.");
                }

                if (TryDetectBlackKeys(mat, outline, keySpacing, out int blackKeyCount, out float averageAspect, out List<OpenCvSharp.Rect> blackKeyRects))
                {
                    finalBlackKeyCount = blackKeyCount;
                    VerboseLog($"Detected {blackKeyCount} black-key candidates; avg aspect ratio={averageAspect:F2}");
                    int inferredFirstMidiNote = firstVisibleMidiNote;

                    if (!markerCalibrationApplied && hasWhiteKeyStructure && visibleWhiteKeyCount > 0)
                    {
                        var rangePiano = FindAnyObjectByType<VirtualPiano>();
                        if (rangePiano != null)
                        {
                            if (TryInferFirstVisibleMidiNote(
                                blackKeyRects,
                                outline.X,
                                leftEdge,
                                keySpacing,
                                visibleWhiteKeyCount,
                                out inferredFirstMidiNote))
                            {
                                int endMidi = GetLastVisibleMidiNote(inferredFirstMidiNote, visibleWhiteKeyCount);
                                VerboseLog($"Generating visible keyboard: MIDI {inferredFirstMidiNote} -> MIDI {endMidi}");
                                rangePiano.GenerateVisibleRange(inferredFirstMidiNote, visibleWhiteKeyCount);
                            }
                            else
                            {
                                VerboseWarning("Relative black-key pattern is reliable, but absolute MIDI octave is unresolved; visible keyboard generation skipped.");
                            }
                        }
                    }

                    // attempt refined corner detection first
                    if (TryDetectOutlineCorners(mat, out OpenCvSharp.Point2f[] corners))
                    {
                        VerboseLog($"Detected outline corners: {corners.Length}");
                    }

                    if (TryVerifyPianoPattern(blackKeyRects, keySpacing, out string patternDescription))
                    {
                        finalPattern = "VERIFIED";
                        VerboseLog($"Piano pattern verified: {patternDescription}");
                        DisplayVerifiedBlackKeyGeometry(mat, blackKeyRects);

                        var vp = FindAnyObjectByType<VirtualPiano>();
                        if (vp != null && !markerCalibrationApplied)
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
                                VerboseLog("Applied estimated pose to VirtualPiano and parented it to the reference plane.");
                            }
                            else
                            {
                                VerboseWarning("Pose estimation failed.");
                            }
                        }
                        else
                        {
                            VerboseWarning("VirtualPiano instance not found in scene.");
                        }
                    }
                    else
                    {
                        VerboseWarning($"Piano pattern verification failed: {patternDescription}");
                    }
                }
                else
                {
                    VerboseWarning("Black key detection failed inside the detected keyboard outline.");
                    if (hasWhiteKeyStructure && visibleWhiteKeyCount > 0)
                        VerboseWarning("No black-key pattern was detected; absolute MIDI octave is unresolved and visible keyboard generation is skipped.");
                }
            }
            else
            {
                if (TryDetectKeyboardStructure(
                    mat,
                    out OpenCvSharp.Rect structureRegion,
                    out float structureLeftEdge,
                    out float structureRightEdge,
                    out float structureSpacing,
                    out int structureLineCount,
                    out float structureAngle))
                {
                    int visibleWhiteKeyCount = structureLineCount - 1;
                    finalWhiteLineCount = structureLineCount;
                    VerboseLog("Keyboard structure detected.");
                    VerboseLog($"Detected {structureLineCount} white-key separator lines.");
                    VerboseLog($"Estimated visible white keys: {visibleWhiteKeyCount}.");
                    VerboseLog($"Estimated keyboard angle: {structureAngle:F1} degrees.");
                    VerboseLog($"Structure region: x={structureRegion.X} y={structureRegion.Y} w={structureRegion.Width} h={structureRegion.Height}; left={structureLeftEdge:F1} right={structureRightEdge:F1} spacing={structureSpacing:F1}");

                    if (!markerCalibrationApplied && visibleWhiteKeyCount > 0)
                    {
                        var vp = FindAnyObjectByType<VirtualPiano>();
                        if (vp != null)
                        {
                            if (TryDetectBlackKeys(mat, structureRegion, structureSpacing, out int blackKeyCount, out float averageAspect, out List<OpenCvSharp.Rect> blackKeyRects))
                            {
                                finalBlackKeyCount = blackKeyCount;
                                VerboseLog($"Detected {blackKeyCount} supporting black-key candidates; avg aspect ratio={averageAspect:F2}");
                                if (TryVerifyPianoPattern(blackKeyRects, structureSpacing, out string patternDescription))
                                    finalPattern = "VERIFIED";
                                else
                                    VerboseWarning($"Black-key pattern validation failed; continuing from white-key structure: {patternDescription}");

                                int inferredFirstMidiNote = firstVisibleMidiNote;
                                if (TryInferFirstVisibleMidiNote(
                                    blackKeyRects,
                                    structureRegion.X,
                                    structureLeftEdge,
                                    structureSpacing,
                                    visibleWhiteKeyCount,
                                    out inferredFirstMidiNote))
                                {
                                    int endMidi = GetLastVisibleMidiNote(inferredFirstMidiNote, visibleWhiteKeyCount);
                                    VerboseLog($"Generating visible keyboard: MIDI {inferredFirstMidiNote} -> MIDI {endMidi}");
                                    vp.GenerateVisibleRange(inferredFirstMidiNote, visibleWhiteKeyCount);
                                }
                                else
                                {
                                    VerboseWarning("Relative black-key pattern is reliable, but absolute MIDI octave is unresolved; visible keyboard generation skipped.");
                                }
                            }
                            else
                            {
                                VerboseWarning("No supporting black keys detected; continuing from white-key structure.");
                                blackKeyRects = new List<OpenCvSharp.Rect>();
                                VerboseWarning("No black-key pattern was detected; absolute MIDI octave is unresolved and visible keyboard generation is skipped.");
                            }

                            if (!markerCalibrationApplied &&
                                EstimateAndApplyPose(mat, structureRegion, blackKeyRects, vp))
                            {
                                if (pianoRenderer != null)
                                    vp.transform.SetParent(pianoRenderer.transform, true);
                                VerboseLog("Applied available pose estimation from the detected keyboard structure.");
                            }
                            else
                            {
                                VerboseWarning("Structure detection succeeded, but existing pose estimation could not be applied. Perspective pose remains a limitation of this fallback.");
                            }
                        }
                        else
                        {
                            VerboseWarning("Keyboard structure detected, but no VirtualPiano instance was found.");
                        }
                    }
                }
                else
                {
                    VerboseWarning("Keyboard outline and repeated white-key structure detection both failed; no virtual keyboard was generated.");
                }
            }
            EssentialLog(
                $"Keyboard detection: blackKeys={finalBlackKeyCount}, " +
                $"whiteLines={finalWhiteLineCount}, pattern={finalPattern}");
        }

        private void EssentialLog(string message)
        {
            Debug.Log(message);
        }

        private void VerboseLog(string message)
        {
            if (verboseLogging)
                Debug.Log(message);
        }

        private void VerboseWarning(string message)
        {
            if (verboseLogging)
                Debug.LogWarning(message);
        }

        private void LogWebcamPixels(WebCamTexture webcam)
        {
            VerboseLog(
                $"Reading WebCamTexture pixels: isPlaying={webcam.isPlaying}, " +
                $"didUpdateThisFrame={webcam.didUpdateThisFrame}, width={webcam.width}, " +
                $"height={webcam.height}, videoRotationAngle={webcam.videoRotationAngle}, " +
                $"videoVerticallyMirrored={webcam.videoVerticallyMirrored}");

            Color32[] pixels = webcam.GetPixels32();
            int expectedLength = webcam.width * webcam.height;

            if (pixels == null)
            {
                VerboseWarning(
                    $"WebCamTexture.GetPixels32 returned null. width={webcam.width}, height={webcam.height}, " +
                    $"expectedLength={expectedLength}");
                return;
            }

            int minimum = 255;
            int maximum = 0;
            long totalIntensity = 0;
            foreach (Color32 pixel in pixels)
            {
                int intensity = (pixel.r + pixel.g + pixel.b) / 3;
                minimum = Mathf.Min(minimum, intensity);
                maximum = Mathf.Max(maximum, intensity);
                totalIntensity += intensity;
            }

            float average = pixels.Length > 0
                ? totalIntensity / (float)pixels.Length
                : 0f;

            int centerIndex = pixels.Length > 0
                ? Mathf.Clamp((webcam.height / 2) * webcam.width + webcam.width / 2, 0, pixels.Length - 1)
                : 0;
            int topLeftIndex = 0;
            int topRightIndex = Mathf.Max(0, webcam.width - 1);
            int bottomLeftIndex = pixels.Length > 0
                ? Mathf.Clamp((webcam.height - 1) * webcam.width, 0, pixels.Length - 1)
                : 0;
            int bottomRightIndex = pixels.Length > 0
                ? pixels.Length - 1
                : 0;

            VerboseLog(
                $"WebCamTexture.GetPixels32: length={pixels.Length}, expected={expectedLength}, " +
                $"minRGB={minimum}, maxRGB={maximum}, averageRGB={average:F2}, " +
                $"topLeft={FormatPixel(pixels, topLeftIndex)}, " +
                $"topRight={FormatPixel(pixels, topRightIndex)}, " +
                $"center={FormatPixel(pixels, centerIndex)}, " +
                $"bottomLeft={FormatPixel(pixels, bottomLeftIndex)}, " +
                $"bottomRight={FormatPixel(pixels, bottomRightIndex)}");
        }

        private void LogWebcamPixelStatistics(WebCamTexture webcam)
        {
            Color32[] pixels = webcam.GetPixels32();
            int minimum = 255;
            int maximum = 0;
            long totalIntensity = 0;
            foreach (Color32 pixel in pixels)
            {
                int intensity = (pixel.r + pixel.g + pixel.b) / 3;
                minimum = Mathf.Min(minimum, intensity);
                maximum = Mathf.Max(maximum, intensity);
                totalIntensity += intensity;
            }

            float average = pixels.Length > 0
                ? totalIntensity / (float)pixels.Length
                : 0f;
            VerboseLog($"WebCamTexture sample: minRGB={minimum}, maxRGB={maximum}, averageRGB={average:F2}");
        }

        private string FormatPixel(Color32[] pixels, int index)
        {
            if (pixels == null || pixels.Length == 0)
                return "(unavailable)";

            Color32 pixel = pixels[Mathf.Clamp(index, 0, pixels.Length - 1)];
            return $"({pixel.r},{pixel.g},{pixel.b},{pixel.a})";
        }

        private void LogMatStatistics(Mat mat)
        {
            if (mat == null || mat.Empty())
            {
                VerboseWarning("OpenCV Mat statistics: Mat is null or empty.");
                return;
            }

            double minimum;
            double maximum;
            double mean;
            using (var gray = new Mat())
            {
                if (mat.Channels() == 1)
                    mat.CopyTo(gray);
                else
                    Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

                Cv2.MinMaxLoc(gray, out minimum, out maximum);
                mean = Cv2.Mean(gray).Val0;
            }

            VerboseLog(
                $"OpenCV Mat statistics: width={mat.Width}, height={mat.Height}, " +
                $"type={mat.Type()}, channels={mat.Channels()}, min={minimum:F2}, " +
                $"max={maximum:F2}, mean={mean:F2}");

            DisplayOriginalDebugImage(mat);
        }

        private void DisplayOriginalDebugImage(Mat mat)
        {
            if (mat == null || mat.Empty())
                return;

            if (!Cv2.ImEncode(".png", mat, out byte[] encodedImage))
            {
                VerboseWarning("Unable to encode the original OpenCV Mat for debug display.");
                return;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(encodedImage))
            {
                Destroy(texture);
                VerboseWarning("Unable to load the encoded original OpenCV Mat into a Unity texture.");
                return;
            }

            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("OpenCVDebugCanvas");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            GameObject previewObject = GameObject.Find("OpenCVDebugPreview");
            RawImage debugPreview = previewObject != null
                ? previewObject.GetComponent<RawImage>()
                : null;
            if (debugPreview == null)
            {
                previewObject = new GameObject("OpenCVDebugPreview");
                previewObject.transform.SetParent(canvas.transform, false);
                debugPreview = previewObject.AddComponent<RawImage>();
            }

            debugPreview.texture = texture;
            debugPreview.color = Color.white;
            debugPreview.enabled = true;
            debugPreview.gameObject.SetActive(true);

            RectTransform rect = debugPreview.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(16f, 16f);
            rect.sizeDelta = new Vector2(320f, 180f);
            debugPreview.transform.SetAsLastSibling();
            VerboseLog($"Displayed original OpenCV Mat: {texture.width}x{texture.height} as 320x180 preview.");
        }

        private void CreateDebugVisualization(Mat source, OpenCvSharp.Rect selectedOutline)
        {
                    if (source == null || source.Empty())
                        return;

                    using var debugImage = source.Clone();
                    using var gray = new Mat();
                    Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
                    using var blurred = new Mat();
                    Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
                    using var edges = new Mat();
                    Cv2.Canny(blurred, edges, 20, 80);

                    int minimumLength = Math.Max(30, Math.Min(source.Width, source.Height) / 10);
                    LineSegmentPoint[] houghLines = Cv2.HoughLinesP(
                        edges,
                        1,
                        Math.PI / 180.0,
                        30,
                        minimumLength,
                        8);

                    int totalHoughLines = houghLines?.Length ?? 0;
                    var candidateLines = new List<LineSegmentPoint>();
                    if (houghLines != null)
                    {
                        foreach (LineSegmentPoint line in houghLines)
                        {
                            float dx = line.P2.X - line.P1.X;
                            float dy = line.P2.Y - line.P1.Y;
                            float length = Mathf.Sqrt(dx * dx + dy * dy);
                            if (length >= minimumLength)
                                candidateLines.Add(line);

                            Cv2.Line(debugImage, line.P1, line.P2, new Scalar(0, 220, 220), 2);
                        }
                    }

                    var angleClusters = new List<List<LineSegmentPoint>>();
                    foreach (LineSegmentPoint line in candidateLines)
                    {
                        float lineAngle = Mathf.Repeat(
                            Mathf.Atan2(line.P2.Y - line.P1.Y, line.P2.X - line.P1.X) * Mathf.Rad2Deg,
                            180f);

                        List<LineSegmentPoint> matchingCluster = null;
                        foreach (List<LineSegmentPoint> cluster in angleClusters)
                        {
                            if (GetUndirectedAngleDifference(lineAngle, GetAverageLineAngle(cluster)) <= 10f)
                            {
                                matchingCluster = cluster;
                                break;
                            }
                        }

                        if (matchingCluster == null)
                        {
                            matchingCluster = new List<LineSegmentPoint>();
                            angleClusters.Add(matchingCluster);
                        }
                        matchingCluster.Add(line);
                    }

                    var separatorLines = new List<LineSegmentPoint>();
                    int positionClusterCount = 0;
                    foreach (List<LineSegmentPoint> cluster in angleClusters)
                    {
                        if (cluster.Count < 5)
                            continue;

                        float clusterAngle = GetAverageLineAngle(cluster);
                        float radians = clusterAngle * Mathf.Deg2Rad;
                        Vector2 normal = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
                        var projected = new List<KeyValuePair<float, LineSegmentPoint>>();
                        foreach (LineSegmentPoint line in cluster)
                        {
                            Vector2 midpoint = new Vector2(
                                (line.P1.X + line.P2.X) * 0.5f,
                                (line.P1.Y + line.P2.Y) * 0.5f);
                            projected.Add(new KeyValuePair<float, LineSegmentPoint>(
                                Vector2.Dot(midpoint, normal),
                                line));
                        }

                        projected.Sort((first, second) => first.Key.CompareTo(second.Key));
                        var positions = new List<float>();
                        foreach (KeyValuePair<float, LineSegmentPoint> item in projected)
                        {
                            if (positions.Count == 0 || item.Key - positions[positions.Count - 1] > 8f)
                            {
                                positions.Add(item.Key);
                                separatorLines.Add(item.Value);
                            }
                        }

                        positionClusterCount += positions.Count;
                    }

                    foreach (LineSegmentPoint line in candidateLines)
                        Cv2.Line(debugImage, line.P1, line.P2, new Scalar(255, 140, 0), 2);

                    foreach (LineSegmentPoint line in separatorLines)
                        Cv2.Line(debugImage, line.P1, line.P2, new Scalar(0, 255, 0), 4);

                    Cv2.FindContours(
                        edges,
                        out Point[][] contours,
                        out HierarchyIndex[] hierarchy,
                        RetrievalModes.External,
                        ContourApproximationModes.ApproxSimple);

                    int candidateQuadrilateralCount = 0;
                    double imageArea = source.Width * source.Height;
                    OpenCvSharp.Point[] selectedQuadrilateral = null;
                    double selectedQuadrilateralArea = 0;
                    foreach (Point[] contour in contours)
                    {
                        double contourArea = Math.Abs(Cv2.ContourArea(contour));
                        if (contourArea < imageArea * 0.01)
                            continue;

                        Point[] approximation = Cv2.ApproxPolyDP(
                            contour,
                            0.02 * Cv2.ArcLength(contour, true),
                            true);
                        if (approximation.Length != 4 || !Cv2.IsContourConvex(approximation))
                            continue;

                        candidateQuadrilateralCount++;
                        Cv2.Polylines(
                            debugImage,
                            new[] { approximation },
                            true,
                            new Scalar(0, 140, 255),
                            4);

                        if (contourArea > selectedQuadrilateralArea)
                        {
                            selectedQuadrilateralArea = contourArea;
                            selectedQuadrilateral = approximation;
                        }
                    }

                    if (selectedQuadrilateral != null)
                    {
                        for (int i = 0; i < selectedQuadrilateral.Length; i++)
                        {
                            Point first = selectedQuadrilateral[i];
                            Point second = selectedQuadrilateral[(i + 1) % selectedQuadrilateral.Length];
                            Cv2.Line(debugImage, first, second, new Scalar(0, 0, 255), 6);
                            Cv2.Circle(debugImage, first, 10, new Scalar(255, 0, 255), -1);
                        }
                    }
                    else if (selectedOutline.Width > 0 && selectedOutline.Height > 0)
                    {
                        var outlineCorners = new[]
                        {
                            new Point(selectedOutline.Left, selectedOutline.Top),
                            new Point(selectedOutline.Right, selectedOutline.Top),
                            new Point(selectedOutline.Right, selectedOutline.Bottom),
                            new Point(selectedOutline.Left, selectedOutline.Bottom),
                        };
                        Cv2.Polylines(debugImage, new[] { outlineCorners }, true, new Scalar(255, 0, 255), 5);
                    }

                    VerboseLog($"Debug Hough lines: total={totalHoughLines}, candidate separator lines={candidateLines.Count}, angle clusters={angleClusters.Count}, position clusters={positionClusterCount}");
                    VerboseLog($"Debug contours: detected={contours.Length}, candidate quadrilaterals={candidateQuadrilateralCount}, selected quadrilateral={(selectedQuadrilateral != null)}");
                    DisplayDebugImage(debugImage);
        }

        private void DisplayDebugImage(Mat debugImage)
        {
                    if (!verboseLogging)
                        return;

                    if (debugImage == null || debugImage.Empty())
                        return;

                    if (!Cv2.ImEncode(".png", debugImage, out byte[] encodedImage))
                        return;

                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!texture.LoadImage(encodedImage))
                    {
                        Destroy(texture);
                        return;
                    }

                    Canvas canvas = FindAnyObjectByType<Canvas>();
                    if (canvas == null)
                    {
                        GameObject canvasObject = new GameObject("OpenCVDebugCanvas");
                        canvas = canvasObject.AddComponent<Canvas>();
                        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                        canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
                        canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                    }

                    GameObject previewObject = GameObject.Find("OpenCVDebugPreview");
                    RawImage debugPreview = previewObject != null
                        ? previewObject.GetComponent<RawImage>()
                        : null;
                    if (debugPreview == null)
                    {
                        previewObject = new GameObject("OpenCVDebugPreview");
                        previewObject.transform.SetParent(canvas.transform, false);
                        debugPreview = previewObject.AddComponent<RawImage>();
                    }

                    debugPreview.texture = texture;
                    debugPreview.color = Color.white;
                    debugPreview.enabled = true;
                    debugPreview.gameObject.SetActive(true);
                    RectTransform rect = debugPreview.rectTransform;
                    rect.anchorMin = new Vector2(0.05f, 0.05f);
                    rect.anchorMax = new Vector2(0.95f, 0.95f);
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    debugPreview.transform.SetAsLastSibling();
                    VerboseLog($"Displayed OpenCV debug image: {texture.width}x{texture.height}");
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

            VerboseWarning("Creating a readable RGBA32 copy of the texture because the source is either not readable or compressed.");
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

        private Mat WebCamTextureToMat(WebCamTexture webcam)
        {
            if (webcam == null || webcam.width <= 16 || webcam.height <= 16)
                return null;

            RenderTexture temporaryTarget = RenderTexture.GetTemporary(
                webcam.width,
                webcam.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Default);
            RenderTexture previousTarget = RenderTexture.active;
            Texture2D frame = new Texture2D(webcam.width, webcam.height, TextureFormat.RGBA32, false);

            try
            {
                if (!temporaryTarget.IsCreated())
                    temporaryTarget.Create();

                VerboseLog(
                    $"Webcam GPU readback target: source={webcam.width}x{webcam.height}, " +
                    $"target={temporaryTarget.width}x{temporaryTarget.height}, " +
                    $"format={temporaryTarget.format}, created={temporaryTarget.IsCreated()}, " +
                    $"activeBefore={previousTarget != null}");

                Graphics.Blit(webcam, temporaryTarget);
                GL.Flush();
                RenderTexture.active = temporaryTarget;
                frame.ReadPixels(
                    new UnityEngine.Rect(0, 0, webcam.width, webcam.height),
                    0,
                    0,
                    false);
                frame.Apply(false, false);
                LogTexturePixels(frame, "Webcam GPU readback Texture2D");

                return TextureToMat(frame);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                RenderTexture.ReleaseTemporary(temporaryTarget);
                Destroy(frame);
            }
        }

        private void LogTexturePixels(Texture2D texture, string label)
        {
            if (texture == null || !texture.isReadable)
            {
                VerboseWarning($"{label}: texture is null or not readable.");
                return;
            }

            Color32[] pixels = texture.GetPixels32();
            int minimum = 255;
            int maximum = 0;
            long totalIntensity = 0;
            foreach (Color32 pixel in pixels)
            {
                int intensity = (pixel.r + pixel.g + pixel.b) / 3;
                minimum = Mathf.Min(minimum, intensity);
                maximum = Mathf.Max(maximum, intensity);
                totalIntensity += intensity;
            }

            float average = pixels.Length > 0
                ? totalIntensity / (float)pixels.Length
                : 0f;
            VerboseLog(
                $"{label}: width={texture.width}, height={texture.height}, format={texture.format}, " +
                $"length={pixels.Length}, minRGB={minimum}, maxRGB={maximum}, averageRGB={average:F2}");
        }

        private bool IsMatBlack(Mat mat)
        {
            if (mat == null || mat.Empty())
                return true;

            using var gray = new Mat();
            if (mat.Channels() == 1)
                mat.CopyTo(gray);
            else
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.MinMaxLoc(gray, out double minimum, out double maximum);
            return maximum <= 0.0;
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

        private sealed class CalibrationMarkerCandidate
        {
            public OpenCvSharp.Rect Bounds;
            public Point2f Center;
            public double Area;
            public double FillRatio;
        }

        private OpenCvSharp.Rect GetCalibrationMarkerRegion(Mat src, OpenCvSharp.Rect outline)
        {
            bool validOutline = outline.Width >= src.Width * 0.2f &&
                outline.Height >= src.Height * 0.1f &&
                outline.Width / (float)Math.Max(1, outline.Height) >= 2.5f;
            OpenCvSharp.Rect baseRegion = outline;
            if (!validOutline)
            {
                baseRegion = FindFullFrameKeyboardBand(src);
                if (baseRegion.Width <= 0 || baseRegion.Height <= 0)
                {
                    baseRegion = new OpenCvSharp.Rect(
                        0,
                        0,
                        src.Width,
                        Mathf.Max(1, Mathf.RoundToInt(src.Height * 0.55f)));
                }
            }

            int bottom = Mathf.Min(
                src.Height,
                baseRegion.Bottom + calibrationMarkerBottomExpansionPixels);
            int x = Mathf.Clamp(baseRegion.X, 0, Mathf.Max(0, src.Width - 1));
            int y = Mathf.Clamp(baseRegion.Y, 0, Mathf.Max(0, src.Height - 1));
            int right = Mathf.Clamp(baseRegion.Right, x + 1, src.Width);
            bottom = Mathf.Clamp(bottom, y + 1, src.Height);
            return new OpenCvSharp.Rect(x, y, right - x, bottom - y);
        }

        private bool DetectCalibrationMarkers(
            Mat src,
            OpenCvSharp.Rect searchRegion,
            out List<CalibrationMarkerCandidate> selectedMarkers)
        {
            selectedMarkers = new List<CalibrationMarkerCandidate>();
            if (src == null || src.Empty())
                return false;

            int regionX = Mathf.Clamp(searchRegion.X, 0, src.Width - 1);
            int regionY = Mathf.Clamp(searchRegion.Y, 0, src.Height - 1);
            int regionRight = Mathf.Clamp(searchRegion.X + searchRegion.Width, regionX + 1, src.Width);
            int regionBottom = Mathf.Clamp(searchRegion.Y + searchRegion.Height, regionY + 1, src.Height);
            searchRegion = new OpenCvSharp.Rect(
                regionX,
                regionY,
                regionRight - regionX,
                regionBottom - regionY);
            if (searchRegion.Width <= 0 || searchRegion.Height <= 0)
                return false;

            using var roi = new Mat(src, searchRegion);
            using var hsv = new Mat();
            Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
            using var yellowMask = CreateCalibrationMarkerMask(hsv, yellowMarkerHueMin, yellowMarkerHueMax);

            var yellowDiagnosticRects = new List<OpenCvSharp.Rect>();
            List<CalibrationMarkerCandidate> yellowCandidates =
                FindCalibrationMarkerCandidates(yellowMask, yellowDiagnosticRects, searchRegion);
            bool found = TrySelectCalibrationMarkerTriplet(yellowCandidates, selectedMarkers);

            if (!found)
            {
                using var debugImage = src.Clone();
                Cv2.Rectangle(debugImage, searchRegion, new Scalar(255, 0, 255), 5);
                foreach (OpenCvSharp.Rect rect in yellowDiagnosticRects)
                    DrawCalibrationCandidateDiagnostic(debugImage, rect);
                DisplayDebugMat(debugImage, "KeyboardGeometryDebugPreview", 1500, 750);
            }

            if (found)
            {
                float spacingLeft = selectedMarkers[1].Center.X - selectedMarkers[0].Center.X;
                float spacingRight = selectedMarkers[2].Center.X - selectedMarkers[1].Center.X;
                EssentialLog(
                    $"Calibration anchors: C3=({selectedMarkers[0].Center.X:F1},{selectedMarkers[0].Center.Y:F1}) " +
                    $"C4=({selectedMarkers[1].Center.X:F1},{selectedMarkers[1].Center.Y:F1}) " +
                    $"C5=({selectedMarkers[2].Center.X:F1},{selectedMarkers[2].Center.Y:F1}) | " +
                    $"spacing=C3-C4:{spacingLeft:F1}px, C4-C5:{spacingRight:F1}px");
            }
            else
                EssentialLog("Calibration anchors unavailable; marker-based alignment skipped.");

            return found;
        }

        private void DetectKeyboardGeometry(
            Mat src,
            OpenCvSharp.Rect keyboardRegion,
            List<CalibrationMarkerCandidate> markers)
        {
            if (src == null || src.Empty() || markers == null || markers.Count != 3)
                return;

            if (geometryDetector == null)
                return;

            var markerCenters = new List<Vector2>
            {
                new Vector2(markers[0].Center.X, markers[0].Center.Y),
                new Vector2(markers[1].Center.X, markers[1].Center.Y),
                new Vector2(markers[2].Center.X, markers[2].Center.Y),
            };
            KeyboardGeometry geometry = geometryDetector.Detect(
                src,
                keyboardRegion,
                markerCenters);
            if (geometry == null || geometry.MeasurementRegion.Width <= 0)
                return;

            EssentialLog(
                $"Geometry measurement ROI: x={geometry.MeasurementRegion.X}, y={geometry.MeasurementRegion.Y}, " +
                $"width={geometry.MeasurementRegion.Width}, height={geometry.MeasurementRegion.Height}");
            EssentialLog($"Geometry raw vertical candidates: {FormatGeometryValues(geometry.RawVerticalCandidates)}");
            EssentialLog($"Geometry boundaries: {FormatGeometryValues(geometry.Boundaries)}");
            EssentialLog($"Geometry widths: {FormatGeometryValues(geometry.Widths)}");
            EssentialLog($"Geometry centers: {FormatGeometryValues(geometry.Centers)}");
            EssentialLog(
                $"C3=({markers[0].Center.X:F1},{markers[0].Center.Y:F1}) " +
                $"C4=({markers[1].Center.X:F1},{markers[1].Center.Y:F1}) " +
                $"C5=({markers[2].Center.X:F1},{markers[2].Center.Y:F1}) " +
                $"nearestCenters=C3:{FormatNearestCenter(geometry.NearestC3Center)}, " +
                $"C4:{FormatNearestCenter(geometry.NearestC4Center)}, " +
                $"C5:{FormatNearestCenter(geometry.NearestC5Center)}");

            using var debugImage = src.Clone();
            Cv2.Rectangle(debugImage, geometry.MeasurementRegion, new Scalar(255, 255, 0), 4);
            DrawCalibrationMarker(debugImage, markers[0], new Scalar(0, 255, 255), "C3");
            DrawCalibrationMarker(debugImage, markers[1], new Scalar(0, 255, 255), "C4");
            DrawCalibrationMarker(debugImage, markers[2], new Scalar(0, 255, 255), "C5");
            foreach (float boundary in geometry.Boundaries)
            {
                int x = Mathf.RoundToInt(boundary);
                Cv2.Line(
                    debugImage,
                    new Point(x, geometry.MeasurementRegion.Top),
                    new Point(x, geometry.MeasurementRegion.Bottom),
                    new Scalar(0, 255, 0),
                    3);
            }

            foreach (float center in geometry.Centers)
            {
                Cv2.Circle(
                    debugImage,
                    new Point(
                        Mathf.RoundToInt(center),
                        geometry.MeasurementRegion.Top + geometry.MeasurementRegion.Height / 2),
                    6,
                    new Scalar(0, 0, 255),
                    -1);
            }

            DisplayDebugMat(debugImage, "KeyboardGeometryDebugPreview", 1500, 750);
        }
        private string FormatGeometryValues(IReadOnlyList<float> values)
        {
            if (values == null || values.Count == 0)
                return string.Empty;

            var formatted = new List<string>(values.Count);
            foreach (float value in values)
                formatted.Add(value.ToString("F1"));
            return string.Join(", ", formatted);
        }

        private string FormatNearestCenter(float? center)
        {
            return center.HasValue ? $"{center.Value:F1}px" : "NONE";
        }

        private Mat CreateCalibrationMarkerMask(Mat hsv, int hueMin, int hueMax)
        {
            var mask = new Mat();
            Cv2.InRange(
                hsv,
                new Scalar(hueMin, markerSaturationMin, markerValueMin),
                new Scalar(hueMax, 255, 255),
                mask);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
            return mask;
        }

        private List<CalibrationMarkerCandidate> FindCalibrationMarkerCandidates(
            Mat mask,
            List<OpenCvSharp.Rect> diagnosticRects,
            OpenCvSharp.Rect searchRegion)
        {
            var candidates = new List<CalibrationMarkerCandidate>();
            Cv2.FindContours(
                mask,
                out Point[][] contours,
                out HierarchyIndex[] hierarchy,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            foreach (Point[] contour in contours)
            {
                OpenCvSharp.Rect bounds = Cv2.BoundingRect(contour);
                diagnosticRects.Add(new OpenCvSharp.Rect(
                    bounds.X + searchRegion.X,
                    bounds.Y + searchRegion.Y,
                    bounds.Width,
                    bounds.Height));
                double area = Cv2.ContourArea(contour);
                double boxArea = Math.Max(1, bounds.Width * bounds.Height);
                double fillRatio = area / boxArea;
                float aspect = bounds.Width / (float)Math.Max(1, bounds.Height);
                if (area < markerMinimumArea ||
                    area > searchRegion.Width * searchRegion.Height * 0.08 ||
                    bounds.Width < 6 ||
                    bounds.Height < 6 ||
                    bounds.Width > Mathf.Max(40f, searchRegion.Width * 0.2f) ||
                    bounds.Height > Mathf.Max(35f, searchRegion.Height * 0.75f) ||
                    aspect < 0.3f ||
                    aspect > 3.5f ||
                    fillRatio < 0.3)
                    continue;

                candidates.Add(new CalibrationMarkerCandidate
                {
                    Bounds = new OpenCvSharp.Rect(
                        bounds.X + searchRegion.X,
                        bounds.Y + searchRegion.Y,
                        bounds.Width,
                        bounds.Height),
                    Center = new Point2f(
                        searchRegion.X + bounds.X + bounds.Width * 0.5f,
                        searchRegion.Y + bounds.Y + bounds.Height * 0.5f),
                    Area = area,
                    FillRatio = fillRatio,
                });
            }

            return candidates;
        }

        private bool TrySelectCalibrationMarkerTriplet(
            List<CalibrationMarkerCandidate> candidates,
            List<CalibrationMarkerCandidate> selected)
        {
            if (candidates == null || candidates.Count < 3)
                return false;

            var ordered = new List<CalibrationMarkerCandidate>(candidates);
            ordered.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));
            float bestScore = float.PositiveInfinity;
            CalibrationMarkerCandidate bestLeft = null;
            CalibrationMarkerCandidate bestMiddle = null;
            CalibrationMarkerCandidate bestRight = null;
            for (int i = 0; i < ordered.Count - 2; i++)
            {
                for (int j = i + 1; j < ordered.Count - 1; j++)
                {
                    for (int k = j + 1; k < ordered.Count; k++)
                    {
                        float leftGap = ordered[j].Center.X - ordered[i].Center.X;
                        float rightGap = ordered[k].Center.X - ordered[j].Center.X;
                        if (leftGap <= 0f || rightGap <= 0f)
                            continue;

                        float meanGap = (leftGap + rightGap) * 0.5f;
                        float spacingError = Mathf.Abs(leftGap - rightGap) / meanGap;
                        float geometryPenalty =
                            1f / Mathf.Max(1f, (float)ordered[i].Area) +
                            1f / Mathf.Max(1f, (float)ordered[j].Area) +
                            1f / Mathf.Max(1f, (float)ordered[k].Area);
                        float score = spacingError + geometryPenalty * 1000f;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestLeft = ordered[i];
                            bestMiddle = ordered[j];
                            bestRight = ordered[k];
                        }
                    }
                }
            }

            if (bestLeft == null || bestScore > 0.45f)
                return false;

            selected.Add(bestLeft);
            selected.Add(bestMiddle);
            selected.Add(bestRight);
            return true;
        }

        private bool TryApplyMarkerCalibration(
            Mat mat,
            List<CalibrationMarkerCandidate> markers)
        {
            if (mat == null || markers == null || markers.Count != 3 || pianoRenderer == null)
                return false;

            VirtualPiano vp = FindAnyObjectByType<VirtualPiano>();
            if (vp == null || !vp.GenerateVisibleRange(48, 15))
                return false;

            if (!TryMapNormalizedImageToPlaneWorld(
                    markers[0].Center.X / mat.Width,
                    1f - markers[0].Center.Y / mat.Height,
                    out Vector3 targetC3) ||
                !TryMapNormalizedImageToPlaneWorld(
                    markers[1].Center.X / mat.Width,
                    1f - markers[1].Center.Y / mat.Height,
                    out Vector3 targetC4) ||
                !TryMapNormalizedImageToPlaneWorld(
                    markers[2].Center.X / mat.Width,
                    1f - markers[2].Center.Y / mat.Height,
                    out Vector3 targetC5))
                return false;

            PianoKey c3 = vp.GetKey(48);
            PianoKey c4 = vp.GetKey(60);
            PianoKey c5 = vp.GetKey(72);
            if (c3 == null || c4 == null || c5 == null)
                return false;

            Vector3 planeNormal = pianoRenderer.transform.up.normalized;
            Vector3 planeRight = pianoRenderer.transform.right.normalized;
            Vector3 planeUp = pianoRenderer.transform.forward.normalized;
            Vector3 origin = pianoRenderer.transform.position;
            Vector2 modelVectorLeft = new Vector2(
                Vector3.Dot(c4.transform.position - c3.transform.position, planeRight),
                Vector3.Dot(c4.transform.position - c3.transform.position, planeUp));
            Vector2 modelVectorRight = new Vector2(
                Vector3.Dot(c5.transform.position - c3.transform.position, planeRight),
                Vector3.Dot(c5.transform.position - c3.transform.position, planeUp));
            Vector2 targetVectorLeft = new Vector2(
                Vector3.Dot(targetC4 - targetC3, planeRight),
                Vector3.Dot(targetC4 - targetC3, planeUp));
            Vector2 targetVectorRight = new Vector2(
                Vector3.Dot(targetC5 - targetC3, planeRight),
                Vector3.Dot(targetC5 - targetC3, planeUp));
            if (modelVectorLeft.sqrMagnitude < 1e-6f ||
                modelVectorRight.sqrMagnitude < 1e-6f ||
                targetVectorLeft.sqrMagnitude < 1e-6f ||
                targetVectorRight.sqrMagnitude < 1e-6f)
                return false;

            float scale = (
                targetVectorLeft.magnitude / modelVectorLeft.magnitude +
                targetVectorRight.magnitude / modelVectorRight.magnitude) * 0.5f;
            float angle = (
                Vector2.SignedAngle(modelVectorLeft, targetVectorLeft) +
                Vector2.SignedAngle(modelVectorRight, targetVectorRight)) * 0.5f;
            vp.transform.localScale = new Vector3(
                vp.transform.localScale.x * scale,
                vp.transform.localScale.y,
                vp.transform.localScale.z * scale);
            vp.transform.rotation =
                Quaternion.AngleAxis(angle, planeNormal) * vp.transform.rotation;

            Vector3 transformedC3 = c3.transform.position;
            vp.transform.position += targetC3 - transformedC3;
            if (pianoRenderer != null)
                vp.transform.SetParent(pianoRenderer.transform, true);

            EssentialLog(
                $"Keyboard alignment: marker MIDI anchors 48,60,72 applied; " +
                $"image span={targetVectorRight.magnitude:F1}px.");
            return true;
        }

        private void DrawCalibrationMarker(
            Mat image,
            CalibrationMarkerCandidate candidate,
            Scalar color,
            string label)
        {
            Cv2.Rectangle(image, candidate.Bounds, color, 4);
            var center = new Point(
                Mathf.RoundToInt(candidate.Center.X),
                Mathf.RoundToInt(candidate.Center.Y));
            int radius = Mathf.Max(
                18,
                Mathf.RoundToInt(Mathf.Max(candidate.Bounds.Width, candidate.Bounds.Height) * 0.65f));
            Cv2.Circle(image, center, radius, color, 5);
            Cv2.Circle(image, center, 7, color, -1);
            Cv2.PutText(
                image,
                label,
                new Point(
                    candidate.Bounds.X,
                    Math.Max(30, candidate.Bounds.Y - 16)),
                HersheyFonts.HersheySimplex,
                1.1,
                color,
                3);
        }

        private void DrawCalibrationCandidateDiagnostic(
            Mat image,
            OpenCvSharp.Rect bounds)
        {
            Cv2.Rectangle(image, bounds, new Scalar(180, 180, 180), 2);
        }

        private bool TryDetectWhiteKeyLines(Mat src, OpenCvSharp.Rect outline, out float leftEdge, out float rightEdge, out float keySpacing, out int lineCount)
        {
            leftEdge = 0f;
            rightEdge = 0f;
            keySpacing = 0f;
            lineCount = 0;

            if (src == null || src.Empty() || outline.Width <= 0 || outline.Height <= 0)
                return false;

            VerboseLog(
                $"TryDetectWhiteKeyLines input region: x={outline.X}, y={outline.Y}, " +
                $"width={outline.Width}, height={outline.Height}; source={src.Width}x{src.Height}.");

            using var roi = new Mat(src, outline);
            using var gray = new Mat();
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 20, 80);

            Cv2.MinMaxLoc(edges, out double edgeMinimum, out double edgeMaximum);
            VerboseLog(
                $"White-key edge image: dimensions={edges.Width}x{edges.Height}, " +
                $"min={edgeMinimum:F1}, max={edgeMaximum:F1}, mean={Cv2.Mean(edges).Val0:F2}.");

            LineSegmentPoint[] lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180.0, 40, outline.Height * 0.2, 6);
            VerboseLog($"HoughLinesP found {(lines?.Length ?? 0)} raw lines in the keyboard ROI.");
            if (lines == null || lines.Length == 0)
                return false;

            var verticalX = new List<float>();
            foreach (var line in lines)
            {
                float dx = Math.Abs(line.P1.X - line.P2.X);
                float dy = Math.Abs(line.P1.Y - line.P2.Y);
                float xPosition = (line.P1.X + line.P2.X) * 0.5f;

                if (dy < outline.Height * 0.12f)
                    continue;

                float slope = dx / (dy + 1e-6f);
                if (slope < 0.5f)
                    verticalX.Add(xPosition);
            }

            VerboseLog($"White key line candidates after vertical filter: {verticalX.Count}");
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

            VerboseLog($"White key unique x positions: {uniqueX.Count}");
            VerboseLog(
                $"White-key accepted x positions (sorted, ROI coordinates): " +
                $"{string.Join(", ", uniqueX.ConvertAll(x => x.ToString("F1")))}");
            if (uniqueX.Count < 2)
                return false;

            leftEdge = uniqueX[0] + outline.X;
            rightEdge = uniqueX[^1] + outline.X;
            lineCount = uniqueX.Count;
            VerboseLog(
                $"White-key separator span: minX={uniqueX[0]:F1}, maxX={uniqueX[^1]:F1} " +
                $"in ROI; global leftEdge={leftEdge:F1}, rightEdge={rightEdge:F1}.");

            float totalSpacing = 0f;
            for (int i = 1; i < uniqueX.Count; i++)
            {
                totalSpacing += uniqueX[i] - uniqueX[i - 1];
            }

            keySpacing = totalSpacing / (uniqueX.Count - 1);
            VerboseLog(
                $"White-key structure result: lines={lineCount}, region-derived span=" +
                $"({leftEdge:F1}..{rightEdge:F1}), keySpacing={keySpacing:F1}.");
            return true;
        }

        private bool TryDetectKeyboardStructure(
            Mat src,
            out OpenCvSharp.Rect region,
            out float leftEdge,
            out float rightEdge,
            out float keySpacing,
            out int lineCount,
            out float angleDegrees)
        {
            region = new OpenCvSharp.Rect();
            leftEdge = 0f;
            rightEdge = 0f;
            keySpacing = 0f;
            lineCount = 0;
            angleDegrees = 0f;

            if (src == null || src.Empty())
                return false;

            VerboseLog(
                $"TryDetectKeyboardStructure input region: x=0, y=0, width={src.Width}, " +
                $"height={src.Height}; no previous structure region is reused.");

            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 20, 80);

            int minimumLength = Math.Max(30, Math.Min(src.Width, src.Height) / 10);
            Cv2.MinMaxLoc(edges, out double edgeMinimum, out double edgeMaximum);
            VerboseLog(
                $"Keyboard-structure edge image: dimensions={edges.Width}x{edges.Height}, " +
                $"min={edgeMinimum:F1}, max={edgeMaximum:F1}, mean={Cv2.Mean(edges).Val0:F2}; " +
                $"Hough minimumLength={minimumLength}.");
            LineSegmentPoint[] lines = Cv2.HoughLinesP(
                edges,
                1,
                Math.PI / 180.0,
                30,
                minimumLength,
                8);

            if (lines == null || lines.Length == 0)
                return false;

            var angleGroups = new List<List<LineSegmentPoint>>();
            foreach (var line in lines)
            {
                float dx = line.P2.X - line.P1.X;
                float dy = line.P2.Y - line.P1.Y;
                float length = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (length < minimumLength)
                    continue;

                List<LineSegmentPoint> matchingGroup = null;
                foreach (List<LineSegmentPoint> group in angleGroups)
                {
                    float groupAngle = GetAverageLineAngle(group);

                    if (GetUndirectedAngleDifference(angle, groupAngle) <= 10f)
                    {
                        matchingGroup = group;
                        break;
                    }
                }

                if (matchingGroup == null)
                {
                    matchingGroup = new List<LineSegmentPoint>();
                    angleGroups.Add(matchingGroup);
                }
                matchingGroup.Add(line);
            }

            List<LineSegmentPoint> bestLines = null;
            float bestAngle = 0f;
            float bestSpacing = 0f;
            float bestScore = 0f;

            foreach (List<LineSegmentPoint> group in angleGroups)
            {
                if (group.Count < 5)
                    continue;

                float groupAngle = GetAverageLineAngle(group);
                float radians = groupAngle * Mathf.Deg2Rad;
                Vector2 normal = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
                var projected = new List<KeyValuePair<float, LineSegmentPoint>>();
                foreach (LineSegmentPoint line in group)
                {
                    Vector2 midpoint = new Vector2(
                        (line.P1.X + line.P2.X) * 0.5f,
                        (line.P1.Y + line.P2.Y) * 0.5f);
                    projected.Add(new KeyValuePair<float, LineSegmentPoint>(
                        Vector2.Dot(midpoint, normal),
                        line));
                }
                projected.Sort((a, b) => a.Key.CompareTo(b.Key));

                var positions = new List<float>();
                var representativeLines = new List<LineSegmentPoint>();
                foreach (KeyValuePair<float, LineSegmentPoint> item in projected)
                {
                    if (positions.Count == 0 || item.Key - positions[positions.Count - 1] > 8f)
                    {
                        positions.Add(item.Key);
                        representativeLines.Add(item.Value);
                    }
                }

                if (positions.Count < 5)
                    continue;

                var gaps = new List<float>();
                for (int i = 1; i < positions.Count; i++)
                    gaps.Add(positions[i] - positions[i - 1]);

                float medianGap = GetMedian(gaps);
                if (medianGap <= 4f)
                    continue;

                int longestRunStart = 0;
                int longestRunLength = 1;
                int runStart = 0;
                for (int i = 1; i < gaps.Count; i++)
                {
                    if (gaps[i] < medianGap * 0.45f || gaps[i] > medianGap * 1.8f)
                    {
                        int runLength = i - runStart + 1;
                        if (runLength > longestRunLength)
                        {
                            longestRunStart = runStart;
                            longestRunLength = runLength;
                        }
                        runStart = i;
                    }
                }

                int finalRunLength = gaps.Count - runStart + 1;
                if (finalRunLength > longestRunLength)
                {
                    longestRunStart = runStart;
                    longestRunLength = finalRunLength;
                }

                if (longestRunLength < 5)
                    continue;

                float runSpacing = 0f;
                for (int i = longestRunStart + 1; i < longestRunStart + longestRunLength; i++)
                    runSpacing += positions[i] - positions[i - 1];
                runSpacing /= longestRunLength - 1;

                float spacingError = 0f;
                for (int i = longestRunStart + 1; i < longestRunStart + longestRunLength; i++)
                    spacingError += Mathf.Abs((positions[i] - positions[i - 1]) - runSpacing) / runSpacing;
                spacingError /= longestRunLength - 1;

                float score = longestRunLength * 10f - spacingError * 10f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLines = representativeLines.GetRange(longestRunStart, longestRunLength);
                    bestAngle = groupAngle;
                    bestSpacing = runSpacing;
                }
            }

            if (bestLines == null || bestLines.Count < 5)
            {
                VerboseLog("Keyboard structure result: no repeated separator-line group selected.");
                return false;
            }

            VerboseLog($"Keyboard structure initial selected separator count: {bestLines.Count}.");

            float bestRadians = bestAngle * Mathf.Deg2Rad;
            Vector2 separatorNormal = new Vector2(-Mathf.Sin(bestRadians), Mathf.Cos(bestRadians));
            Vector2 separatorDirection = new Vector2(Mathf.Cos(bestRadians), Mathf.Sin(bestRadians));
            var selectedProjections = new List<float>();
            float selectedTangentMin = float.MaxValue;
            float selectedTangentMax = float.MinValue;
            foreach (LineSegmentPoint line in bestLines)
            {
                Vector2 p1 = new Vector2(line.P1.X, line.P1.Y);
                Vector2 p2 = new Vector2(line.P2.X, line.P2.Y);
                selectedProjections.Add(Vector2.Dot((p1 + p2) * 0.5f, separatorNormal));
                selectedTangentMin = Mathf.Min(selectedTangentMin, Vector2.Dot(p1, separatorDirection), Vector2.Dot(p2, separatorDirection));
                selectedTangentMax = Mathf.Max(selectedTangentMax, Vector2.Dot(p1, separatorDirection), Vector2.Dot(p2, separatorDirection));
            }

            // Re-use all same-angle Hough evidence in the selected height band. The
            // selected run establishes the lattice; candidates outside it extend the
            // lattice without assuming a particular number of keys.
            var continuationCandidates = new List<KeyValuePair<float, LineSegmentPoint>>();
            float heightBandPadding = Mathf.Max(20f, src.Height * 0.08f);
            foreach (List<LineSegmentPoint> group in angleGroups)
            {
                foreach (LineSegmentPoint line in group)
                {
                    float lineAngle = Mathf.Atan2(line.P2.Y - line.P1.Y, line.P2.X - line.P1.X) * Mathf.Rad2Deg;
                    if (GetUndirectedAngleDifference(lineAngle, bestAngle) > 10f)
                        continue;

                    Vector2 p1 = new Vector2(line.P1.X, line.P1.Y);
                    Vector2 p2 = new Vector2(line.P2.X, line.P2.Y);
                    float tangentMin = Mathf.Min(Vector2.Dot(p1, separatorDirection), Vector2.Dot(p2, separatorDirection));
                    float tangentMax = Mathf.Max(Vector2.Dot(p1, separatorDirection), Vector2.Dot(p2, separatorDirection));
                    if (tangentMax < selectedTangentMin - heightBandPadding ||
                        tangentMin > selectedTangentMax + heightBandPadding)
                        continue;

                    float projection = Vector2.Dot((p1 + p2) * 0.5f, separatorNormal);
                    continuationCandidates.Add(new KeyValuePair<float, LineSegmentPoint>(projection, line));
                }
            }

            continuationCandidates.Sort((a, b) => a.Key.CompareTo(b.Key));
            var uniqueCandidates = new List<KeyValuePair<float, LineSegmentPoint>>();
            foreach (KeyValuePair<float, LineSegmentPoint> candidate in continuationCandidates)
            {
                if (uniqueCandidates.Count == 0 ||
                    candidate.Key - uniqueCandidates[^1].Key > 8f)
                {
                    uniqueCandidates.Add(candidate);
                }
            }

            selectedProjections.Sort();
            float selectedProjectionMin = selectedProjections[0];
            float selectedProjectionMax = selectedProjections[^1];
            var recoveredLines = new List<LineSegmentPoint>(bestLines);
            var recoveredProjections = new List<float>(selectedProjections);
            RecoverSeparatorContinuations(
                uniqueCandidates,
                selectedProjectionMin,
                selectedProjectionMax,
                bestSpacing,
                recoveredLines,
                recoveredProjections);

            recoveredProjections.Sort();
            var recoveredPoints = new List<OpenCvSharp.Point>();
            foreach (LineSegmentPoint line in recoveredLines)
            {
                recoveredPoints.Add(line.P1);
                recoveredPoints.Add(line.P2);
            }

            OpenCvSharp.Rect lineBounds = Cv2.BoundingRect(recoveredPoints);
            region = ExpandAndClampRect(lineBounds, src.Width, src.Height, 0.15f, 0.35f);
            VerboseLog(
                $"Keyboard structure region calculation: selectedLines={bestLines.Count}, recoveredLines={recoveredLines.Count}, " +
                $"lineBounds=({lineBounds.X},{lineBounds.Y},{lineBounds.Width},{lineBounds.Height}), " +
                $"expanded/clamped region=({region.X},{region.Y},{region.Width},{region.Height}), " +
                $"horizontalExpansion=15%, verticalExpansion=35%.");

            var recoveredLineXPositions = new List<float>();
            foreach (LineSegmentPoint line in recoveredLines)
            {
                recoveredLineXPositions.Add((line.P1.X + line.P2.X) * 0.5f);
            }

            recoveredLineXPositions.Sort();
            leftEdge = recoveredLineXPositions[0];
            rightEdge = recoveredLineXPositions[^1];
            keySpacing = bestSpacing;
            lineCount = recoveredLines.Count;
            angleDegrees = bestAngle;
            LogWhiteSeparatorDiagnostics(recoveredLines, recoveredLineXPositions, bestSpacing, bestAngle);
            VerboseLog(
                $"Keyboard structure selected separator group: angle={angleDegrees:F1} degrees, " +
                $"xPositions={string.Join(", ", recoveredLineXPositions.ConvertAll(x => x.ToString("F1")))}, " +
                $"left={leftEdge:F1}, right={rightEdge:F1}, spacing={keySpacing:F1}, lineCount={lineCount}.");
            VerboseLog(
                $"Keyboard structure recovered/extended separator count: {recoveredLines.Count}; " +
                $"final extent={leftEdge:F1}..{rightEdge:F1}; final spacing={keySpacing:F1}.");

            using (var debugImage = src.Clone())
            {
                foreach (LineSegmentPoint line in recoveredLines)
                {
                    Cv2.Line(debugImage, line.P1, line.P2, new Scalar(0, 255, 0), 4);
                    var midpoint = new Point(
                        (line.P1.X + line.P2.X) / 2,
                        (line.P1.Y + line.P2.Y) / 2);
                    Cv2.Circle(debugImage, midpoint, 7, new Scalar(0, 0, 255), -1);
                }

                Cv2.Rectangle(debugImage, region, new Scalar(255, 0, 255), 5);
                DisplayDebugMat(debugImage, "KeyboardStructureDebugPreview", 1200, 600);
            }
            VerboseLog($"Displayed keyboard structure debug: {recoveredLines.Count} final separator lines.");

            return region.Width > 0 && region.Height > 0;
        }

        private void LogWhiteSeparatorDiagnostics(
            List<LineSegmentPoint> lines,
            List<float> xPositions,
            float globalSpacing,
            float referenceAngle)
        {
            if (lines == null || xPositions == null || xPositions.Count < 2)
                return;

            var sortedLines = new List<LineSegmentPoint>(lines);
            sortedLines.Sort((a, b) =>
                ((a.P1.X + a.P2.X) * 0.5f).CompareTo((b.P1.X + b.P2.X) * 0.5f));

            var gaps = new List<float>();
            for (int i = 1; i < xPositions.Count; i++)
                gaps.Add(xPositions[i] - xPositions[i - 1]);
            float medianGap = GetMedian(gaps);
            float smallGapThreshold = Mathf.Max(6f, medianGap * 0.55f);
            float largeGapThreshold = medianGap * 1.55f;

            var lineDiagnostics = new List<string>();
            for (int i = 0; i < sortedLines.Count; i++)
            {
                LineSegmentPoint line = sortedLines[i];
                float x = (line.P1.X + line.P2.X) * 0.5f;
                float angle = Mathf.Atan2(line.P2.Y - line.P1.Y, line.P2.X - line.P1.X) * Mathf.Rad2Deg;
                float previousGap = i > 0 ? x - xPositions[i - 1] : 0f;
                float nextGap = i + 1 < xPositions.Count ? xPositions[i + 1] - x : 0f;
                lineDiagnostics.Add(
                    $"x={x:F1},angle={angle:F1},prev={previousGap:F1},next={nextGap:F1}");
            }

            float indexMean = (xPositions.Count - 1) * 0.5f;
            float xMean = 0f;
            foreach (float x in xPositions)
                xMean += x;
            xMean /= xPositions.Count;

            float covariance = 0f;
            float indexVariance = 0f;
            for (int i = 0; i < xPositions.Count; i++)
            {
                float indexDelta = i - indexMean;
                covariance += indexDelta * (xPositions[i] - xMean);
                indexVariance += indexDelta * indexDelta;
            }

            float trendSpacing = indexVariance > 0f ? covariance / indexVariance : medianGap;
            float trendIntercept = xMean - trendSpacing * indexMean;
            var gapDiagnostics = new List<string>();
            for (int i = 0; i < gaps.Count; i++)
            {
                float predictedGap = trendSpacing;
                float gap = gaps[i];
                string classification;
                if (gap < smallGapThreshold)
                    classification = "suspicious-small/duplicate";
                else if (gap > largeGapThreshold)
                    classification = "suspicious-large/missing-or-outlier";
                else
                    classification = "consistent";

                gapDiagnostics.Add(
                    $"{xPositions[i]:F1}->{xPositions[i + 1]:F1}={gap:F1}" +
                    $"(trend={predictedGap:F1},{classification})");
            }

            float leftTrendGap = trendSpacing;
            float rightTrendGap = trendSpacing;
            if (xPositions.Count >= 4)
            {
                leftTrendGap = (xPositions[2] - xPositions[0]) * 0.5f;
                int last = xPositions.Count - 1;
                rightTrendGap = (xPositions[last] - xPositions[last - 2]) * 0.5f;
            }

            VerboseLog(
                $"White separator diagnostics: referenceAngle={referenceAngle:F1}, " +
                $"globalSpacing={globalSpacing:F1}, medianGap={medianGap:F1}, " +
                $"small<{smallGapThreshold:F1}, large>{largeGapThreshold:F1}.");
            VerboseLog($"White separator lines: {string.Join("; ", lineDiagnostics)}");
            VerboseLog($"White separator gaps: {string.Join("; ", gapDiagnostics)}");
            VerboseLog(
                $"White separator local spacing trend: fitted={trendSpacing:F2}px/line, " +
                $"left={leftTrendGap:F2}px, right={rightTrendGap:F2}px, " +
                $"slope={(rightTrendGap - leftTrendGap):F2}px across image; " +
                $"modelX0={trendIntercept:F1}.");
        }

        private void RecoverSeparatorContinuations(
            List<KeyValuePair<float, LineSegmentPoint>> candidates,
            float selectedMin,
            float selectedMax,
            float spacing,
            List<LineSegmentPoint> recoveredLines,
            List<float> recoveredProjections)
        {
            if (candidates == null || candidates.Count == 0 || spacing <= 4f)
                return;

            RecoverSeparatorContinuationsInDirection(
                candidates,
                selectedMin,
                -1f,
                spacing,
                recoveredLines,
                recoveredProjections);
            RecoverSeparatorContinuationsInDirection(
                candidates,
                selectedMax,
                1f,
                spacing,
                recoveredLines,
                recoveredProjections);
        }

        private void RecoverSeparatorContinuationsInDirection(
            List<KeyValuePair<float, LineSegmentPoint>> candidates,
            float startingProjection,
            float direction,
            float spacing,
            List<LineSegmentPoint> recoveredLines,
            List<float> recoveredProjections)
        {
            float frontier = startingProjection;
            int missingSteps = 0;
            while (missingSteps < 2)
            {
                float expected = frontier + direction * spacing;
                KeyValuePair<float, LineSegmentPoint>? bestCandidate = null;
                float bestDistance = float.MaxValue;

                foreach (KeyValuePair<float, LineSegmentPoint> candidate in candidates)
                {
                    float delta = (candidate.Key - expected) * direction;
                    if (delta < -spacing * 0.45f || delta > spacing * 0.55f)
                        continue;

                    bool alreadyRecovered = false;
                    foreach (float recoveredProjection in recoveredProjections)
                    {
                        if (Mathf.Abs(recoveredProjection - candidate.Key) <= 8f)
                        {
                            alreadyRecovered = true;
                            break;
                        }
                    }

                    if (!alreadyRecovered && Mathf.Abs(candidate.Key - expected) < bestDistance)
                    {
                        bestCandidate = candidate;
                        bestDistance = Mathf.Abs(candidate.Key - expected);
                    }
                }

                if (bestCandidate.HasValue)
                {
                    recoveredLines.Add(bestCandidate.Value.Value);
                    recoveredProjections.Add(bestCandidate.Value.Key);
                    frontier = bestCandidate.Value.Key;
                    missingSteps = 0;
                }
                else
                {
                    frontier = expected;
                    missingSteps++;
                }
            }
        }

        private float GetAverageLineAngle(List<LineSegmentPoint> lines)
        {
            if (lines == null || lines.Count == 0)
                return 0f;

            float sin = 0f;
            float cos = 0f;
            foreach (LineSegmentPoint line in lines)
            {
                float angle = Mathf.Atan2(line.P2.Y - line.P1.Y, line.P2.X - line.P1.X) * 2f;
                sin += Mathf.Sin(angle);
                cos += Mathf.Cos(angle);
            }

            return Mathf.Repeat(0.5f * Mathf.Atan2(sin, cos) * Mathf.Rad2Deg, 180f);
        }

        private float GetUndirectedAngleDifference(float first, float second)
        {
            float difference = Mathf.Abs(first - second) % 180f;
            return difference > 90f ? 180f - difference : difference;
        }

        private bool TryInferFirstVisibleMidiNote(
            List<OpenCvSharp.Rect> blackKeyRects,
            int regionX,
            float leftEdge,
            float whiteKeySpacing,
            int visibleWhiteKeyCount,
            out int firstMidiNote)
        {
            firstMidiNote = 0;
            if (blackKeyRects == null || blackKeyRects.Count < 3 ||
                whiteKeySpacing <= 0f || visibleWhiteKeyCount <= 0)
                return false;

            if (!TryVerifyPianoPattern(blackKeyRects, whiteKeySpacing, out string patternDescription))
            {
                VerboseWarning($"Black-key pattern is not reliable enough for pitch tracking: {patternDescription}");
                return false;
            }

            var centers = new List<float>();
            foreach (OpenCvSharp.Rect rect in blackKeyRects)
                centers.Add(regionX + rect.X + rect.Width * 0.5f);
            centers.Sort();

            var gaps = new List<float>();
            for (int i = 1; i < centers.Count; i++)
                gaps.Add(centers[i] - centers[i - 1]);

            float medianGap = GetMedian(gaps);
            if (medianGap <= 0f)
                return false;

            var groups = new List<int>();
            int currentGroup = 1;
            foreach (float gap in gaps)
            {
                if (gap > medianGap * 1.5f)
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

            VerboseLog($"Detected black keys: {centers.Count}");
            VerboseLog($"Black key centers (ordered): {string.Join(", ", centers.ConvertAll(center => center.ToString("F1")))}");
            VerboseLog($"Detected black-key groups: {string.Join(",", groups)}");

            bool hasTwoGroup = groups.Contains(2);
            bool hasThreeGroup = groups.Contains(3);
            if (!hasTwoGroup && !hasThreeGroup)
                return false;

            int[] whitePitchClasses = { 0, 2, 4, 5, 7, 9, 11 };
            string[] whiteNoteNames = { "C", "D", "E", "F", "G", "A", "B" };
            float bestError = float.PositiveInfinity;
            float secondBestError = float.PositiveInfinity;
            int bestPhase = -1;

            for (int phase = 0; phase < whitePitchClasses.Length; phase++)
            {
                float totalError = 0f;
                foreach (float center in centers)
                {
                    float normalizedPosition = (center - leftEdge) / whiteKeySpacing;
                    float nearestError = float.PositiveInfinity;
                    for (int whiteIndex = 0; whiteIndex < visibleWhiteKeyCount; whiteIndex++)
                    {
                        int notePhase = (phase + whiteIndex) % 7;
                        if (notePhase != 0 && notePhase != 1 && notePhase != 3 && notePhase != 4 && notePhase != 5)
                            continue;

                        float expectedPosition = whiteIndex + 0.5f;
                        nearestError = Mathf.Min(nearestError, Mathf.Abs(normalizedPosition - expectedPosition));
                    }

                    totalError += nearestError;
                }

                float averageError = totalError / centers.Count;
                if (averageError < bestError)
                {
                    secondBestError = bestError;
                    bestError = averageError;
                    bestPhase = phase;
                }
                else if (averageError < secondBestError)
                {
                    secondBestError = averageError;
                }
            }

            if (bestPhase < 0 || bestError > 0.45f ||
                float.IsInfinity(secondBestError) ||
                secondBestError - bestError < 0.08f)
                return false;

            VerboseLog(
                $"Detected relative keyboard phase: {whiteNoteNames[bestPhase]} " +
                $"(score={bestError:F2}, margin={(secondBestError - bestError):F2}); " +
                "absolute octave unresolved from image evidence.");
            return false;
        }

        private int FindNearestMidiForWhitePhase(int referenceMidiNote, int pitchClass)
        {
            int bestMidi = Mathf.Clamp(referenceMidiNote, 21, 108);
            int bestDistance = int.MaxValue;
            for (int midi = 21; midi <= 108; midi++)
            {
                if (midi % 12 != pitchClass)
                    continue;

                int distance = Mathf.Abs(midi - referenceMidiNote);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestMidi = midi;
                }
            }

            return bestMidi;
        }

        private int GetLastVisibleMidiNote(int firstMidiNote, int visibleWhiteKeyCount)
        {
            int midi = Mathf.Clamp(firstMidiNote, 21, 108);
            int remainingWhiteKeys = visibleWhiteKeyCount;
            while (midi <= 108 && remainingWhiteKeys > 0)
            {
                if (!IsBlackMidiNote(midi))
                    remainingWhiteKeys--;
                midi++;
            }

            return Mathf.Min(108, midi - 1);
        }

        private bool IsBlackMidiNote(int midi)
        {
            int pitchClass = ((midi % 12) + 12) % 12;
            return pitchClass == 1 || pitchClass == 3 || pitchClass == 6 ||
                   pitchClass == 8 || pitchClass == 10;
        }

        private OpenCvSharp.Rect ExpandAndClampRect(OpenCvSharp.Rect rect, int width, int height, float horizontalExpansion, float verticalExpansion)
        {
            int expandX = Mathf.RoundToInt(rect.Width * horizontalExpansion);
            int expandY = Mathf.RoundToInt(rect.Height * verticalExpansion);
            int x = Mathf.Max(0, rect.X - expandX);
            int y = Mathf.Max(0, rect.Y - expandY);
            int right = Mathf.Min(width, rect.X + rect.Width + expandX);
            int bottom = Mathf.Min(height, rect.Y + rect.Height + expandY);
            return new OpenCvSharp.Rect(x, y, right - x, bottom - y);
        }

        private bool TryDetectBlackKeys(
            Mat src,
            OpenCvSharp.Rect outline,
            float whiteKeySpacing,
            out int blackKeyCount,
            out float averageAspect,
            out List<OpenCvSharp.Rect> blackKeyRects)
        {
            blackKeyCount = 0;
            averageAspect = 0f;
            blackKeyRects = new List<OpenCvSharp.Rect>();

            if (src == null || src.Empty())
                return false;

            OpenCvSharp.Rect keybedRegion = FindFullFrameKeyboardBand(src);
            if (keybedRegion.Width <= 0 || keybedRegion.Height <= 0)
                return false;

            using var roi = new Mat(src, keybedRegion);
            using var gray = new Mat();
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            using var blackHat = new Mat();
            using var blackHatKernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new Size(Math.Max(9, keybedRegion.Width / 80), Math.Max(15, keybedRegion.Height / 3)));
            Cv2.MorphologyEx(blurred, blackHat, MorphTypes.BlackHat, blackHatKernel);
            using var thresh = new Mat();
            Cv2.Threshold(blackHat, thresh, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            using var cleanupKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 5));
            Cv2.MorphologyEx(thresh, thresh, MorphTypes.Open, cleanupKernel);
            Cv2.MorphologyEx(thresh, thresh, MorphTypes.Close, cleanupKernel);

            LogBlackKeyMaskDiagnostics(thresh, keybedRegion);
            Cv2.FindContours(thresh, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            int rejectedContourCount = 0;
            int rejectedWidthCount = 0;
            int rejectedShortHeightCount = 0;
            int rejectedAspectCount = 0;
            int rejectedHeightRangeCount = 0;
            int rejectedWideCount = 0;
            int rejectedSeparatorCount = 0;
            var rejectedRects = new List<OpenCvSharp.Rect>();
            var acceptedGeometry = new List<string>();
            var contourGeometry = new List<string>();

            foreach (Point[] contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                float aspect = rect.Width / (float)rect.Height;
                double contourArea = Cv2.ContourArea(contour);
                double boxArea = Math.Max(1, rect.Width * rect.Height);
                float fillRatio = (float)(contourArea / boxArea);
                float minimumHeight = keybedRegion.Height * 0.12f;
                float maximumHeight = keybedRegion.Height * 0.9f;
                float maximumWidth = keybedRegion.Width * 0.045f;
                float centerY = rect.Y + rect.Height * 0.5f;
                bool touchesBandTop = rect.Y <= 1;
                bool narrowShortTopArtifact = touchesBandTop &&
                    rect.Width <= Mathf.Max(6, Mathf.RoundToInt(whiteKeySpacing * 0.3f)) &&
                    rect.Height <= Mathf.Max(28, Mathf.RoundToInt(keybedRegion.Height * 0.28f));
                string rejectionReason = "accepted";

                if (rect.Width < 5)
                    rejectedWidthCount++;
                if (rect.Height < 10)
                    rejectedShortHeightCount++;
                if (aspect > 0.7f)
                    rejectedAspectCount++;
                if (aspect < 0.08f)
                    rejectedAspectCount++;
                if (rect.Height < minimumHeight)
                    rejectedHeightRangeCount++;
                if (rect.Height > maximumHeight)
                    rejectedHeightRangeCount++;
                if (rect.Width > maximumWidth)
                    rejectedWideCount++;
                if (narrowShortTopArtifact)
                    rejectedSeparatorCount++;

                bool rejected = rect.Width < 4 ||
                    rect.Height < 10 ||
                    aspect > 0.7f ||
                    aspect < 0.03f ||
                    rect.Height < minimumHeight ||
                    rect.Height > maximumHeight ||
                    rect.Width > maximumWidth ||
                    centerY < keybedRegion.Height * 0.05f ||
                    centerY > keybedRegion.Height * 0.95f ||
                    narrowShortTopArtifact;
                if (narrowShortTopArtifact)
                    rejectionReason = "separator-like top-touching narrow/short body";
                else if (rect.Width < 4)
                    rejectionReason = "too narrow";
                else if (rect.Height < 10)
                    rejectionReason = "too short";
                else if (aspect > 0.7f || aspect < 0.03f)
                    rejectionReason = "aspect";
                else if (rect.Height < minimumHeight || rect.Height > maximumHeight)
                    rejectionReason = "height range";
                else if (rect.Width > maximumWidth)
                    rejectionReason = "too wide";
                else if (centerY < keybedRegion.Height * 0.05f ||
                    centerY > keybedRegion.Height * 0.95f)
                    rejectionReason = "center outside band";

                contourGeometry.Add(
                    $"x={rect.X + rect.Width * 0.5f:F1},y={rect.Y},w={rect.Width}," +
                    $"h={rect.Height},area={contourArea:F0},fill={fillRatio:F2}," +
                    $"top={touchesBandTop},reason={rejectionReason}");
                if (rejected)
                {
                    rejectedContourCount++;
                    rejectedRects.Add(rect);
                    continue;
                }

                blackKeyRects.Add(rect);
                acceptedGeometry.Add(
                    $"x={rect.X + rect.Width * 0.5f:F1},w={rect.Width},h={rect.Height},fill={fillRatio:F2}");
            }

            VerboseLog(
                $"Black-key detection: {contours.Length} contours found. " +
                $"Black-key filtering: {rejectedContourCount} rejected, {blackKeyRects.Count} accepted " +
                $"(width={rejectedWidthCount}, shortHeight={rejectedShortHeightCount}, " +
                $"aspect={rejectedAspectCount}, heightRange={rejectedHeightRangeCount}, " +
                $"wide={rejectedWideCount}, separatorLike={rejectedSeparatorCount}).");
            VerboseLog($"Black-key contour geometry: {string.Join("; ", contourGeometry)}");
            int rawCandidateCount = blackKeyRects.Count;
            float blackKeySpacing = EstimateBlackKeyCenterSpacing(blackKeyRects, whiteKeySpacing);
            blackKeyRects = DeduplicateBlackKeyRects(blackKeyRects, blackKeySpacing);
            int deduplicatedCandidateCount = blackKeyRects.Count;
            blackKeySpacing = EstimateBlackKeyCenterSpacing(blackKeyRects, blackKeySpacing);
            LogSortedBlackKeyCandidates("deduplicated", blackKeyRects, blackKeySpacing);
            blackKeyRects = FilterPlausibleBlackKeyRects(blackKeyRects, blackKeySpacing);
            VerboseLog(
                $"Black-key candidates: raw={rawCandidateCount}, deduplicated={deduplicatedCandidateCount}, " +
                $"validated={blackKeyRects.Count}, " +
                $"localSpacing={blackKeySpacing:F1}px (white-support={whiteKeySpacing:F1}px).");
            VerboseLog($"Black-key accepted geometry: {string.Join("; ", acceptedGeometry)}");
            LogSortedBlackKeyCandidates("validated", blackKeyRects, blackKeySpacing);
            AnalyzeBlackKeyCandidatesInWhiteKeyCoordinates(blackKeyRects, whiteKeySpacing);
            DisplayBlackKeyPatternDebug(src, keybedRegion, blackKeyRects, rejectedRects, blackKeySpacing);
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

        private void AnalyzeBlackKeyCandidatesInWhiteKeyCoordinates(
            List<OpenCvSharp.Rect> blackKeyRects,
            float whiteKeySpacing)
        {
            if (blackKeyRects == null || blackKeyRects.Count == 0 || whiteKeySpacing <= 0f)
                return;

            var ordered = new List<OpenCvSharp.Rect>(blackKeyRects);
            ordered.Sort((a, b) => a.X.CompareTo(b.X));
            float referenceX = ordered[0].X + ordered[0].Width * 0.5f;
            var positions = new List<float>();
            var gaps = new List<float>();
            var interpretation = new List<string>();

            for (int i = 0; i < ordered.Count; i++)
            {
                float center = ordered[i].X + ordered[i].Width * 0.5f;
                float normalizedPosition = (center - referenceX) / whiteKeySpacing;
                positions.Add(normalizedPosition);

                if (i == 0)
                    continue;

                float normalizedGap = normalizedPosition - positions[i - 1];
                gaps.Add(normalizedGap);
                string classification;
                if (normalizedGap < 0.7f)
                    classification = "suspicious-close/duplicate";
                else if (normalizedGap < 1.35f)
                    classification = "within-group~1";
                else if (normalizedGap < 2.45f)
                    classification = "group-gap~2";
                else if (normalizedGap < 3.55f)
                    classification = "possible-missing-key~3";
                else
                    classification = "large-gap/multiple-missing";

                interpretation.Add(
                    $"{ordered[i - 1].X + ordered[i - 1].Width * 0.5f:F1}->" +
                    $"{center:F1}: {normalizedGap:F2} ({classification})");
            }

            VerboseLog(
                $"Black-key white-coordinate positions: refX={referenceX:F1}, " +
                $"whiteSpacing={whiteKeySpacing:F1}, " +
                $"{string.Join(", ", positions.ConvertAll(position => position.ToString("F2")))}");
            VerboseLog(
                $"Black-key white-coordinate gaps: " +
                $"{string.Join(", ", gaps.ConvertAll(gap => gap.ToString("F2")))}");
            VerboseLog(
                $"Black-key white-coordinate interpretation: " +
                $"{string.Join("; ", interpretation)}");

            var compactPattern = new List<string>();
            foreach (float gap in gaps)
            {
                if (gap < 0.7f)
                    compactPattern.Add("D");
                else if (gap < 1.35f)
                    compactPattern.Add("1");
                else if (gap < 2.45f)
                    compactPattern.Add("2");
                else if (gap < 3.55f)
                    compactPattern.Add("3?");
                else
                    compactPattern.Add("N");
            }

            VerboseLog(
                $"Black-key white-coordinate pattern diagnostic: " +
                $"{string.Join(" ", compactPattern)}; " +
                "diagnostic only, no candidates or groups changed.");
        }

        private OpenCvSharp.Rect FindFullFrameKeyboardBand(Mat src)
        {
            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(9, 9), 0);

            var rowMeans = new double[src.Height];
            for (int y = 0; y < src.Height; y++)
                rowMeans[y] = Cv2.Mean(blurred.Row(y)).Val0;

            var rowDifferences = new List<float>();
            for (int y = 1; y < rowMeans.Length; y++)
                rowDifferences.Add((float)(rowMeans[y] - rowMeans[y - 1]));

            int transitionY = src.Height / 2;
            float strongestTransition = float.MinValue;
            int searchStart = Mathf.Max(20, src.Height / 10);
            int searchEnd = Mathf.Min(src.Height - 20, src.Height * 4 / 5);
            for (int y = searchStart; y < searchEnd; y++)
            {
                float transition = rowDifferences[y - 1];
                if (transition > strongestTransition)
                {
                    strongestTransition = transition;
                    transitionY = y;
                }
            }

            float whiteLevel = float.MinValue;
            for (int y = transitionY; y < src.Height; y++)
                whiteLevel = Mathf.Max(whiteLevel, (float)rowMeans[y]);

            float whiteThreshold = whiteLevel * 0.82f;
            int whiteStart = transitionY;
            while (whiteStart < src.Height && rowMeans[whiteStart] < whiteThreshold)
                whiteStart++;

            int whiteEnd = whiteStart;
            while (whiteEnd < src.Height && rowMeans[whiteEnd] >= whiteThreshold)
                whiteEnd++;

            int whiteHeight = Mathf.Max(30, whiteEnd - whiteStart);
            int upper = Mathf.Max(0, whiteStart - Mathf.RoundToInt(whiteHeight * 1.55f));
            int lower = Mathf.Min(src.Height, whiteStart + Mathf.RoundToInt(whiteHeight * 0.18f));
            if (lower <= upper)
                lower = Mathf.Min(src.Height, upper + Mathf.Max(40, src.Height / 6));

            var region = new OpenCvSharp.Rect(0, upper, src.Width, Math.Max(1, lower - upper));
            VerboseLog($"Black-key search band: x=0 y={region.Y} w={region.Width} h={region.Height}.");
            return region;
        }

        private void LogBlackKeyMaskDiagnostics(Mat mask, OpenCvSharp.Rect roi)
        {
            if (mask == null || mask.Empty())
            {
                VerboseWarning("Black-key mask diagnostics: mask is null or empty.");
                return;
            }

            Cv2.MinMaxLoc(mask, out double minimum, out double maximum);
            double mean = Cv2.Mean(mask).Val0;
            int nonZeroPixels = Cv2.CountNonZero(mask);
            VerboseLog(
                $"Black-key mask: roi=({roi.X},{roi.Y},{roi.Width},{roi.Height}), " +
                $"dimensions={mask.Width}x{mask.Height}, type={mask.Type()}, channels={mask.Channels()}, " +
                $"min={minimum:F2}, max={maximum:F2}, mean={mean:F2}, nonZeroPixels={nonZeroPixels}");

            DisplayDebugMat(mask, "BlackKeyMaskDebugPreview", 320, 180);
        }

        private void DisplayBlackKeyPatternDebug(
            Mat src,
            OpenCvSharp.Rect region,
            List<OpenCvSharp.Rect> acceptedRects,
            List<OpenCvSharp.Rect> rejectedRects,
            float whiteKeySpacing)
        {
            if (src == null || src.Empty())
                return;

            using var debugImage = src.Clone();
            foreach (OpenCvSharp.Rect rect in rejectedRects)
            {
                var globalRect = new OpenCvSharp.Rect(
                    region.X + rect.X,
                    region.Y + rect.Y,
                    rect.Width,
                    rect.Height);
                Cv2.Rectangle(debugImage, globalRect, new Scalar(0, 0, 255), 2);
            }

            var groups = GetBlackKeyGroups(acceptedRects, whiteKeySpacing);
            for (int i = 0; i < acceptedRects.Count; i++)
            {
                OpenCvSharp.Rect rect = acceptedRects[i];
                int centerX = region.X + rect.X + rect.Width / 2;
                int centerY = region.Y + rect.Y + rect.Height / 2;
                Cv2.Rectangle(
                    debugImage,
                    new OpenCvSharp.Rect(region.X + rect.X, region.Y + rect.Y, rect.Width, rect.Height),
                    new Scalar(0, 255, 0),
                    3);
                Cv2.Circle(debugImage, new Point(centerX, centerY), 6, new Scalar(255, 255, 0), -1);
            }

            foreach (List<OpenCvSharp.Rect> group in groups)
            {
                if (group.Count == 0)
                    continue;

                int labelX = region.X + group[0].X;
                int labelY = Math.Max(20, region.Y + group[0].Y - 8);
                Cv2.Line(
                    debugImage,
                    new Point(labelX, region.Y),
                    new Point(labelX, region.Y + region.Height),
                    new Scalar(0, 255, 255),
                    2);
                Cv2.PutText(
                    debugImage,
                    group.Count.ToString(),
                    new Point(labelX, labelY),
                    HersheyFonts.HersheySimplex,
                    0.8,
                    new Scalar(0, 255, 255),
                    2);
            }

            int axisY = region.Y + region.Height / 2;
            Cv2.Line(
                debugImage,
                new Point(region.X, axisY),
                new Point(region.X + region.Width, axisY),
                new Scalar(255, 0, 0),
                2);
            Cv2.Rectangle(debugImage, region, new Scalar(255, 0, 255), 4);
            DisplayDebugMat(debugImage, "BlackKeyPatternDebugPreview", 1500, 750);
            VerboseLog(
                $"Black-key pattern debug: accepted={acceptedRects.Count}, rejected={rejectedRects.Count}, " +
                $"groups={string.Join(",", groups.ConvertAll(group => group.Count.ToString()))}.");
        }

        private void DisplayVerifiedBlackKeyGeometry(
            Mat src,
            List<OpenCvSharp.Rect> blackKeyRects)
        {
            if (src == null || src.Empty() || blackKeyRects == null || blackKeyRects.Count < 2)
                return;

            var ordered = new List<OpenCvSharp.Rect>(blackKeyRects);
            ordered.Sort((a, b) => a.X.CompareTo(b.X));
            var centers = new List<Point2f>();
            foreach (OpenCvSharp.Rect rect in ordered)
            {
                centers.Add(new Point2f(
                    rect.X + rect.Width * 0.5f,
                    rect.Y + rect.Height * 0.5f));
            }

            float meanX = 0f;
            float meanY = 0f;
            foreach (Point2f center in centers)
            {
                meanX += center.X;
                meanY += center.Y;
            }
            meanX /= centers.Count;
            meanY /= centers.Count;

            float covariance = 0f;
            float varianceX = 0f;
            foreach (Point2f center in centers)
            {
                float dx = center.X - meanX;
                covariance += dx * (center.Y - meanY);
                varianceX += dx * dx;
            }

            float slope = varianceX > 0.01f ? covariance / varianceX : 0f;
            float angle = Mathf.Atan2(slope, 1f) * Mathf.Rad2Deg;
            Vector2 tangent = new Vector2(1f, slope).normalized;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            float tangentMin = float.MaxValue;
            float tangentMax = float.MinValue;
            float normalMin = float.MaxValue;
            float normalMax = float.MinValue;

            foreach (OpenCvSharp.Rect rect in ordered)
            {
                var corners = new[]
                {
                    new Vector2(rect.Left, rect.Top),
                    new Vector2(rect.Right, rect.Top),
                    new Vector2(rect.Right, rect.Bottom),
                    new Vector2(rect.Left, rect.Bottom),
                };
                foreach (Vector2 corner in corners)
                {
                    tangentMin = Mathf.Min(tangentMin, Vector2.Dot(corner, tangent));
                    tangentMax = Mathf.Max(tangentMax, Vector2.Dot(corner, tangent));
                    normalMin = Mathf.Min(normalMin, Vector2.Dot(corner, normal));
                    normalMax = Mathf.Max(normalMax, Vector2.Dot(corner, normal));
                }
            }

            OpenCvSharp.Rect keybedBand = FindFullFrameKeyboardBand(src);
            foreach (Vector2 point in new[]
            {
                new Vector2(keybedBand.Left, keybedBand.Top),
                new Vector2(keybedBand.Right, keybedBand.Top),
                new Vector2(keybedBand.Right, keybedBand.Bottom),
                new Vector2(keybedBand.Left, keybedBand.Bottom),
            })
            {
                normalMin = Mathf.Min(normalMin, Vector2.Dot(point, normal));
                normalMax = Mathf.Max(normalMax, Vector2.Dot(point, normal));
            }

            Vector2 cornerA = tangent * tangentMin + normal * normalMin;
            Vector2 cornerB = tangent * tangentMax + normal * normalMin;
            Vector2 cornerC = tangent * tangentMax + normal * normalMax;
            Vector2 cornerD = tangent * tangentMin + normal * normalMax;
            var quad = new[]
            {
                ToOpenCvPoint(cornerA),
                ToOpenCvPoint(cornerB),
                ToOpenCvPoint(cornerC),
                ToOpenCvPoint(cornerD),
            };

            float robustSpacing = EstimateBlackKeyCenterSpacing(ordered, 0f);
            VerboseLog(
                $"Verified black-key geometry: corners=" +
                $"({cornerA.x:F1},{cornerA.y:F1}),({cornerB.x:F1},{cornerB.y:F1})," +
                $"({cornerC.x:F1},{cornerC.y:F1}),({cornerD.x:F1},{cornerD.y:F1}); " +
                $"axisAngle={angle:F2} degrees, span={tangentMin:F1}..{tangentMax:F1}, " +
                $"robustSpacing={robustSpacing:F1}px.");

            using var debugImage = src.Clone();
            Cv2.Polylines(debugImage, new[] { quad }, true, new Scalar(0, 165, 255), 5);
            foreach (Point2f center in centers)
                Cv2.Circle(debugImage, new Point((int)center.X, (int)center.Y), 7, new Scalar(255, 255, 0), -1);
            DisplayDebugMat(debugImage, "VerifiedBlackKeyGeometryDebugPreview", 1500, 750);
        }

        private Point ToOpenCvPoint(Vector2 point)
        {
            return new Point(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y));
        }

        private List<OpenCvSharp.Rect> DeduplicateBlackKeyRects(
            List<OpenCvSharp.Rect> blackKeyRects,
            float whiteKeySpacing)
        {
            if (blackKeyRects == null || blackKeyRects.Count < 2 || whiteKeySpacing <= 0f)
                return blackKeyRects ?? new List<OpenCvSharp.Rect>();

            var ordered = new List<OpenCvSharp.Rect>(blackKeyRects);
            ordered.Sort((a, b) => a.X.CompareTo(b.X));
            var result = new List<OpenCvSharp.Rect>();
            float duplicateDistance = whiteKeySpacing * 0.55f;
            foreach (OpenCvSharp.Rect candidate in ordered)
            {
                if (result.Count == 0)
                {
                    result.Add(candidate);
                    continue;
                }

                float previousCenter = result[result.Count - 1].X + result[result.Count - 1].Width * 0.5f;
                float candidateCenter = candidate.X + candidate.Width * 0.5f;
                if (candidateCenter - previousCenter < duplicateDistance &&
                    ShouldMergeCloseBlackKeyCandidates(result[result.Count - 1], candidate, whiteKeySpacing))
                {
                    if (BlackKeyCandidateStrength(candidate) >
                        BlackKeyCandidateStrength(result[result.Count - 1]))
                        result[result.Count - 1] = candidate;
                }
                else
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        private bool ShouldMergeCloseBlackKeyCandidates(
            OpenCvSharp.Rect first,
            OpenCvSharp.Rect second,
            float spacing)
        {
            float firstCenter = first.X + first.Width * 0.5f;
            float secondCenter = second.X + second.Width * 0.5f;
            float centerDistance = Mathf.Abs(secondCenter - firstCenter);
            bool horizontalOverlap = first.X < second.X + second.Width &&
                second.X < first.X + first.Width;
            float firstHeight = Mathf.Max(1, first.Height);
            float heightRatio = Mathf.Min(firstHeight, second.Height) /
                Mathf.Max(firstHeight, second.Height);
            float yDistance = Mathf.Abs(
                (first.Y + first.Height * 0.5f) -
                (second.Y + second.Height * 0.5f));

            bool sameVerticalExtent = yDistance <= Mathf.Max(8f, spacing * 0.2f) &&
                heightRatio >= 0.65f;
            return horizontalOverlap || (centerDistance < spacing * 0.45f && sameVerticalExtent);
        }

        private float BlackKeyCandidateStrength(OpenCvSharp.Rect rect)
        {
            float area = rect.Width * rect.Height;
            float aspect = rect.Height / (float)Mathf.Max(1, rect.Width);
            return area * Mathf.Clamp(aspect, 1f, 12f);
        }

        private void LogSortedBlackKeyCandidates(
            string stage,
            List<OpenCvSharp.Rect> blackKeyRects,
            float spacing)
        {
            if (blackKeyRects == null || blackKeyRects.Count == 0)
                return;

            var ordered = new List<OpenCvSharp.Rect>(blackKeyRects);
            ordered.Sort((a, b) => a.X.CompareTo(b.X));
            var entries = new List<string>();
            float previousCenter = 0f;
            for (int i = 0; i < ordered.Count; i++)
            {
                OpenCvSharp.Rect rect = ordered[i];
                float center = rect.X + rect.Width * 0.5f;
                float distance = i == 0 ? 0f : center - previousCenter;
                float normalized = i == 0 || spacing <= 0f ? 0f : distance / spacing;
                entries.Add(
                    $"x={center:F1},d={distance:F1},n={normalized:F2}," +
                    $"w={rect.Width},h={rect.Height},y={rect.Y}");
                previousCenter = center;
            }

            VerboseLog($"Black-key {stage} centers: {string.Join("; ", entries)}");
        }

        private float EstimateBlackKeyCenterSpacing(
            List<OpenCvSharp.Rect> blackKeyRects,
            float fallbackSpacing)
        {
            if (blackKeyRects == null || blackKeyRects.Count < 2)
                return fallbackSpacing;

            var centers = new List<float>();
            foreach (OpenCvSharp.Rect rect in blackKeyRects)
                centers.Add(rect.X + rect.Width * 0.5f);
            centers.Sort();

            var gaps = new List<float>();
            for (int i = 1; i < centers.Count; i++)
            {
                float gap = centers[i] - centers[i - 1];
                if (gap > 2f)
                    gaps.Add(gap);
            }

            if (gaps.Count == 0)
                return fallbackSpacing;

            gaps.Sort();
            int localGapCount = Mathf.Max(1, Mathf.CeilToInt(gaps.Count * 0.55f));
            float localSpacing = GetMedian(gaps.GetRange(0, localGapCount));
            return localSpacing > 2f ? localSpacing : fallbackSpacing;
        }

        private List<OpenCvSharp.Rect> FilterPlausibleBlackKeyRects(
            List<OpenCvSharp.Rect> blackKeyRects,
            float whiteKeySpacing)
        {
            if (blackKeyRects == null || blackKeyRects.Count < 3 || whiteKeySpacing <= 0f)
                return blackKeyRects ?? new List<OpenCvSharp.Rect>();

            var ordered = new List<OpenCvSharp.Rect>(blackKeyRects);
            ordered.Sort((a, b) => a.X.CompareTo(b.X));
            var centers = new List<float>();
            foreach (OpenCvSharp.Rect rect in ordered)
                centers.Add(rect.X + rect.Width * 0.5f);

            var result = new List<OpenCvSharp.Rect>();
            for (int i = 0; i < ordered.Count; i++)
            {
                float previousGap = i > 0
                    ? (centers[i] - centers[i - 1]) / whiteKeySpacing
                    : float.PositiveInfinity;
                float nextGap = i + 1 < centers.Count
                    ? (centers[i + 1] - centers[i]) / whiteKeySpacing
                    : float.PositiveInfinity;
                if ((previousGap >= 0.55f && previousGap <= 3.0f) ||
                    (nextGap >= 0.55f && nextGap <= 3.0f))
                    result.Add(ordered[i]);
            }

            return result;
        }

        private List<List<OpenCvSharp.Rect>> GetBlackKeyGroups(
            List<OpenCvSharp.Rect> blackKeyRects,
            float whiteKeySpacing)
        {
            var groups = new List<List<OpenCvSharp.Rect>>();
            if (blackKeyRects == null || blackKeyRects.Count == 0)
                return groups;

            var ordered = new List<OpenCvSharp.Rect>(blackKeyRects);
            ordered.Sort((a, b) => a.X.CompareTo(b.X));
            var centers = new List<float>();
            foreach (OpenCvSharp.Rect rect in ordered)
                centers.Add(rect.X + rect.Width * 0.5f);

            var gaps = new List<float>();
            for (int i = 1; i < centers.Count; i++)
                gaps.Add(centers[i] - centers[i - 1]);

            whiteKeySpacing = EstimateBlackKeyCenterSpacing(blackKeyRects, whiteKeySpacing);

            var normalizedGaps = new List<float>();
            foreach (float gap in gaps)
                normalizedGaps.Add(gap / whiteKeySpacing);

            VerboseLog(
                $"Black-key normalized gaps: " +
                $"{string.Join(", ", normalizedGaps.ConvertAll(gap => gap.ToString("F2")))}.");

            if (TryPartitionBlackKeyGroups(ordered, normalizedGaps, out List<int> partition, out float partitionScore))
            {
                var partitionedGroups = new List<List<OpenCvSharp.Rect>>();
                int offset = 0;
                foreach (int size in partition)
                {
                    partitionedGroups.Add(ordered.GetRange(offset, size));
                    offset += size;
                }

                VerboseLog(
                    $"Black-key group partition: {string.Join("|", partition)}; " +
                    $"score={partitionScore:F2}.");
                return partitionedGroups;
            }

            VerboseWarning("Black-key group partition is unreliable; retaining geometric groups for debug only.");
            var current = new List<OpenCvSharp.Rect>();
            for (int i = 0; i < ordered.Count; i++)
            {
                bool groupBoundary = i > 0 &&
                    normalizedGaps[i - 1] > 1.45f &&
                    normalizedGaps[i - 1] < 2.7f;
                if (current.Count > 0 && groupBoundary && current.Count >= 2)
                {
                    groups.Add(current);
                    current = new List<OpenCvSharp.Rect>();
                }
                current.Add(ordered[i]);
            }

            if (current.Count > 0)
                groups.Add(current);
            return groups;
        }

        private bool TryPartitionBlackKeyGroups(
            List<OpenCvSharp.Rect> ordered,
            List<float> normalizedGaps,
            out List<int> bestPartition,
            out float bestScore)
        {
            bestPartition = null;
            bestScore = float.PositiveInfinity;
            if (ordered == null || ordered.Count < 4 || normalizedGaps.Count != ordered.Count - 1)
                return false;

            for (int firstSize = 1; firstSize <= 3; firstSize++)
            {
                if (firstSize == 1 && ordered.Count < 5)
                    continue;

                SearchBlackKeyPartitions(
                    normalizedGaps,
                    0,
                    firstSize,
                    new List<int>(),
                    0f,
                    ref bestPartition,
                    ref bestScore);
            }

            if (bestPartition == null || bestPartition.Count < 2)
                return false;

            bool hasFullGroup = false;
            foreach (int size in bestPartition)
                hasFullGroup |= size == 2 || size == 3;
            return hasFullGroup && bestScore <= Math.Max(4f, ordered.Count * 0.22f);
        }

        private void SearchBlackKeyPartitions(
            List<float> normalizedGaps,
            int startIndex,
            int groupSize,
            List<int> currentPartition,
            float currentScore,
            ref List<int> bestPartition,
            ref float bestScore)
        {
            int nextIndex = startIndex + groupSize;
            bool isTrailingPartial = groupSize == 1 &&
                startIndex > 0 &&
                nextIndex == normalizedGaps.Count + 1;
            if (nextIndex > normalizedGaps.Count && !isTrailingPartial)
                return;

            float score = currentScore;
            for (int i = startIndex; i < nextIndex - 1; i++)
                score += Mathf.Abs(normalizedGaps[i] - 1f);

            var updatedPartition = new List<int>(currentPartition) { groupSize };
            if (nextIndex == normalizedGaps.Count + 1)
            {
                if (updatedPartition.Count >= 2 && score < bestScore)
                {
                    bestScore = score;
                    bestPartition = updatedPartition;
                }
                return;
            }

            float boundaryGap = normalizedGaps[nextIndex - 1];
            score += Mathf.Abs(boundaryGap - 2f);
            if (boundaryGap < 1.2f)
                score += 1.5f;

            int previousSize = updatedPartition[updatedPartition.Count - 1];
            for (int nextSize = 2; nextSize <= 3; nextSize++)
            {
                float alternationPenalty = previousSize == nextSize ? 0.7f : 0f;
                SearchBlackKeyPartitions(
                    normalizedGaps,
                    nextIndex,
                    nextSize,
                    updatedPartition,
                    score + alternationPenalty,
                    ref bestPartition,
                    ref bestScore);
            }

            SearchBlackKeyPartitions(
                normalizedGaps,
                nextIndex,
                1,
                updatedPartition,
                score,
                ref bestPartition,
                ref bestScore);
        }

        private void DisplayDebugMat(Mat mat, string objectName, float width, float height)
        {
            if (objectName != "KeyboardGeometryDebugPreview")
                return;

            if (mat == null || mat.Empty())
                return;

            if (!Cv2.ImEncode(".png", mat, out byte[] encodedImage))
            {
                VerboseWarning($"Unable to encode debug Mat for {objectName}.");
                return;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(encodedImage))
            {
                Destroy(texture);
                VerboseWarning($"Unable to load debug Mat texture for {objectName}.");
                return;
            }

            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("OpenCVDebugCanvas");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            GameObject previewObject = GameObject.Find(objectName);
            RawImage preview = previewObject != null
                ? previewObject.GetComponent<RawImage>()
                : null;
            if (preview == null)
            {
                previewObject = new GameObject(objectName);
                previewObject.transform.SetParent(canvas.transform, false);
                preview = previewObject.AddComponent<RawImage>();
            }

            preview.texture = texture;
            preview.color = Color.white;
            preview.enabled = true;
            preview.gameObject.SetActive(true);

            RectTransform rect = preview.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(16f, 16f);
            rect.sizeDelta = new Vector2(width, height);
            preview.transform.SetAsLastSibling();
            VerboseLog($"Displayed {objectName}: {texture.width}x{texture.height} as {width}x{height} preview.");
        }

        private bool TryVerifyPianoPattern(
            List<OpenCvSharp.Rect> blackKeyRects,
            float whiteKeySpacing,
            out string patternDescription)
        {
            patternDescription = string.Empty;
            if (blackKeyRects == null || blackKeyRects.Count < 5)
            {
                patternDescription = "Not enough black-key candidates for a 2/3 group pattern.";
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

            whiteKeySpacing = EstimateBlackKeyCenterSpacing(blackKeyRects, whiteKeySpacing);
            if (whiteKeySpacing <= 0f)
            {
                patternDescription = "Invalid key spacing.";
                return false;
            }

            var groupedRects = GetBlackKeyGroups(blackKeyRects, whiteKeySpacing);
            var groups = new List<int>();
            foreach (List<OpenCvSharp.Rect> group in groupedRects)
                groups.Add(group.Count);

            if (groups.Count < 2)
            {
                patternDescription = "Too few black-key groups detected.";
                return false;
            }

            bool hasTwoGroup = false;
            bool hasThreeGroup = false;
            bool validPattern = true;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] == 2)
                    hasTwoGroup = true;
                else if (groups[i] == 3)
                    hasThreeGroup = true;
                else if (groups[i] != 1 || (i != 0 && i != groups.Count - 1))
                    validPattern = false;
            }

            patternDescription = "groups=" + string.Join(",", groups);
            return validPattern && (hasTwoGroup || hasThreeGroup) && groups.Count >= 2;
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
            int bestContourIndex = -1;
            int largestAnyContourIndex = -1;
            int areaRejectedCount = 0;
            int polygonRejectedCount = 0;
            int dimensionsRejectedCount = 0;
            int aspectRejectedCount = 0;
            int validCandidateCount = 0;

            for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
            {
                Point[] contour = contours[contourIndex];
                double contourArea = Math.Abs(Cv2.ContourArea(contour));
                var rectAny = Cv2.BoundingRect(contour);

                if (rectAny.Width * rectAny.Height > largestAnyArea)
                {
                    largestAnyArea = rectAny.Width * rectAny.Height;
                    largestAnyRect = rectAny;
                    largestAnyContourIndex = contourIndex;
                }

                if (contourArea < imageArea * 0.02)
                {
                    areaRejectedCount++;
                    continue;
                }

                var approx = Cv2.ApproxPolyDP(contour, 0.02 * Cv2.ArcLength(contour, true), true);
                if (approx.Length != 4 || !Cv2.IsContourConvex(approx))
                {
                    polygonRejectedCount++;
                    continue;
                }

                var rect = Cv2.BoundingRect(approx);
                if (rect.Width < src.Width * 0.2 || rect.Height < src.Height * 0.12)
                {
                    dimensionsRejectedCount++;
                    continue;
                }

                float aspect = rect.Width / (float)rect.Height;
                if (aspect < 2.5f)
                {
                    aspectRejectedCount++;
                    continue;
                }

                double area = rect.Width * rect.Height;
                validCandidateCount++;
                VerboseLog(
                    $"Outline candidate {validCandidateCount - 1}: " +
                    $"rect=({rect.X},{rect.Y},{rect.Width},{rect.Height}), area={area:F1}, aspect={aspect:F3}.");
                if (area > bestArea)
                {
                    bestArea = area;
                    bestRect = rect;
                    bestContourIndex = contourIndex;
                }
            }

            VerboseLog($"Outline detection: {contours.Length} contours examined.");
            VerboseLog(
                $"Outline filtering: area={areaRejectedCount}, polygon={polygonRejectedCount}, " +
                $"dimensions={dimensionsRejectedCount}, aspect={aspectRejectedCount}, " +
                $"valid={validCandidateCount}.");

            if (bestRect.Width == 0 && largestAnyRect.Width > src.Width * 0.3 && largestAnyRect.Height > src.Height * 0.15 && largestAnyRect.Width / (float)largestAnyRect.Height > 2f)
            {
                bestRect = largestAnyRect;
                bestContourIndex = largestAnyContourIndex;
                VerboseLog(
                    $"Keyboard outline fallback selected contour {largestAnyContourIndex}: " +
                    $"rect=({largestAnyRect.X},{largestAnyRect.Y},{largestAnyRect.Width},{largestAnyRect.Height}), " +
                    $"area={largestAnyArea:F1}, aspect={largestAnyRect.Width / (float)largestAnyRect.Height:F3}.");
            }

            if (bestRect.Width > 0)
            {
                VerboseLog(
                    $"Keyboard outline selected: rect=({bestRect.X},{bestRect.Y},{bestRect.Width},{bestRect.Height}).");
            }
            else
            {
                VerboseLog("Keyboard outline selection: no valid candidate selected.");
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