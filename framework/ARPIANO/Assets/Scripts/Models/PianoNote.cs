// PianoNote.cs
// Purpose: Represents a single piano note with its properties and behaviors.
// This model is independent of the original input source (Midi file, Midi Keyboard Input or another format)

namespace ARPIANO.Scripts.Models
{
    public class PianoNote
    {
        public PianoNote()
        {
        }

        public PianoNote(int midiNumber, int velocity, float startTime, float duration)
        {
            MidiNumber = midiNumber;
            Velocity = velocity;
            StartTime = startTime;
            Duration = duration;
        }

        // MIDI note number (21-108 for a standard 88-key piano).
        public int MidiNumber { get; set; }

        // Time in seconds when the note starts playing.
        public float StartTime { get; set; } // Time in seconds when the note starts playing.

        // Time in seconds when the note starts playing.
        public float Duration { get; set; }

        // Runtime state used by the lesson player to avoid firing events multiple times.
        public bool HasStarted { get; set; }
        public bool HasFinished { get; set; }

        // Velocity of the note (0-127), representing how hard the note is played.
        // can be use to adjust Beam brightness or other visual/audio effects in the ARPIANO system.
        public int Velocity { get; set; } 

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
            return  $"{GetNoteName()} | MIDI: {MidiNumber} | " +
                    $"Start: {StartTime:F2}s | " +
                    $"Duration: {Duration:F2}s | " +
                    $"Velocity: {Velocity}";

        }
    }
}