# ARPianoFramework

## COMMITING
git push origin andy/setup
git push github andy/setup

## Overview

This project aims to create a modular and extensible framework for Mixed Reality (MR) piano tutorials using the Unity3D game engine. Existing prototype implementations for piano visualization, synchronized teacher-student interaction, and desktop/HMD visualization are consolidated into a unified architecture that supports interchangeable modules for lesson authoring, visualization, display, and tracking.

The framework is intended as a research and development platform for immersive piano instruction across different hardware setups and interaction paradigms.


## Technologies

-> Unity 6
-> .NET
-> DryWetMidi (planned)
-> Vuforia / OpenCV (planned)
-> Meta XR SDK (planned)
-> Git


## Planned Architecture 


![Description](documentation/images/ArchicetcureMockup.png)

The framework is designed to be modular. Every major component should be replaceable without affecting the remaining system.


# Development Overflow 

The project will be developed incrementally. Every milestone should produce a working prototype before moving to the next one.


# Milestone 1 – Project Foundation

- [x] Create Unity project
- [x] Design folder structure
- [x] Define interfaces
- [x] Create core models (`PianoNote`, `Lesson`)
- [x] Set up Git repository
- [x] Create modular project architecture


# Milestone 2 – MIDI & Lesson System

- [x] Integrate DryWetMIDI
- [x] Parse MIDI files
- [x] Convert MIDI notes into `PianoNote`
- [x] Create `Lesson` objects
- [x] Print parsed notes for debugging


# Milestone 3 – Lesson Playback

- [x] Create `LessonPlayer`
- [x] Internal playback timer
- [x] Trigger `NoteStarted`
- [x] Trigger `NoteStopped`
- [x] Pause / Resume
- [x] Stop playback


# Milestone 4 – Virtual Piano

- [x] Create 88-key virtual keyboard
- [x] Create `PianoKey` component
- [x] Map MIDI notes (21–108) to keys
- [x] Subscribe to `LessonPlayer`
- [x] Highlight pressed keys
- [x] Release highlighted keys


# Milestone 5 – Visualization

- [x] Create initial Beam Visualizer prototype
- [ ] Spawn beams on `NoteStarted`
- [ ] Remove beams on `NoteStopped`
- [ ] Synchronize beam timing
- [ ] Support chords (multiple simultaneous notes)
- [ ] Verify beam positioning
- [ ] Verify beam movement direction
- [ ] Verify beam endpoint at the top of piano keys
- [ ] Synchronize beam timing with lesson playback
- [ ] Verify beam duration
- [ ] Decide whether the current beam implementation should be improved or redesigned

# Milestone 5.5 – Beam Animation

- [ ] Define correct beam coordinate system
- [ ] Animate beams toward piano keys
- [ ] Make beams arrive exactly at the target key
- [ ] Scale beam length to note duration
- [ ] Fade beams after NoteStopped
- [ ] Add configurable beam speed
- [ ] Verify behavior from the intended camera perspective


# Milestone 6 – Piano Tracking

## Phase 1 – Manual Alignment

- [x] Display a static piano image
- [x] Overlay the virtual piano
- [x] Manually align the virtual keyboard

## Phase 2 – Webcam Integration
- [ ] Display webcam feed
- [ ] Replace static image input with webcam frames
- [ ] Verify camera image orientation
- [ ] Verify calibration pipeline using the webcam
- [ ] Test tracking with a real piano

## Phase 3 – Keyboard Detection (OpenCV)

- [x] Integrate OpenCV (OpenCvSharp integrated and used by the tracker)
- [x] Detect keyboard edges (outline detection implemented via contour approximation)
- [x] Estimate keyboard corners (quad corners detected from contour approx; ordering and CCW sort applied)
- [x] Automatically align virtual keyboard (corner-based 2D similarity alignment implemented; PCA-based in-plane rotation fallback available)
- [ ] Validate the alignment with additional images
- [ ] Test robustness against rotation, scaling and perspective changes
- [ ] Test detection with partial keyboard visibility

## Phase 4 – Advanced Tracking
- [ ] Improve detection robustness
- [ ] Support partial keyboard visibility
- [ ] Investigate feature matching
- [ ] Investigate improved perspective handling
- [ ] Investigate improved perspective handling
- [ ] Full homography → 3D pose decomposition, if required


# Milestone 7 – Framework Modularity

- [ ] Create interchangeable tracker modules
- [ ] Create interchangeable visualizers
- [ ] Introduce event-driven communication
- [ ] Separate Lesson, Tracking and Visualization systems
- [ ] Remove unnecessary dependencies between components
- [ ] Document framework architecture
- [ ] Validate that modules can be replaced independently


# Milestone 8 – Mixed Reality

- [ ] Integrate Meta XR SDK
- [ ] Deploy to Meta Quest
- [ ] Verify display abstraction
- [ ] Test complete MR pipeline


# Immediate Tasks

Current focus:

- [x] Design project architecture
- [x] Create folder structure
- [x] Define interfaces
- [x] Create `PianoNote` model
- [x] Research DryWetMIDI integration

No visualization or tracking should be implemented before the core architecture is in place.



# Future Features

- Multiple visualization modes
- Hand tracking
- Live MIDI keyboard input
- Markerless piano detection improvements
- Automatic sheet music generation
- Additional lesson formats
- Performance recording and replay
- Real-time webcam tracking
- Advanced 3D camera pose estimation