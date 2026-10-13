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
using System.Threading;
using FFmpegUnityBind2;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

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

        private sealed class AudioMuxCallbacks : IFFmpegCallbacksHandler
        {
            private readonly Action<bool> onFinished;

            public AudioMuxCallbacks(Action<bool> onFinished)
            {
                this.onFinished = onFinished;
            }

            public void OnStart(long executionId) { }

            public void OnLog(long executionId, string message)
            {
                UnityEngine.Debug.Log($"[MIDI Recorder] FFmpeg: {message}");
            }

            public void OnWarning(long executionId, string message)
            {
                UnityEngine.Debug.LogWarning($"[MIDI Recorder] FFmpeg: {message}");
            }

            public void OnError(long executionId, string message)
            {
                UnityEngine.Debug.LogError($"[MIDI Recorder] FFmpeg: {message}");
            }

            public void OnSuccess(long executionId)
            {
                onFinished?.Invoke(true);
            }

            public void OnFail(long executionId)
            {
                onFinished?.Invoke(false);
            }

            public void OnCanceled(long executionId)
            {
                onFinished?.Invoke(false);
            }
        }

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

        private string GetRecordingsDirectory()
        {
            string projectDirectory = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectDirectory, "Recordings");
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private IEnumerator RenderMidiToWav(string midiPath, string wavPath)
        {
            string runtimeDirectory =
                Path.Combine(Application.streamingAssetsPath, "FluidSynth");

            string fluidsynthPath = Path.Combine(runtimeDirectory, "fluidsynth.exe");
            string soundfontPath = Path.Combine(runtimeDirectory, "FluidR3_GM.sf2");

            if (!File.Exists(fluidsynthPath) || !File.Exists(soundfontPath))
            {
                UnityEngine.Debug.LogError(
                    $"[MIDI Recorder] FluidSynth runtime or SoundFont is missing: " +
                    $"{runtimeDirectory}");

                SetStatus("Audio rendering files are missing. Check the Console.");
                yield break;
            }

            if (!File.Exists(midiPath))
            {
                UnityEngine.Debug.LogError(
                    $"[MIDI Recorder] MIDI file not found: {midiPath}");

                SetStatus("Could not render audio: MIDI file is missing.");
                yield break;
            }

            if (File.Exists(wavPath))
                File.Delete(wavPath);

            string arguments =
                "-ni -F " + QuoteArgument(wavPath) +
                " -r 44100 " +
                QuoteArgument(soundfontPath) + " " +
                QuoteArgument(midiPath);

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fluidsynthPath,
                    Arguments = arguments,
                    WorkingDirectory = runtimeDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        UnityEngine.Debug.Log($"[FluidSynth] {e.Data}");
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        UnityEngine.Debug.LogWarning($"[FluidSynth] {e.Data}");
                };

                bool started;

                try
                {
                    started = process.Start();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError(
                        $"[MIDI Recorder] Could not start FluidSynth: {ex}");

                    SetStatus("Could not start audio rendering.");
                    yield break;
                }

                if (!started)
                {
                    SetStatus("Could not start audio rendering.");
                    yield break;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.HasExited)
                    yield return null;

                if (process.ExitCode != 0 ||
                    !File.Exists(wavPath) ||
                    new FileInfo(wavPath).Length == 0)
                {
                    UnityEngine.Debug.LogError(
                        $"[MIDI Recorder] FluidSynth failed. Exit code: {process.ExitCode}");

                    SetStatus("Audio rendering failed. Check the Console.");
                    yield break;
                }
            }
        }

        private IEnumerator MuxAudioIntoVideo(
            string videoPath,
            string wavPath,
            string outputPath)
        {
            bool finished = false;
            bool succeeded = false;

            string temporaryOutput = outputPath + ".muxing.mp4";

            if (File.Exists(temporaryOutput))
                File.Delete(temporaryOutput);

            string arguments =
                "-y -i " + QuoteArgument(videoPath) +
                " -i " + QuoteArgument(wavPath) +
                " -map 0:v:0 -map 1:a:0" +
                " -c:v copy -c:a aac -b:a 192k" +
                " -af apad -shortest " +
                QuoteArgument(temporaryOutput);

            FFmpeg.Execute(
                arguments,
                new AudioMuxCallbacks(success =>
                {
                    succeeded = success;
                    finished = true;
                }));

            while (!finished)
                yield return null;

            if (!succeeded ||
                !File.Exists(temporaryOutput) ||
                new FileInfo(temporaryOutput).Length == 0)
            {
                if (File.Exists(temporaryOutput))
                    File.Delete(temporaryOutput);

                UnityEngine.Debug.LogError(
                    "[MIDI Recorder] FFmpeg audio muxing failed.");

                SetStatus("Adding audio to video failed. Check the Console.");
                yield break;
            }

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            File.Move(temporaryOutput, outputPath);
        }

        private IEnumerator SaveRecordingCoroutine(string filename)
        {
            isSaving = true;

            string recordingsDirectory = GetRecordingsDirectory();
            Directory.CreateDirectory(recordingsDirectory);

            string midiPath = Path.Combine(recordingsDirectory, filename + ".mid");
            string finalVideoPath = Path.Combine(recordingsDirectory, filename + ".mp4");

            if (pendingSaveMidi && !SaveMidiFile(filename))
            {
                isSaving = false;
                yield break;
            }

            if (pendingSaveVideo)
            {
                SetStatus("Finishing video encoding...");

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

                if (pendingSaveMidi)
                {
                    string wavPath = Path.Combine(
                        recordingsDirectory, filename + ".audio-temp.wav");

                    SetStatus("Rendering piano audio...");

                    yield return RenderMidiToWav(midiPath, wavPath);

                    if (!File.Exists(wavPath) ||
                        new FileInfo(wavPath).Length == 0)
                    {
                        isSaving = false;
                        yield break;
                    }

                    string muxedPath = Path.Combine(
                        recordingsDirectory, filename + ".mux-temp.mp4");

                    SetStatus("Adding piano audio to video...");

                    yield return MuxAudioIntoVideo(
                        videoPath, wavPath, muxedPath);

                    if (!File.Exists(muxedPath) ||
                        new FileInfo(muxedPath).Length == 0)
                    {
                        isSaving = false;
                        yield break;
                    }

                    if (File.Exists(finalVideoPath))
                        File.Delete(finalVideoPath);

                    File.Move(muxedPath, finalVideoPath);

                    if (File.Exists(videoPath))
                        File.Delete(videoPath);

                    if (File.Exists(wavPath))
                        File.Delete(wavPath);
                }

                UnityEngine.Debug.Log(
                    $"[MIDI Recorder] Video saved: {finalVideoPath}");
            }

            if (savePanel != null)
                savePanel.SetActive(false);

            if (pendingSaveMidi && pendingSaveVideo)
                SetStatus($"Saved MIDI and video with piano audio as '{filename}'.");
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
