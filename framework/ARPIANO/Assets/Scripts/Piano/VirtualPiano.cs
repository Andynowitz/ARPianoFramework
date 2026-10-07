// VirtualPiano.cs
// Purpose: Manages the virtual piano interface, including key interactions and visual feedback.

using UnityEngine;
using System.Collections.Generic;
using ARPIANO.Scripts.Models;
using ARPIANO.Scripts.Lessons;

namespace ARPIANO.Scripts.Piano
{
    public class VirtualPiano : MonoBehaviour
    {
        private PianoKey whiteKeyPrefab;
        private PianoKey blackKeyPrefab;

        private float whiteKeySpacing = 1.05f;
        // how much the black key is raised above white keys (Y)
        private float blackKeyHeight = 0.1f;
        // Z offset so black keys render in front of white keys (closer to camera)
        private float blackKeyDepth = -0.2f;
        // vertical blackKey offset
        private float blackKeyForward = 1.0f;

        private float keyboardSpawnDistance = 15f;

        private enum KeyboardSize { Keys25 = 25, Keys49 = 49, Keys61 = 61, Keys76 = 76, Keys88 = 88 }
        // choose size from code; defaults to full 88-key (standard A0..C8)
        [SerializeField] private KeyboardSize keyboardSize = KeyboardSize.Keys88;
        //private KeyboardSize keyboardSize = KeyboardSize.Keys88;

        private bool hasRequestedRange;
        private int requestedFirstMidiNote;
        private int requestedWhiteKeyCount;

        private readonly Dictionary<int, PianoKey> keys = new();
        private LessonPlayer lessonPlayer;
        private bool lessonEventsSubscribed;

        public IReadOnlyDictionary<int, PianoKey> Keys => keys;

        private void OnEnable()
        {
            TrySubscribeLessonPlayer();
        }

        private void OnDisable()
        {
            if (!lessonEventsSubscribed || lessonPlayer == null)
                return;

            lessonPlayer.NoteStarted -= HandleNoteStarted;
            lessonPlayer.NoteStopped -= HandleNoteStopped;
            lessonEventsSubscribed = false;
        }

        private void Update()
        {
            if (!lessonEventsSubscribed)
                TrySubscribeLessonPlayer();
        }

        private void TrySubscribeLessonPlayer()
        {
            if (lessonEventsSubscribed)
                return;

            if (lessonPlayer == null)
                lessonPlayer = FindAnyObjectByType<LessonPlayer>();

            if (lessonPlayer == null)
                return;

            lessonPlayer.NoteStarted += HandleNoteStarted;
            lessonPlayer.NoteStopped += HandleNoteStopped;
            lessonEventsSubscribed = true;
        }
        private void Awake()
        {
            // Load prefabs programmatically so Inspector cannot override them
            whiteKeyPrefab = PianoPrefabLoader.LoadWhiteKey();
            blackKeyPrefab = PianoPrefabLoader.LoadBlackKey();

            Debug.Log($"VirtualPiano Awake: whiteKeyPrefab={(whiteKeyPrefab!=null?whiteKeyPrefab.name:"null")}, blackKeyPrefab={(blackKeyPrefab!=null?blackKeyPrefab.name:"null")}");

        }

        private struct KeyDefinition
        {
            public bool Black;
            public float Offset;
            public KeyDefinition(bool black, float offset)
            {
                Black = black;
                Offset = offset;
            }
        }

        private readonly KeyDefinition[] octave =
        {
            new(false,0),      // A
            new(true,0f),    // A#
            new(false,0),      // B
            new(false,0),      // C
            new(true,0f),    // C#
            new(false,0),      // D
            new(true,0f),    // D#
            new(false,0),      // E
            new(false,0),      // F
            new(true,0f),    // F#
            new(false,0),      // G
            new(true,0f)     // G#
        };

        
        public bool GenerateVisibleRange(int firstMidiNote, int whiteKeyCount)
        {
            if (whiteKeyCount <= 0)
                return false;

            hasRequestedRange = true;
            requestedFirstMidiNote = Mathf.Clamp(firstMidiNote, 21, 108);
            requestedWhiteKeyCount = whiteKeyCount;
            GenerateKeyboard();
            return keys.Count > 0;
        }

