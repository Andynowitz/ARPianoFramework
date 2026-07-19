// Beam.cs
// Purpose: Represents a visual beam that corresponds to a piano note, showing its duration and timing in the ARPIANO system.

using UnityEngine;
using ARPIANO.Scripts.Models;

namespace ARPIANO.Scripts.Visualizers
{
    public class Beam : MonoBehaviour
    {
        public int MidiNumber { get; private set; }

        private float fallingSpeed = 3f;
        private Vector3 fallDirection = Vector3.down;

        private PianoNote note;

        private bool active;

        public void Initialize(int midi)
        {
            Initialize(midi, Vector3.down);
        }

        public void Initialize(int midi, Vector3 direction)
        {
            MidiNumber = midi;
            fallDirection = direction.normalized;
            active = true;
        }

        private void Update()
        {
            if (!active)
                return;

            // fall relative to key orientation rather than camera
            transform.Translate(fallDirection * fallingSpeed * Time.deltaTime, Space.World);
        }

        public void Finish()
        {
            active = false;

            Destroy(gameObject);
        }

    }
}