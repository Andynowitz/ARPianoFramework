using UnityEngine;
using UnityEngine.UI;
using ARPIANO.Scripts.Models;
using ARPIANO.Scripts.Tracking;
using ARPIANO.Scripts.UI;
using System.Collections.Generic;
using ARPIANO.Scripts.Lessons;
using ARPIANO.Scripts.MIDI;
using System;

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

        [SerializeField] private float whiteBeamWidth = 300f;
        [SerializeField] private float blackBeamWidth = 220f;

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
        private readonly List<Beam> allLiveBeams = new();
        private readonly HashSet<PianoNote> scheduledBeams = new();

        // Live MIDI input
        private LiveMIDIInput liveMIDIInput;

        private readonly Dictionary<LiveMIDINoteEvent, Beam> liveBeams = new();

        private UIManager uiManager;
        private readonly Dictionary<LiveMIDINoteEvent, Image> lessonKeyFeedback = new();

        private static readonly Color correctKeyColor =
            new Color(0.2f, 0.9f, 0.3f, 0.45f);

        private static readonly Color incorrectKeyColor =
            new Color(0.95f, 0.2f, 0.2f, 0.45f);

        private bool eventsSubscribed;
        private bool mappingCreated;
        private float lastLessonTime = -1f;

        private Lesson lastLesson;

        private bool invalidCalibrationLogged;

        private int mappingFirstMidiNote;
        private int mappingLastMidiNote;
        private Vector2 mappingTopLeft;
        private Vector2 mappingTopRight;
        private Vector2 mappingBottomRight;
        private Vector2 mappingBottomLeft;

        private void Awake()
        {
            lessonPlayer = FindAnyObjectByType<LessonPlayer>();

            liveMIDIInput = FindAnyObjectByType<LiveMIDIInput>();

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
            SubscribeToLiveMIDI();
        }

        private void Update()
        {
            if (!eventsSubscribed)
                TrySubscribe();

            Lesson currentLesson =
                lessonPlayer != null
                    ? lessonPlayer.CurrentLesson
                    : null;

            if (currentLesson != lastLesson)
            {
                ClearLessonBeams();
                lastLesson = currentLesson;
                lastLessonTime = -1f;
            }

            // Key overlays only belong to Lesson Mode.
            if (lessonKeyFeedback.Count > 0 && !IsLessonModeActive())
                ClearLessonKeyFeedback();

            if (mappingCreated && IsMappingStale())
            {
                ClearLessonBeams();
                mappingCreated = false;
                noteMapper = null;
                homography?.Dispose();
                homography = null;
            }

            if (!mappingCreated)
            {
                TryCreateMapping();
                return;
            }

            float currentTime = lessonPlayer.GetCurrentTime;

            if (lastLessonTime >= 0f && currentTime < lastLessonTime)
            {
                ClearLessonBeams();
            }

            lastLessonTime = currentTime;

            ScheduleUpcomingBeams();

            UpdateActiveBeams(currentTime);

            UpdateLiveBeams();
        }

        private void UpdateActiveBeams(float currentTime)
        {
            foreach (Beam beam in activeBeams.Values)
            {
                if (beam != null)
                    beam.UpdateForLessonTime(currentTime);
            }
        }

        private void UpdateLiveBeams()
        {
            for (int i = allLiveBeams.Count - 1; i >= 0; i--)
            {
                Beam beam = allLiveBeams[i];

                if (beam == null)
                {
                    allLiveBeams.RemoveAt(i);
                    continue;
                }

                beam.UpdateForLive(Time.deltaTime);

                // Remove beams that have finished their animation.
                if (beam == null || !beam.gameObject.activeInHierarchy)
                    allLiveBeams.RemoveAt(i);
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
            {
                calibrationController =
                    FindAnyObjectByType<KeyboardCalibrationController>(
                        FindObjectsInactive.Include);
            }

            if (calibration == null)
            {
                calibration =
                    FindAnyObjectByType<KeyboardCalibration>(
                        FindObjectsInactive.Include);
            }

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

            if (!HasValidCalibration(calibration))
            {
                if (!invalidCalibrationLogged)
                {
                    Debug.LogWarning(
                        "BeamVisualizer: Calibration corners are invalid; " +
                        "waiting for a valid calibration.");

                    invalidCalibrationLogged = true;
                }

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

            mappingFirstMidiNote = calibration.FirstMidiNote;
            mappingLastMidiNote = calibration.LastMidiNote;
            mappingTopLeft = calibration.TopLeft;
            mappingTopRight = calibration.TopRight;
            mappingBottomRight = calibration.BottomRight;
            mappingBottomLeft = calibration.BottomLeft;

            mappingCreated = true;
            invalidCalibrationLogged = false;

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
                GetBeamWidth(isBlackKey);

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

        private void SubscribeToLiveMIDI()
        {
            if (liveMIDIInput == null)
                liveMIDIInput = FindAnyObjectByType<LiveMIDIInput>();

            if (liveMIDIInput == null)
                return;

            liveMIDIInput.NotePressed -= OnLiveNotePressed;
            liveMIDIInput.NotePressed += OnLiveNotePressed;

            liveMIDIInput.NoteReleased -= OnLiveNoteReleased;
            liveMIDIInput.NoteReleased += OnLiveNoteReleased;

            liveMIDIInput.AllNotesReleased -= OnAllLiveNotesReleased;
            liveMIDIInput.AllNotesReleased += OnAllLiveNotesReleased;
        }

        private void UnsubscribeFromLiveMIDI()
        {
            if (liveMIDIInput == null)
                return;

            liveMIDIInput.NotePressed -= OnLiveNotePressed;
            liveMIDIInput.NoteReleased -= OnLiveNoteReleased;
            liveMIDIInput.AllNotesReleased -= OnAllLiveNotesReleased;
        }

        private void OnLiveNotePressed(LiveMIDINoteEvent note)
        {
            if (!mappingCreated || webcamRect == null ||
                homography == null || noteMapper == null)
                return;

            if (liveBeams.ContainsKey(note))
                return;

            if (IsLessonModeActive())
            {
                ShowLessonKeyFeedback(note);
                return;
            }

            if (beamPrefab == null)
                return;

            float normalizedX =
                noteMapper.GetNormalizedCenter(note.NoteNumber);

            if (normalizedX < 0f)
            {
                Debug.LogWarning(
                    $"[Live Beam] MIDI note {note.NoteNumber} is outside the calibrated range.");
                return;
            }

            Vector2 imageTarget =
                homography.NormalizedToImage(
                    new Vector2(normalizedX, 0f));

            Vector2 targetPosition =
                ImageToWebcamLocalPosition(imageTarget);

            float beamAngle = GetKeyboardTiltAngle();

            bool isBlackKey = IsBlackKey(note.NoteNumber);

            float beamWidth =
                GetBeamWidth(isBlackKey);

            Color beamColor =
                isBlackKey ? blackBeamColor : whiteBeamColor;

            Beam beam = Instantiate(beamPrefab, webcamRect);

            RectTransform beamRect =
                beam.GetComponent<RectTransform>();

            if (beamRect == null)
            {
                Destroy(beam.gameObject);
                Debug.LogError("[Live Beam] Beam prefab has no RectTransform.");
                return;
            }

            beamRect.SetParent(webcamRect, false);
            beamRect.SetAsLastSibling();
            beamRect.anchorMin = new Vector2(0.5f, 0.5f);
            beamRect.anchorMax = new Vector2(0.5f, 0.5f);

            beam.gameObject.SetActive(true);

            beam.InitializeForLive(
                note.NoteNumber,
                targetPosition,
                beamWidth,
                beamColor,
                beamAngle,
                beamSpeed);

            liveBeams.Add(note, beam);
            allLiveBeams.Add(beam);

            Debug.Log(
                $"[Live Beam] Created MIDI {note.NoteNumber}, channel {note.Channel}");
        }

        private void OnLiveNoteReleased(LiveMIDINoteEvent note)
        {
            if (lessonKeyFeedback.TryGetValue(note, out Image overlay))
            {
                RemoveLessonKeyFeedback(note, overlay);
                return;
            }

            if (!liveBeams.TryGetValue(note, out Beam beam))
                return;

            // Stop growing and start moving upward.
            if (beam != null)
                beam.ReleaseLive();

            // The key is no longer held, so it can be pressed again.
            liveBeams.Remove(note);

            Debug.Log(
                $"[Live Beam] Released MIDI {note.NoteNumber}, channel {note.Channel}"
            );
        }

        private void OnAllLiveNotesReleased()
        {
            foreach (Beam beam in liveBeams.Values)
            {
                if (beam != null)
                    beam.Finish();
            }

            liveBeams.Clear();
            allLiveBeams.Clear();

            ClearLessonKeyFeedback();
        }

        private bool IsLessonModeActive()
        {
            if (uiManager == null)
                uiManager = FindConfiguredUIManager();

            // The active UI panel is the source of truth: a lesson may
            // stay loaded after navigating away from Lesson Mode.
            return uiManager != null && uiManager.IsLessonMode;
        }

        // The scene can contain more than one UIManager component; prefer
        // the instance that actually has the panels assigned.
        private static UIManager FindConfiguredUIManager()
        {
            UIManager[] managers =
                FindObjectsByType<UIManager>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            UIManager fallback = null;

            foreach (UIManager manager in managers)
            {
                if (manager == null)
                    continue;

                if (manager.HasPanelReferences)
                    return manager;

                if (fallback == null)
                    fallback = manager;
            }

            return fallback;
        }

        private void ShowLessonKeyFeedback(LiveMIDINoteEvent note)
        {
            if (note == null || lessonKeyFeedback.ContainsKey(note))
                return;

            if (!TryGetKeyOverlayGeometry(
                    note.NoteNumber,
                    out Vector2 center,
                    out Vector2 size,
                    out float angle))
                return;

            GameObject overlayObject = new GameObject(
                $"LessonKeyFeedback_{note.NoteNumber}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

            RectTransform overlayRect =
                overlayObject.GetComponent<RectTransform>();

            overlayRect.SetParent(webcamRect, false);
            overlayRect.SetAsLastSibling();
            overlayRect.anchorMin = new Vector2(0.5f, 0.5f);
            overlayRect.anchorMax = new Vector2(0.5f, 0.5f);
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.sizeDelta = size;
            overlayRect.anchoredPosition = center;
            overlayRect.localRotation = Quaternion.Euler(0f, 0f, angle);

            bool isExpected =
                lessonPlayer != null &&
                lessonPlayer.AwaitingNote != null &&
                lessonPlayer.AwaitingNote.MidiNumber == note.NoteNumber;

            Image image = overlayObject.GetComponent<Image>();
            image.color = isExpected ? correctKeyColor : incorrectKeyColor;
            image.raycastTarget = false;

            lessonKeyFeedback.Add(note, image);
        }

        private void RemoveLessonKeyFeedback(
            LiveMIDINoteEvent note,
            Image overlay)
        {
            lessonKeyFeedback.Remove(note);

            if (overlay != null)
                Destroy(overlay.gameObject);
        }

        private void ClearLessonKeyFeedback()
        {
            foreach (Image overlay in lessonKeyFeedback.Values)
            {
                if (overlay != null)
                    Destroy(overlay.gameObject);
            }

            lessonKeyFeedback.Clear();
        }

        private bool TryGetKeyOverlayGeometry(
            int midiNote,
            out Vector2 center,
            out Vector2 size,
            out float angle)
        {
            center = Vector2.zero;
            size = Vector2.zero;
            angle = 0f;

            if (noteMapper == null ||
                homography == null ||
                webcamRect == null ||
                calibration == null)
                return false;

            float normalizedCenter =
                noteMapper.GetNormalizedCenter(midiNote);

            if (normalizedCenter < 0f)
                return false;

            int whiteKeyCount = calibration.WhiteKeyCount;

            if (whiteKeyCount <= 0)
                return false;

            bool isBlackKey = IsBlackKey(midiNote);

            float widthRatio =
                isBlackKey && whiteBeamWidth > 0f
                    ? blackBeamWidth / whiteBeamWidth
                    : 1f;

            float halfWidthNormalized =
                widthRatio / whiteKeyCount * 0.5f;

            float left = normalizedCenter - halfWidthNormalized;
            float right = normalizedCenter + halfWidthNormalized;

            Vector2 topLeft = ImageToWebcamLocalPosition(
                homography.NormalizedToImage(new Vector2(left, 0f)));

            Vector2 topRight = ImageToWebcamLocalPosition(
                homography.NormalizedToImage(new Vector2(right, 0f)));

            Vector2 bottomLeft = ImageToWebcamLocalPosition(
                homography.NormalizedToImage(new Vector2(left, 1f)));

            Vector2 bottomRight = ImageToWebcamLocalPosition(
                homography.NormalizedToImage(new Vector2(right, 1f)));

            center = (topLeft + topRight + bottomLeft + bottomRight) * 0.25f;

            size = new Vector2(
                Vector2.Distance(topLeft, topRight),
                Vector2.Distance(topLeft, bottomLeft));

            angle =
                Mathf.Atan2(
                    topRight.y - topLeft.y,
                    topRight.x - topLeft.x) * Mathf.Rad2Deg;

            return size.x > 0f && size.y > 0f;
        }

        private void ClearLessonBeams()
        {
            foreach (Beam beam in activeBeams.Values)
            {
                if (beam != null)
                    beam.Finish();
            }

            activeBeams.Clear();
            scheduledBeams.Clear();
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

            UnsubscribeFromLiveMIDI();

            ClearLessonBeams();
            ClearLessonKeyFeedback();

            foreach (Beam beam in liveBeams.Values)
            {   
                if (beam != null)
                    beam.Finish();
            }

            liveBeams.Clear();
            allLiveBeams.Clear();
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

        private static bool HasValidCalibration(
            KeyboardCalibration keyboardCalibration)
        {
            if (keyboardCalibration == null)
                return false;

            Vector2[] corners = keyboardCalibration.GetImageCorners();

            if (corners == null || corners.Length != 4)
                return false;

            for (int i = 0; i < corners.Length; i++)
            {
                if (float.IsNaN(corners[i].x) ||
                    float.IsNaN(corners[i].y) ||
                    float.IsInfinity(corners[i].x) ||
                    float.IsInfinity(corners[i].y))
                    return false;
            }

            float width =
                Vector2.Distance(corners[0], corners[1]);

            float height =
                Vector2.Distance(corners[0], corners[3]);

            return width > 1f && height > 1f;
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

        private float GetBeamWidth(bool isBlackKey)
        {
            float whiteWidth = GetCalibratedWhiteKeyWidth();

            if (!isBlackKey)
                return whiteWidth;

            if (whiteBeamWidth <= 0f)
                return blackBeamWidth;

            return whiteWidth * (blackBeamWidth / whiteBeamWidth);
        }

        private float GetCalibratedWhiteKeyWidth()
        {
            if (calibration == null ||
                homography == null ||
                webcamRect == null)
                return whiteBeamWidth;

            int whiteKeyCount = calibration.WhiteKeyCount;

            if (whiteKeyCount <= 0)
                return whiteBeamWidth;

            Vector2 topLeftLocal = ImageToWebcamLocalPosition(
                homography.NormalizedToImage(new Vector2(0f, 0f)));

            Vector2 topRightLocal = ImageToWebcamLocalPosition(
                homography.NormalizedToImage(new Vector2(1f, 0f)));

            float keyboardWidth = Vector2.Distance(
                topLeftLocal,
                topRightLocal);

            if (keyboardWidth <= 0f)
                return whiteBeamWidth;

            return keyboardWidth / whiteKeyCount;
        }

    }
}