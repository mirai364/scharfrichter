using System;
using System.Collections.Generic;
using System.IO;

namespace Scharfrichter.Codec.Charts
{
    public static class ChuniPC
    {
        public struct Point
        {
            public int linearOffset;
            public int position;
        }

        public struct resetPoint
        {
            public int resetLinearOffset;
            public int currentIdentifier;
        }

        private sealed class ReadState
        {
            public ChartChuni Chart = new ChartChuni();
            public int Resolution;
            public int CurrentMeasure;
            public resetPoint HoldResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            public resetPoint SlideResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            public resetPoint AirHoldResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            public Dictionary<Point, List<int>> HoldPending = new Dictionary<Point, List<int>>();
            public Dictionary<Point, List<int>> SlidePending = new Dictionary<Point, List<int>>();
            public Dictionary<Point, List<int>> AirHoldPending = new Dictionary<Point, List<int>>();
        }

        /// <summary>
        /// Reads a CHUNITHM PC chart from a tab-separated text stream.
        /// </summary>
        public static ChartChuni Read(StreamReader source)
        {
            ReadState state = new ReadState();
            string line;

            while ((line = source.ReadLine()) != null)
            {
                ProcessLine(line, state);
            }

            AddMeasureEntries(state.Chart, state.CurrentMeasure, state.Resolution);
            FinalizeChart(state.Chart);
            return state.Chart;
        }

        /// <summary>
        /// Handles a single chart line and routes note records to event-specific builders.
        /// </summary>
        private static void ProcessLine(string line, ReadState state)
        {
            string[] parts = line.Split('\t');
            if (parts.Length == 0)
                return;

            if (TryReadHeaderLine(parts, state))
                return;
            if (!IsEventLine(parts))
                return;

            state.CurrentMeasure = int.Parse(parts[1]);
            int measurePosition = int.Parse(parts[2]);
            int notesPosition = 0;
            int notesWidth = 0;
            ReadNotePlacement(parts, out notesPosition, out notesWidth);

            ProcessEventLine(parts, state, measurePosition, notesPosition, notesWidth);
        }

