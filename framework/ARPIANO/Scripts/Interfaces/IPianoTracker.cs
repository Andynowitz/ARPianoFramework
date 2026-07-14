// IPianoTracker.cs
// Purpose: Defines the contract for any component that can track the state of a piano in the ARPIANO system.

public interface IPianoTracker
{
    bool DetectPiano(); // returns true if a piano is detected, false otherwise
}