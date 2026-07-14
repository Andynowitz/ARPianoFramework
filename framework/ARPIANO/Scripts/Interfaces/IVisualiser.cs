// IVisualiser.cs
// Purpose: Defines the contract for any component that can visualize notes in the ARPIANO system

using ARPIANO.Models;

public interface IVisualiser
{
    void PianoNoteStarted(PianoNote note);

    void PianoNoteStopped(PianoNote note);
}