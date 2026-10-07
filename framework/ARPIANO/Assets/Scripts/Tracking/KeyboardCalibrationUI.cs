using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARPIANO.Scripts.Tracking
{
    public class KeyboardCalibrationUI :
        MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        public enum PointType
        {
            TopLeft,
            TopRight,
            BottomRight,
            BottomLeft
        }

        [SerializeField] private PointType pointType;
        [SerializeField] private RectTransform webcamRect;
        [SerializeField] private KeyboardCalibration calibration;
        [SerializeField] private KeyboardCalibrationController calibrationController;
        private Image pointImage;
        private RectTransform pointRect;

        private void Awake()
        {
            pointRect = GetComponent<RectTransform>();
            pointImage = GetComponent<Image>();
        }

        private void Start()
        {
            if (webcamRect == null)
            {
                Debug.LogError(
                    $"KeyboardCalibrationUI ({pointType}): " +
                    "Webcam Rect is not assigned.");

                return;
            }

            if (calibration == null)
            {
                Debug.LogError(
                    $"KeyboardCalibrationUI ({pointType}): " +
                    "KeyboardCalibration is not assigned.");

                return;
            }

            SetInitialPosition();
        }


        private void UpdatePosition(PointerEventData eventData)
        {
            if (webcamRect == null || calibration == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    webcamRect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            Rect rect = webcamRect.rect;

            localPoint.x = Mathf.Clamp(
                localPoint.x,
                rect.xMin,
                rect.xMax);

            localPoint.y = Mathf.Clamp(
                localPoint.y,
                rect.yMin,
                rect.yMax);

            pointRect.anchoredPosition = localPoint;

            UpdateCalibration(localPoint, rect);
        }

        private void SetInitialPosition()
        {
            Rect rect = webcamRect.rect;

            Vector2 normalizedPosition = pointType switch
            {
                PointType.TopLeft =>
                    new Vector2(0.25f, 0.70f),

                PointType.TopRight =>
                    new Vector2(0.75f, 0.70f),

                PointType.BottomRight =>
                    new Vector2(0.75f, 0.30f),

                PointType.BottomLeft =>
                    new Vector2(0.25f, 0.30f),

                _ => new Vector2(0.5f, 0.5f)
            };

            Vector2 localPoint = new Vector2(
                Mathf.Lerp(
                    rect.xMin,
                    rect.xMax,
                    normalizedPosition.x),

                Mathf.Lerp(
                    rect.yMin,
                    rect.yMax,
                    normalizedPosition.y));

            pointRect.anchoredPosition = localPoint;

            UpdateCalibration(localPoint, rect);
        }

        private void UpdateCalibration(
            Vector2 localPoint,
            Rect rect)
        {
            float u = Mathf.InverseLerp(
                rect.xMin,
                rect.xMax,
                localPoint.x);

            float v = Mathf.InverseLerp(
                rect.yMin,
                rect.yMax,
                localPoint.y);

            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            Vector2 imagePoint = new Vector2(
                u * 1280f,
                (1f - v) * 720f);

            ApplyCalibrationPoint(imagePoint);
        }

        private Vector2 GetCurrentImagePoint()
        {
            Rect rect = webcamRect.rect;

            float u = Mathf.InverseLerp(
                rect.xMin,
                rect.xMax,
                pointRect.anchoredPosition.x);

            float v = Mathf.InverseLerp(
                rect.yMin,
                rect.yMax,
                pointRect.anchoredPosition.y);

            return new Vector2(
                u * 1280f,
                (1f - v) * 720f);
        }

        private void ApplyCalibrationPoint(Vector2 imagePoint)
        {
            switch (pointType)
            {
                case PointType.TopLeft:
                    calibration.TopLeft = imagePoint;
                    break;

                case PointType.TopRight:
                    calibration.TopRight = imagePoint;
                    break;

                case PointType.BottomRight:
                    calibration.BottomRight = imagePoint;
                    break;

                case PointType.BottomLeft:
                    calibration.BottomLeft = imagePoint;
                    break;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (calibrationController != null &&
                calibrationController.IsCalibrated)
                return;

            UpdatePosition(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (calibrationController != null &&
                calibrationController.IsCalibrated)
                return;

            UpdatePosition(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (calibrationController != null &&
                calibrationController.IsCalibrated)
                return;

            UpdatePosition(eventData);

            Vector2 imagePoint = GetCurrentImagePoint();

            Debug.Log(
                $"Calibration point moved: " +
                $"{pointType} = ({imagePoint.x:F2}, {imagePoint.y:F2})");
        }

        public void Lock()
        {
            if (pointImage != null)
            {
                pointImage.raycastTarget = false;
            }
        }

    }
}