using System.IO;
using UnityEngine;
using ARPIANO.Scripts.MIDI;
using ARPIANO.Scripts.Models;

public class ParserTest : MonoBehaviour
{
    void Start()
    {
        var parser = new MIDIParser();
        string path = Path.Combine(Application.streamingAssetsPath, "0_row row_3_5.mid");

        Lesson lesson = parser.ParseMIDIFile(path);

        Debug.Assert(lesson != null);
        Debug.Assert(lesson.Name == "0_row row_3_5");
        Debug.Assert(lesson.BPM > 0);
        Debug.Assert(lesson.Notes.Count > 0);
    }
}