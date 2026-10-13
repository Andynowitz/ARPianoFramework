using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using FFmpegUnityBind2;

namespace ARPIANO.Scripts.Recording
{
    public class ScreenMp4Recorder : MonoBehaviour
    {
        [Header("Recording")]
        [SerializeField, Min(1)] private int frameRate = 60;
        [SerializeField, Min(320)] private int captureWidth = 1280;
        [SerializeField, Min(180)] private int captureHeight = 720;

        [Header("Safety Limits")]
        [SerializeField, Min(1)] private int maxRecordingSeconds = 120;
        [SerializeField, Min(1)] private int maxFrames = 7200;

        [Header("Memory Limits")]
        [SerializeField, Min(1)] private int maxPendingReadbacks = 3;
        [SerializeField, Min(1)] private int maxQueuedFrames = 4;

        private const int BytesPerPixel = 3;

        private bool isRecording;
        private bool isEncoding;
        private bool captureFailed;

        private int frameCount;
        private int droppedFrames;
        private float nextCaptureTime;
        private float recordingStartTime;

        private string framesDirectory;
        private string rawVideoPath;
        private string outputPath;

        private RenderTexture screenCaptureTarget;
        private RenderTexture captureTarget;
        private Coroutine captureCoroutine;
        private FFmpegProcess ffmpegProcess;

        private BlockingCollection<byte[]> frameQueue;
        private Task writerTask;

        public bool IsRecording => isRecording;
        public bool IsEncoding => isEncoding;

        private string RecordingsDirectory =>
            Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Recordings",
                "Video");

        private sealed class PendingFrame
        {
            public AsyncGPUReadbackRequest Request;
        }

        /*private void Update()
        {
            if (Keyboard.current == null)
                return;

            if (Keyboard.current.f9Key.wasPressedThisFrame)
                StartRecording();

            if (Keyboard.current.f10Key.wasPressedThisFrame)
                StopRecording();
        } */

        public void StartRecording()
        {
            if (isRecording || isEncoding)
                return;

            try
            {
                Directory.CreateDirectory(RecordingsDirectory);

                string timestamp = DateTime.Now.ToString(
                    "yyyyMMdd_HHmmss");

                framesDirectory = Path.Combine(
                    RecordingsDirectory, $"frames_{timestamp}");

                Directory.CreateDirectory(framesDirectory);

                rawVideoPath = Path.Combine(
                    framesDirectory, "capture.rgb");

                outputPath = Path.Combine(
                    RecordingsDirectory, $"recording_{timestamp}.mp4");

                screenCaptureTarget = new RenderTexture(
                    Screen.width,
                    Screen.height,
                    0,
                    RenderTextureFormat.ARGB32);

                screenCaptureTarget.Create();

                captureTarget = new RenderTexture(
                    captureWidth,
                    captureHeight,
                    0,
                    RenderTextureFormat.ARGB32);

                captureTarget.Create();

                frameQueue = new BlockingCollection<byte[]>(
                    maxQueuedFrames);

                frameCount = 0;
                droppedFrames = 0;
                captureFailed = false;

                nextCaptureTime = Time.realtimeSinceStartup;
                recordingStartTime = Time.realtimeSinceStartup;

                // A background worker writes raw frame data to disk.
                writerTask = Task.Run(() =>
                {
                    using (var stream = new FileStream(
                        rawVideoPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024))
                    {
                        foreach (byte[] frame in
                                 frameQueue.GetConsumingEnumerable())
                        {
                            stream.Write(frame, 0, frame.Length);
                        }
                    }
                });

                isRecording = true;
                captureCoroutine = StartCoroutine(CaptureFrames());

                Debug.Log($"Screen recording started: {outputPath}");
            }
            catch (Exception exception)
            {
                isRecording = false;
                CleanupCaptureResources();

                Debug.LogError(
                    $"Could not start recording: {exception}");
            }
        }

        public void StopRecording()
        {
            if (!isRecording)
                return;

            isRecording = false;

            Debug.Log("Stopping capture; pending frames will be processed.");
            // CaptureFrames drains outstanding GPU requests and finalizes
            // the background writer before starting FFmpeg.
        }

        private IEnumerator CaptureFrames()
        {
            float interval = 1f / frameRate;
            var pending = new System.Collections.Generic.List<PendingFrame>();

            while (isRecording || pending.Count > 0)
            {
                if (isRecording)
                    yield return new WaitForEndOfFrame();
                else
                    yield return null;

                // Process completed requests in order to preserve frame order.
                while (pending.Count > 0 && pending[0].Request.done)
                {
                    AsyncGPUReadbackRequest request = pending[0].Request;
                    pending.RemoveAt(0);

                    if (request.hasError)
                    {
                        captureFailed = true;
                        droppedFrames++;
                        Debug.LogError("GPU readback failed.");
                        continue;
                    }

                    try
                    {
                        // Copy GPU-owned memory before the request expires.
                        byte[] data = request.GetData<byte>().ToArray();

                        // Never wait for disk I/O on the Unity main thread.
                        if (frameQueue.TryAdd(data))
                        {
                            frameCount++;
                        }
                        else
                        {
                            droppedFrames++;
                        }
                    }
                    catch (Exception exception)
                    {
                        captureFailed = true;
                        Debug.LogError(
                            $"Could not process captured frame: {exception.Message}");
                    }
                }

                if (!isRecording)
                    continue;

                float elapsed =
                    Time.realtimeSinceStartup - recordingStartTime;

                if (elapsed >= maxRecordingSeconds ||
                    frameCount + pending.Count >= maxFrames)
                {
                    Debug.LogWarning(
                        $"Recording limit reached at {elapsed:F1}s.");
                    isRecording = false;
                    continue;
                }

                if (Time.realtimeSinceStartup < nextCaptureTime)
                    continue;

                nextCaptureTime += interval;

                if (pending.Count >= maxPendingReadbacks)
                {
                    droppedFrames++;
                    continue;
                }

                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(
                        screenCaptureTarget);

                    Graphics.Blit(screenCaptureTarget, captureTarget);

                    var item = new PendingFrame
                    {
                        Request = AsyncGPUReadback.Request(
                            captureTarget, 0, TextureFormat.RGB24)
                    };

                    pending.Add(item);
                }
                catch (Exception exception)
                {
                    captureFailed = true;
                    isRecording = false;

                    Debug.LogError(
                        $"Screen capture failed: {exception}");
                }
            }

