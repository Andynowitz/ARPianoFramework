using UnityEngine;
using UnityEngine.UI;
using ARPIANO.Scripts.Piano;

namespace ARPIANO.Scripts.Tracking
{
    public class KeyboardCalibrationController : MonoBehaviour
    {
        [Header("Calibration")]
        [SerializeField] private KeyboardCalibration calibration;

        [Header("Calibration Points")]
        [SerializeField] private RectTransform topLeft;
        [SerializeField] private RectTransform topRight;
        [SerializeField] private RectTransform bottomRight;
        [SerializeField] private RectTransform bottomLeft;

        [Header("UI")]
        [SerializeField] private Button calibrateButton;
        [SerializeField] private GameObject calibrationPanel;
        [SerializeField] private GameObject modeSelectionPanel;

        [Header("Rectangle")]
        [SerializeField] private Color rectangleColor = Color.green;
        [SerializeField] private Color calibratedColor = Color.red;
        [SerializeField] private float rectangleThickness = 4f;

        public bool IsCalibrated { get; private set; }        private RectTransform canvasRect;

        private Image topLine;
        private Image rightLine;
        private Image bottomLine;
        private Image leftLine;

        private const string CalibrationSavedKey = "ARPiano_Calibration_Saved";
        private const string CalibrationVersionKey = "ARPiano_Calibration_Version";
        private const int CurrentCalibrationVersion = 2;
        private const string TopLeftXKey = "ARPiano_TopLeftX";
        private const string TopLeftYKey = "ARPiano_TopLeftY";
        private const string TopRightXKey = "ARPiano_TopRightX";
        private const string TopRightYKey = "ARPiano_TopRightY";
        private const string BottomRightXKey = "ARPiano_BottomRightX";
        private const string BottomRightYKey = "ARPiano_BottomRightY";
        private const string BottomLeftXKey = "ARPiano_BottomLeftX";
        private const string BottomLeftYKey = "ARPiano_BottomLeftY";

        private void Start()
        {
            LoadCalibration();
        }

        private void Awake()
        {
            canvasRect = GetComponent<RectTransform>();

            if (calibrateButton != null)
            {
                calibrateButton.onClick.AddListener(Calibrate);
            }

            CreateRectangle();
        }

        private void Update()
        {
            if (topLeft == null ||
                topRight == null ||
                bottomRight == null ||
                bottomLeft == null)
            {
                return;
            }

            UpdateRectangle();
        }

        public void Calibrate()
        {
            LockPoint(topLeft);
            LockPoint(topRight);
            LockPoint(bottomRight);
            LockPoint(bottomLeft);

            SetCalibrationColor();

            if (calibration == null)
            {
                Debug.LogError(
                    "KeyboardCalibrationController: " +
                    "KeyboardCalibration is not assigned.");

                return;
            }

            Debug.Log("========================================");
            Debug.Log("        KEYBOARD CALIBRATION");
            Debug.Log("========================================");

            LogPoint(
                "TopLeft C3",
                calibration.TopLeft);

            LogPoint(
                "TopRight C6",
                calibration.TopRight);

            LogPoint(
                "BottomRight C6",
                calibration.BottomRight);

            LogPoint(
                "BottomLeft C3",
                calibration.BottomLeft);

            float width = Vector2.Distance(
                calibration.TopLeft,
                calibration.TopRight);

            float height = Vector2.Distance(
                calibration.TopLeft,
                calibration.BottomLeft);

            Debug.Log(
                $"Calibration width: {width:F2} px");

            Debug.Log(
                $"Calibration height: {height:F2} px");

            Debug.Log(
                $"Keyboard range: " +
                $"{calibration.FirstMidiNote} → " +
                $"{calibration.LastMidiNote}");

            Debug.Log(
                $"White keys: {calibration.WhiteKeyCount}");

            float whiteKeyWidth =
                width / calibration.WhiteKeyCount;

            Debug.Log(
                $"Estimated white-key width: " +
                $"{whiteKeyWidth:F2} px");

            Debug.Log(
                $"Estimated white-key width: " +
                $"{whiteKeyWidth / 1280f:F4} normalized");

            Debug.Log(
                $"F#4 MIDI 66 is inside calibrated range: " +
                $"{66 >= calibration.FirstMidiNote && 66 <= calibration.LastMidiNote}");

            Debug.Log("========================================");
            Debug.Log("Keyboard calibration completed.");
            Debug.Log("========================================");

            VirtualPiano virtualPiano = FindAnyObjectByType<VirtualPiano>();

            if (virtualPiano != null)
            {
                bool generated = virtualPiano.GenerateVisibleRange(
                    calibration.FirstMidiNote,
                    calibration.WhiteKeyCount);

                Debug.Log(
                    $"Virtual piano generation after calibration: " +
                    $"success={generated}, " +
                    $"keys={virtualPiano.Keys.Count}");
            }
            else
            {
                Debug.LogError(
                    "KeyboardCalibrationController: VirtualPiano was not found.");
            }

            if (calibrateButton != null)
            {
                calibrateButton.interactable = false;
            }

            UpdateRectangle();

            // Calibration completed successfully.
            IsCalibrated = true;

            SaveCalibration();

            if (calibrationPanel != null)
                calibrationPanel.SetActive(false);

            if (modeSelectionPanel != null)
                modeSelectionPanel.SetActive(true);

        }

