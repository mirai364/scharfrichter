using System;
using System.IO;

namespace Scharfrichter.Codec.Charts
{
    public static class PopnPC
    {
        private struct PopnEvent
        {
            public long Offset;
            public int Type;
            public int Value;
            public int Parameter;
            public int ScoreLength;
        }

        /// <summary>
        /// Reads a Pop'n Music PC chart stream into a chart model.
        /// When version is 0, auto-detects the format by attempting
        /// to parse with v24 and falling back to v1 if the stream
        /// does not end exactly at the expected position.
        /// </summary>
        public static Chart Read(Stream source, int maxIndex, int version)
        {
            if (version == 0)
            {
                // Try v24 first; if it doesn't consume all data, use v1
                long savedPos = source.Position;
                try
                {
                    Chart chartV24 = TryRead(source, maxIndex, 24);
                    if (chartV24 != null)
                        return chartV24;
                }
                catch
                {
                }
                source.Position = savedPos;
                version = 1;
            }

            return TryRead(source, maxIndex, version);
        }

        /// <summary>
        /// Attempts to read the chart with the given version.
        /// Returns null if the version doesn't match the data format
        /// (stream position not matching the end).
        /// </summary>
        private static Chart TryRead(Stream source, int maxIndex, int version)
        {
            long savedPos = source.Position;
            Chart chart = new Chart();
            BinaryReader reader = new BinaryReader(source);
            Fraction[,] lastSample = new Fraction[9, 2];

            try
            {
                while (TryReadEvent(reader, version, out PopnEvent popnEvent))
                {
                    Entry entry = CreateBaseEntry(popnEvent.Offset);
                    if (ApplyEventType(entry, popnEvent, lastSample, maxIndex))
                        chart.Entries.Add(entry);

                    AddFreezeEndIfNeeded(chart, entry, popnEvent.ScoreLength);
                    if (entry.Type == EntryType.EndOfSong)
                        break;
                }

                // Allow the stream to end after the end marker (trailing padding bytes are OK)
                if (source.Position > source.Length)
                {
                    source.Position = savedPos;
                    return null;
                }

                FinalizeChart(chart);
                return chart;
            }
            catch
            {
                source.Position = savedPos;
                return null;
            }
        }

        /// <summary>
        /// Reads one binary Pop'n event from the stream.
        /// </summary>
        private static bool TryReadEvent(BinaryReader reader, int version, out PopnEvent popnEvent)
        {
            popnEvent = new PopnEvent();
            if (reader.BaseStream.Length == reader.BaseStream.Position)
                return false;

            popnEvent.Offset = reader.ReadInt32();
            popnEvent.Type = reader.ReadInt16();
            if (popnEvent.Type == 0x0645)
                return true;

            popnEvent.Value = reader.ReadByte();
            popnEvent.Parameter = reader.ReadByte();
            if (version >= 24)
                popnEvent.ScoreLength = reader.ReadInt32();
            return true;
        }

        /// <summary>
        /// Creates an entry with shared timing and default value fields initialized.
        /// </summary>
        private static Entry CreateBaseEntry(long eventOffset)
        {
            Entry entry = new Entry();
            entry.LinearOffset = new Fraction(eventOffset, 1);
            entry.Value = new Fraction(0, 1);
            return entry;
        }

        /// <summary>
        /// Converts a Pop'n event type into an entry and updates sample state when needed.
        /// </summary>
        private static bool ApplyEventType(Entry entry, PopnEvent popnEvent, Fraction[,] lastSample, int maxIndex)
        {
            int overflow = popnEvent.Parameter & 0b00001111;
            int eventValue = popnEvent.Value;
            int eventParameter = popnEvent.Parameter;

            switch (popnEvent.Type)
            {
                case 0x0145:
                    ApplyNoteMarker(entry, eventValue, lastSample);
                    break;
                case 0x0245:
                    ApplySampleChange(entry, eventValue, eventParameter, overflow, lastSample);
                    break;
                case 0x0345:
                    ApplyBgmMarker(entry, maxIndex, eventParameter >> 4);
                    break;
                case 0x0445:
                    entry.Type = EntryType.Tempo;
                    entry.Value = new Fraction(eventValue + overflow * 256, 1);
                    break;
                case 0x0645:
                    entry.Type = EntryType.EndOfSong;
                    entry.Player = 1;
                    break;
                case 0x0745:
                    entry.Type = EntryType.Marker;
                    entry.Player = 0;
                    entry.Value = new Fraction(eventValue + overflow * 256, 1);
                    entry.Parameter = eventParameter >> 4;
                    entry.Column = 0;
                    break;
                case 0x0845:
                    entry.Type = EntryType.Judgement;
                    entry.Player = 0;
                    entry.Value = new Fraction(eventValue, 1);
                    entry.Parameter = eventParameter >> 4;
                    break;
                case 0x0B00:
                    entry.Type = EntryType.Measure;
                    entry.Player = eventParameter + 1;
                    break;
                default:
                    entry.Type = EntryType.Invalid;
                    break;
            }

            return entry.Type != EntryType.Invalid;
        }

        /// <summary>
        /// Applies a playable marker event and resolves its current sample value.
        /// </summary>
        private static void ApplyNoteMarker(Entry entry, int eventValue, Fraction[,] lastSample)
        {
            eventValue &= 0b00001111;
            entry.Type = EntryType.Marker;
            entry.Player = eventValue > 4 ? 2 : 1;
            entry.Column = eventValue > 4 ? eventValue - 4 : eventValue;
            entry.Value = lastSample[entry.Column, entry.Player - 1];
        }

        /// <summary>
        /// Applies a sample change event and stores it for following marker events.
        /// </summary>
        private static void ApplySampleChange(Entry entry, int eventValue, int eventParameter, int overflow, Fraction[,] lastSample)
        {
            eventParameter >>= 4;
            entry.Type = EntryType.Sample;
            entry.Player = eventParameter > 4 ? 2 : 1;
            entry.Column = eventParameter > 4 ? eventParameter - 4 : eventParameter;
            entry.Value = new Fraction(eventValue + overflow * 256, 1);
            lastSample[entry.Column, entry.Player - 1] = entry.Value;
        }

        /// <summary>
        /// Applies a background music marker event.
        /// </summary>
        private static void ApplyBgmMarker(Entry entry, int maxIndex, int parameter)
        {
            entry.Type = EntryType.Marker;
            entry.Player = 0;
            entry.Value = new Fraction(maxIndex == -1 ? 1 : maxIndex, 1);
            entry.Parameter = parameter;
            entry.Column = parameter;
        }

        /// <summary>
        /// Adds a freeze end marker for long-note events.
        /// </summary>
        private static void AddFreezeEndIfNeeded(Chart chart, Entry entry, int scoreLength)
        {
            if (entry.Type != EntryType.Marker || entry.Player <= 0 || scoreLength <= 0)
                return;

            Entry freezeEntry = new Entry();
            freezeEntry.Type = EntryType.Marker;
            freezeEntry.Freeze = true;
            freezeEntry.Player = entry.Player;
            freezeEntry.LinearOffset = entry.LinearOffset + new Fraction(scoreLength, 1);
            freezeEntry.Column = entry.Column;
            freezeEntry.Value = new Fraction(0, 1);
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
        /// Writes a Pop'n Music PC chart to a stream.
        /// </summary>
        public static void Write(Stream target, Chart chart)
        {
            // Unsupported
        }
    }
}