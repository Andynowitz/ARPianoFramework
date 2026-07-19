// IVisualiser.cs
// Purpose: Defines the contract for any component that can visualize notes in the ARPIANO system

using ARPIANO.Scripts.Models;

namespace ARPIANO.Scripts.Visualizers
{
    public interface IVisualizer
    {
        void NoteStarted(PianoNote note);
        void NoteStopped(PianoNote note);
    }
}