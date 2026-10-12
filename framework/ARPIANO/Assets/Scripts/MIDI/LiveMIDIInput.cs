
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

        private sealed class PendingControlChange
        {
            public int Channel;
            public int Controller;
            public int Value;
        }

        private readonly ConcurrentQueue<PendingNoteEvent> pendingEvents =
            new ConcurrentQueue<PendingNoteEvent>();
        private readonly ConcurrentQueue<PendingControlChange>
            pendingControlChanges =
            new ConcurrentQueue<PendingControlChange>();

        public event Action<MidiEvent> RawMidiEventReceived;

        // Key: MIDI channel and note number.
        // Value: physically held note instances, in press order.
        private readonly Dictionary<
            (int Channel, int NoteNumber),
            List<LiveMIDINoteEvent>> activeNotes =
            new Dictionary<
                (int Channel, int NoteNumber),
                List<LiveMIDINoteEvent>>();

        private readonly Dictionary<
            (int Channel, int NoteNumber),
            List<LiveMIDINoteEvent>> sustainedNotes =
            new Dictionary<
                (int Channel, int NoteNumber),
                List<LiveMIDINoteEvent>>();

        private readonly Dictionary<int, bool> sustainByChannel =
            new Dictionary<int, bool>();

        private InputDevice inputDevice;

        public event Action<LiveMIDINoteEvent> NotePressed;
        public event Action<LiveMIDINoteEvent> NoteReleased;
        public event Action AllNotesReleased;

        public bool IsConnected => inputDevice != null;

        // Read-only view of the notes currently held.
        public IReadOnlyCollection<LiveMIDINoteEvent> ActiveNotes
        {
            get
            {
                var notes = new List<LiveMIDINoteEvent>();

                foreach (var entries in activeNotes.Values)
                    notes.AddRange(entries);

                return notes;
            }
        }

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

            RawMidiEventReceived?.Invoke(e.Event);
            
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
            else if (e.Event is ControlChangeEvent controlChange &&
                     (int)controlChange.ControlNumber == 64)
            {
                pendingControlChanges.Enqueue(new PendingControlChange
                {
                    Channel = (int)controlChange.Channel,
                    Controller = 64,
                    Value = (int)controlChange.ControlValue
                });
            }
        }

        private void Update()
        {
            while (pendingControlChanges.TryDequeue(
                       out var controlChange))
            {
                if (controlChange.Controller == 64)
                {
                    bool wasDown =
                        IsSustainDown(controlChange.Channel);

                    bool isDown =
                        controlChange.Value >= 64;

                    sustainByChannel[controlChange.Channel] = isDown;

                    if (wasDown && !isDown)
                        ReleaseSustainedNotes(controlChange.Channel);
                }
            }

            while (pendingEvents.TryDequeue(out var pending))
            {
                var note = pending.Note;
                var key = (note.Channel, note.NoteNumber);

                if (pending.IsNoteOn)
                {
                    if (!activeNotes.TryGetValue(key, out var entries))
                    {
                        entries = new List<LiveMIDINoteEvent>();
                        activeNotes.Add(key, entries);
                    }

                    entries.Add(note);
                    NotePressed?.Invoke(note);

                    Debug.Log(
                        $"[Live MIDI] PRESSED: MIDI {note.NoteNumber}, " +
                        $"velocity {note.Velocity}, channel {note.Channel}");
                }
                else
                {
                    if (activeNotes.TryGetValue(key, out var entries) &&
                        entries.Count > 0)
                    {
                        int lastIndex = entries.Count - 1;
                        LiveMIDINoteEvent activeNote = entries[lastIndex];
                        entries.RemoveAt(lastIndex);

                        if (entries.Count == 0)
                            activeNotes.Remove(key);

                        if (IsSustainDown(note.Channel))
                        {
                            if (!sustainedNotes.TryGetValue(
                                    key,
                                    out var sustainedEntries))
                            {
                                sustainedEntries =
                                    new List<LiveMIDINoteEvent>();
                                sustainedNotes.Add(
                                    key,
                                    sustainedEntries);
                            }

                            sustainedEntries.Add(activeNote);
                        }
                        else
                        {
                            NoteReleased?.Invoke(activeNote);
                        }

                        Debug.Log(
                            $"[Live MIDI] RELEASED: MIDI {note.NoteNumber}, " +
                            $"channel {note.Channel}");
                    }
                }
            }
        }

        public bool IsNoteActive(int channel, int noteNumber)
        {
            return activeNotes.TryGetValue(
                       (channel, noteNumber),
                       out var entries) &&
                entries.Count > 0;
        }

        private void OnDestroy()
        {
            ReleaseAllNotes();
            DisposeInputDevice();
            pendingEvents.Clear();
            pendingControlChanges.Clear();
            activeNotes.Clear();
            sustainedNotes.Clear();
            sustainByChannel.Clear();
        }

        private bool IsSustainDown(int channel)
        {
            return sustainByChannel.TryGetValue(
                       channel,
                       out bool isDown) &&
                isDown;
        }

        private void ReleaseSustainedNotes(int channel)
        {
            var keys = new List<(int Channel, int NoteNumber)>();

            foreach (var pair in sustainedNotes)
            {
                if (pair.Key.Channel == channel)
                    keys.Add(pair.Key);
            }

            foreach (var key in keys)
            {
                if (!sustainedNotes.TryGetValue(
                        key,
                        out var entries))
                    continue;

                foreach (var note in entries)
                    NoteReleased?.Invoke(note);

                sustainedNotes.Remove(key);
            }
        }

        private void ReleaseAllNotes()
        {
            foreach (var entries in activeNotes.Values)
            {
                foreach (var note in entries)
                    NoteReleased?.Invoke(note);
            }

            foreach (var entries in sustainedNotes.Values)
            {
                foreach (var note in entries)
                    NoteReleased?.Invoke(note);
            }

            AllNotesReleased?.Invoke();
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