        private void LogPoint(
            string name,
            Vector2 point)
        {
            Debug.Log(
                $"{name}: " +
                $"x={point.x:F2}, y={point.y:F2}");
        }

        private void CreateRectangle()
        {
            topLine = CreateLine("CalibrationTopLine");
            rightLine = CreateLine("CalibrationRightLine");
            bottomLine = CreateLine("CalibrationBottomLine");
            leftLine = CreateLine("CalibrationLeftLine");
        }

        private Image CreateLine(string objectName)
        {
            GameObject lineObject =
                new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));

            lineObject.transform.SetParent(
                transform,
                false);

            Image image =
                lineObject.GetComponent<Image>();

            image.color = rectangleColor;
            image.raycastTarget = false;

            return image;
        }

        private void UpdateRectangle()
        {
            UpdateLine(
                topLine.rectTransform,
                topLeft.anchoredPosition,
                topRight.anchoredPosition);

            UpdateLine(
                rightLine.rectTransform,
                topRight.anchoredPosition,
                bottomRight.anchoredPosition);

            UpdateLine(
                bottomLine.rectTransform,
                bottomRight.anchoredPosition,
                bottomLeft.anchoredPosition);

            UpdateLine(
                leftLine.rectTransform,
                bottomLeft.anchoredPosition,
                topLeft.anchoredPosition);
        }

        private void UpdateLine(
            RectTransform line,
            Vector2 start,
            Vector2 end)
        {
            Vector2 direction = end - start;

            float length = direction.magnitude;

            Vector2 midpoint =
                (start + end) * 0.5f;

            line.anchoredPosition = midpoint;

            line.sizeDelta = new Vector2(
                length,
                rectangleThickness);

            float angle =
                Mathf.Atan2(
                    direction.y,
                    direction.x)
                * Mathf.Rad2Deg;

            line.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    angle);
        }

        private void SetCalibrationColor()
        {
            if (topLeft != null)
                SetPointColor(topLeft, calibratedColor);

            if (topRight != null)
                SetPointColor(topRight, calibratedColor);

            if (bottomRight != null)
                SetPointColor(bottomRight, calibratedColor);

            if (bottomLeft != null)
                SetPointColor(bottomLeft, calibratedColor);

            if (topLine != null)
                topLine.color = calibratedColor;

            if (rightLine != null)
                rightLine.color = calibratedColor;

            if (bottomLine != null)
                bottomLine.color = calibratedColor;

            if (leftLine != null)
                leftLine.color = calibratedColor;
        }

        private void SetPointColor(
            RectTransform point,
            Color color)
        {
            Image image = point.GetComponent<Image>();

            if (image != null)
                image.color = color;
        }

        private void LockPoint(RectTransform point)
        {
            if (point == null)
                return;

            KeyboardCalibrationUI calibrationUI =
                point.GetComponent<KeyboardCalibrationUI>();

            if (calibrationUI != null)
            {
                calibrationUI.Lock();
            }
        }

        public void ResetCalibration()
        {
            Debug.Log("KeyboardCalibrationController: ResetCalibration() called.");

            IsCalibrated = false;
            PlayerPrefs.DeleteKey(CalibrationSavedKey);
            PlayerPrefs.DeleteKey(CalibrationVersionKey);
            PlayerPrefs.Save();

            SetRectangleColor(rectangleColor);
            
            UnlockPoint(topLeft);
            UnlockPoint(topRight);
            UnlockPoint(bottomRight);
            UnlockPoint(bottomLeft);

            if (calibrateButton != null)
                calibrateButton.interactable = true;

            SetPointColor(topLeft, calibratedColor);
            SetPointColor(topRight, calibratedColor);
            SetPointColor(bottomRight, calibratedColor);
            SetPointColor(bottomLeft, calibratedColor);

            UpdateRectangle();

            Debug.Log($"Reset complete. IsCalibrated={IsCalibrated}");
        }

        private void UnlockPoint(RectTransform point)
        {
            if (point == null)
                return;

            KeyboardCalibrationUI pointUI =
                point.GetComponent<KeyboardCalibrationUI>();

            if (pointUI != null)
                pointUI.Unlock();
        }

        
        private void SetRectangleColor(Color color)
        {
            if (topLine != null)
                topLine.color = color;

            if (rightLine != null)
                rightLine.color = color;

            if (bottomLine != null)
                bottomLine.color = color;

            if (leftLine != null)
                leftLine.color = color;
        }

        
        private void SaveCalibration()
        {
            if (calibration == null ||
                !IsValidCalibration(calibration.GetImageCorners()))
            {
                Debug.LogError(
                    "Keyboard calibration was not saved because its " +
                    "corners are invalid.");
                return;
            }

            if (!HasCalibrationUI(topLeft) ||
                !HasCalibrationUI(topRight) ||
                !HasCalibrationUI(bottomRight) ||
                !HasCalibrationUI(bottomLeft))
            {
                Debug.LogError(
                    "Keyboard calibration was not saved because one or " +
                    "more calibration points are missing.");
                return;
            }

            SavePoint(TopLeftXKey, TopLeftYKey, topLeft);
            SavePoint(TopRightXKey, TopRightYKey, topRight);
            SavePoint(BottomRightXKey, BottomRightYKey, bottomRight);
            SavePoint(BottomLeftXKey, BottomLeftYKey, bottomLeft);

            PlayerPrefs.SetInt(CalibrationSavedKey, 1);
            PlayerPrefs.SetInt(
                CalibrationVersionKey,
                CurrentCalibrationVersion);
            PlayerPrefs.Save();

            Debug.Log("Keyboard calibration positions saved.");
        }

        private void SavePoint(string xKey, string yKey, RectTransform point)
        {
            KeyboardCalibrationUI pointUI =
                point != null
                    ? point.GetComponent<KeyboardCalibrationUI>()
                    : null;

            if (pointUI == null)
                return;

            Vector2 imagePoint = pointUI.GetImagePoint();
            PlayerPrefs.SetFloat(xKey, imagePoint.x);
            PlayerPrefs.SetFloat(yKey, imagePoint.y);
        }

        
        private void LoadCalibration()
        {
            if (PlayerPrefs.GetInt(CalibrationSavedKey, 0) != 1 ||
                PlayerPrefs.GetInt(
                    CalibrationVersionKey,
                    0) != CurrentCalibrationVersion)
            {
                if (PlayerPrefs.GetInt(CalibrationSavedKey, 0) == 1)
                {
                    PlayerPrefs.DeleteKey(CalibrationSavedKey);
                    PlayerPrefs.DeleteKey(CalibrationVersionKey);
                    PlayerPrefs.Save();
                    SetInitialPositions();
                }

                Debug.Log("No saved keyboard calibration found.");
                return;
            }

            if (!TryLoadPoint(
                    TopLeftXKey,
                    TopLeftYKey,
                    topLeft) ||
                !TryLoadPoint(
                    TopRightXKey,
                    TopRightYKey,
                    topRight) ||
                !TryLoadPoint(
                    BottomRightXKey,
                    BottomRightYKey,
                    bottomRight) ||
                !TryLoadPoint(
                    BottomLeftXKey,
                    BottomLeftYKey,
                    bottomLeft))
            {
                PlayerPrefs.DeleteKey(CalibrationSavedKey);
                PlayerPrefs.DeleteKey(CalibrationVersionKey);
                PlayerPrefs.Save();
                SetInitialPositions();
                Debug.LogWarning(
                    "Saved keyboard calibration is incomplete or invalid.");
                return;
            }

            if (calibration == null ||
                !IsValidCalibration(calibration.GetImageCorners()))
            {
                PlayerPrefs.DeleteKey(CalibrationSavedKey);
                PlayerPrefs.DeleteKey(CalibrationVersionKey);
                PlayerPrefs.Save();
                SetInitialPositions();
                Debug.LogWarning(
                    "Saved keyboard calibration geometry is invalid.");
                return;
            }

            UpdateRectangle();

            // Restore the calibrated state, lock the points,
            // regenerate the piano, and show the mode selection.
            Calibrate();

            Debug.Log("Saved keyboard calibration restored.");
        }

        private void SetInitialPositions()
        {
            topLeft?.GetComponent<KeyboardCalibrationUI>()
                ?.SetInitialPositionForCalibration();
            topRight?.GetComponent<KeyboardCalibrationUI>()
                ?.SetInitialPositionForCalibration();
            bottomRight?.GetComponent<KeyboardCalibrationUI>()
                ?.SetInitialPositionForCalibration();
            bottomLeft?.GetComponent<KeyboardCalibrationUI>()
                ?.SetInitialPositionForCalibration();
        }

        private bool HasCalibrationUI(RectTransform point)
        {
            return point != null &&
                point.GetComponent<KeyboardCalibrationUI>() != null;
        }

        private bool IsValidCalibration(Vector2[] corners)
        {
            if (corners == null || corners.Length != 4)
                return false;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 point = corners[i];

                if (float.IsNaN(point.x) ||
                    float.IsNaN(point.y) ||
                    float.IsInfinity(point.x) ||
                    float.IsInfinity(point.y) ||
                    point.x < 0f ||
                    point.x > 1280f ||
                    point.y < 0f ||
                    point.y > 720f)
                    return false;
            }

            float width =
                Vector2.Distance(corners[0], corners[1]);

            float height =
                Vector2.Distance(corners[0], corners[3]);

            return width > 1f && height > 1f;
        }

        private bool TryLoadPoint(
            string xKey,
            string yKey,
            RectTransform point)
        {
            if (point == null)
                return false;

            if (!PlayerPrefs.HasKey(xKey) ||
                !PlayerPrefs.HasKey(yKey))
                return false;

            Vector2 imagePoint = new Vector2(
                PlayerPrefs.GetFloat(xKey),
                PlayerPrefs.GetFloat(yKey));

            if (float.IsNaN(imagePoint.x) ||
                float.IsNaN(imagePoint.y) ||
                float.IsInfinity(imagePoint.x) ||
                float.IsInfinity(imagePoint.y) ||
                imagePoint.x < 0f ||
                imagePoint.x > 1280f ||
                imagePoint.y < 0f ||
                imagePoint.y > 720f)
                return false;

            KeyboardCalibrationUI pointUI =
                point.GetComponent<KeyboardCalibrationUI>();

            return pointUI != null &&
                pointUI.SetImagePoint(imagePoint);
        }

    }
}