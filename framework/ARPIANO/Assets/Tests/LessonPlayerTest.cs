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
            Debug.Log("Created an auto LessonPlayer because none was found in the scene.");
        }

        lessonPlayer.NoteStarted += OnNoteStarted;
        lessonPlayer.NoteStopped += OnNoteStopped;

        Lesson lesson = new Lesson();
        lesson.AddNote(new PianoNote(60, 100, 0f, 1f));
        lesson.AddNote(new PianoNote(64, 100, 0f, 1f));
        lesson.AddNote(new PianoNote(67, 100, 0f, 1f));
        lesson.AddNote(new PianoNote(72, 100, 1.5f, 0.5f));

        lessonPlayer.LoadLesson(lesson);
        lessonPlayer.Play();

        Debug.Log($"Loaded test lesson with {lesson.Notes.Count} notes.");
    }

    private void OnNoteStarted(PianoNote note)
    {
        Debug.Log($"START: {note.GetNoteName()} at {lessonPlayer.CurrentTime:F2}s");
    }

    private void OnNoteStopped(PianoNote note)
    {
        Debug.Log($"STOP : {note.GetNoteName()} at {lessonPlayer.CurrentTime:F2}s");
    }
}