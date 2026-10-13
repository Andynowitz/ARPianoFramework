using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Common;
using TMPro;
using UnityEngine.InputSystem;
using ARPIANO.Scripts.Recording;

namespace ARPIANO.Scripts.MIDI

{

    public class MIDIRecorder : MonoBehaviour

    {

        [Header("UI")]

        [SerializeField] private Button recordButton;

        [SerializeField] private Button stopButton;

        [SerializeField] private GameObject savePanel;

        [SerializeField] private Text countdownText;

        [SerializeField] private TMP_InputField filenameInput;

        [SerializeField] private Text statusText;

        [Header("MIDI")]

        [SerializeField] private LiveMIDIInput midiInput;

        [SerializeField] private int ticksPerQuarterNote = 480;

        [Header("Recording Modes")]
        [SerializeField] private Toggle recordMidiToggle;
        [SerializeField] private Toggle recordVideoToggle;
        [SerializeField] private ScreenMp4Recorder screenMp4Recorder;

        private bool sessionRecordsMidi;
        private bool sessionRecordsVideo;

        private bool pendingSaveMidi;
        private bool pendingSaveVideo;
        private bool isSaving;

        private sealed class RecordedEvent
        {
            public MidiEvent Event;
            public long Timestamp;
        }

        private readonly ConcurrentQueue<RecordedEvent> pending =
            new ConcurrentQueue<RecordedEvent>();

        private readonly List<RecordedEvent> recorded =
            new List<RecordedEvent>();

        private bool isRecording;
        private bool isCountdownRunning;
        private long recordingStartTimestamp;

        private void Awake()
        {
            if (midiInput == null)
                midiInput = FindAnyObjectByType<LiveMIDIInput>();

            if (stopButton != null)
                stopButton.gameObject.SetActive(false);

            if (savePanel != null)
                savePanel.SetActive(false);

            if (countdownText != null)
                countdownText.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (midiInput != null)
                midiInput.RawMidiEventReceived += OnRawMidiEvent;
        }

        private void OnDisable()
        {
            if (midiInput != null)
                midiInput.RawMidiEventReceived -= OnRawMidiEvent;
        }

        public void StartRecording()
        {
            if (isRecording || isCountdownRunning)
                return;

            sessionRecordsMidi = recordMidiToggle == null ||
                                 recordMidiToggle.isOn;

            sessionRecordsVideo = recordVideoToggle != null &&
                                  recordVideoToggle.isOn;

            if (!sessionRecordsMidi && !sessionRecordsVideo)
            {
                SetStatus("Select MIDI recording, video recording, or both.");
                return;
            }

            if (sessionRecordsMidi && midiInput == null)
            {
                SetStatus("MIDI input is not available.");
                return;
            }

            if (sessionRecordsVideo && screenMp4Recorder == null)
            {
                SetStatus("Video recorder is not assigned.");
                Debug.LogError(
                    "[MIDI Recorder] ScreenMp4Recorder is not assigned.");
                return;
            }

            StartCoroutine(CountdownAndRecord());
        }

