# ARPianoFramework


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

- [ ] Create Beam Visualizer
- [ ] Spawn beams on `NoteStarted`
- [ ] Remove beams on `NoteStopped`
- [ ] Synchronize beam timing
- [ ] Support chords (multiple simultaneous notes)
- [ ] Allow multiple visualization modules


# Milestone 6 – Piano Tracking

### Phase 1 – Manual Alignment
- [ ] Display a static piano image
- [ ] Overlay the virtual piano
- [ ] Manual calibration

### Phase 2 – Marker-Based Tracking
- [ ] Integrate Vuforia
- [ ] Track marker
- [ ] Automatically align virtual piano

### Phase 3 – Markerless Tracking
- [ ] Replace marker with keyboard detection
- [ ] Investigate OpenCV
- [ ] Support static webcam calibration
- [ ] Prepare interface for future tracking algorithms


# Milestone 7 – Framework Modularity

- [ ] Create interchangeable tracker modules
- [ ] Create interchangeable visualizers
- [ ] Introduce event-driven communication
- [ ] Separate Lesson, Tracking and Visualization systems
- [ ] Document framework architecture


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
- Markerless piano detection
- Automatic sheet music generation
- Additional lesson formats
- Performance recording and replay