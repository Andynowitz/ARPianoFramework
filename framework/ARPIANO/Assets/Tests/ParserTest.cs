using System.IO;    
using UnityEngine;
using ARPIANO.Scripts.MIDI;
using ARPIANO.Scripts.Models;

namespace ARPIANO.Tests
{
    public class ParserTest : MonoBehaviour
    {
        void Start()
        {
            Debug.Log("ParserTest started!");

            var parser = new MIDIParser();
            string path = Path.Combine(Application.streamingAssetsPath, "0_row row_3_5.mid");

            Lesson lesson = parser.ParseMIDIFile(path);

            foreach (var note in lesson.Notes)
            {
                Debug.Log(note.ToString());
            }

            Debug.Assert(lesson != null);
            Debug.Assert(lesson.Name == "0_row row_3_5");
            Debug.Assert(lesson.BPM > 0);
            Debug.Assert(lesson.Notes.Count > 0);

            Debug.Log($"Parsed Lesson: {lesson.Name}, BPM: {lesson.BPM}, Notes Count: {lesson.Notes.Count}");
        }
    }
}