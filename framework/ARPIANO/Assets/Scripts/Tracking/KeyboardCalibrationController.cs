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

            PlayerPrefs.DeleteKey("ARPiano_Calibration_Saved");
            
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
            SavePoint(TopLeftXKey, TopLeftYKey, topLeft);
            SavePoint(TopRightXKey, TopRightYKey, topRight);
            SavePoint(BottomRightXKey, BottomRightYKey, bottomRight);
            SavePoint(BottomLeftXKey, BottomLeftYKey, bottomLeft);

            PlayerPrefs.SetInt(CalibrationSavedKey, 1);
            PlayerPrefs.Save();

            Debug.Log("Keyboard calibration positions saved.");
        }

        private void SavePoint(string xKey, string yKey, RectTransform point)
        {
            PlayerPrefs.SetFloat(xKey, point.anchoredPosition.x);
            PlayerPrefs.SetFloat(yKey, point.anchoredPosition.y);
        }

        
        private void LoadCalibration()
        {
            if (PlayerPrefs.GetInt(CalibrationSavedKey, 0) != 1)
            {
                Debug.Log("No saved keyboard calibration found.");
                return;
            }

            LoadPoint(TopLeftXKey, TopLeftYKey, topLeft);
            LoadPoint(TopRightXKey, TopRightYKey, topRight);
            LoadPoint(BottomRightXKey, BottomRightYKey, bottomRight);
            LoadPoint(BottomLeftXKey, BottomLeftYKey, bottomLeft);

            // Synchronize the model with the restored UI points.
            UpdateCalibrationFromUI();

            UpdateRectangle();

            // Restore the calibrated state, lock the points,
            // regenerate the piano, and show the mode selection.
            Calibrate();

            Debug.Log("Saved keyboard calibration restored.");
        }

        private void UpdateCalibrationFromUI()
        {
            UpdateModelPoint(topLeft, point => calibration.TopLeft = point);
            UpdateModelPoint(topRight, point => calibration.TopRight = point);
            UpdateModelPoint(bottomRight, point => calibration.BottomRight = point);
            UpdateModelPoint(bottomLeft, point => calibration.BottomLeft = point);
        }

        private void UpdateModelPoint(
            RectTransform point,
            System.Action<Vector2> applyPoint)
        {
            if (point == null || calibration == null)
                return;

            KeyboardCalibrationUI pointUI =
                point.GetComponent<KeyboardCalibrationUI>();

            if (pointUI == null)
                return;

            // Convert the saved UI position into the model's image coordinates.
            Rect rect = pointUI.GetComponent<RectTransform>().parent
                .GetComponent<RectTransform>().rect;

            float u = Mathf.InverseLerp(
                rect.xMin, rect.xMax, point.anchoredPosition.x);

            float v = Mathf.InverseLerp(
                rect.yMin, rect.yMax, point.anchoredPosition.y);

            applyPoint(new Vector2(u * 1280f, (1f - v) * 720f));
        }

        private void LoadPoint(string xKey, string yKey, RectTransform point)
        {
            if (point == null)
                return;

            point.anchoredPosition = new Vector2(
                PlayerPrefs.GetFloat(xKey),
                PlayerPrefs.GetFloat(yKey));
        }

    }
}