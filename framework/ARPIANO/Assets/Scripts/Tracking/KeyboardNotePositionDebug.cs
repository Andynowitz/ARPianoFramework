using UnityEngine;
using UnityEngine.UI;
using ARPIANO.Scripts.Models;

namespace ARPIANO.Scripts.Tracking
{
    public class KeyboardNotePositionDebug : MonoBehaviour
    {
        [Header("Calibration")]
        [SerializeField] private KeyboardCalibration calibration;
        [SerializeField] private KeyboardCalibrationController calibrationController;

        //[Header("Webcam")]
        private RectTransform webcamRect;

        [Header("Debug")]
        [SerializeField] private float markerSize = 20f;

        private readonly int[] testMidiNotes =
        {
            48, // C3
            60, // C4
            66, // F#4
            72, // C5
            84  // C6
        };

        private KeyboardHomography homography;
        private KeyboardNoteMapper noteMapper;

        private bool mappingCreated;

        private void Update()
        {
            if (mappingCreated)
                return;

            if (calibrationController == null)
            {
                Debug.LogError(
                    "KeyboardNotePositionDebug: " +
                    "KeyboardCalibrationController is not assigned.");

                enabled = false;
                return;
            }

            if (!calibrationController.IsCalibrated)
                return;

            if (!TryFindWebcamRect())
                return;

            CreateMapping();
        }
        
        private void CreateMapping()
        {
            if (calibration == null)
            {
                Debug.LogError(
                    "KeyboardNotePositionDebug: " +
                    "KeyboardCalibration is not assigned.");

                enabled = false;
                return;
            }

            if (webcamRect == null)
            {
                Debug.LogError(
                    "KeyboardNotePositionDebug: " +
                    "Webcam Rect is not assigned.");

                enabled = false;
                return;
            }

            homography = new KeyboardHomography(
                calibration.TopLeft,
                calibration.TopRight,
                calibration.BottomRight,
                calibration.BottomLeft);

            noteMapper = new KeyboardNoteMapper(
                calibration.FirstMidiNote,
                calibration.LastMidiNote);

            Debug.Log("=== MIDI POSITION DEBUG ===");

            foreach (int midiNote in testMidiNotes)
            {
                CreateMarkerForMidiNote(midiNote);
            }

            Debug.Log("===========================");

            mappingCreated = true;
        }

        private void CreateMarkerForMidiNote(int midiNote)
        {
            float normalizedX =
                noteMapper.GetNormalizedCenter(midiNote);

            if (normalizedX < 0f)
            {
                Debug.LogWarning(
                    $"MIDI {midiNote} is outside the calibrated range.");

                return;
            }

            Vector2 normalizedPoint =
                new Vector2(normalizedX, 0.5f);

            Vector2 imagePoint =
                homography.NormalizedToImage(normalizedPoint);

            Debug.Log(
                $"MIDI {midiNote} " +
                $"({PianoNote.GetNoteName(midiNote)}): " +
                $"normalizedX={normalizedX:F4}, " +
                $"imagePoint=({imagePoint.x:F2}, {imagePoint.y:F2})");

            CreateMarker(imagePoint, midiNote);
        }

        private void CreateMarker(
            Vector2 imagePoint,
            int midiNote)
        {
            float u = imagePoint.x / 1280f;
            float v = 1f - imagePoint.y / 720f;

            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            Vector2 localPoint = new Vector2(
                Mathf.Lerp(
                    webcamRect.rect.xMin,
                    webcamRect.rect.xMax,
                    u),

                Mathf.Lerp(
                    webcamRect.rect.yMin,
                    webcamRect.rect.yMax,
                    v));

            GameObject markerObject =
                new GameObject(
                    $"MIDIMarker_{midiNote}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));

            RectTransform marker =
                markerObject.GetComponent<RectTransform>();

            Image image =
                markerObject.GetComponent<Image>();

            marker.SetParent(webcamRect, false);

            marker.anchorMin = new Vector2(0.5f, 0.5f);
            marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0.5f);

            marker.anchoredPosition = localPoint;

            marker.sizeDelta =
                new Vector2(markerSize, markerSize);

            image.color = Color.yellow;
            image.raycastTarget = false;
        }

        private void OnDestroy()
        {
            homography?.Dispose();
        }

        private bool TryFindWebcamRect()
        {
            GameObject webcamPreview = GameObject.Find("WebcamPreview");

            if (webcamPreview == null)
                return false;

            webcamRect = webcamPreview.GetComponent<RectTransform>();

            return webcamRect != null;
        }

    }
}