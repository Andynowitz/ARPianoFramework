// LessonPlayer.cs
// Purpose: Manages the playback of a musical lesson, including note activation and deactivation based on timing.
// Should also work for more notes at the same time
// The LessonPlayer checks the current time and activates/deactivates notes accordingly, allowing for real-time playback of the lesson.

using UnityEngine;
using ARPIANO.Scripts.Models;
using System;

namespace ARPIANO.Scripts.Lessons
{
    public class LessonPlayer : MonoBehaviour, ILessonPlayer
    {
        private Lesson currentLesson;
        private float currentTime;
        private bool isPlaying;

        public event Action<PianoNote> NoteStarted;
        public event Action<PianoNote> NoteStopped;

        public float GetCurrentTime => currentTime;
        public Lesson CurrentLesson => currentLesson;
        public bool IsPlaying => isPlaying;
        
        public void LoadLesson(Lesson lesson)
        {
            currentLesson = lesson;
            currentTime = 0f;
            isPlaying = false;

            // Reset runtime state
            ResetLessonState();

        }

        // Starts playback or resumes it if paused.
        public void Play()
        {
            if (currentLesson == null) return;

            isPlaying = true;
        }

        // Pauses playback without resetting the current time.
        public void Pause()
        {
            isPlaying = false;
        }

        // Stops playback and resets the current time to the beginning of the lesson.
        public void Stop()
        {
            isPlaying = false;
            currentTime = 0f;

            DeactivateAllNotes();
            ResetLessonState();
        }

        // Update is called once per frame
        private void Update()
        {
            if (!isPlaying || currentLesson == null)
                return;

            currentTime += Time.deltaTime;

            foreach (var note in currentLesson.Notes)
            {
                if (!note.HasStarted && currentTime >= note.StartTime)
                {
                    note.HasStarted = true;
                    NoteStarted?.Invoke(note);
                }

                if (!note.HasFinished && currentTime >= note.EndTime)
                {
                    note.HasFinished = true;
                    NoteStopped?.Invoke(note);
                }
            }

            if (currentTime >= currentLesson.GetDuration())
            {
                Stop();
            }
        }

        private void ResetLessonState()
        {
            if (currentLesson == null)
              return;

            foreach (var note in currentLesson.Notes)
            {
                note.HasStarted = false;
                note.HasFinished = false;
            }
        }

        private void DeactivateAllNotes()
        {

            if (currentLesson == null)
                return;

            foreach (var note in currentLesson.Notes)
            {
                if (note.HasStarted && !note.HasFinished)
                {
                    note.HasFinished = true;
                    NoteStopped?.Invoke(note);
                }
            }
        }
    }
}