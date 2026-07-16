// ILessonPlayer.
// Purpose: Interface for lesson player classes.

using ARPIANO.Scripts.Models;
using System;

public interface ILessonPlayer
{
    void LoadLesson(Lesson lesson); // Load a lesson into the player.

    void Play();

    void Pause();

    void Stop();

    float CurrentTime { get; }

    event Action<PianoNote> NoteStarted; // Event triggered when a note starts playing.
    event Action<PianoNote> NoteStopped; // Event triggered when a note stops playing.
}