        private IEnumerator CountdownAndRecord()
        {
            isCountdownRunning = true;

            if (recordButton != null)
                recordButton.interactable = false;

            if (countdownText != null)
                countdownText.gameObject.SetActive(true);

            for (int i = 3; i >= 1; i--)
            {
                if (countdownText != null)
                    countdownText.text = i.ToString();

                yield return new WaitForSeconds(1f);
            }

            if (countdownText != null)
                countdownText.text = "PLAY";

            yield return new WaitForSeconds(0.5f);

            if (sessionRecordsMidi)
            {
                while (pending.TryDequeue(out _)) { }
                recorded.Clear();
                recordingStartTimestamp = Stopwatch.GetTimestamp();
                isRecording = true;
            }
            else
            {
                // Video-only sessions must not collect MIDI events.
                isRecording = false;
            }

            if (sessionRecordsVideo)
            {
                screenMp4Recorder.StartRecording();

                if (!screenMp4Recorder.IsRecording)
                {
                    isRecording = false;
                    isCountdownRunning = false;

                    if (recordButton != null)
                    {
                        recordButton.gameObject.SetActive(true);
                        recordButton.interactable = true;
                    }

                    if (stopButton != null)
                        stopButton.gameObject.SetActive(false);

                    if (countdownText != null)
                        countdownText.gameObject.SetActive(false);

                    sessionRecordsMidi = false;
                    sessionRecordsVideo = false;
                    SetStatus("Could not start video recording.");
                    yield break;
                }
            }

            if (countdownText != null)
                countdownText.gameObject.SetActive(false);

            if (recordButton != null)
                recordButton.gameObject.SetActive(false);

            if (stopButton != null)
                stopButton.gameObject.SetActive(true);

            SetStatus(
                sessionRecordsMidi && sessionRecordsVideo
                    ? "Recording MIDI and video..."
                    : sessionRecordsMidi
                        ? "Recording MIDI..."
                        : "Recording video...");

            isCountdownRunning = false;
        }

        private void OnRawMidiEvent(MidiEvent midiEvent)
        {
            if (!isRecording)
                return;

            pending.Enqueue(new RecordedEvent
            {
                Event = midiEvent,
                Timestamp = Stopwatch.GetTimestamp()
            });
        }

        private void Update()
        {
            while (pending.TryDequeue(out var item))
                recorded.Add(item);

            if (savePanel == null || !savePanel.activeInHierarchy)
                return;

            if (filenameInput != null &&
                filenameInput.isFocused &&
                Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame ||
                 Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                SaveRecording();
            }
        }

        public void StopRecording()
        {
            if (isCountdownRunning)
            {
                StopAllCoroutines();
                isCountdownRunning = false;
                sessionRecordsMidi = false;
                sessionRecordsVideo = false;

                if (countdownText != null)
                    countdownText.gameObject.SetActive(false);

                if (recordButton != null)
                {
                    recordButton.gameObject.SetActive(true);
                    recordButton.interactable = true;
                }

                if (stopButton != null)
                    stopButton.gameObject.SetActive(false);

                SetStatus("Recording cancelled.");
                return;
            }

            bool wasMidiRecording = isRecording;

            if (wasMidiRecording)
            {
                isRecording = false;

                while (pending.TryDequeue(out var item))
                    recorded.Add(item);
            }

            if (sessionRecordsVideo && screenMp4Recorder != null)
                screenMp4Recorder.StopRecording();

            pendingSaveMidi = sessionRecordsMidi && wasMidiRecording;
            pendingSaveVideo = sessionRecordsVideo;

            if (stopButton != null)
                stopButton.gameObject.SetActive(false);

            if (recordButton != null)
            {
                recordButton.gameObject.SetActive(true);
                recordButton.interactable = true;
            }

            if (filenameInput != null)
                filenameInput.text = "";

            if ((pendingSaveMidi || pendingSaveVideo) && savePanel != null)
                savePanel.SetActive(true);

            if (pendingSaveMidi && pendingSaveVideo)
                SetStatus("Recording stopped. Enter a filename to save both files.");
            else if (pendingSaveMidi)
                SetStatus($"Captured {recorded.Count} MIDI events.");
            else if (pendingSaveVideo)
                SetStatus("Video capture stopped. Enter a filename to save the video.");

            sessionRecordsMidi = false;
            sessionRecordsVideo = false;
        }

        public void SaveRecording()
        {
            if (isSaving)
                return;

            string filename = filenameInput != null
                ? filenameInput.text.Trim()
                : "PianoRecording";

            foreach (char c in Path.GetInvalidFileNameChars())
                filename = filename.Replace(c, '_');

            if (string.IsNullOrWhiteSpace(filename))
                filename = "PianoRecording";

            StartCoroutine(SaveRecordingCoroutine(filename));
        }

