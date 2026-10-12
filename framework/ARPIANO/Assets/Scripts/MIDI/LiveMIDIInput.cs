
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace ARPIANO.Scripts.MIDI
{
    public sealed class LiveMIDINoteEvent
    {
        public int NoteNumber { get; }
        public int Velocity { get; }
        public int Channel { get; }

        public LiveMIDINoteEvent(int noteNumber, int velocity, int channel)
        {
            NoteNumber = noteNumber;
            Velocity = velocity;
            Channel = channel;
        }
    }

    public class LiveMIDIInput : MonoBehaviour
    {
        private sealed class PendingNoteEvent
        {
            public bool IsNoteOn;
            public LiveMIDINoteEvent Note;
        }

        private readonly ConcurrentQueue<PendingNoteEvent> pendingEvents =
            new ConcurrentQueue<PendingNoteEvent>();

        // Key: MIDI channel and note number.
        // Value: most recent Note On data for that active note.
        private readonly Dictionary<(int Channel, int NoteNumber), LiveMIDINoteEvent>
            activeNotes = new Dictionary<(int Channel, int NoteNumber), LiveMIDINoteEvent>();

        private InputDevice inputDevice;

        public event Action<LiveMIDINoteEvent> NotePressed;
        public event Action<LiveMIDINoteEvent> NoteReleased;

        public bool IsConnected => inputDevice != null;

        // Read-only view of the notes currently held.
        public IReadOnlyCollection<LiveMIDINoteEvent> ActiveNotes => activeNotes.Values;

        private void Start()
        {
            try
            {
                var devices = InputDevice.GetAll();

                Debug.Log($"[Live MIDI] Found {devices.Count} input device(s).");

                foreach (var device in devices)
                {
                    Debug.Log($"[Live MIDI] Available device: {device.Name}");
                    device.Dispose();
                }

                if (InputDevice.GetDevicesCount() == 0)
                {
                    Debug.LogWarning("[Live MIDI] No input devices found.");
                    return;
                }

                // Temporary: use the first detected device.
                inputDevice = InputDevice.GetByIndex(0);
                inputDevice.EventReceived += OnMidiEventReceived;
                inputDevice.StartEventsListening();

                Debug.Log($"[Live MIDI] Listening to: {inputDevice.Name}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Live MIDI] Failed to start: {exception}");
                DisposeInputDevice();
            }
        }

        private void OnMidiEventReceived(
            object sender,
            MidiEventReceivedEventArgs e)
        {
            // The callback may run on a background thread.
            // Capture MIDI data here; update Unity state in Update().
            if (e.Event is NoteOnEvent noteOn)
            {
                int velocity = (int)noteOn.Velocity;

                pendingEvents.Enqueue(new PendingNoteEvent
                {
                    IsNoteOn = velocity > 0,
                    Note = new LiveMIDINoteEvent(
                        (int)noteOn.NoteNumber,
                        velocity,
                        (int)noteOn.Channel)
                });
            }
            else if (e.Event is NoteOffEvent noteOff)
            {
                pendingEvents.Enqueue(new PendingNoteEvent
                {
                    IsNoteOn = false,
                    Note = new LiveMIDINoteEvent(
                        (int)noteOff.NoteNumber,
                        (int)noteOff.Velocity,
                        (int)noteOff.Channel)
                });
            }
        }

        private void Update()
        {
            while (pendingEvents.TryDequeue(out var pending))
            {
                var note = pending.Note;
                var key = (note.Channel, note.NoteNumber);

                if (pending.IsNoteOn)
                {
                    // Ignore duplicate Note On messages while the same
                    // channel/pitch pair is already active.
                    if (!activeNotes.ContainsKey(key))
                    {
                        activeNotes.Add(key, note);
                        NotePressed?.Invoke(note);

                        Debug.Log(
                            $"[Live MIDI] PRESSED: MIDI {note.NoteNumber}, " +
                            $"velocity {note.Velocity}, channel {note.Channel}");
                            
                        Debug.Log($"[Live MIDI] Active notes: {activeNotes.Count}");
                    }
                }
                else
                {
                    if (activeNotes.TryGetValue(key, out var activeNote))
                    {
                        activeNotes.Remove(key);
                        NoteReleased?.Invoke(activeNote);

                        Debug.Log(
                            $"[Live MIDI] RELEASED: MIDI {note.NoteNumber}, " +
                            $"channel {note.Channel}");
                    }
                }
            }
        }

        public bool IsNoteActive(int channel, int noteNumber)
        {
            return activeNotes.ContainsKey((channel, noteNumber));
        }

        private void OnDestroy()
        {
            DisposeInputDevice();
            pendingEvents.Clear();
            activeNotes.Clear();
        }

        private void DisposeInputDevice()
        {
            if (inputDevice == null)
                return;

            inputDevice.EventReceived -= OnMidiEventReceived;

            try
            {
                inputDevice.StopEventsListening();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[Live MIDI] Error stopping input: {exception.Message}");
            }

            inputDevice.Dispose();
            inputDevice = null;
        }
    }
}
