using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
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

        private readonly List<GameObject> markers = new();

        private KeyboardHomography homography;
        private KeyboardNoteMapper noteMapper;

        private bool mappingCreated;

        private int mappingFirstMidiNote;
        private int mappingLastMidiNote;
        private Vector2 mappingTopLeft;
        private Vector2 mappingTopRight;
        private Vector2 mappingBottomRight;
        private Vector2 mappingBottomLeft;

        private void Update()
        {
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

            if (mappingCreated && IsMappingStale())
                ResetMapping();

            if (mappingCreated)
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

            mappingFirstMidiNote = calibration.FirstMidiNote;
            mappingLastMidiNote = calibration.LastMidiNote;
            mappingTopLeft = calibration.TopLeft;
            mappingTopRight = calibration.TopRight;
            mappingBottomRight = calibration.BottomRight;
            mappingBottomLeft = calibration.BottomLeft;

            Debug.Log("=== MIDI POSITION DEBUG ===");

            foreach (int midiNote in GetTestMidiNotes())
            {
                CreateMarkerForMidiNote(midiNote);
            }

            Debug.Log("===========================");

            mappingCreated = true;
        }

        private List<int> GetTestMidiNotes()
        {
            int first = calibration.FirstMidiNote;
            int last = calibration.LastMidiNote;

            List<int> notes = new List<int>();

            AddTestNote(notes, first);
            AddTestNote(notes, last);

            int middle = (first + last) / 2;
            AddTestNote(notes, middle);

            AddTestNote(notes, first + 12);
            AddTestNote(notes, last - 12);

            for (int midi = first; midi <= last; midi++)
            {
                if (IsBlackKey(midi))
                {
                    AddTestNote(notes, midi);
                    break;
                }
            }

            return notes;
        }

        private void AddTestNote(List<int> notes, int midiNote)
        {
            if (midiNote < calibration.FirstMidiNote ||
                midiNote > calibration.LastMidiNote ||
                notes.Contains(midiNote))
                return;

            notes.Add(midiNote);
        }

        private static bool IsBlackKey(int midi)
        {
            return midi % 12 is 1 or 3 or 6 or 8 or 10;
        }

        private bool IsMappingStale()
        {
            if (calibration == null)
                return true;

            return calibration.FirstMidiNote != mappingFirstMidiNote ||
                   calibration.LastMidiNote != mappingLastMidiNote ||
                   calibration.TopLeft != mappingTopLeft ||
                   calibration.TopRight != mappingTopRight ||
                   calibration.BottomRight != mappingBottomRight ||
                   calibration.BottomLeft != mappingBottomLeft;
        }

        private void ResetMapping()
        {
            DestroyMarkers();

            mappingCreated = false;
            noteMapper = null;
            homography?.Dispose();
            homography = null;
        }

        private void DestroyMarkers()
        {
            foreach (GameObject marker in markers)
            {
                if (marker != null)
                    Destroy(marker);
            }

            markers.Clear();
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

            markers.Add(markerObject);
        }

        private void OnDestroy()
        {
            DestroyMarkers();
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