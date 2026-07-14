// PianoNote.cs
// Purpose: Represents a single piano note with its properties and behaviors.
// This model is independent of the original input source (Midi file, Midi Keyboard Input or another format)

namespace ARPIANO.Models
{
    public class PianoNote
    {
        // MIDI note number (21-108 for a standard 88-key piano).
        public int MidiNumber { get; private set; }

        // Time in seconds when the note starts playing.
        public float StartTime { get; private set; } // Time in seconds when the note starts playing.

        // Time in seconds when the note starts playing.
        public float Duration { get; private set; }

        // Indicates whether the note is currently active (being played).
        public bool IsActive { get; private set; }

        // Velocity of the note (0-127), representing how hard the note is played.
        // can be use to adjust Beam brightness or other visual/audio effects in the ARPIANO system.
        public int velocity { get; private set; } 

        // Time in seconds when the note ends playing, calculated as StartTime + Duration.
        public float EndTime => StartTime + Duration; 

        public string GetNoteName()
        {
            string[] noteNames =
            {
                "C", "C#", "D", "D#", "E", "F",
                "F#", "G", "G#", "A", "A#", "B"
            };

            int octave = (MidiNumber / 12) - 1;
            string note = noteNames[MidiNumber % 12];

            return $"{note}{octave}";
        }

        public override string ToString()
        {
            return $"{GetNoteName()} (MIDI {MidiNumber}) | Start: {StartTime:F2}s | Duration: {Duration:F2}s";
        }


    }
}