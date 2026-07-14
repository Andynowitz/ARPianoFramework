// MIDIParser.cs
// Purpose: Parses MIDI files and provides note data to the ARPIANO system.

using ARPIANO.Scripts.Models;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using System;
using System.IO;

namespace ARPIANO.Scripts.MIDI
{
    public class MIDIParser
    {
        // Parses a MIDI file and returns a Lesson object containing the parsed notes.
        public Lesson ParseMIDIFile(string filePath)
        {
            MidiFile midiFile = MidiFile.Read(filePath);

            Lesson lesson = new Lesson();

            TempoMap tempoMap = midiFile.GetTempoMap();

            var tempo = tempoMap.GetTempoAtTime(new MetricTimeSpan(0));
            lesson.BPM = (int)Math.Round(tempo.BeatsPerMinute);

            var timeSignature = tempoMap.GetTimeSignatureAtTime(new MetricTimeSpan(0));
            lesson.TimeSignatureNumerator = timeSignature.Numerator;
            lesson.TimeSignatureDenominator = timeSignature.Denominator;

            lesson.Name = Path.GetFileNameWithoutExtension(filePath);

            foreach (var note in midiFile.GetNotes())
            {
                PianoNote pianoNote = new PianoNote
                {
                    MidiNumber = note.NoteNumber,
                    Velocity = note.Velocity,
                    StartTime = (float)note.TimeAs<MetricTimeSpan>(tempoMap).TotalSeconds,
                    Duration = (float)note.LengthAs<MetricTimeSpan>(tempoMap).TotalSeconds
                };

                lesson.AddNote(pianoNote);
            }


            return lesson;
        }

    }
}