            captureCoroutine = null;

            // No more frames can be added after capture stops.
            frameQueue.CompleteAdding();

            while (!writerTask.IsCompleted)
                yield return null;

            if (writerTask.IsFaulted)
            {
                Debug.LogError(
                    $"Frame writer failed: {writerTask.Exception}");

                CleanupCaptureResources();
                isEncoding = false;
                yield break;
            }

            CleanupCaptureResources();

            Debug.Log(
                $"Capture finished: {frameCount} frames accepted, " +
                $"{droppedFrames} frames dropped.");

            if (captureFailed)
            {
                Debug.LogWarning(
                    "One or more capture operations failed. " +
                    "Check the Console for details.");
            }

            if (frameCount == 0)
            {
                Debug.LogWarning("No frames were saved.");
                isEncoding = false;
                yield break;
            }

            StartCoroutine(EncodeVideo());
        }

        private IEnumerator EncodeVideo()
        {
            isEncoding = true;

            float elapsed = Mathf.Max(
                Time.realtimeSinceStartup - recordingStartTime,
                0.1f);

            // Match playback duration to elapsed capture time.
            float effectiveFrameRate = frameCount / elapsed;
            effectiveFrameRate = Mathf.Max(effectiveFrameRate, 1f);

            string fps = effectiveFrameRate.ToString(
                CultureInfo.InvariantCulture);

            string rawPath = rawVideoPath.Replace("\\", "/");
            string videoPath = outputPath.Replace("\\", "/");

            string command =
                $"-y -f rawvideo " +
                $"-pixel_format rgb24 " +
                $"-video_size {captureWidth}x{captureHeight} " +
                $"-framerate {fps} " +
                $"-i \"{rawPath}\" " +
                "-c:v libx264 -pix_fmt yuv420p " +
                $"\"{videoPath}\"";

            Debug.Log(
                $"Encoding {frameCount} frames over {elapsed:F2}s " +
                $"at {fps} FPS.");

            var callbacks = new RecordingCallbacks(success =>
            {
                if (success && File.Exists(outputPath))
                {
                    Debug.Log($"MP4 recording saved: {outputPath}");

                    try
                    {
                        if (Directory.Exists(framesDirectory))
                        {
                            Directory.Delete(framesDirectory, true);
                        }

                        Debug.Log(
                            $"Temporary recording folder removed: {framesDirectory}");
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"Failed to delete temporary recording folder " +
                            $"'{framesDirectory}': {exception}");
                    }
                }
                else
                {
                    Debug.LogError(
                        $"FFmpeg failed or MP4 is missing. " +
                        $"Temporary data retained at: {framesDirectory}");
                }

                isEncoding = false;
            });

            ffmpegProcess = FFmpeg.Execute(command, callbacks);

            while (isEncoding && ffmpegProcess != null &&
                   !ffmpegProcess.IsDone)
            {
                yield return null;
            }
        }

        private void CleanupCaptureResources()
        {
            if (screenCaptureTarget != null)
            {
                screenCaptureTarget.Release();
                Destroy(screenCaptureTarget);
                screenCaptureTarget = null;
            }

            if (captureTarget != null)
            {
                captureTarget.Release();
                Destroy(captureTarget);
                captureTarget = null;
            }

            if (frameQueue != null)
            {
                frameQueue.Dispose();
                frameQueue = null;
            }
        }

        private void OnDisable()
        {
            if (isRecording)
                StopRecording();
        }

        private sealed class RecordingCallbacks : IFFmpegCallbacksHandler
        {
            private readonly Action<bool> onFinished;

            public RecordingCallbacks(Action<bool> onFinished)
            {
                this.onFinished = onFinished;
            }

            public void OnStart(long executionId) { }

            public void OnLog(long executionId, string message)
            {
                Debug.Log($"FFmpeg: {message}");
            }

            public void OnWarning(long executionId, string message)
            {
                Debug.LogWarning($"FFmpeg: {message}");
            }

            public void OnError(long executionId, string message)
            {
                Debug.LogError($"FFmpeg: {message}");
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

        public bool IsProcessing =>
            isRecording || isEncoding || captureCoroutine != null;

        public bool TryRenameCompletedRecording(
            string fileName,
            out string savedPath)
        {
            savedPath = null;

            if (IsProcessing || string.IsNullOrEmpty(outputPath))
                return false;

            if (!File.Exists(outputPath))
                return false;

            string safeName = Path.GetFileNameWithoutExtension(fileName);

            if (string.IsNullOrWhiteSpace(safeName))
                return false;

            string destination = Path.Combine(
                RecordingsDirectory,
                safeName + ".mp4");

            try
            {
                if (string.Equals(
                    outputPath,
                    destination,
                    StringComparison.OrdinalIgnoreCase))
                {
                    savedPath = destination;
                    return true;
                }

                if (File.Exists(destination))
                    File.Delete(destination);

                File.Move(outputPath, destination);

                outputPath = destination;
                savedPath = destination;
                Debug.Log($"MP4 recording saved: {destination}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not rename MP4 recording: {exception}");
                return false;
            }
        }

    }
}