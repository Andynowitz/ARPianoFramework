# ARPianoFramework - Agent Instructions

## Project

ARPianoFramework is a Unity 6 C# Bachelor thesis project for a modular
Mixed Reality piano instruction framework.

The project focuses on:

- MIDI-based piano lessons
- Lesson playback
- Virtual piano visualization
- Piano tracking using OpenCvSharp
- Modular and event-driven architecture

## Technology

- Unity 6
- C#
- .NET
- DryWetMidi
- OpenCvSharp
- Git
- Meta XR SDK is planned but not currently implemented

## Current implementation

The following functionality is implemented:

- PianoNote model
- Lesson model
- DryWetMidi MIDI parsing
- Lesson creation
- LessonPlayer
- NoteStarted / NoteStopped events
- Pause / Resume / Stop
- Virtual 88-key piano
- MIDI 21-108 mapping
- PianoKey component
- Piano key highlighting
- Initial BeamVisualizer prototype
- OpenCvSharp integration
- Static piano image processing
- Piano pattern detection
- Keyboard edge detection
- Keyboard corner estimation
- Automatic 2D virtual-piano alignment

## Current limitations

### Webcam

A physical webcam is available, but webcam integration into Unity has
NOT been implemented yet.

Do not assume that webcam input is currently available.

### Beam visualization

Beam visualization is only a prototype.

It has been tested, but its positioning, timing, movement and endpoint
behavior have not yet been validated as correct.

Do not mark beam visualization as complete.

Before modifying the beam system, inspect the existing implementation
and explain the current coordinate system and movement logic.

### Piano tracking

The current OpenCV tracking pipeline is based on a static piano image.

The webcam should be integrated only after the static-image tracking
pipeline is sufficiently stable.

## Architecture principles

Keep the system modular.

LessonPlayer should primarily handle:

- loading lessons
- playback state
- playback timing
- NoteStarted events
- NoteStopped events

LessonPlayer should NOT directly depend on specific visualizers.

Visualizers should subscribe to LessonPlayer events.

Tracking should be separated from lesson playback and visualization.

Prefer interfaces and dependency inversion where appropriate.

Avoid unnecessary FindAnyObjectByType calls when a cleaner dependency
injection or serialized reference is possible.

## Development rules

Before modifying code:

1. Inspect the relevant files.
2. Understand the existing implementation.
3. Explain the problem and proposed solution.
4. Make the smallest appropriate change.
5. Do not rewrite unrelated working systems.
6. Do not introduce new packages unless necessary.
7. Preserve existing functionality.
8. Test the change.
9. Report exactly which files were changed.

Do not implement future features unless explicitly requested.

Do not implement:

- Meta Quest integration
- YOLO
- hand tracking
- live MIDI keyboard input
- advanced 3D pose estimation

unless explicitly requested.

## Bachelor thesis priority

Prioritize:

1. Correctness
2. Modular architecture
3. Reproducible testing
4. Clear implementation
5. Evaluation
6. Documentation

Do not prioritize unnecessary visual polish over correctness.

## Git

Do not commit or push changes unless explicitly requested.

Before making a significant architectural change, explain the planned
change first.