        private IEnumerator SaveRecordingCoroutine(string filename)
        {
            isSaving = true;

            if (pendingSaveMidi)
            {
                if (!SaveMidiFile(filename))
                {
                    isSaving = false;
                    yield break;
                }
            }

            if (pendingSaveVideo)
            {
                SetStatus("Finishing video encoding...");

                // Wait for capture finalization and FFmpeg encoding.
                while (screenMp4Recorder != null &&
                    screenMp4Recorder.IsProcessing)
                {
                    yield return null;
                }

                if (screenMp4Recorder == null ||
                    !screenMp4Recorder.TryRenameCompletedRecording(
                        filename, out string videoPath))
                {
                    SetStatus("Could not save the MP4. Check the Console.");
                    isSaving = false;
                    yield break;
                }

                Debug.Log($"[MIDI Recorder] Video saved: {videoPath}");
            }

            if (savePanel != null)
                savePanel.SetActive(false);

            if (pendingSaveMidi && pendingSaveVideo)
                SetStatus($"Saved MIDI and video as '{filename}'.");
            else if (pendingSaveMidi)
                SetStatus($"Saved MIDI as '{filename}.mid'.");
            else if (pendingSaveVideo)
                SetStatus($"Saved video as '{filename}.mp4'.");

            pendingSaveMidi = false;
            pendingSaveVideo = false;
            isSaving = false;
        }

        private bool SaveMidiFile(string filename)
        {
            try
            {
                var midiEvents = new List<MidiEvent>();
                long previousTicks = 0;

                foreach (var item in recorded)
                {
                    if (item.Event is not NoteOnEvent &&
                        item.Event is not NoteOffEvent &&
                        item.Event is not ControlChangeEvent)
                    {
                        continue;
                    }

                    double elapsedSeconds =
                        (item.Timestamp - recordingStartTimestamp) /
                        (double)Stopwatch.Frequency;

                    long absoluteTicks = Math.Max(
                        0,
                        (long)Math.Round(
                            elapsedSeconds * ticksPerQuarterNote * 2.0
                        )
                    );

                    long deltaTicks = Math.Max(
                        0,
                        absoluteTicks - previousTicks
                    );

                    MidiEvent midiEvent = CloneEvent(item.Event);
                    midiEvent.DeltaTime = deltaTicks;

                    midiEvents.Add(midiEvent);
                    previousTicks = absoluteTicks;
                }

                string projectDirectory =
                    Directory.GetParent(Application.dataPath).FullName;

                string recordingsDirectory =
                    Path.Combine(projectDirectory, "Recordings");

                Directory.CreateDirectory(recordingsDirectory);

                string filePath =
                    Path.Combine(recordingsDirectory, filename + ".mid");

                var track = new TrackChunk();
                track.Events.AddRange(midiEvents);

                var midiFile = new MidiFile(track)
                {
                    TimeDivision =
                        new TicksPerQuarterNoteTimeDivision(
                            (short)ticksPerQuarterNote
                        )
                };

                midiFile.Write(filePath);

                Debug.Log($"[MIDI Recorder] MIDI saved: {filePath}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[MIDI Recorder] Failed to save MIDI: {exception}"
                );

                SetStatus("Failed to save MIDI. Check the Console.");
                return false;
            }
        }


        private static MidiEvent CloneEvent(MidiEvent source)
        {
            if (source is NoteOnEvent on)
                return new NoteOnEvent(on.NoteNumber, on.Velocity)
                {
                    Channel = on.Channel
                };

            if (source is NoteOffEvent off)
                return new NoteOffEvent(off.NoteNumber, off.Velocity)
                {
                    Channel = off.Channel
                };

            if (source is ControlChangeEvent cc)
                return new ControlChangeEvent(
                    cc.ControlNumber,
                    cc.ControlValue)
                {
                    Channel = cc.Channel
                };

            return null;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }
    }
}
