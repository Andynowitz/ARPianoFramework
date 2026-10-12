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

        // Live-performance state
        private bool isLiveBeam;
        private bool isGrowing;
        private float liveElapsedTime;
        private bool isReturning;
        private float returnDistance;

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

            direction = (target - start).normalized;

            if (!InitializeComponents())
                return;

            rectTransform.sizeDelta =
                new Vector2(width, beamLength);

            rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, rotationAngle);

            image.color = color;

            // Keep the leading edge at the beam's anchored position.
            rectTransform.pivot = new Vector2(0.5f, 0f);
            rectTransform.anchoredPosition = start;

            isLiveBeam = false;
            isGrowing = false;
            active = true;
        }

        // Initializes a beam for a physical MIDI key press.
        public void InitializeForLive(
            int midi,
            Vector2 target,
            float width,
            Color color,
            float rotationAngle,
            float movementSpeed)
        {
            MidiNumber = midi;

            this.width = width;
            speed = movementSpeed;
            targetPosition = target;
            spawnPosition = target;
            spawnTime = 0f;
            noteStartTime = 0f;
            noteEndTime = 0f;
            direction = Vector2.zero;
            liveElapsedTime = 0f;
            isReturning = false;
            returnDistance = 0f;

            if (!InitializeComponents())
                return;

            rectTransform.pivot = new Vector2(0.5f, 0f);
            rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, rotationAngle);

            // The beam starts at the keyboard and grows while held.
            rectTransform.anchoredPosition = target;
            rectTransform.sizeDelta = new Vector2(width, 0f);

            image.color = color;

            isLiveBeam = true;
            isGrowing = true;
            active = true;
        }

        // Shared component validation for both modes.
        private bool InitializeComponents()
        {
            rectTransform = GetComponent<RectTransform>();
            image = GetComponent<Image>();

            if (rectTransform == null)
            {
                Debug.LogError("Beam requires a RectTransform.");
                active = false;
                return false;
            }

            if (image == null)
            {
                Debug.LogError("Beam requires an Image component.");
                active = false;
                return false;
            }

            return true;
        }

        // Existing lesson-timed animation. Keep this behavior unchanged.
        public void UpdateForLessonTime(float currentTime)
        {
            if (!active || rectTransform == null || isLiveBeam)
                return;

            float elapsedSinceSpawn =
                Mathf.Max(0f, currentTime - spawnTime);

            if (currentTime < noteStartTime)
            {
                rectTransform.anchoredPosition =
                    spawnPosition + direction * speed * elapsedSinceSpawn;
            }
            else
            {
                rectTransform.anchoredPosition = targetPosition;
            }

            float remainingDuration =
                Mathf.Clamp(
                    noteEndTime - currentTime,
                    0f,
                    noteEndTime - noteStartTime);

            float visibleLength = remainingDuration * speed;

            rectTransform.sizeDelta =
                new Vector2(width, visibleLength);

            if (currentTime >= noteEndTime)
                Finish();
        }

        // Grows while held; moves upward after release.
        public void UpdateForLive(float deltaTime)
        {
            if (!active || !isLiveBeam || rectTransform == null)
                return;

            float delta = Mathf.Max(0f, deltaTime);

            if (isGrowing)
            {
                liveElapsedTime += delta;

                rectTransform.anchoredPosition = targetPosition;
                rectTransform.sizeDelta =
                    new Vector2(width, liveElapsedTime * speed);
            }
            else if (isReturning)
            {
                // Move the beam upward, keeping its length fixed.
                Vector2 upward = new Vector2(
                    Mathf.Sin(-GetKeyboardAngleRadians()),
                    Mathf.Cos(-GetKeyboardAngleRadians())
                ).normalized;

                Vector2 movement = upward * speed * delta;

                rectTransform.anchoredPosition += movement;

                returnDistance -= movement.magnitude;

                if (returnDistance <= 0f)
                    Finish();
            }
        }

        // Stop growing and begin the upward return.
        public void ReleaseLive()
        {
            if (!active || !isLiveBeam || !isGrowing)
                return;

            isGrowing = false;
            isReturning = true;

            // Return far enough to move the beam out of view.
            returnDistance =
                (rectTransform != null ? rectTransform.rect.height : 0f)
                + 1000f;
        }

        private float GetKeyboardAngleRadians()
        {
            return rectTransform != null
                ? rectTransform.localEulerAngles.z * Mathf.Deg2Rad
                : 0f;
        }

        public void Finish()
        {
            active = false;
            isGrowing = false;
            Destroy(gameObject);
        }
    }
}
