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

        [SerializeField] private float whiteBeamWidth = 30f;
        [SerializeField] private float blackBeamWidth = 20f;

        [SerializeField] private float beamSpeed = 400f;

        [Header("Beam Colors")]
        [SerializeField] private Color whiteBeamColor =
            new Color32(255, 97, 97, 255);

        [SerializeField] private Color blackBeamColor =
            new Color32(125, 35, 35, 255);

        private RectTransform webcamRect;

        private KeyboardHomography homography;
        private KeyboardNoteMapper noteMapper;

        private readonly Dictionary<PianoNote, Beam> activeBeams = new();
        private readonly HashSet<PianoNote> scheduledBeams = new();

        private bool eventsSubscribed;
        private bool mappingCreated;
        private float lastLessonTime = -1f;

        private void Awake()
        {
            lessonPlayer = FindAnyObjectByType<LessonPlayer>();

            if (calibration == null)
                calibration = FindAnyObjectByType<KeyboardCalibration>();

            if (calibrationController == null)
            {
                calibrationController =
                    FindAnyObjectByType<KeyboardCalibrationController>();
            }

            if (beamPrefab == null)
            {
                beamPrefab =
                    Resources.Load<Beam>("Prefabs/Beam");

                if (beamPrefab == null)
                {
                    Debug.LogError(
                        "BeamVisualizer could not find Beam prefab at " +
                        "Resources/Prefabs/Beam.");
                }
                else
                {
                    Debug.Log(
                        "BeamVisualizer auto-loaded Beam prefab from " +
                        "Resources/Prefabs/Beam.");
                }
            }
        }

        private void OnEnable()
        {
            Debug.Log(
                "BeamVisualizer enabled and attempting to subscribe " +
                "to LessonPlayer events.");

            TrySubscribe();
        }

        private void Update()
        {
            if (!eventsSubscribed)
                TrySubscribe();

            if (!mappingCreated)
            {
                TryCreateMapping();
                return;
            }

            float currentTime = lessonPlayer.GetCurrentTime;

            if (lastLessonTime >= 0f && currentTime < lastLessonTime)
            {
                scheduledBeams.Clear();
                activeBeams.Clear();
            }

            lastLessonTime = currentTime;

            ScheduleUpcomingBeams();

            UpdateActiveBeams(currentTime);
        }

        private void UpdateActiveBeams(float currentTime)
        {
            foreach (Beam beam in activeBeams.Values)
            {
                if (beam != null)
                    beam.UpdateForLessonTime(currentTime);
            }
        }

        private void TrySubscribe()
        {
            if (eventsSubscribed)
                return;

            if (lessonPlayer == null)
                lessonPlayer =
                    FindAnyObjectByType<LessonPlayer>();

            if (lessonPlayer == null)
                return;

            lessonPlayer.NoteStarted += NoteStarted;
            lessonPlayer.NoteStopped += NoteStopped;

            eventsSubscribed = true;

            Debug.Log(
                "BeamVisualizer subscribed to LessonPlayer events.");
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

            Debug.Log(
                "BeamVisualizer keyboard mapping created.");
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

        private void ScheduleUpcomingBeams()
        {
            if (lessonPlayer == null ||
                lessonPlayer.CurrentLesson == null ||
                !lessonPlayer.IsPlaying)
            {
                return;
            }

            float currentTime =
                lessonPlayer.GetCurrentTime;

            foreach (PianoNote note
                     in lessonPlayer.CurrentLesson.Notes)
            {
                if (scheduledBeams.Contains(note))
                    continue;

                float normalizedX =
                    noteMapper.GetNormalizedCenter(
                        note.MidiNumber);

                if (normalizedX < 0f)
                    continue;

                Vector2 normalizedTarget =
                    new Vector2(
                        normalizedX,
                        0f);

                Vector2 imageTarget =
                    homography.NormalizedToImage(
                        normalizedTarget);

                Vector2 targetPosition =
                    ImageToWebcamLocalPosition(
                        imageTarget);

                float beamAngle = GetKeyboardTiltAngle();

                Vector2 startPosition =
                    GetBeamStartPosition(
                        targetPosition,
                        beamAngle);

                bool isBlackKey =
                    IsBlackKey(
                        note.MidiNumber);

                float beamLength =
                    note.Duration * beamSpeed;

                float distance =
                    Vector2.Distance(
                        startPosition,
                        targetPosition);

                float travelTime =
                    distance /
                    beamSpeed;

                float spawnTime =
                    note.StartTime -
                    travelTime;

                if (currentTime >= note.EndTime)
                    continue;

                if (currentTime >= spawnTime)
                {
                    SpawnBeam(
                        note,
                        startPosition,
                        targetPosition,
                        beamAngle,
                        spawnTime);

                    scheduledBeams.Add(note);
                }
            }
        }

        private void SpawnBeam(
            PianoNote note,
            Vector2 startPosition,
            Vector2 targetPosition,
            float beamAngle,
            float spawnTime)
        {
            bool isBlackKey =
                IsBlackKey(
                    note.MidiNumber);

            float beamWidth =
                isBlackKey
                    ? blackBeamWidth
                    : whiteBeamWidth;

            Color beamColor =
                isBlackKey
                    ? blackBeamColor
                    : whiteBeamColor;

            float beamLength =
                note.Duration * beamSpeed;

            Beam beam =
                Instantiate(
                    beamPrefab,
                    webcamRect);

            RectTransform beamRect =
                beam.GetComponent<RectTransform>();

            if (beamRect == null)
            {
                Debug.LogError(
                    "Beam prefab does not have a RectTransform.");

                Destroy(
                    beam.gameObject);

                return;
            }

            beamRect.SetParent(
                webcamRect,
                false);

            beamRect.SetAsLastSibling();

            beamRect.anchorMin =
                new Vector2(
                    0.5f,
                    0.5f);

            beamRect.anchorMax =
                new Vector2(
                    0.5f,
                    0.5f);

            beamRect.pivot =
                new Vector2(
                    0.5f,
                    0.5f);

            beamRect.sizeDelta =
                new Vector2(
                    beamWidth,
                    beamLength);

            beamRect.anchoredPosition =
                startPosition;

            beam.gameObject.SetActive(true);

            beam.Initialize(
                note.MidiNumber,
                startPosition,
                targetPosition,
                beamWidth,
                beamLength,
                beamColor,
                beamAngle,
                spawnTime,
                note.StartTime,
                note.Duration,
                beamSpeed);

            activeBeams[note] = beam;

            beam.UpdateForLessonTime(
                lessonPlayer.GetCurrentTime);

            Debug.Log(
                $"BEAM SPAWNED: " +
                $"MIDI={note.MidiNumber}, " +
                $"startTime={note.StartTime:F2}, " +
                $"duration={note.Duration:F2}, " +
                $"width={beamWidth:F1}, " +
                $"length={beamLength:F1}, " +
                $"currentTime={lessonPlayer.GetCurrentTime:F2}");
        }

        public void NoteStarted(PianoNote note)
        {
            if (activeBeams.TryGetValue(
                    note,
                    out Beam beam) &&
                beam != null)
            {
                beam.UpdateForLessonTime(
                    lessonPlayer.GetCurrentTime);
            }

            Debug.Log(
                $"NOTE STARTED: " +
                $"{note.GetNoteNames()} " +
                $"at {lessonPlayer.GetCurrentTime:F2}s");
        }

        public void NoteStopped(PianoNote note)
        {
            if (!activeBeams.TryGetValue(
                    note,
                    out Beam beam))
            {
                return;
            }

            if (beam != null)
                beam.Finish();

            activeBeams.Remove(note);
        }

        private Vector2 ImageToWebcamLocalPosition(
            Vector2 imagePoint)
        {
            float u =
                imagePoint.x / 1280f;

            float v =
                1f -
                imagePoint.y / 720f;

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
            Vector2 targetPosition,
            float beamAngle)
        {
            float angleRadians =
                -beamAngle * Mathf.Deg2Rad;

            Vector2 direction =
                new Vector2(
                    Mathf.Sin(angleRadians),
                    -Mathf.Cos(angleRadians));

            float startDistance =
                webcamRect.rect.height +
                beamStartOffset;

            return targetPosition -
                direction * startDistance;
        }
        private bool IsBlackKey(int midi)
        {
            return midi % 12 is
                1 or 3 or 6 or 8 or 10;
        }

        private void OnDisable()
        {
            if (eventsSubscribed &&
                lessonPlayer != null)
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

        private float GetKeyboardTiltAngle()
        {
            Vector2 edge =
                calibration.TopRight -
                calibration.TopLeft;

            return -Mathf.Atan2(edge.y, edge.x) * Mathf.Rad2Deg;
        }

    }
}