using UnityEngine;
using UnityEngine.UI;
using ARPIANO.Scripts.Models;
using ARPIANO.Scripts.Tracking;
using System.Collections.Generic;
using ARPIANO.Scripts.Lessons;

namespace ARPIANO.Scripts.Visualizers
{
    public class BeamVisualizer : MonoBehaviour, IVisualizer
    {
        private LessonPlayer lessonPlayer;

        [SerializeField] private Beam beamPrefab;

        [Header("Calibration")]
        [SerializeField] private KeyboardCalibration calibration;
        [SerializeField] private KeyboardCalibrationController calibrationController;

        [Header("Beam")]
        [SerializeField] private float beamStartOffset = 100f;
        [SerializeField] private float beamWidth = 20f;

        private RectTransform webcamRect;

        private KeyboardHomography homography;
        private KeyboardNoteMapper noteMapper;

        private readonly Dictionary<int, Beam> activeBeams = new();

        private bool eventsSubscribed;
        private bool mappingCreated;

        private void Awake()
        {
            lessonPlayer = FindAnyObjectByType<LessonPlayer>();

            if (beamPrefab == null)
            {
                beamPrefab = Resources.Load<Beam>("Prefabs/Beam");

                if (beamPrefab == null)
                {
                    Debug.LogError(
                        "BeamVisualizer could not find Beam prefab at Resources/Prefabs/Beam.");
                }
                else
                {
                    Debug.Log(
                        "BeamVisualizer auto-loaded Beam prefab from Resources/Prefabs/Beam.");
                }
            }
        }

        private void OnEnable()
        {
            Debug.Log(
                "BeamVisualizer enabled and attempting to subscribe to LessonPlayer events.");

            TrySubscribe();
        }

        private void Update()
        {
            if (!eventsSubscribed)
                TrySubscribe();

            if (!mappingCreated)
                TryCreateMapping();
        }

        private void TrySubscribe()
        {
            if (eventsSubscribed)
                return;

            if (lessonPlayer == null)
                lessonPlayer = FindAnyObjectByType<LessonPlayer>();

            if (lessonPlayer == null)
                return;

            lessonPlayer.NoteStarted += NoteStarted;
            lessonPlayer.NoteStopped += NoteStopped;

            eventsSubscribed = true;

            Debug.Log("BeamVisualizer subscribed to LessonPlayer events.");
        }

        private void TryCreateMapping()
        {
            if (calibrationController == null)
                return;

            if (!calibrationController.IsCalibrated)
                return;

            if (calibration == null)
            {
                Debug.LogError(
                    "BeamVisualizer: KeyboardCalibration is not assigned.");

                enabled = false;
                return;
            }

            if (!TryFindWebcamRect())
                return;

            homography = new KeyboardHomography(
                calibration.TopLeft,
                calibration.TopRight,
                calibration.BottomRight,
                calibration.BottomLeft);

            noteMapper = new KeyboardNoteMapper(
                calibration.FirstMidiNote,
                calibration.LastMidiNote);

            mappingCreated = true;

            Debug.Log("BeamVisualizer keyboard mapping created.");
        }

        private bool TryFindWebcamRect()
        {
            GameObject webcamPreview =
                GameObject.Find("WebcamPreview");

            if (webcamPreview == null)
                return false;

            webcamRect =
                webcamPreview.GetComponent<RectTransform>();

            return webcamRect != null;
        }

        public void NoteStarted(PianoNote note)
        {
            Debug.Log($"Beam spawn requested for {note.GetNoteNames()}");

            if (!mappingCreated)
            {
                Debug.LogWarning(
                    "BeamVisualizer cannot spawn beam because calibration mapping is not ready.");
                return;
            }

            if (beamPrefab == null)
            {
                Debug.LogError(
                    "BeamVisualizer cannot spawn beam because beamPrefab is null.");
                return;
            }

            float normalizedX =
                noteMapper.GetNormalizedCenter(note.MidiNumber);

            if (normalizedX < 0f)
            {
                Debug.LogWarning(
                    $"MIDI {note.MidiNumber} is outside the calibrated range.");
                return;
            }

            Vector2 normalizedTarget =
                new Vector2(normalizedX, 0f);

            Vector2 imageTarget =
                homography.NormalizedToImage(normalizedTarget);

            Vector2 targetPosition =
                ImageToWebcamLocalPosition(imageTarget);

            Vector2 startPosition =
                GetBeamStartPosition(targetPosition);

            Beam beam = Instantiate(
                beamPrefab,
                webcamRect);

            RectTransform beamRect =
                beam.GetComponent<RectTransform>();

            if (beamRect == null)
            {
                Debug.LogError("Beam prefab does not have a RectTransform.");
                Destroy(beam.gameObject);
                return;
            }

            // Make absolutely sure this is a visible UI element.
            beamRect.SetParent(webcamRect, false);

            beamRect.anchorMin = new Vector2(0.5f, 0.5f);
            beamRect.anchorMax = new Vector2(0.5f, 0.5f);
            beamRect.pivot = new Vector2(0.5f, 0.5f);

            beamRect.sizeDelta = new Vector2(40f, 300f);
            beamRect.anchoredPosition = startPosition;

            beam.gameObject.SetActive(true);

            Debug.Log(
                $"BEAM CREATED: MIDI={note.MidiNumber}, " +
                $"start=({startPosition.x:F1},{startPosition.y:F1}), " +
                $"target=({targetPosition.x:F1},{targetPosition.y:F1}), " +
                $"size={beamRect.sizeDelta}");

            beam.Initialize(
                note.MidiNumber,
                startPosition,
                targetPosition);

            activeBeams[note.MidiNumber] = beam;
        }

        public void NoteStopped(PianoNote note)
        {
            if (!activeBeams.TryGetValue(
                    note.MidiNumber,
                    out Beam beam))
            {
                return;
            }

            beam.Finish();

            activeBeams.Remove(note.MidiNumber);
        }

        private Vector2 ImageToWebcamLocalPosition(
            Vector2 imagePoint)
        {
            float u =
                imagePoint.x / 1280f;

            float v =
                1f - imagePoint.y / 720f;

            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            return new Vector2(
                Mathf.Lerp(
                    webcamRect.rect.xMin,
                    webcamRect.rect.xMax,
                    u),

                Mathf.Lerp(
                    webcamRect.rect.yMin,
                    webcamRect.rect.yMax,
                    v));
        }

        private Vector2 GetBeamStartPosition(
            Vector2 targetPosition)
        {
            Rect rect = webcamRect.rect;

            /*
             * The beam starts at the top of the webcam frame.
             *
             * X is kept identical to the target so the beam
             * travels straight toward the calibrated piano edge.
             *
             * Y is the top of the webcam frame.
             */
            float startY = rect.yMax + beamStartOffset;

            return new Vector2(
                targetPosition.x,
                startY);
        }

        private void OnDisable()
        {
            if (eventsSubscribed && lessonPlayer != null)
            {
                lessonPlayer.NoteStarted -= NoteStarted;
                lessonPlayer.NoteStopped -= NoteStopped;

                eventsSubscribed = false;
            }
        }

        private void OnDestroy()
        {
            homography?.Dispose();
        }
    }
}