        /// <summary>
        /// Reads non-note header lines such as resolution and creator metadata.
        /// </summary>
        private static bool TryReadHeaderLine(string[] parts, ReadState state)
        {
            if (parts[0] == "RESOLUTION")
            {
                state.Resolution = int.Parse(parts[1]);
                return true;
            }

            if (parts[0] == "CREATOR")
            {
                state.Chart.Tags["DESIGNER"] = parts[1];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether a parsed line represents a note or timing event.
        /// </summary>
        private static bool IsEventLine(string[] parts)
        {
            return parts[0].Length == 3 && parts[0] != "MET";
        }

        /// <summary>
        /// Reads note position and width columns when they are present.
        /// </summary>
        private static void ReadNotePlacement(string[] parts, out int notesPosition, out int notesWidth)
        {
            notesPosition = 0;
            notesWidth = 0;
            if (parts.Length > 4)
            {
                notesPosition = int.Parse(parts[3]);
                int tmp;
                if (int.TryParse(parts[4], out tmp))
                    notesWidth = tmp;
            }
        }

        /// <summary>
        /// Converts a parsed event line into one or more chart entries.
        /// </summary>
        private static void ProcessEventLine(string[] parts, ReadState state, int measurePosition, int notesPosition, int notesWidth)
        {
            int startLinearOffset = state.CurrentMeasure * state.Resolution + measurePosition;

            switch (parts[0])
            {
                case "BPM":
                    AddTempoEntry(state.Chart, startLinearOffset, parts[3]);
                    break;
                case "TAP":
                    AddMarkerEntry(state.Chart, 1, startLinearOffset, notesPosition, 10 + notesWidth);
                    break;
                case "CHR":
                    AddMarkerEntry(state.Chart, 1, startLinearOffset, notesPosition, 20 + notesWidth);
                    break;
                case "HLD":
                    AddLinkedMarkerPair(state.Chart, state.HoldPending, ref state.HoldResetPoint, 2, startLinearOffset, startLinearOffset + int.Parse(parts[5]), notesPosition, notesWidth, notesPosition, notesWidth, 20);
                    break;
                case "FLK":
                    AddMarkerEntry(state.Chart, 1, startLinearOffset, notesPosition, 30 + notesWidth);
                    break;
                case "AIR":
                    AddMarkerEntry(state.Chart, 5, startLinearOffset, notesPosition, 10 + notesWidth);
                    break;
                case "AUL":
                    AddMarkerEntry(state.Chart, 5, startLinearOffset, notesPosition, 30 + notesWidth);
                    break;
                case "AUR":
                    AddMarkerEntry(state.Chart, 5, startLinearOffset, notesPosition, 40 + notesWidth);
                    break;
                case "ADW":
                    AddMarkerEntry(state.Chart, 5, startLinearOffset, notesPosition, 20 + notesWidth);
                    break;
                case "ADL":
                    AddMarkerEntry(state.Chart, 5, startLinearOffset, notesPosition, 50 + notesWidth);
                    break;
                case "ADR":
                    AddMarkerEntry(state.Chart, 5, startLinearOffset, notesPosition, 60 + notesWidth);
                    break;
                case "AHD":
                    AddLinkedMarkerPair(state.Chart, state.AirHoldPending, ref state.AirHoldResetPoint, 4, startLinearOffset, startLinearOffset + int.Parse(parts[6]), notesPosition, notesWidth, notesPosition, notesWidth, 20);
                    break;
                case "MNE":
                    AddMarkerEntry(state.Chart, 1, startLinearOffset, notesPosition, 40 + notesWidth);
                    break;
                case "SLC":
                    AddSlideMarkerPair(parts, state, startLinearOffset, notesPosition, notesWidth, 40);
                    break;
                case "SLD":
                    AddSlideMarkerPair(parts, state, startLinearOffset, notesPosition, notesWidth, 20);
                    break;
                case "SFL":
                    AddSpeedChangeTags(parts, state.Chart, state.CurrentMeasure, state.Resolution, measurePosition, notesPosition);
                    break;
                default:
                    Console.WriteLine("There is a sign that has not been defined");
                    break;
            }
        }

        /// <summary>
        /// Adds a BPM change entry at the specified linear offset.
        /// </summary>
        private static void AddTempoEntry(ChartChuni chart, int linearOffset, string bpmText)
        {
            EntryChuni entry = CreateBaseEntry(EntryTypeChuni.Tempo, 0, linearOffset, 0, 0);
            entry.Value = new Fraction((int)(double.Parse(bpmText) * 1000), 1000);
            chart.Entries.Add(entry);
        }

        /// <summary>
        /// Adds a single marker entry for tap, air, flick, and mine events.
        /// </summary>
        private static void AddMarkerEntry(ChartChuni chart, int player, int linearOffset, int column, int value)
        {
            EntryChuni entry = CreateBaseEntry(EntryTypeChuni.Marker, player, linearOffset, column, value);
            chart.Entries.Add(entry);
        }

        /// <summary>
        /// Creates an initialized entry with common CHUNITHM fields filled in.
        /// </summary>
        private static EntryChuni CreateBaseEntry(EntryTypeChuni type, int player, int linearOffset, int column, int value)
        {
            EntryChuni entry = new EntryChuni();
            entry.Type = type;
            entry.Player = player;
            entry.LinearOffset = new Fraction(linearOffset, 1);
            entry.Column = column;
            entry.Value = new Fraction(value, 1);
            return entry;
        }

        /// <summary>
        /// Adds a linked start/end marker pair and merges it with pending endpoints when needed.
        /// </summary>
        private static void AddLinkedMarkerPair(ChartChuni chart, Dictionary<Point, List<int>> pendingEntries, ref resetPoint reset, int player, int startLinearOffset, int endLinearOffset, int startColumn, int startWidth, int endColumn, int endWidth, int endValueBase)
        {
            int currentIdentifier = AllocateIdentifier(ref reset, startLinearOffset, endLinearOffset);
            Point startPoint = new Point() { linearOffset = startLinearOffset, position = startColumn };

            if (pendingEntries.ContainsKey(startPoint))
            {
                currentIdentifier = MergeWithPendingStart(chart, pendingEntries, startPoint);
                reset.currentIdentifier--;
            }
            else
            {
                EntryChuni startEntry = CreateBaseEntry(EntryTypeChuni.Marker, player, startLinearOffset, startColumn, 10 + startWidth);
                startEntry.Identifier = currentIdentifier;
                chart.Entries.Add(startEntry);
            }

            EntryChuni endEntry = CreateBaseEntry(EntryTypeChuni.Marker, player, endLinearOffset, endColumn, endValueBase + endWidth);
            endEntry.Identifier = currentIdentifier;
            chart.Entries.Add(endEntry);
            RegisterPendingEnd(pendingEntries, endLinearOffset, endColumn, chart.Entries.Count - 1);
        }

        /// <summary>
        /// Allocates a lane-local identifier for overlapping linked notes.
        /// </summary>
        private static int AllocateIdentifier(ref resetPoint reset, int startLinearOffset, int endLinearOffset)
        {
            if (startLinearOffset <= reset.resetLinearOffset)
            {
                reset.currentIdentifier++;
                reset.resetLinearOffset = Math.Max(reset.resetLinearOffset, endLinearOffset);
            }
            else
            {
                reset.currentIdentifier = 0;
                reset.resetLinearOffset = endLinearOffset;
            }

            return reset.currentIdentifier;
        }

        /// <summary>
        /// Reuses a previously written end point as the start of a connected note.
        /// </summary>
        private static int MergeWithPendingStart(ChartChuni chart, Dictionary<Point, List<int>> pendingEntries, Point point)
        {
            List<int> list = pendingEntries[point];
            int key = list[0];
            list.Remove(key);

            EntryChuni entry = chart.Entries[key];
            entry.Value = new Fraction(entry.Value.Numerator + 10, 1);
            chart.Entries[key] = entry;

            if (list.Count <= 0)
                pendingEntries.Remove(point);
            else
                pendingEntries[point] = list;

            return entry.Identifier;
        }

        /// <summary>
        /// Registers an end point that may become the next connected note start.
        /// </summary>
        private static void RegisterPendingEnd(Dictionary<Point, List<int>> pendingEntries, int linearOffset, int position, int entryIndex)
        {
            Point point = new Point() { linearOffset = linearOffset, position = position };
            if (pendingEntries.ContainsKey(point))
            {
                List<int> list = pendingEntries[point];
                list.Add(entryIndex);
                pendingEntries[point] = list;
            }
            else
            {
                pendingEntries[point] = new List<int> { entryIndex };
            }
        }

        /// <summary>
        /// Adds a slide or slide-control marker pair.
        /// </summary>
        private static void AddSlideMarkerPair(string[] parts, ReadState state, int startLinearOffset, int notesPosition, int notesWidth, int endValueBase)
        {
            int endLinearOffset = startLinearOffset + int.Parse(parts[5]);
            int endColumn = int.Parse(parts[6]);
            int endWidth = parts.Length > 7 ? int.Parse(parts[7]) : notesWidth;
            AddLinkedMarkerPair(state.Chart, state.SlidePending, ref state.SlideResetPoint, 3, startLinearOffset, endLinearOffset, notesPosition, notesWidth, endColumn, endWidth, endValueBase);
        }

        /// <summary>
        /// Appends HISPEED/TIL tags for a CHUNITHM speed-change event.
        /// </summary>
        private static void AddSpeedChangeTags(string[] parts, ChartChuni chart, int currentMeasure, int resolution, int measurePosition, int duration)
        {
            string til00 = "";
            if (chart.Tags.ContainsKey("TIL00"))
                til00 = chart.Tags["TIL00"] + ", ";

            chart.Tags["TIL00"] = til00 + FormatTilingPoint(currentMeasure, measurePosition, resolution) + ":" + double.Parse(parts[4]) + ", ";

            int nextMeasure = (int)Math.Floor(((double)currentMeasure * resolution + measurePosition + duration) / resolution);
            int nextPosition = currentMeasure * resolution + measurePosition + duration - nextMeasure * resolution;
            chart.Tags["TIL00"] += FormatTilingPoint(nextMeasure, nextPosition, resolution) + ":1.0";
            chart.Tags["HISPEED"] = "00";
        }

        /// <summary>
        /// Formats a CHUNITHM timing point using the 1920 ticks-per-measure bmson style.
        /// </summary>
        private static string FormatTilingPoint(int measure, int position, int resolution)
        {
            if (position == 0)
                return measure + "'0";

            int tick = (int)((480.0 * 4) * ((double)position / resolution));
            return measure + "'" + tick.ToString();
        }

        /// <summary>
        /// Adds measure entries after the last parsed measure.
        /// </summary>
        private static void AddMeasureEntries(ChartChuni chart, int currentMeasure, int resolution)
        {
            for (int i = 0; i <= currentMeasure + 3; i++)
            {
                EntryChuni entry = CreateBaseEntry(EntryTypeChuni.Measure, 1, i * resolution, 0, 0);
                chart.Entries.Add(entry);
            }
        }

        /// <summary>
        /// Sorts parsed entries and initializes the default BPM from the first tempo event.
        /// </summary>
        private static void FinalizeChart(ChartChuni chart)
        {
            chart.Entries.Sort();
            foreach (EntryChuni entry in chart.Entries)
            {
                if (entry.Type == EntryTypeChuni.Tempo)
                {
                    chart.DefaultBPM = entry.Value;
                    break;
                }
            }
        }

        /// <summary>
        /// Writes a CHUNITHM PC chart to a stream.
        /// </summary>
        public static void Write(Stream target, ChartChuni chart)
        {
            // Unsupported
        }
    }
}
