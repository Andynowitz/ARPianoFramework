using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ARPIANO.Scripts.Tracking
{
    public class WebcamCapture : MonoBehaviour
    {
        [SerializeField] private int requestedWidth = 1280;
        [SerializeField] private int requestedHeight = 720;
        [SerializeField] private int requestedFps = 30;

        public WebCamTexture ActiveTexture { get; private set; }

        private Canvas webcamCanvas;
        private RawImage preview;
        private Coroutine startupRoutine;

        private void OnEnable()
        {
            StartWebcam();
        }

        private void OnDisable()
        {
            StopWebcam();
        }

        private void OnDestroy()
        {
            StopWebcam();
        }

        private void StartWebcam()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            Debug.Log($"Available webcams: {devices.Length}");

            webcamCanvas = GetOrCreateWebcamCanvas();
            preview = GetOrCreateWebcamPreview(webcamCanvas);
            CreateOrConfigureWebcamBackground(webcamCanvas);
            HideStaticPianoPreview();

            if (devices.Length == 0)
            {
                Debug.LogWarning("No webcam is available.");
                return;
            }

            WebCamDevice selectedDevice = devices[0];
            Debug.Log($"Selected webcam: {selectedDevice.name}");
            Debug.Log($"Requested webcam resolution: {requestedWidth}x{requestedHeight} at {requestedFps} FPS");

            ActiveTexture = new WebCamTexture(
                selectedDevice.name,
                requestedWidth,
                requestedHeight,
                requestedFps);

            preview.texture = ActiveTexture;
            preview.color = Color.white;
            preview.uvRect = new Rect(0f, 0f, 1f, 1f);
            preview.enabled = true;
            preview.gameObject.SetActive(true);

            UpdatePreviewLayout((float)requestedWidth / requestedHeight);
            preview.transform.SetAsLastSibling();

            ActiveTexture.Play();
            LogPreviewRenderState();
            startupRoutine = StartCoroutine(LogActualResolutionWhenReady());
        }

        private Canvas GetOrCreateWebcamCanvas()
        {
            GameObject canvasObject = GameObject.Find("WebcamCanvas");
            Canvas canvas = canvasObject != null
                ? canvasObject.GetComponent<Canvas>()
                : null;

            if (canvas == null)
            {
                canvasObject = new GameObject("WebcamCanvas");
                canvas = canvasObject.AddComponent<Canvas>();
                CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = true;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 10000;
            canvas.gameObject.SetActive(true);
            webcamCanvas = canvas;
            return canvas;
        }

        private RawImage GetOrCreateWebcamPreview(Canvas canvas)
        {
            Transform previewTransform = canvas.transform.Find("WebcamPreview");
            RawImage webcamPreview = previewTransform != null
                ? previewTransform.GetComponent<RawImage>()
                : null;

            if (webcamPreview == null)
            {
                GameObject imageObject = new GameObject("WebcamPreview");
                imageObject.transform.SetParent(canvas.transform, false);
                webcamPreview = imageObject.AddComponent<RawImage>();
            }

            webcamPreview.gameObject.name = "WebcamPreview";
            webcamPreview.color = Color.white;
            webcamPreview.enabled = true;
            webcamPreview.gameObject.SetActive(true);
            return webcamPreview;
        }

        private void CreateOrConfigureWebcamBackground(Canvas canvas)
        {
            Transform backgroundTransform = canvas.transform.Find("WebcamBackground");
            Image background = backgroundTransform != null
                ? backgroundTransform.GetComponent<Image>()
                : null;

            if (background == null)
            {
                GameObject backgroundObject = new GameObject("WebcamBackground");
                backgroundObject.transform.SetParent(canvas.transform, false);
                background = backgroundObject.AddComponent<Image>();
            }

            background.gameObject.name = "WebcamBackground";
            background.color = new Color(0.03f, 0.03f, 0.03f, 1f);
            background.enabled = true;
            background.gameObject.SetActive(true);

            RectTransform rect = background.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            background.transform.SetAsFirstSibling();
        }

        private void HideStaticPianoPreview()
        {
            RawImage[] images = FindObjectsByType<RawImage>(FindObjectsInactive.Include);
            foreach (RawImage image in images)
            {
                if (image == preview)
                    continue;

                string objectName = image.gameObject.name.ToLowerInvariant();
                string textureName = image.texture != null ? image.texture.name.ToLowerInvariant() : string.Empty;
                if (objectName.Contains("piano88") || textureName.Contains("piano88"))
                    image.gameObject.SetActive(false);
            }

            GameObject pianoObject = GameObject.Find("Piano88");
            if (pianoObject != null)
            {
                Renderer[] renderers = pianoObject.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                    renderer.enabled = false;
            }
        }

        private void UpdatePreviewLayout(float aspectRatio)
        {
            if (preview == null)
                return;

            RectTransform rect = preview.rectTransform;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private void LogPreviewRenderState()
        {
            Rect rect = preview.rectTransform.rect;
            Debug.Log(
                $"Webcam preview state: activeInHierarchy={preview.gameObject.activeInHierarchy}, " +
                $"enabled={preview.enabled}, textureAssigned={preview.texture != null}, " +
                $"width={rect.width}, height={rect.height}, canvasEnabled={webcamCanvas.enabled}, " +
                $"renderMode={webcamCanvas.renderMode}, sortingOrder={webcamCanvas.sortingOrder}");
        }

        private IEnumerator LogActualResolutionWhenReady()
        {
            while (ActiveTexture != null && ActiveTexture.isPlaying &&
                   ActiveTexture.width <= 16 && ActiveTexture.height <= 16)
            {
                yield return null;
            }

            if (ActiveTexture != null)
            {
                Debug.Log($"Actual webcam resolution: {ActiveTexture.width}x{ActiveTexture.height}");

                if (ActiveTexture.height > 0)
                {
                    UpdatePreviewLayout((float)ActiveTexture.width / ActiveTexture.height);
                    LogPreviewRenderState();
                }
            }

            startupRoutine = null;
        }

        private void StopWebcam()
        {
            if (startupRoutine != null)
            {
                StopCoroutine(startupRoutine);
                startupRoutine = null;
            }

            if (ActiveTexture == null)
                return;

            if (ActiveTexture.isPlaying)
                ActiveTexture.Stop();

            if (preview != null && preview.texture == ActiveTexture)
                preview.texture = null;

            ActiveTexture = null;
        }
    }
}
