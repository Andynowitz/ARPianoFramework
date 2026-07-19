// PianoKey.cs
// Purpose: Represents a single piano key with its properties and behaviors.

using UnityEngine;
using ARPIANO.Scripts.Models;

namespace ARPIANO.Scripts.Piano
{
    public class PianoKey : MonoBehaviour
    {
        public int MidiNumber { get; private set; }
        public string NoteName { get; private set; }
        public bool IsBlack { get; private set; }

        private Renderer[] renderers;

        private Color defaultColor;
        private Color pressedColor = Color.green;

        public void Initialize(int midiNumber, bool isBlack)
        {
            MidiNumber = midiNumber;
            IsBlack = isBlack;

            NoteName = ARPIANO.Scripts.Models.PianoNote.GetNoteName(midiNumber);

            renderers = GetComponentsInChildren<Renderer>();

            defaultColor = isBlack ? Color.black : Color.white;

            foreach (Renderer r in renderers)
                r.material.color = defaultColor;
        }

        public void Press()
        {
            foreach (Renderer r in renderers)
                r.material.color = pressedColor;
        }

        public void Release()
        {
            foreach (Renderer r in renderers)
                r.material.color = defaultColor;
        }
    }
}