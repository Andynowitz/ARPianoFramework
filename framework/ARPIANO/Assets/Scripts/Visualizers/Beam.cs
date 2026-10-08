using UnityEngine;
using UnityEngine.UI;

namespace ARPIANO.Scripts.Visualizers
{
    public class Beam : MonoBehaviour
    {
        public int MidiNumber { get; private set; }

        private RectTransform rectTransform;
        private Image image;

        private Vector2 startPosition;
        private Vector2 targetPosition;
        private Vector2 direction;

        private float speed = 400f;
        private bool active;

        public void Initialize(
            int midi,
            Vector2 start,
            Vector2 target)
        {
            MidiNumber = midi;

            startPosition = start;
            targetPosition = target;

            direction = (targetPosition - startPosition).normalized;

            rectTransform = GetComponent<RectTransform>();
            image = GetComponent<Image>();

            if (rectTransform == null)
            {
                Debug.LogError("Beam requires a RectTransform.");
                return;
            }

            rectTransform.anchoredPosition = startPosition;

            active = true;
        }

        private void Update()
        {
            if (!active || rectTransform == null)
                return;

            Vector2 currentPosition = rectTransform.anchoredPosition;

            currentPosition += direction * speed * Time.deltaTime;

            rectTransform.anchoredPosition = currentPosition;

            if (Vector2.Dot(
                    targetPosition - currentPosition,
                    direction) <= 0f)
            {
                rectTransform.anchoredPosition = targetPosition;
                Finish();
            }
        }

        public void Finish()
        {
            active = false;
            Destroy(gameObject);
        }
    }
}