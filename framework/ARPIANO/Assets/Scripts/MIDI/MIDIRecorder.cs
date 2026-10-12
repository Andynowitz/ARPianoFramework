
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
            if (isRecording)
                return;

            if (midiInput == null)
            {
                SetStatus("MIDI input is not available.");
                return;
            }

            StartCoroutine(CountdownAndRecord());
        }

        private IEnumerator CountdownAndRecord()
        {
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

            while (pending.TryDequeue(out _)) { }
            recorded.Clear();

            recordingStartTimestamp = Stopwatch.GetTimestamp();
            isRecording = true;

            if (countdownText != null)
                countdownText.gameObject.SetActive(false);

            if (recordButton != null)
                recordButton.gameObject.SetActive(false);

            if (stopButton != null)
                stopButton.gameObject.SetActive(true);

            SetStatus("Recording...");
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
            {
                recorded.Add(item);
            }

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
            if (!isRecording)
                return;

            isRecording = false;

            while (pending.TryDequeue(out var item))
                recorded.Add(item);

            if (stopButton != null)
                stopButton.gameObject.SetActive(false);

            if (recordButton != null)
            {
                recordButton.gameObject.SetActive(true);
                recordButton.interactable = true;
            }

            if (filenameInput != null)
                filenameInput.text = "";

            if (savePanel != null)
                savePanel.SetActive(true);

            SetStatus($"Captured {recorded.Count} MIDI events.");
        }


        public void SaveRecording()
        {
            string filename = filenameInput != null
                ? filenameInput.text.Trim()
                : "PianoRecording";

            foreach (char c in Path.GetInvalidFileNameChars())
                filename = filename.Replace(c, '_');

            if (string.IsNullOrWhiteSpace(filename))
                filename = "PianoRecording";

            try
            {
                var midiEvents = new List<MidiEvent>();
                long previousTicks = 0;

                foreach (var item in recorded)
                {
                    if (!(item.Event is NoteOnEvent) &&
                        !(item.Event is NoteOffEvent) &&
                        !(item.Event is ControlChangeEvent))
                        continue;

                    double elapsedSeconds =
                        (item.Timestamp - recordingStartTimestamp) /
                        (double)Stopwatch.Frequency;

                    long absoluteTicks = Math.Max(
                        0,
                        (long)Math.Round(
                            elapsedSeconds * ticksPerQuarterNote * 2.0));

                    long deltaTicks = Math.Max(
                        0,
                        absoluteTicks - previousTicks);

                    MidiEvent copy = CloneEvent(item.Event);
                    if (copy == null)
                        continue;

                    copy.DeltaTime = deltaTicks;
                    midiEvents.Add(copy);
                    previousTicks = absoluteTicks;
                }

                var track = new TrackChunk();
                track.Events.AddRange(midiEvents);

                var midiFile = new MidiFile(track);
                midiFile.TimeDivision =
                    new TicksPerQuarterNoteTimeDivision(
                        (short)ticksPerQuarterNote);

                // Save inside the Unity project root.
                string projectDirectory =
                    Directory.GetParent(Application.dataPath).FullName;

                string recordingsDirectory =
                    Path.Combine(projectDirectory, "Recordings");

                Directory.CreateDirectory(recordingsDirectory);

                string path = Path.Combine(
                    recordingsDirectory,
                    filename + ".mid");

                midiFile.Write(path);

                SetStatus("Saved: " + path);
                Debug.Log("[MIDI Recorder] Saved: " + path);

                if (savePanel != null)
                    savePanel.SetActive(false);
            }
            catch (Exception exception)
            {
                Debug.LogError("[MIDI Recorder] Save failed: " + exception);
                SetStatus("Could not save MIDI file. Check the Console.");
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
