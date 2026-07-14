// Lesson.cs
// Purpose: Represents a musical lesson, which consists of a sequence of piano notes and their associated metadata.

namespace ARPIANO.Models
{
    public class Lesson
    {
        // Name of the lesson.
        public string Name { get; private set; }

        // Author of the lesson.
        public string Author { get; private set; }

        // Tempo of the lesson, represented as a string (e.g., "120 BPM").
        public string Tempo { get; private set; }

        // Description of the lesson.
        public string Description { get; private set; }

        // List of piano notes that make up the lesson.
        public List<PianoNote> Notes { get; private set; }

        // Add a note to the List of notes in the lesson.
        public void AddNote(PianoNote note)
        {
            Notes.Add(note);
        }

        // get the total number of notes in the lesson.
        public int GetTotalNotes()
        {
            return Notes.Count;
        }

        // get the total duration of the lesson in seconds, based on the end time of the last note.
        public float GetDuration()
        {
            if (Notes.Count == 0)
                return 0;

            float maxEndTime = 0f;

            foreach (var note in Notes)
            {
                if (note.EndTime > maxEndTime)
                {
                    maxEndTime = note.EndTime;
                }
            }

            return maxEndTime;
        }

        // print the lesson details, including name, number of notes, and total duration.
        public override string ToString()
        {
            return $"{Name} ({Notes.Count} notes, {GetDuration():F2}s)";
        }

    }
}