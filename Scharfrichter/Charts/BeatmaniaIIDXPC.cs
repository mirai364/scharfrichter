using System;
using System.Collections.Generic;
using System.IO;

namespace Scharfrichter.Codec.Charts
{
    public static class BeatmaniaIIDXPC
    {
        private struct IidxEvent
        {
            public long Offset;
            public int Type;
            public int Parameter;
            public int Value;
        }

        /// <summary>
        /// Reads a Beatmania IIDX PC chart stream into a chart model.
        /// </summary>
        public static Chart Read(Stream source, Dictionary<int, int> ignore)
        {
            Chart chart = new Chart();
            BinaryReader reader = new BinaryReader(source);
            Fraction[,] lastSample = new Fraction[9, 2];

            while (TryReadEvent(reader, out IidxEvent iidxEvent))
            {
                Entry entry = CreateBaseEntry(iidxEvent.Offset);
                ApplyMssCompatibility(ref iidxEvent, entry);
                ApplyReadEvent(entry, iidxEvent, ignore, lastSample);

                if (entry.Type != EntryType.Invalid)
                    chart.Entries.Add(entry);
                AddFreezeEndIfNeeded(chart, entry, iidxEvent.Value);
            }

            FinalizeChart(chart);
            return chart;
        }

        /// <summary>
        /// Reads one IIDX event and returns false when the terminator is reached.
        /// </summary>
        private static bool TryReadEvent(BinaryReader reader, out IidxEvent iidxEvent)
        {
            iidxEvent = new IidxEvent();
            iidxEvent.Offset = reader.ReadInt32();
            if (iidxEvent.Offset >= 0x7FFFFFFF)
                return false;

            iidxEvent.Type = reader.ReadByte();
            iidxEvent.Parameter = reader.ReadByte();
            iidxEvent.Value = reader.ReadUInt16();
            return true;
        }

        /// <summary>
        /// Creates an entry with common timing and value fields initialized.
        /// </summary>
        private static Entry CreateBaseEntry(long eventOffset)
        {
            Entry entry = new Entry();
            entry.LinearOffset = new Fraction(eventOffset, 1);
            entry.Value = new Fraction(0, 1);
            return entry;
        }

        /// <summary>
        /// Converts unofficial MSS scratch parameters into the normal scratch column.
        /// </summary>
        private static void ApplyMssCompatibility(ref IidxEvent iidxEvent, Entry entry)
        {
            if ((iidxEvent.Type == 0x00 || iidxEvent.Type == 0x01) && iidxEvent.Parameter == 107)
            {
                entry.IsMss = true;
                iidxEvent.Parameter = 7;
            }
        }

        /// <summary>
        /// Converts one raw IIDX event into an internal chart entry.
        /// </summary>
        private static void ApplyReadEvent(Entry entry, IidxEvent iidxEvent, Dictionary<int, int> ignore, Fraction[,] lastSample)
        {
            switch (iidxEvent.Type)
            {
                case 0x00:
                    ApplyPlayerMarker(entry, 1, iidxEvent.Parameter, ignore, lastSample);
                    break;
                case 0x01:
                    ApplyPlayerMarker(entry, 2, iidxEvent.Parameter, ignore, lastSample);
                    break;
                case 0x02:
                    ApplySampleChange(entry, 1, iidxEvent.Parameter, iidxEvent.Value, lastSample);
                    break;
                case 0x03:
                    ApplySampleChange(entry, 2, iidxEvent.Parameter, iidxEvent.Value, lastSample);
                    break;
                case 0x04:
                    entry.Type = EntryType.Tempo;
                    entry.Value = new Fraction(iidxEvent.Value, iidxEvent.Parameter);
                    break;
                case 0x06:
                    entry.Type = EntryType.EndOfSong;
                    entry.Player = iidxEvent.Parameter + 1;
                    break;
                case 0x07:
                    ApplyBgmMarker(entry, iidxEvent.Parameter, iidxEvent.Value, ignore);
                    break;
                case 0x08:
                    entry.Type = EntryType.Judgement;
                    entry.Player = 0;
                    entry.Value = new Fraction(iidxEvent.Value, 1);
                    entry.Parameter = iidxEvent.Parameter;
                    break;
                case 0x0C:
                    entry.Type = iidxEvent.Parameter == 0 ? EntryType.Measure : EntryType.Invalid;
                    entry.Player = iidxEvent.Parameter + 1;
                    break;
                default:
                    entry.Type = EntryType.Invalid;
                    break;
            }
        }

