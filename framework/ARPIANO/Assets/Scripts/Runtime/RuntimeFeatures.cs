namespace ARPIANO.Scripts
{
    /// <summary>
    /// Code-level switches for optional subsystems. Flip a flag back to true to
    /// re-enable a subsystem; nothing else has to change.
    /// </summary>
    public static class RuntimeFeatures
    {
        /// <summary>
        /// OpenCV keyboard detection (frame readback, outline/black-key detection,
        /// marker calibration). Disabled: no OpenCV code path executes.
        /// </summary>
        public static readonly bool OpenCVTrackingEnabled = false;

        /// <summary>
        /// Virtual piano mesh generation, key meshes and lesson key highlighting.
        /// Disabled: no keys are created and nothing subscribes to lesson notes.
        /// </summary>
        public static readonly bool VirtualPianoEnabled = false;
    }
}
