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
            using (FileStream stream = File.OpenRead(filePath))
            {
                return ParseMIDIStream(
                    stream,
                    Path.GetFileName(filePath));
            }
        }

        // Parses MIDI bytes, which also works for Android StreamingAssets.
        public Lesson ParseMIDIBytes(byte[] data, string fileName)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("MIDI data is empty.");

            using (MemoryStream stream = new MemoryStream(data))
            {
                return ParseMIDIStream(stream, fileName);
            }
        }

        private Lesson ParseMIDIStream(Stream stream, string fileName)
        {
            MidiFile midiFile = MidiFile.Read(stream);

            Lesson lesson = new Lesson();

            TempoMap tempoMap = midiFile.GetTempoMap();

            var tempo = tempoMap.GetTempoAtTime(new MetricTimeSpan(0));
            lesson.BPM = (int)Math.Round(tempo.BeatsPerMinute);

            var timeSignature = tempoMap.GetTimeSignatureAtTime(new MetricTimeSpan(0));
            lesson.TimeSignatureNumerator = timeSignature.Numerator;
            lesson.TimeSignatureDenominator = timeSignature.Denominator;

            string lessonName = Path.GetFileNameWithoutExtension(fileName);
            lesson.Name = string.IsNullOrEmpty(lessonName)
                ? "Lesson"
                : lessonName.Replace('_', ' ');

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