        /// <summary>
        /// Applies a playable marker event unless the player side is ignored.
        /// </summary>
        private static void ApplyPlayerMarker(Entry entry, int player, int column, Dictionary<int, int> ignore, Fraction[,] lastSample)
        {
            if (ignore.ContainsKey(player))
                return;

            entry.Type = EntryType.Marker;
            entry.Player = player;
            entry.Column = column;
            entry.Value = lastSample[entry.Column, entry.Player - 1];
        }

        /// <summary>
        /// Applies a sample-change event and stores it for following marker events.
        /// </summary>
        private static void ApplySampleChange(Entry entry, int player, int column, int value, Fraction[,] lastSample)
        {
            entry.Type = EntryType.Sample;
            entry.Player = player;
            entry.Column = column;
            entry.Value = new Fraction(value, 1);
            lastSample[entry.Column, entry.Player - 1] = entry.Value;
        }

        /// <summary>
        /// Applies a background music marker unless BGM is ignored.
        /// </summary>
        private static void ApplyBgmMarker(Entry entry, int parameter, int value, Dictionary<int, int> ignore)
        {
            if (ignore.ContainsKey(3))
                return;

            entry.Type = EntryType.Marker;
            entry.Player = 0;
            entry.Value = new Fraction(value, 1);
            entry.Parameter = parameter;
            entry.Column = 0;
        }

        /// <summary>
        /// Adds a freeze end marker when a marker event carries a duration value.
        /// </summary>
        private static void AddFreezeEndIfNeeded(Chart chart, Entry entry, int value)
        {
            if (entry.Type != EntryType.Marker || entry.Player <= 0 || value <= 0)
                return;

            Entry freezeEntry = new Entry();
            freezeEntry.Type = EntryType.Marker;
            freezeEntry.Freeze = true;
            freezeEntry.Player = entry.Player;
            freezeEntry.LinearOffset = entry.LinearOffset + new Fraction(value, 1);
            freezeEntry.Column = entry.Column;
            freezeEntry.Value = new Fraction(0, 1);
            if (entry.IsMss)
                freezeEntry.IsMss = true;
            chart.Entries.Add(freezeEntry);
        }

