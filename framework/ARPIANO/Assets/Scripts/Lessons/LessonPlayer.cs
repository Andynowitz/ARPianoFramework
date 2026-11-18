// LessonPlayer.cs
// Purpose: Manages the playback of a musical lesson, including note activation and deactivation based on timing.
// Should also work for more notes at the same time
// The LessonPlayer checks the current time and activates/deactivates notes accordingly, allowing for real-time playback of the lesson.

using UnityEngine;
using ARPIANO.Scripts.Models;
using ARPIANO.Scripts.MIDI;
using System;

namespace ARPIANO.Scripts.Lessons
{
    public class LessonPlayer : MonoBehaviour, ILessonPlayer
    {
        private Lesson currentLesson;
        private float currentTime;
        private bool isPlaying;

        private LiveMIDIInput liveMIDIInput;
        private PianoNote awaitingNote;
        private PianoNote lastGatedNote;

        public PianoNote AwaitingNote => awaitingNote;

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
            awaitingNote = null;
            lastGatedNote = null;

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
            awaitingNote = null;
            lastGatedNote = null;

            DeactivateAllNotes();
            ResetLessonState();
        }

        private void Awake()
        {
            TrySubscribeLiveMIDI();
        }

        // Update is called once per frame
        private void Update()
        {
            if (!isPlaying || currentLesson == null)
                return;

            float nextTime = currentTime + Time.deltaTime;

            PianoNote nextNote = FindNextUnstartedNote();

            if (nextNote != null &&
                nextNote != lastGatedNote &&
                nextTime >= nextNote.StartTime)
            {
                // Freeze the timeline on the expected note and wait for
                // the matching live MIDI input.
                TrySubscribeLiveMIDI();

                currentTime = nextNote.StartTime;
                awaitingNote = nextNote;
                lastGatedNote = nextNote;
                isPlaying = false;

                Debug.Log(
                    $"LessonPlayer: waiting for {nextNote.GetNoteNames()} " +
                    $"(MIDI {nextNote.MidiNumber}).");

                return;
            }

            currentTime = nextTime;

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

        private PianoNote FindNextUnstartedNote()
        {
            PianoNote next = null;

            foreach (var note in currentLesson.Notes)
            {
                if (note.HasStarted)
                    continue;

                if (next == null || note.StartTime < next.StartTime)
                    next = note;
            }

            return next;
        }

        private void TrySubscribeLiveMIDI()
        {
            if (liveMIDIInput != null)
                return;

            liveMIDIInput = FindAnyObjectByType<LiveMIDIInput>();

            if (liveMIDIInput == null)
                return;

            liveMIDIInput.NotePressed -= OnLiveNotePressed;
            liveMIDIInput.NotePressed += OnLiveNotePressed;
        }

        private void OnLiveNotePressed(LiveMIDINoteEvent note)
        {
            if (awaitingNote == null || note == null)
                return;

            if (note.NoteNumber != awaitingNote.MidiNumber)
            {
                // Incorrect note: keep waiting, do not advance.
                return;
            }

            awaitingNote = null;
            isPlaying = true;

            Debug.Log(
                $"LessonPlayer: correct note {note.NoteNumber} played; " +
                $"resuming lesson.");
        }

        private void OnDisable()
        {
            if (liveMIDIInput != null)
            {
                liveMIDIInput.NotePressed -= OnLiveNotePressed;
                liveMIDIInput = null;
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