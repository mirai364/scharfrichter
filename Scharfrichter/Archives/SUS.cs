using Scharfrichter.Codec.Charts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Scharfrichter.Codec.Archives
{
    public class SUS : Archive
    {
        private const int ColumnsPerPlayer = 16;
        private const int MaxIdentifierChannels = 35;
        private const int MaxLaneOperation = 80;
        private const int TempoOperation = 81;

        public ChartChuni chart;

        /// <summary>
        /// Initializes an empty SUS archive wrapper.
        /// </summary>
        public SUS() { }

        private sealed class SusWriteBuffers
        {
            public MemoryStream Header = new MemoryStream();
            public MemoryStream ShortNote = new MemoryStream();
            public MemoryStream Hold = new MemoryStream();
            public MemoryStream Slide = new MemoryStream();
            public MemoryStream AirHold = new MemoryStream();
            public MemoryStream Air = new MemoryStream();
            public StreamWriter HeaderWriter;
            public StreamWriter ShortNoteWriter;
            public StreamWriter HoldWriter;
            public StreamWriter SlideWriter;
            public StreamWriter AirHoldWriter;
            public StreamWriter AirWriter;

            /// <summary>
            /// Creates stream writers and initializes each SUS data section label.
            /// </summary>
            public SusWriteBuffers()
            {
                HeaderWriter = new StreamWriter(Header);
                ShortNoteWriter = CreateSectionWriter(ShortNote, "ShortNote");
                HoldWriter = CreateSectionWriter(Hold, "Hold");
                SlideWriter = CreateSectionWriter(Slide, "Slide");
                AirHoldWriter = CreateSectionWriter(AirHold, "AirHold");
                AirWriter = CreateSectionWriter(Air, "Air");
            }

            /// <summary>
            /// Flushes all section writers before their streams are copied to the target.
            /// </summary>
            public void Flush()
            {
                HeaderWriter.Flush();
                ShortNoteWriter.Flush();
                HoldWriter.Flush();
                SlideWriter.Flush();
                AirHoldWriter.Flush();
                AirWriter.Flush();
            }
        }

        private struct LaneDefinition
        {
            public EntryTypeChuni Type;
            public int Player;
            public int Column;
            public string Lane;
        }

        /// <summary>
        /// Removes empty evenly-spaced slots from a SUS channel value array.
        /// </summary>
        private static int[] Reduce(int[] source)
        {
            long[] primes = Util.Primes;
            int primeCount = Util.PrimeCount;
            int count = source.Length;
            int[] result = new int[count];
            bool fail = false;

            Array.Copy(source, result, count);

            while (!fail && count > 1)
            {
                for (int i = 0; i < primeCount; i++)
                {
                    int p = (int)primes[i];
                    fail = false;

                    if (count % p == 0)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            if (j % p != 0 && result[j] != 0)
                            {
                                fail = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        fail = true;
                    }

                    if (!fail)
                    {
                        result = KeepEveryNthValue(result, count, p);
                        count = result.Length;
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Creates a smaller array by keeping only every nth source value.
        /// </summary>
        private static int[] KeepEveryNthValue(int[] source, int count, int step)
        {
            int[] result = new int[count / step];
            int index = 0;
            for (int i = 0; i < count; i += step)
            {
                result[index] = source[i];
                index++;
            }
            return result;
        }

        /// <summary>
        /// Writes the chart as a SUS text stream.
        /// </summary>
        public bool Write(Stream target, bool enableBackspinScratch)
        {
            int delayPoint = 0;
            Dictionary<int, Fraction> bpmMap = new Dictionary<int, Fraction>();
            BinaryWriter writer = new BinaryWriter(target, Encoding.GetEncoding(932));
            SusWriteBuffers buffers = new SusWriteBuffers();

            PrepareHeaderTags();
            WriteHeader(buffers.HeaderWriter);
            WriteChannels(buffers, bpmMap, delayPoint);
            WriteFooter(buffers.HeaderWriter);
            FlushSusOutput(writer, buffers);
            return true;
        }

        /// <summary>
        /// Normalizes generated tags that are controlled by the SUS writer.
        /// </summary>
        private void PrepareHeaderTags()
        {
            chart.Tags["BPM"] = Math.Round((double)(chart.DefaultBPM), 3).ToString();
        }

        /// <summary>
        /// Writes the SUS metadata, request, and BPM section headers.
        /// </summary>
        private void WriteHeader(StreamWriter writer)
        {
            string wave = "music.wav";
            string waveOffset = "0";
            string jacket = "jacket.jpg";

            writer.WriteLine("Music info");
            writer.WriteLine("#TITLE \"" + chart.Tags["TITLE"] + "\"");
            writer.WriteLine("#ARTIST \"" + chart.Tags["ARTIST"] + "\"");
            writer.WriteLine("#DESIGNER \"" + chart.Tags["DESIGNER"] + "\"");
            writer.WriteLine("#DIFFICULTY " + chart.Tags["TYPE"]);
            writer.WriteLine("#PLAYLEVEL " + chart.Tags["PLAYLEVEL"]);
            writer.WriteLine("#SONGID \"" + chart.Tags["ID"] + "\"");
            writer.WriteLine("#WAVE \"" + wave + "\"");
            writer.WriteLine("#WAVEOFFSET " + waveOffset);
            writer.WriteLine("#JACKET \"" + jacket + "\"");
            writer.WriteLine("#BASEBPM " + chart.Tags["BPM"]);
            writer.WriteLine("");
            writer.WriteLine("Request");
            writer.WriteLine("#REQUEST \"mertonome enabled\"");
            writer.WriteLine("#REQUEST \"ticks_per_beat 480\"");
            writer.WriteLine("");
            writer.WriteLine("BPM");
        }

        /// <summary>
        /// Walks measures and lane operations, writing SUS channels for matching entries.
        /// </summary>
        private void WriteChannels(SusWriteBuffers buffers, Dictionary<int, Fraction> bpmMap, int delayPoint)
        {
            int currentMeasure = 0;
            int currentOperation = 0;
            int measureCount = chart.Measures;
            int bpmCount = 0;
            bool repeat = false;
            List<EntryChuni> measureEntries = new List<EntryChuni>();
            List<EntryChuni> entries = new List<EntryChuni>();
            string measureString = "";

            while (currentMeasure < measureCount)
            {
                if (!repeat)
                {
                    entries.Clear();
                    LaneDefinition lane;
                    if (currentOperation == 0)
                    {
                        measureString = FormatMeasureNumber(currentMeasure + delayPoint);
                        CollectMeasureEntries(currentMeasure, measureEntries);
                    }
                    else if (TryGetLaneDefinition(currentOperation, out lane))
                    {
                        CollectLaneEntries(currentMeasure, lane, measureEntries, entries);
                    }
                    else
                    {
                        currentOperation = 0;
                        currentMeasure++;
                        continue;
                    }
                }

                repeat = false;
                if (entries.Count > 0)
                {
                    LaneDefinition lane;
                    TryGetLaneDefinition(currentOperation, out lane);
                    repeat = WriteLaneEntries(buffers, bpmMap, ref bpmCount, entries, lane, measureString);
                }

                if (!repeat)
                    currentOperation++;
            }
        }

        /// <summary>
        /// Gets the SUS lane represented by the current operation index.
        /// </summary>
        private static bool TryGetLaneDefinition(int operation, out LaneDefinition lane)
        {
            lane = new LaneDefinition() { Type = EntryTypeChuni.Invalid, Player = 0, Column = 0, Lane = "00" };
            if (operation >= 1 && operation <= MaxLaneOperation)
            {
                int zeroBased = operation - 1;
                int player = (zeroBased / ColumnsPerPlayer) + 1;
                int column = zeroBased % ColumnsPerPlayer;
                lane.Type = EntryTypeChuni.Marker;
                lane.Player = player;
                lane.Column = column;
                lane.Lane = player.ToString() + Util.ConvertToBMEString(column, 1);
                return true;
            }

            if (operation == TempoOperation)
            {
                lane.Type = EntryTypeChuni.Tempo;
                lane.Player = 0;
                lane.Column = 0;
                lane.Lane = "08";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Copies entries for the specified measure into a reusable buffer.
        /// </summary>
        private void CollectMeasureEntries(int currentMeasure, List<EntryChuni> measureEntries)
        {
            measureEntries.Clear();
            foreach (EntryChuni entry in chart.Entries)
            {
                if (entry.MetricMeasure == currentMeasure)
                    measureEntries.Add(entry);
                else if (entry.MetricMeasure > currentMeasure)
                    break;
            }
        }

        /// <summary>
        /// Filters the current measure entries down to the selected SUS lane.
        /// </summary>
        private static void CollectLaneEntries(int currentMeasure, LaneDefinition lane, List<EntryChuni> measureEntries, List<EntryChuni> entries)
        {
            foreach (EntryChuni entry in measureEntries)
            {
                if (entry.MetricMeasure == currentMeasure &&
                    entry.Player == lane.Player &&
                    entry.Type == lane.Type &&
                    entry.Column == lane.Column &&
                    !entry.Used)
                {
                    entries.Add(entry);
                }
            }
        }

        /// <summary>
        /// Writes all identifier channels for one lane and reports whether the lane must repeat.
        /// </summary>
        private static bool WriteLaneEntries(SusWriteBuffers buffers, Dictionary<int, Fraction> bpmMap, ref int bpmCount, List<EntryChuni> entries, LaneDefinition lane, string measureString)
        {
            bool repeat = false;
            int loopCount = NeedsIdentifierChannels(lane.Player) ? MaxIdentifierChannels : 1;
            for (int identifier = 0; identifier < loopCount; identifier++)
            {
                List<EntryChuni> identifierEntries = GetIdentifierEntries(entries, identifier);
                if (identifierEntries.Count <= 0)
                    continue;

                int[] values = CreateLaneValues(buffers.HeaderWriter, bpmMap, ref bpmCount, entries, identifierEntries, lane, ref repeat);
                if (values != null)
                    WriteChannelLine(buffers, lane.Player, measureString, BuildLaneString(lane, identifier), values);
            }

            return repeat;
        }

        /// <summary>
        /// Determines whether a lane family uses the extra SUS identifier digit.
        /// </summary>
        private static bool NeedsIdentifierChannels(int player)
        {
            return player != 0 && player != 1 && player != 5;
        }

        /// <summary>
        /// Selects entries assigned to one identifier channel.
        /// </summary>
        private static List<EntryChuni> GetIdentifierEntries(List<EntryChuni> entries, int identifier)
        {
            List<EntryChuni> result = new List<EntryChuni>();
            foreach (EntryChuni entry in entries)
            {
                if (entry.Identifier == identifier)
                    result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// Appends the SUS identifier suffix when the lane family requires it.
        /// </summary>
        private static string BuildLaneString(LaneDefinition lane, int identifier)
        {
            if (NeedsIdentifierChannels(lane.Player))
                return lane.Lane + Util.ConvertToBMEString(identifier, 1);
            return lane.Lane;
        }

        /// <summary>
        /// Builds the value array for one SUS channel line.
        /// </summary>
        private static int[] CreateLaneValues(StreamWriter headerWriter, Dictionary<int, Fraction> bpmMap, ref int bpmCount, List<EntryChuni> allEntries, List<EntryChuni> identifierEntries, LaneDefinition lane, ref bool repeat)
        {
            long common = GetCommonDenominator(identifierEntries);
            long commonDivisor = GetCommonDivisor(common, 7680);
            int[] values = new int[common / commonDivisor];
            bool write = false;

            if (lane.Type == EntryTypeChuni.Marker && lane.Player != 0)
                write = FillMarkerValues(values, identifierEntries, common, commonDivisor, ref repeat);
            else if (lane.Type == EntryTypeChuni.Tempo)
                write = FillTempoValues(values, allEntries, common, commonDivisor, headerWriter, bpmMap, ref bpmCount, ref repeat);
            else
                write = FillRawValues(values, identifierEntries, common, commonDivisor, ref repeat);

            return write ? Reduce(values) : null;
        }

        /// <summary>
        /// Finds the shared denominator needed to represent all entry offsets in a channel.
        /// </summary>
        private static long GetCommonDenominator(List<EntryChuni> entries)
        {
            long common = 1;
            for (int i = 0; i < 2; i++)
            {
                foreach (EntryChuni entry in entries)
                {
                    if (common % entry.MetricOffset.Denominator != 0 && common <= int.MaxValue)
                        common *= entry.MetricOffset.Denominator;
                }
            }
            return common;
        }

        /// <summary>
        /// Calculates a divisor that keeps generated channel arrays within a practical length.
        /// </summary>
        private static long GetCommonDivisor(long common, long divisorLimit)
        {
            long commonDivisor = 1;
            while ((common / commonDivisor) > divisorLimit)
                commonDivisor *= 2;
            return commonDivisor;
        }

        /// <summary>
        /// Fills marker values for one SUS channel line.
        /// </summary>
        private static bool FillMarkerValues(int[] values, List<EntryChuni> entries, long common, long commonDivisor, ref bool repeat)
        {
            bool write = false;
            foreach (EntryChuni entry in entries)
            {
                int offset = GetValueOffset(entry, common, commonDivisor);
                if (TryPlaceValue(values, offset, entry.Freeze ? 1295 : (int)(double)entry.Value, entry, ref repeat))
                    write = true;
            }
            return write;
        }

        /// <summary>
        /// Fills tempo table values and emits #BPM records for new BPM values.
        /// </summary>
        private static bool FillTempoValues(int[] values, List<EntryChuni> entries, long common, long commonDivisor, StreamWriter headerWriter, Dictionary<int, Fraction> bpmMap, ref int bpmCount, ref bool repeat)
        {
            bool write = false;
            foreach (EntryChuni entry in entries)
            {
                int offset = GetValueOffset(entry, common, commonDivisor);
                int entryIndex = GetOrRegisterBpm(headerWriter, bpmMap, ref bpmCount, entry.Value);
                if (TryPlaceValue(values, offset, entryIndex, entry, ref repeat))
                    write = true;
            }
            return write;
        }

        /// <summary>
        /// Fills raw numeric values for non-marker channel types.
        /// </summary>
        private static bool FillRawValues(int[] values, List<EntryChuni> entries, long common, long commonDivisor, ref bool repeat)
        {
            bool write = false;
            foreach (EntryChuni entry in entries)
            {
                int offset = GetValueOffset(entry, common, commonDivisor);
                int value = (int)(entry.Value.Numerator / entry.Value.Denominator);
                if (TryPlaceValue(values, offset, value, entry, ref repeat))
                    write = true;
            }
            return write;
        }

        /// <summary>
        /// Converts an entry metric offset into a channel array index.
        /// </summary>
        private static int GetValueOffset(EntryChuni entry, long common, long commonDivisor)
        {
            long multiplier = common / entry.MetricOffset.Denominator;
            return (int)((entry.MetricOffset.Numerator * multiplier) / commonDivisor);
        }

        /// <summary>
        /// Places a value in the channel array unless it collides with an existing event.
        /// </summary>
        private static bool TryPlaceValue(int[] values, int offset, int value, EntryChuni entry, ref bool repeat)
        {
            if (offset < 0 || offset >= values.Length || entry.Used)
                return false;

            if (values[offset] != 0)
            {
                repeat = true;
                return false;
            }

            values[offset] = value;
            entry.Used = true;
            return true;
        }

        /// <summary>
        /// Gets an existing BPM table index or writes a new #BPM definition.
        /// </summary>
        private static int GetOrRegisterBpm(StreamWriter headerWriter, Dictionary<int, Fraction> bpmMap, ref int bpmCount, Fraction value)
        {
            foreach (KeyValuePair<int, Fraction> bpmEntry in bpmMap)
            {
                if (bpmEntry.Value == value)
                    return bpmEntry.Key;
            }

            bpmCount++;
            headerWriter.WriteLine("#BPM" + bpmCount.ToString("00") + ":" + Math.Round((double)value, 3).ToString());
            bpmMap[bpmCount] = value;
            return bpmCount;
        }

        /// <summary>
        /// Writes a reduced value array to the correct SUS output section.
        /// </summary>
        private static void WriteChannelLine(SusWriteBuffers buffers, int player, string measureString, string laneString, int[] values)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("#" + measureString + laneString + ":");
            for (int i = 0; i < values.Length; i++)
                builder.Append(values[i].ToString("00"));

            GetSectionWriter(buffers, player).WriteLine(builder.ToString());
        }

        /// <summary>
        /// Selects the writer for a SUS player or event section.
        /// </summary>
        private static StreamWriter GetSectionWriter(SusWriteBuffers buffers, int player)
        {
            switch (player)
            {
                case 0: return buffers.HeaderWriter;
                case 1: return buffers.ShortNoteWriter;
                case 2: return buffers.HoldWriter;
                case 3: return buffers.SlideWriter;
                case 4: return buffers.AirHoldWriter;
                case 5: return buffers.AirWriter;
                default: return buffers.HeaderWriter;
            }
        }

        /// <summary>
        /// Writes measure pulse and optional high-speed metadata.
        /// </summary>
        private void WriteFooter(StreamWriter writer)
        {
            writer.WriteLine("");
            writer.WriteLine("Measure's pulse");
            writer.WriteLine("#00002: 4");

            if (chart.Tags.ContainsKey("TIL00"))
            {
                writer.WriteLine("");
                writer.WriteLine("#TIL00: " + "\"" + chart.Tags["TIL00"] + "\"");
                writer.WriteLine("#HISPEED 00");
            }
        }

        /// <summary>
        /// Flushes all SUS sections to the target stream in the expected order.
        /// </summary>
        private static void FlushSusOutput(BinaryWriter writer, SusWriteBuffers buffers)
        {
            buffers.Flush();
            writer.Write(buffers.Header.ToArray());
            writer.Write(buffers.ShortNote.ToArray());
            writer.Write(buffers.Hold.ToArray());
            writer.Write(buffers.Slide.ToArray());
            writer.Write(buffers.AirHold.ToArray());
            writer.Write(buffers.Air.ToArray());
            writer.Flush();
        }

        /// <summary>
        /// Creates a section writer and emits its display label.
        /// </summary>
        private static StreamWriter CreateSectionWriter(MemoryStream stream, string title)
        {
            StreamWriter writer = new StreamWriter(stream);
            writer.WriteLine("");
            writer.WriteLine(title);
            return writer;
        }

        /// <summary>
        /// Formats a measure number as a three-digit SUS value.
        /// </summary>
        private static string FormatMeasureNumber(int measure)
        {
            string measureString = measure.ToString();
            while (measureString.Length < 3)
                measureString = "0" + measureString;
            return measureString;
        }
    }
}
