# Third-Party Licenses and Attributions

ARPianoFramework uses third-party software and audio assets. Their respective licences apply to those components and are separate from the licence of ARPianoFramework itself.

This document records the components currently identified in the project. Before redistributing a release, verify the exact versions, bundled binaries, licence files and notices.

---

## 1. Fluid (R3) General MIDI SoundFont

**Component:** FluidR3_GM.sf2
**Author:** Frank Wen and contributors
**License:** MIT License
**Original attribution:** Copyright (c) 2000–2002, 2008 Frank Wen

The original SoundFont README states:

> I hereby release Fluid under the MIT license, as described in COPYING.

The README also credits contributors who helped create the SoundFont, including Suren M. Seron, Scott Hanan, Steve Aupperle, Chris Gillman, Alex Taubr, Chris Prola, Andrew Klenk, Winfried Hubbe, Dylan, Tim, Gort, Uros Katic and Ethan Winer.

### MIT License

Copyright (c) 2000–2002, 2008 Frank Wen

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### Source references

- Original SoundFont download: https://sourceforge.net/projects/androidframe/files/soundfonts/FluidR3_GM.sf2/download
- SoundFont README: https://sources.debian.org/src/fluid-soundfont/3.1-5.2/README
- Debian package information: https://packages.debian.org/search?keywords=fluid-soundfont

The SoundFont is used by FluidSynth to render MIDI recordings into audio. Verify that the downloaded file corresponds to the original release and retain the accompanying licence and attribution information when redistributing it.

---

## 2. FluidSynth

- **Project:** FluidSynth
- **Version:** 2.6.1
- **Source:** https://github.com/FluidSynth/fluidsynth
- **License:** GNU Lesser General Public License, version 2.1 or later (LGPL-2.1-or-later)
- **Usage:** Command-line MIDI rendering and audio synthesis.
- **Bundled components:** `fluidsynth.exe`, `libfluidsynth-3.dll`
- **License text:** See the accompanying LGPL-2.1 license text and the upstream FluidSynth project.

FluidSynth is distributed under the GNU Lesser General Public License. Its license and applicable redistribution requirements must be respected when distributing FluidSynth binaries with ARPianoFramework. Third-party dependencies bundled with the Windows release may be subject to separate licenses.

---

## 3. FFmpeg

**Component:** FFmpeg binaries used by FFmpegUnityBind2
**Purpose:** Video recording, encoding and combining video with rendered piano audio
**Upstream project:** https://ffmpeg.org/
**License:** Depends on the exact FFmpeg build configuration.

FFmpeg builds may be licensed under LGPL or GPL depending on the enabled components and configuration. Determine which build is included with this project before distributing it.

Include the applicable licence text, copyright notices and any required source-code or corresponding-source information for the exact build.

References:

- https://ffmpeg.org/legal.html
- https://ffmpeg.org/download.html

---

## 4. FFmpegUnityBind2

**Component:** FFmpegUnityBind2 Unity integration
**Purpose:** Integrate FFmpeg functionality into Unity
**Upstream project:** https://github.com/Spirit30/FFmpegUnityBind2
**License:** Apache License 2.0, according to the upstream repository.

Retain the applicable copyright, licence and NOTICE information supplied with the version included in this project. Verify whether the bundled version contains additional third-party components.

---

## 5. DryWetMIDI

**Component:** DryWetMIDI
**Purpose:** Read, parse and write Standard MIDI Files
**Upstream project:** https://github.com/melanchall/drywetmidi
**License:** MIT, according to the upstream project.

Retain the copyright and licence notice from the exact version used by ARPianoFramework.

---

## 6. OpenCvSharp and OpenCV

**Components:** OpenCvSharp and the OpenCV native runtime
**Purpose:** Keyboard outline detection, corner estimation and computer vision
**Upstream projects:**

- OpenCvSharp: https://github.com/shimat/opencvsharp
- OpenCV: https://opencv.org/

OpenCvSharp is distributed under Apache License 2.0. OpenCV is distributed under its own licence terms. Verify the exact managed package and native binaries included in this project, and retain the applicable licence and attribution notices.

---

## 7. Unity

**Component:** Unity 6 and associated Unity packages
**Purpose:** Application runtime, rendering and development environment
**Website:** https://unity.com/

Unity and its packages are subject to their respective licence terms. Consult the applicable Unity terms and the licences supplied with any third-party Unity packages included in the project.

---

## 8. Additional dependencies and assets

Other libraries, native plugins, Unity packages, operating-system components and assets may have their own licence requirements.

Before publishing or distributing ARPianoFramework:

- Verify every dependency and bundled binary.
- Preserve the original licence and copyright notices.
- Include required NOTICE files and attribution.
- Check the exact FFmpeg build configuration.
- Confirm the provenance and licence of all SoundFonts and other audio assets.
- Review the redistribution requirements for the intended distribution method.

This document is a project-maintained inventory, not legal advice or a guarantee that every dependency has been fully audited.
