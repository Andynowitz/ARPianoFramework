using UnityEngine;

namespace ARPIANO.Scripts.Tracking
{
    public class KeyboardCalibration : MonoBehaviour
    {
        [Header("Known piano range")]
        [SerializeField] private int firstMidiNote = 48; // C3
        [SerializeField] private int lastMidiNote = 84;  // C6

        [Header("Camera/image coordinates")]
        [SerializeField] private Vector2 topLeft;
        [SerializeField] private Vector2 topRight;
        [SerializeField] private Vector2 bottomRight;
        [SerializeField] private Vector2 bottomLeft;

        public int FirstMidiNote => firstMidiNote;
        public int LastMidiNote => lastMidiNote;

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