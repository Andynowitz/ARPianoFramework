// VirtualPianoTest.cs
// Check if highlighting works for virtual piano keys when a lesson is played.

using UnityEngine;
using ARPIANO.Scripts.Lessons;
using ARPIANO.Scripts.Models;

public class VirtualPianoTest : MonoBehaviour
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

        // C4
        lesson.AddNote(new PianoNote(
            midiNumber: 60,
            velocity: 100,
            startTime: 0f,
            duration: 1f));

        // E4
        lesson.AddNote(new PianoNote(
            midiNumber: 64,
            velocity: 100,
            startTime: 1f,
            duration: 1f));

        // G4
        lesson.AddNote(new PianoNote(
            midiNumber: 67,
            velocity: 100,
            startTime: 2f,
            duration: 1f));

        lessonPlayer.LoadLesson(lesson);
        lessonPlayer.Play();

        Debug.Log("Playing C4 → E4 → G4");
    }

    private void OnNoteStarted(PianoNote note)
    {
        Debug.Log($"START: {note.GetNoteName()} ({note.MidiNumber}) at {lessonPlayer.GetCurrentTime:F2}s");
    }

    private void OnNoteStopped(PianoNote note)
    {
        Debug.Log($"STOP : {note.GetNoteName()} ({note.MidiNumber}) at {lessonPlayer.GetCurrentTime:F2}s");
    }
}