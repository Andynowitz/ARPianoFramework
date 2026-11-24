using UnityEngine;

namespace ARPIANO.Scripts.Diagnostics
{
    // Standalone player start-up settings.
    //
    // 1. Webcam/MIDI driven players are used while the user looks at a physical
    //    keyboard, so the window is often not focused or partly covered. Unity
    //    stops updating a standalone player in that state, and with the D3D12
    //    backend that surfaces as multi-second freezes alternating with short
    //    bursts of normal frames. Keeping the player running avoids it.
    // 2. Standalone players write every Debug.Log to Player.log, and extracting
    //    the managed stack trace is the most expensive part of that call. Builds
    //    log MIDI/beam events at note rate. Warnings and errors keep theirs.
    // 3. QualitySettings use vSyncCount 0 and targetFrameRate stays -1, so a
    //    player submits hundreds of frames per second into a flip-model DXGI
    //    swapchain. The desktop compositor cannot keep up and Present blocks for
    //    hundreds of milliseconds, which is the periodic freeze the Editor never
    //    shows (the Editor presents into its own window and its loop is paced).
    //    Capping the player at display rate keeps pacing stable.
    internal static class PlayerLoggingSetup
    {
        private const int StandaloneFrameRateCap = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {

#if !UNITY_EDITOR
            Application.runInBackground = true;
            if (Application.targetFrameRate <= 0 && QualitySettings.vSyncCount == 0)
                Application.targetFrameRate = StandaloneFrameRateCap;

            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
#endif
        }
    }
}
