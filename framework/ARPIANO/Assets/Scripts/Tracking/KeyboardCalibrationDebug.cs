using UnityEngine;

namespace ARPIANO.Scripts.Tracking
{
    public class KeyboardCalibrationDebug : MonoBehaviour
    {
        [Header("Calibration")]
        [SerializeField] private KeyboardCalibration calibration;

        [Header("Debug MIDI Notes")]
        [SerializeField] private int[] testMidiNotes =
        {
            48, // C3
            60, // C4
            66, // F#4
            72, // C5
            84  // C6
        };

        private KeyboardHomography homography;
        private KeyboardNoteMapper noteMapper;

        private void Start()
        {
            if (calibration == null)
            {
                Debug.LogError(
                    "KeyboardCalibrationDebug: No KeyboardCalibration assigned.");
                return;
            }

            Debug.Log("=== Keyboard Calibration Debug ===");

            LogCalibrationPoints();

            CreateMapping();

            if (noteMapper != null)
            {
                LogTestNotes();
            }
        }

        private void CreateMapping()
        {
            homography = new KeyboardHomography(
                calibration.TopLeft,
                calibration.TopRight,
                calibration.BottomRight,
                calibration.BottomLeft);

            noteMapper = new KeyboardNoteMapper(
                calibration.FirstMidiNote,
                calibration.LastMidiNote);

            Debug.Log(
                $"Keyboard range: " +
                $"{calibration.FirstMidiNote} → {calibration.LastMidiNote}");

            Debug.Log(
                $"White key count: {calibration.WhiteKeyCount}");
        }

        private void LogCalibrationPoints()
        {
            Debug.Log(
                $"TopLeft C3: {calibration.TopLeft}");

            Debug.Log(
                $"TopRight C6: {calibration.TopRight}");

            Debug.Log(
                $"BottomRight C6: {calibration.BottomRight}");

            Debug.Log(
                $"BottomLeft C3: {calibration.BottomLeft}");
        }

        private void LogTestNotes()
        {
            foreach (int midiNote in testMidiNotes)
            {
                float normalizedX =
                    noteMapper.GetNormalizedCenter(midiNote);

                if (normalizedX < 0f)
                {
                    Debug.LogWarning(
                        $"MIDI {midiNote} is outside the calibrated range.");
                    continue;
                }

                Vector2 normalizedPoint =
                    new Vector2(normalizedX, 0.5f);

                Vector2 imagePoint =
                    homography.NormalizedToImage(normalizedPoint);

                Debug.Log(
                    $"MIDI {midiNote}: " +
                    $"normalizedX={normalizedX:F4}, " +
                    $"imagePoint={imagePoint}");
            }
        }

        private void OnDestroy()
        {
            homography?.Dispose();
        }
    }
}