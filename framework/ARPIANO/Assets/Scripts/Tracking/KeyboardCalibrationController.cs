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

        [Header("Rectangle")]
        [SerializeField] private Color rectangleColor = Color.green;
        [SerializeField] private Color calibratedColor = Color.red;
        [SerializeField] private float rectangleThickness = 4f;

        public bool IsCalibrated { get; private set; }        private RectTransform canvasRect;

        private Image topLine;
        private Image rightLine;
        private Image bottomLine;
        private Image leftLine;

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
            IsCalibrated = true;

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

    }
}