using UnityEngine;

namespace ARPIANO.Scripts.Tracking
{
    public class KeyboardCalibration : MonoBehaviour
    {
        public const int MinMidiNote = 21; // A0
        public const int MaxMidiNote = 108; // C8
        public const int DefaultFirstMidiNote = 48; // C3
        public const int DefaultLastMidiNote = 84;  // C6

        [Header("Known piano range")]
        [SerializeField] private int firstMidiNote = DefaultFirstMidiNote;
        [SerializeField] private int lastMidiNote = DefaultLastMidiNote;

        [Header("Camera/image coordinates")]
        [SerializeField] private Vector2 topLeft;
        [SerializeField] private Vector2 topRight;
        [SerializeField] private Vector2 bottomRight;
        [SerializeField] private Vector2 bottomLeft;

        public int FirstMidiNote => firstMidiNote;
        public int LastMidiNote => lastMidiNote;

        public static bool IsValidRange(int first, int last)
        {
            return first >= MinMidiNote &&
                   last <= MaxMidiNote &&
                   last > first;
        }

        public void SetRange(int first, int last)
        {
            first = Mathf.Clamp(first, MinMidiNote, MaxMidiNote - 1);
            last = Mathf.Clamp(last, first + 1, MaxMidiNote);

            firstMidiNote = first;
            lastMidiNote = last;
        }

        public Vector2 TopLeft
        {
            get => topLeft;
            set => topLeft = value;
        }

        public Vector2 TopRight
        {
            get => topRight;
            set => topRight = value;
        }

        public Vector2 BottomRight
        {
            get => bottomRight;
            set => bottomRight = value;
        }

        public Vector2 BottomLeft
        {
            get => bottomLeft;
            set => bottomLeft = value;
        }

        public Vector2[] GetImageCorners()
        {
            return new[]
            {
                topLeft,
                topRight,
                bottomRight,
                bottomLeft
            };
        }

        public int WhiteKeyCount
        {
            get
            {
                int count = 0;

                for (int midi = firstMidiNote; midi <= lastMidiNote; midi++)
                {
                    if (!IsBlackKey(midi))
                        count++;
                }

                return count;
            }
        }

        private bool IsBlackKey(int midi)
        {
            return midi % 12 is 1 or 3 or 6 or 8 or 10;
        }
    }
}