// BeamVisualizer.cs
// Purpose: Visualizes the beams of notes in a musical lesson, showing the duration and timing of each note.

using UnityEngine;
using ARPIANO.Scripts.Models;
using ARPIANO.Scripts.Piano;
using System.Collections.Generic;
using ARPIANO.Scripts.Lessons;

namespace ARPIANO.Scripts.Visualizers
{
    public class BeamVisualizer : MonoBehaviour, IVisualizer
    {
        private LessonPlayer lessonPlayer;

        [SerializeField] private Beam beamPrefab;

        private VirtualPiano piano;

        private readonly Dictionary<int, Beam> activeBeams = new();
        private bool eventsSubscribed;

        private void Awake()
        {
            lessonPlayer = FindAnyObjectByType<LessonPlayer>();
            piano = FindAnyObjectByType<VirtualPiano>();

            if (lessonPlayer == null)
                Debug.LogWarning("BeamVisualizer could not find a LessonPlayer in Awake.");

            if (piano == null)
                Debug.LogWarning("BeamVisualizer could not find a VirtualPiano in Awake.");

            if (beamPrefab == null)
            {
                beamPrefab = Resources.Load<Beam>("Prefabs/Beam");
                if (beamPrefab == null)
                {
                    Debug.LogError("BeamVisualizer could not find Beam prefab at Resources/Prefabs/Beam.");
                }
                else
                {
                    Debug.Log("BeamVisualizer auto-loaded Beam prefab from Resources/Prefabs/Beam.");
                }
            }
        }

        private void OnEnable()
        {
            Debug.Log("BeamVisualizer enabled and attempting to subscribe to LessonPlayer events.");
            TrySubscribe();
        }

        private void Update()
        {
            if (!eventsSubscribed)
                TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (eventsSubscribed)
                return;

            if (lessonPlayer == null)
                lessonPlayer = FindAnyObjectByType<LessonPlayer>();

            if (lessonPlayer == null)
                return;

            Debug.Log("BeamVisualizer subscribed to LessonPlayer events.");
            lessonPlayer.NoteStarted += NoteStarted;
            lessonPlayer.NoteStopped += NoteStopped;
            eventsSubscribed = true;
        }

        private void OnDisable()
        {
            if (!eventsSubscribed || lessonPlayer == null)
                return;

            lessonPlayer.NoteStarted -= NoteStarted;
            lessonPlayer.NoteStopped -= NoteStopped;
            eventsSubscribed = false;
        }

        public void NoteStarted(PianoNote note)
        {
            Debug.Log($"Beam spawn requested for {note.GetNoteNames()}");

            if (beamPrefab == null)
            {
                Debug.LogError("BeamVisualizer cannot spawn beam because beamPrefab is null.");
                return;
            }

            if (piano == null)
            {
                piano = FindAnyObjectByType<VirtualPiano>();
                if (piano == null)
                {
                    Debug.LogError("BeamVisualizer cannot spawn beam because VirtualPiano was not found.");
                    return;
                }
            }

            PianoKey key = piano.GetKey(note.MidiNumber);

            if (key == null)
            {
                Debug.LogWarning($"BeamVisualizer could not find PianoKey for MIDI {note.MidiNumber}.");
                return;
            }

            Vector3 keyTopCenter = GetKeyTopCenter(key);
            Beam beam = Instantiate(
                beamPrefab,
                keyTopCenter + Vector3.up * 5f,
                Quaternion.identity);

            Debug.Log($"Beam is {beamPrefab} and spawned at {beam.transform.position}");


            beam.Initialize(note.MidiNumber, -key.transform.forward);

            activeBeams.Add(note.MidiNumber, beam);
        }

        public void NoteStopped(PianoNote note)
        {
            if (!activeBeams.TryGetValue(note.MidiNumber, out Beam beam))
                return;

            beam.Finish();

            activeBeams.Remove(note.MidiNumber);

        }

        private Vector3 GetKeyTopCenter(PianoKey key)
        {
            var renderers = key.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds(key.transform.position, Vector3.zero);
            bool initialized = false;

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!initialized)
                return key.transform.position + Vector3.up * 0.5f;

            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
    }
}