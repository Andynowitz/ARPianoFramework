// LessonPlayerTest.cs
// Purpose: Simple runtime test for the LessonPlayer class using a fake lesson.

using UnityEngine;
using ARPIANO.Scripts.Lessons;
using ARPIANO.Scripts.Models;

public class LessonPlayerTest : MonoBehaviour
{
    [SerializeField]
    private LessonPlayer lessonPlayer;

    private void Start()
    {
        if (lessonPlayer == null)
        {
            lessonPlayer = FindAnyObjectByType<LessonPlayer>();
        }

        if (lessonPlayer == null)
        {
            GameObject playerObject = new GameObject("AutoLessonPlayer");
            lessonPlayer = playerObject.AddComponent<LessonPlayer>();

            Debug.Log(
                "Created an auto LessonPlayer because none was found in the scene.");
        }

        lessonPlayer.NoteStarted += OnNoteStarted;
        lessonPlayer.NoteStopped += OnNoteStopped;

        Lesson lesson = new Lesson();

        // Existing C5 note.
        lesson.AddNote(new PianoNote(72, 100, 0.5f, 0.5f)); // C5

        // Existing chord.
        lesson.AddNote(new PianoNote(60, 100, 1.5f, 1f)); // C4
        lesson.AddNote(new PianoNote(64, 100, 1.5f, 1f)); // E4
        lesson.AddNote(new PianoNote(67, 100, 1.5f, 1f)); // G4


        // C minor blues scale:
        // C - Eb - F - Gb - G - Bb - C
        float scaleStart = 2.5f;
        float noteLength = 0.3f;
        float spacing = 0.4f;

        lesson.AddNote(new PianoNote(60, 100, scaleStart + 0 * spacing, noteLength)); // C4
        lesson.AddNote(new PianoNote(63, 100, scaleStart + 1 * spacing, noteLength)); // Eb4
        lesson.AddNote(new PianoNote(65, 100, scaleStart + 2 * spacing, noteLength)); // F4
        lesson.AddNote(new PianoNote(66, 100, scaleStart + 3 * spacing, noteLength)); // Gb4
        lesson.AddNote(new PianoNote(67, 100, scaleStart + 4 * spacing, noteLength)); // G4
        lesson.AddNote(new PianoNote(70, 100, scaleStart + 5 * spacing, noteLength)); // Bb4
        lesson.AddNote(new PianoNote(72, 100, scaleStart + 6 * spacing, noteLength)); // C5

        lessonPlayer.LoadLesson(lesson);

        Debug.Log($"Loaded test lesson with {lesson.Notes.Count} notes.");
    }

    public void PlayTestLesson()
    {
        Debug.Log("LessonPlayerTest: starting test lesson.");
        lessonPlayer.Play();
    }

    private void OnNoteStarted(PianoNote note)
    {
        Debug.Log($"START: {note.GetNoteNames()} at {lessonPlayer.GetCurrentTime:F2}s");
    }

    private void OnNoteStopped(PianoNote note)
    {
        Debug.Log($"STOP : {note.GetNoteNames()} at {lessonPlayer.GetCurrentTime:F2}s");
    }
}