        private void GenerateKeyboard()
        {
            int keyCount = (int)keyboardSize;
            int startMidi;
            int endMidi;

            if (hasRequestedRange)
            {
                startMidi = requestedFirstMidiNote;
                int remainingWhiteKeys = requestedWhiteKeyCount;
                endMidi = startMidi;
                while (endMidi <= 108 && remainingWhiteKeys > 0)
                {
                    if (!IsBlackKey(endMidi))
                        remainingWhiteKeys--;
                    endMidi++;
                }
                endMidi = Mathf.Min(108, endMidi - 1);
            }
            else
            {
                if (keyboardSize == KeyboardSize.Keys88)
                {
                    // standard 88-key piano A0..C8
                    startMidi = 21;
                }
                else
                {
                    // center around middle C (60)
                    startMidi = Mathf.Clamp(60 - keyCount / 2, 0, 127 - keyCount + 1);
                }

                endMidi = startMidi + keyCount - 1;
            }

            foreach (PianoKey existingKey in keys.Values)
            {
                if (existingKey != null)
                    Destroy(existingKey.gameObject);
            }
            keys.Clear();

            float whiteX = 0f;

            for (int midi = startMidi; midi <= endMidi; midi++)
            {
                int idx = (midi - 21) % 12;
                bool isBlack = octave[idx].Black;
                float keyOffset = octave[idx].Offset;

                PianoKey prefab = isBlack ? blackKeyPrefab : whiteKeyPrefab;

                PianoKey key = null;

                if (prefab != null)
                {
                    key = Instantiate(prefab, transform);
                }
                else
                {
                    // create a flat placeholder key
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.SetParent(transform, false);
                    go.name = (isBlack ? $"BlackKey_{midi}" : $"WhiteKey_{midi}");
                    if (isBlack)
                        go.transform.localScale = new Vector3(whiteKeySpacing * 0.6f, 0.1f, 0.6f);
                    else
                        go.transform.localScale = new Vector3(whiteKeySpacing, 0.1f, 1f);
                    key = go.AddComponent<PianoKey>();
                }

                key.Initialize(midi, isBlack);

                // color the key: try all renderers on the key/gameobject
                var renderers = key.gameObject.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    try
                    {
                        r.material.color = isBlack ? Color.black : Color.white;
                    }
                    catch { }
                }

                if (isBlack)
                {
                    // place the black key above the white key surface
                    key.transform.localPosition = new Vector3(
                        whiteX - whiteKeySpacing * 0.5f,
                        0f,
                        blackKeyForward);
                }
                else
                {
                    key.transform.localPosition = new Vector3(
                        whiteX,
                        0f,
                        0f);

                    whiteX += whiteKeySpacing;
                }

                keys.Add(midi, key);
            }

            Debug.Log($"Generated {keys.Count} keys.");
        }

        private void HandleNoteStarted(PianoNote note)
        {
            if (keys.TryGetValue(note.MidiNumber, out PianoKey key))
            {
                key.Press();
            }
        }

        private void HandleNoteStopped(PianoNote note)
        {
            if (keys.TryGetValue(note.MidiNumber, out PianoKey key))
            {
                key.Release();
            }
        }

        private bool IsBlackKey(int midi)
        {
            switch (midi % 12)
            {
                case 1:
                case 3:
                case 6:
                case 8:
                case 10:
                    return true;

                default:
                    return false;
            }
        }

        public PianoKey GetKey(int midi)
        {
            keys.TryGetValue(midi, out PianoKey key);
            return key;
        }
    }
}