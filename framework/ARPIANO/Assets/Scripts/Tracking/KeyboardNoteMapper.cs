using UnityEngine;

namespace ARPIANO.Scripts.Tracking
{
    public class KeyboardNoteMapper
    {
        private readonly int firstMidiNote;
        private readonly int lastMidiNote;
        private readonly int whiteKeyCount;

        public KeyboardNoteMapper(int firstMidiNote, int lastMidiNote)
        {
            this.firstMidiNote = firstMidiNote;
            this.lastMidiNote = lastMidiNote;

            whiteKeyCount = CountWhiteKeys(
                firstMidiNote,
                lastMidiNote);
        }

        public float GetNormalizedCenter(int midiNote)
        {
            if (midiNote < firstMidiNote ||
                midiNote > lastMidiNote)
            {
                return -1f;
            }

            int whiteIndex = 0;

            for (int midi = firstMidiNote;
                midi < midiNote;
                midi++)
            {
                if (!IsBlackKey(midi))
                    whiteIndex++;
            }

            if (IsBlackKey(midiNote))
            {
                return whiteIndex / (float)whiteKeyCount;
            }

            return (whiteIndex + 0.5f) / whiteKeyCount;
        }
        
        public float GetNormalizedLeftEdge(int midiNote)
        {
            if (midiNote < firstMidiNote ||
                midiNote > lastMidiNote)
            {
                return -1f;
            }

            float whiteIndex = 0f;

            for (int midi = firstMidiNote;
                 midi < midiNote;
                 midi++)
            {
                if (!IsBlackKey(midi))
                    whiteIndex++;
            }

            return whiteIndex / whiteKeyCount;
        }

        private int CountWhiteKeys(int first, int last)
        {
            int count = 0;

            for (int midi = first; midi <= last; midi++)
            {
                if (!IsBlackKey(midi))
                    count++;
            }

            return count;
        }

        private bool IsBlackKey(int midi)
        {
            return midi % 12 is 1 or 3 or 6 or 8 or 10;
        }
    }
}