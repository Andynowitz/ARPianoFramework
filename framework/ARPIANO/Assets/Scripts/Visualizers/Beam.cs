using UnityEngine;
using UnityEngine.UI;

namespace ARPIANO.Scripts.Visualizers
{
    public class Beam : MonoBehaviour
    {
        public int MidiNumber { get; private set; }

        private RectTransform rectTransform;
        private Image image;

        private Vector2 direction;
        private Vector2 targetPosition;

        private float speed = 400f;
        private float width;
        private float spawnTime;
        private float noteStartTime;
        private float noteEndTime;
        private Vector2 spawnPosition;

        private bool active;

        public void Initialize(
            int midi,
            Vector2 start,
            Vector2 target,
            float width,
            float beamLength,
            Color color,
            float rotationAngle,
            float spawnTime,
            float noteStart,
            float duration,
            float movementSpeed)
        {
            MidiNumber = midi;

            this.width = width;
            this.spawnTime = spawnTime;
            noteStartTime = noteStart;
            noteEndTime = noteStart + duration;
            speed = movementSpeed;
            spawnPosition = start;
            targetPosition = target;

            direction =
                (target - start).normalized;

            rectTransform =
                GetComponent<RectTransform>();

            image =
                GetComponent<Image>();

            if (rectTransform == null)
            {
                Debug.LogError(
                    "Beam requires a RectTransform.");
                return;
            }

            if (image == null)
            {
                Debug.LogError(
                    "Beam requires an Image component.");
                return;
            }

            rectTransform.sizeDelta =
                new Vector2(
                    width,
                    beamLength);

            rectTransform.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    rotationAngle);

            image.color = color;

            /*
             * The pivot is at the leading/front edge of the beam.
             *
             * The beam extends behind its movement direction,
             * so it cannot cross the keyboard edge before its
             * front reaches that edge.
             */
            rectTransform.pivot =
                new Vector2(
                    0.5f,
                    0f);

            rectTransform.anchoredPosition =
                start;

            active = true;
        }

        public void UpdateForLessonTime(float currentTime)
        {
            if (!active || rectTransform == null)
                return;

            float elapsedSinceSpawn =
                Mathf.Max(
                    0f,
                    currentTime - spawnTime);

            if (currentTime < noteStartTime)
            {
                rectTransform.anchoredPosition =
                    spawnPosition +
                    direction *
                    speed *
                    elapsedSinceSpawn;
            }
            else
            {
                rectTransform.anchoredPosition =
                    targetPosition;
            }

            float remainingDuration =
                Mathf.Clamp(
                    noteEndTime - currentTime,
                    0f,
                    noteEndTime - noteStartTime);

            float visibleLength =
                remainingDuration * speed;

            rectTransform.sizeDelta =
                new Vector2(
                    width,
                    visibleLength);

            if (currentTime >= noteEndTime)
                Finish();
        }

        public void Finish()
        {
            active = false;
            Destroy(gameObject);
        }
    }
}