        /// <summary>
        /// Sorts entries and initializes the default BPM from the first tempo event.
        /// </summary>
        private static void FinalizeChart(Chart chart)
        {
            chart.Entries.Sort();
            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type == EntryType.Tempo)
                {
                    chart.DefaultBPM = entry.Value;
                    break;
                }
            }
        }

        /// <summary>
        /// Writes a Beatmania IIDX PC chart stream.
        /// </summary>
        public static void Write(Stream target, Chart chart)
        {
            BinaryWriter writer = new BinaryWriter(target);
            WriteNoteCounts(writer, chart);
            foreach (Entry entry in chart.Entries)
                WriteEntryIfSupported(writer, entry);
            WriteTerminator(writer);
        }

        /// <summary>
        /// Writes player note-count metadata records.
        /// </summary>
        private static void WriteNoteCounts(BinaryWriter writer, Chart chart)
        {
            WriteNoteCount(writer, 0, chart.NoteCount(1));
            WriteNoteCount(writer, 1, chart.NoteCount(2));
        }

        /// <summary>
        /// Writes one note-count metadata record.
        /// </summary>
        private static void WriteNoteCount(BinaryWriter writer, byte player, int noteCount)
        {
            writer.Write((Int32)0);
            writer.Write((byte)0x10);
            writer.Write(player);
            writer.Write((Int16)noteCount);
        }

        /// <summary>
        /// Converts and writes an entry when it can be represented by the IIDX PC format.
        /// </summary>
        private static void WriteEntryIfSupported(BinaryWriter writer, Entry entry)
        {
            if (!TryCreateWriteEvent(entry, out IidxEvent iidxEvent))
                return;

            writer.Write((Int32)iidxEvent.Offset);
            writer.Write((byte)iidxEvent.Type);
            writer.Write((byte)iidxEvent.Parameter);
            writer.Write((Int16)iidxEvent.Value);
        }

        /// <summary>
        /// Converts an internal entry into a raw IIDX event.
        /// </summary>
        private static bool TryCreateWriteEvent(Entry entry, out IidxEvent iidxEvent)
        {
            iidxEvent = new IidxEvent();
            iidxEvent.Offset = (Int32)(entry.LinearOffset);
            iidxEvent.Parameter = entry.Parameter & 0xFF;

            switch (entry.Type)
            {
                case EntryType.EndOfSong:
                    iidxEvent.Type = 0x06;
                    iidxEvent.Parameter = 0;
                    iidxEvent.Value = 0;
                    return true;
                case EntryType.Judgement:
                    iidxEvent.Type = 0x08;
                    iidxEvent.Parameter = entry.Parameter;
                    iidxEvent.Value = (Int16)entry.Value;
                    return true;
                case EntryType.Marker:
                    return TryCreateMarkerWriteEvent(entry, ref iidxEvent);
                case EntryType.Measure:
                    iidxEvent.Type = 0x0C;
                    iidxEvent.Parameter = entry.Player - 1;
                    return true;
                case EntryType.Sample:
                    return TryCreateSampleWriteEvent(entry, ref iidxEvent);
                case EntryType.Tempo:
                    ApplyTempoWriteEvent(entry, ref iidxEvent);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Converts a marker entry into a raw IIDX event.
        /// </summary>
        private static bool TryCreateMarkerWriteEvent(Entry entry, ref IidxEvent iidxEvent)
        {
            if (entry.Player < 1)
            {
                iidxEvent.Type = 0x07;
                iidxEvent.Value = (Int16)entry.Value;
            }
            else
            {
                iidxEvent.Type = entry.Player - 1;
                iidxEvent.Value = 0;
                iidxEvent.Parameter = entry.Column;
            }
            return true;
        }

        /// <summary>
        /// Converts a sample-change entry into a raw IIDX event.
        /// </summary>
        private static bool TryCreateSampleWriteEvent(Entry entry, ref IidxEvent iidxEvent)
        {
            if (entry.Player <= 0)
                return false;

            iidxEvent.Type = entry.Player + 1;
            iidxEvent.Value = (Int16)entry.Value;
            iidxEvent.Parameter = entry.Column;
            return true;
        }

        /// <summary>
        /// Converts a tempo entry into the numerator/denominator representation used by IIDX PC.
        /// </summary>
        private static void ApplyTempoWriteEvent(Entry entry, ref IidxEvent iidxEvent)
        {
            long numerator = entry.Value.Numerator;
            long denominator = entry.Value.Denominator;
            while (numerator > 32767 || denominator > 255)
            {
                numerator /= 2;
                denominator /= 2;
            }
            iidxEvent.Value = (Int16)numerator;
            iidxEvent.Parameter = (byte)denominator;
            iidxEvent.Type = 0x04;
        }

        /// <summary>
        /// Writes the end-of-chart sentinel.
        /// </summary>
        private static void WriteTerminator(BinaryWriter writer)
        {
            writer.Write((Int32)0x7FFFFFFF);
            writer.Write((Int32)0);
        }
    }
}
