using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        private struct SlaRegion
        {
            public int Measure;
            public int Offset;
            public int Column;
            public int Width;
            public int Duration;
            public int TimelineId;
        }

        private sealed class ReadState
        {
            public ChartChuni Chart = new ChartChuni();
            public int Resolution;
            public int CurrentMeasure;
            public resetPoint HoldResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            public resetPoint SlideResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            public resetPoint AirHoldResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            public resetPoint AirSlideResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            public resetPoint AirCrushResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            public Dictionary<Point, List<int>> HoldPending = new Dictionary<Point, List<int>>();
            public Dictionary<Point, List<int>> SlidePending = new Dictionary<Point, List<int>>();
            public Dictionary<Point, List<int>> AirHoldPending = new Dictionary<Point, List<int>>();
            public Dictionary<Point, List<int>> AirSlidePending = new Dictionary<Point, List<int>>();
            public Dictionary<Point, List<int>> AirCrushPending = new Dictionary<Point, List<int>>();
            public List<SlaRegion> SlaRegions = new List<SlaRegion>();
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
            SerializeSlaRegions(state);
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
            if (parts[0] == "CLK")
                return;
            if (parts[0] == "MET")
            {
                ProcessMetLine(parts, state);
                return;
            }
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
                state.Chart.Tags["RESOLUTION"] = state.Resolution.ToString();
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
            return parts[0].Length == 3 && parts[0] != "MET" && parts[0] != "CLK";
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
        /// Processes a MET (meter change) event line.
        /// </summary>
        private static void ProcessMetLine(string[] parts, ReadState state)
        {
            int currentMeasure = int.Parse(parts[1]);
            int measurePosition = int.Parse(parts[2]);
            EntryChuni entry = new EntryChuni();
            entry.Type = EntryTypeChuni.Event;
            entry.LinearOffset = new Fraction(currentMeasure * state.Resolution + measurePosition, 1);
            entry.Value = new Fraction(int.Parse(parts[4]), int.Parse(parts[3]));
            // Keep the chart measure from the source (MET 48 192 -> measure 48)
            // so the UGC converter can emit @BEAT at the original measure.
            entry.Parameter = currentMeasure;
            state.Chart.Entries.Add(entry);
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
                    AddMarkerEntry(state.Chart, 1, startLinearOffset, notesPosition, 100 + notesWidth);
                    break;
                case "CHR":
                    AddMarkerEntry(state.Chart, 1, startLinearOffset, notesPosition, 200 + notesWidth);
                    SetEntryTag(state.Chart, 1, startLinearOffset, notesPosition, GetOptionalPart(parts, 5));
                    break;
                case "HLD":
                    AddLinkedMarkerPair(state.Chart, state.HoldPending, ref state.HoldResetPoint, 2, startLinearOffset, startLinearOffset + int.Parse(parts[5]), notesPosition, notesWidth, notesPosition, notesWidth, 200);
                    break;
                case "HXD":
                    // Hard hold: same lane encoding as HLD (h in UGC), but keep
                    // the source type in Tag for downstream converters.
                    AddLinkedMarkerPair(state.Chart, state.HoldPending, ref state.HoldResetPoint, 2, startLinearOffset, startLinearOffset + int.Parse(parts[5]), notesPosition, notesWidth, notesPosition, notesWidth, 200);
                    SetEntryTag(state.Chart, 2, startLinearOffset, notesPosition, "HXD");
                    break;
                case "FLK":
                    AddMarkerEntry(state.Chart, 1, startLinearOffset, notesPosition, 300 + notesWidth);
                    SetEntryTag(state.Chart, 1, startLinearOffset, notesPosition, GetOptionalPart(parts, 5));
                    break;
                case "AIR":
                    AddAirMarkerEntry(state.Chart, parts, startLinearOffset, notesPosition, 100 + notesWidth);
                    break;
                case "AUL":
                    AddAirMarkerEntry(state.Chart, parts, startLinearOffset, notesPosition, 300 + notesWidth);
                    break;
                case "AUR":
                    AddAirMarkerEntry(state.Chart, parts, startLinearOffset, notesPosition, 400 + notesWidth);
                    break;
                case "ADW":
                    AddAirMarkerEntry(state.Chart, parts, startLinearOffset, notesPosition, 200 + notesWidth);
                    break;
                case "ADL":
                    AddAirMarkerEntry(state.Chart, parts, startLinearOffset, notesPosition, 500 + notesWidth);
                    break;
                case "ADR":
                    AddAirMarkerEntry(state.Chart, parts, startLinearOffset, notesPosition, 600 + notesWidth);
                    break;
                case "SLC":
                    AddSlideMarkerPair(parts, state, startLinearOffset, notesPosition, notesWidth, 400);
                    break;
                case "SLD":
                    AddSlideMarkerPair(parts, state, startLinearOffset, notesPosition, notesWidth, 200);
                    break;
                case "SXD":
                    // Hard slide: same lane encoding as SLD but keep source type.
                    AddSlideMarkerPair(parts, state, startLinearOffset, notesPosition, notesWidth, 200);
                    SetEntryTag(state.Chart, 3, startLinearOffset, notesPosition, "SXD");
                    break;
                case "SXC":
                    AddSlideMarkerPair(parts, state, startLinearOffset, notesPosition, notesWidth, 400);
                    SetEntryTag(state.Chart, 3, startLinearOffset, notesPosition, "SXC");
                    break;
                case "ASD":
                case "ASC":
                    // Air slide (v8): S in UGC. Parsed as Player 6 start/end pairs
                    // with height/color metadata on the start entry.
                    AddAirSlideMarkerPair(parts, state, startLinearOffset, notesPosition, notesWidth);
                    break;
                case "ALD":
                    // Air crush (v8): C in UGC. Parsed as Player 7 start/end pairs
                    // with crush interval, height, and color on the start entry.
                    AddAirCrushMarkerPair(parts, state, startLinearOffset, notesPosition, notesWidth);
                    break;
                case "AHD":
                case "AHX":
                    // AHX is the hard variant of AIR-HOLD; the UGC converter
                    // renders both as AIR-HOLD (H), keeping the same C2S layout.
                    AddAirHoldMarkerPair(parts, state, startLinearOffset, notesPosition, notesWidth);
                    break;
                case "MNE":
                    AddMarkerEntry(state.Chart, 1, startLinearOffset, notesPosition, 400 + notesWidth);
                    break;
                case "SFL":
                case "SLP":
                    // SFL:  M O Duration Speed          (speed change, restores to 1.0)
                    // SLP:  M O Interval Speed [0]      (speed change, restores after interval)
                    // Both have the same layout: the speed value is in column 4 and the
                    // duration/interval in column 3. AddSpeedChangeTags emits a speed
                    // change at the start position and a 1.0 restore at the end.
                    AddSpeedChangeTags(parts, state.Chart, state.CurrentMeasure, state.Resolution, measurePosition, notesPosition);
                    break;
                case "DCM":
                    // DCM: M O Duration Speed          (note speed change / overtake)
                    // The speed value is in column 4 and the duration is in column 3.
                    // AddDcmTags emits a note-speed change at the start position and
                    // a 1.0 restore at the end.
                    AddDcmTags(parts, state.Chart, state.CurrentMeasure, state.Resolution, measurePosition, notesPosition);
                    break;
                case "STP":
                    AddStopEvent(parts, state.Chart, startLinearOffset);
                    break;
                case "SLA":
                    // Soflan attribute region: assigns a timeline id to the
                    // covered lane/time range. Parsed here and later applied by
                    // the UGC converter as a @USETIL switch on matching notes.
                    AddSlaRegion(parts, state, notesPosition, notesWidth);
                    break;
                case "ASO":
                    // Air Solid: a newer CHUNITHM mechanic with no UGC v8
                    // (Margrete) note equivalent. Recognised and skipped,
                    // matching PenguinTools' UgcChartConverter.
                    break;
                case "HHD":
                case "HHX":
                    // Heaven Hold / Ex Heaven Hold: newer CHUNITHM mechanics
                    // with no UGC v8 note equivalent. Recognised and skipped.
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
        /// Adds a single AIR marker entry, keeping the companion note type
        /// (HLD / SLD / SLC / AHD / ...) from the source line in the
        /// Parameter field so the UGC converter can group the AIR with the
        /// matching ground-note unit.
        /// </summary>
        private static void AddAirMarkerEntry(ChartChuni chart, string[] parts, int linearOffset, int column, int value)
        {
            EntryChuni entry = CreateBaseEntry(EntryTypeChuni.Marker, 5, linearOffset, column, value);
            if (parts.Length > 5)
                entry.Parameter = MapCompanionCode(parts[5]);
            // AIR color (DEF / GRN / PPL) lives in column 6 when present.
            if (parts.Length > 6)
                entry.Tag = parts[6];
            chart.Entries.Add(entry);
        }

        /// <summary>
        /// Maps an AIR companion note type to the ground player number.
        /// </summary>
        private static int MapCompanionCode(string companion)
        {
            switch (companion)
            {
                case "HLD":
                case "HXD": return 2;
                case "SLD":
                case "SLC":
                case "SXD":
                case "SXC": return 3;
                case "AHD":
                case "AHX": return 4;
                default: return 0;
            }
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
        /// Sets the Tag field on the most recently added start marker for a
        /// (player, linear offset, column) match.
        /// </summary>
        private static void SetEntryTag(ChartChuni chart, int player, int linearOffset, int column, string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;
            for (int i = chart.Entries.Count - 1; i >= 0; i--)
            {
                EntryChuni entry = chart.Entries[i];
                if (entry.Type == EntryTypeChuni.Marker && entry.Player == player &&
                    (int)((double)entry.LinearOffset) == linearOffset && entry.Column == column)
                {
                    entry.Tag = tag;
                    return;
                }
            }
        }

        /// <summary>
        /// Returns an optional tab-separated column value, or an empty string.
        /// </summary>
        private static string GetOptionalPart(string[] parts, int index)
        {
            if (parts.Length > index)
                return parts[index];
            return "";
        }

        /// <summary>
        /// Adds a linked start/end marker pair and merges it with pending endpoints when needed.
        /// Returns the start entry (either the newly created one or the entry the
        /// segment was merged into) so callers can attach metadata to the exact
        /// start entry for this segment.
        /// </summary>
        private static EntryChuni AddLinkedMarkerPair(ChartChuni chart, Dictionary<Point, List<int>> pendingEntries, ref resetPoint reset, int player, int startLinearOffset, int endLinearOffset, int startColumn, int startWidth, int endColumn, int endWidth, int endValueBase)
        {
            int currentIdentifier = AllocateIdentifier(ref reset, startLinearOffset, endLinearOffset);
            Point startPoint = new Point() { linearOffset = startLinearOffset, position = startColumn };

            EntryChuni startEntry;
            if (pendingEntries.ContainsKey(startPoint))
            {
                startEntry = MergeWithPendingStart(chart, pendingEntries, startPoint);
                currentIdentifier = startEntry.Identifier;
                reset.currentIdentifier--;
            }
            else
            {
                startEntry = CreateBaseEntry(EntryTypeChuni.Marker, player, startLinearOffset, startColumn, 100 + startWidth);
                startEntry.Identifier = currentIdentifier;
                chart.Entries.Add(startEntry);
            }

            EntryChuni endEntry = CreateBaseEntry(EntryTypeChuni.Marker, player, endLinearOffset, endColumn, endValueBase + endWidth);
            endEntry.Identifier = currentIdentifier;
            chart.Entries.Add(endEntry);
            RegisterPendingEnd(pendingEntries, endLinearOffset, endColumn, chart.Entries.Count - 1);

            return startEntry;
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
        /// Returns the merged entry.
        /// </summary>
        private static EntryChuni MergeWithPendingStart(ChartChuni chart, Dictionary<Point, List<int>> pendingEntries, Point point)
        {
            List<int> list = pendingEntries[point];
            int key = list[0];
            list.Remove(key);

            EntryChuni entry = chart.Entries[key];
            entry.Value = new Fraction(entry.Value.Numerator + 100, 1);
            chart.Entries[key] = entry;

            if (list.Count <= 0)
                pendingEntries.Remove(point);
            else
                pendingEntries[point] = list;

            return entry;
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
        /// Adds an air-hold marker pair. The companion note (TAP / CHR / HLD /
        /// SLD / AHD / AIR) already exists as an independent row in the C2S
        /// data, so the UGC converter renders the AIR-HOLD (H) as a
        /// self-contained note without emitting an extra lane note.
        /// </summary>
        private static void AddAirHoldMarkerPair(string[] parts, ReadState state, int startLinearOffset, int notesPosition, int notesWidth)
        {
            // C2S AHD layout: AHD M O Cell Width TargetNote Duration [Color]
            // The companion (TargetNote, col 5) and duration (col 6) are always
            // present (7 columns total, index 0..6); an optional color lives in
            // column 7 (index 7).
            int endLinearOffset = startLinearOffset + int.Parse(parts[6]);
            EntryChuni startEntry = AddLinkedMarkerPair(state.Chart, state.AirHoldPending, ref state.AirHoldResetPoint, 4, startLinearOffset, endLinearOffset, notesPosition, notesWidth, notesPosition, notesWidth, 200);

            // Record the companion note type (TAP / CHR / HLD / SLD / SLC /
            // AHD / AIR) as the Parameter so the UGC converter can place the
            // AIR-HOLD right after its connected ground unit. The AHD color
            // (DEF / GRN / PPL) lives in column 7 and is stored in Tag.
            string ahCompanion = parts.Length >= 7 ? parts[5] : "";
            int ahCompanionCode = MapCompanionCode(ahCompanion);
            string ahColor = parts.Length > 7 ? parts[7] : "";
            if (ahCompanionCode != 0)
                startEntry.Parameter = ahCompanionCode;
            if (!string.IsNullOrEmpty(ahColor))
                startEntry.Tag = ahColor;
        }

        /// <summary>
        /// Adds an air-slide (ASD / ASC) marker pair. C2S layout:
        ///   M O Cell Width | TargetNote | Height | Duration | EndCell | EndWidth | EndHeight | ? | Tag
        /// The start entry carries height / end height / color / target note
        /// metadata; end entries carry the chain endpoint.
        /// </summary>
        private static void AddAirSlideMarkerPair(string[] parts, ReadState state, int startLinearOffset, int notesPosition, int notesWidth)
        {
            string noteType = parts[0];
            int endLinearOffset = startLinearOffset + int.Parse(parts[7]);
            int endColumn = int.Parse(parts[8]);
            int endWidth = parts.Length > 9 ? int.Parse(parts[9]) : notesWidth;

            EntryChuni startEntry = AddLinkedMarkerPair(state.Chart, state.AirSlidePending, ref state.AirSlideResetPoint, 6, startLinearOffset, endLinearOffset, notesPosition, notesWidth, endColumn, endWidth, 200);

            // Metadata on the start entry: height (col 6), end height (col 10),
            // tag/color (col 11), target note (col 5).
            if (parts.Length > 6 && double.TryParse(parts[6], out double height))
                startEntry.Height = height;
            if (parts.Length > 10 && double.TryParse(parts[10], out double endHeight))
                startEntry.EndHeight = endHeight;
            if (parts.Length > 11)
                startEntry.Tag = parts[11];
            startEntry.TargetNote = GetOptionalPart(parts, 5);
            // ASC (Air Slide Control) is a control point that uses the
            // apex height (EndHeight, col10) for the UGC S line height
            // (e.g. ASC 128 192 1 1 SLD 1.0 24 0 1 19.0 -> parent line
            // height 19.0*15=285="7X").
            // ASD (Air Slide) uses the start height (Height, col6).
            // Player 6 (AirSlide) Parameter is unused for companion
            // matching, so it can be used as a flag.
            startEntry.Parameter = (noteType == "ASC") ? 1 : 0;
        }

        /// <summary>
        /// Adds an air-crush (ALD) marker pair. C2S layout:
        ///   M O Cell Width | CrushInterval | Height | Duration | EndCell | EndWidth | EndHeight | ? | Tag
        /// The start entry carries crush interval, height, end height and color.
        /// </summary>
        private static void AddAirCrushMarkerPair(string[] parts, ReadState state, int startLinearOffset, int notesPosition, int notesWidth)
        {
            int endLinearOffset = startLinearOffset + int.Parse(parts[7]);
            int endColumn = int.Parse(parts[8]);
            int endWidth = parts.Length > 9 ? int.Parse(parts[9]) : notesWidth;

            EntryChuni startEntry = AddLinkedMarkerPair(state.Chart, state.AirCrushPending, ref state.AirCrushResetPoint, 7, startLinearOffset, endLinearOffset, notesPosition, notesWidth, endColumn, endWidth, 200);

            // Metadata on the start entry: crush interval (col 5), height (col 6),
            // end height (col 10), tag/color (col 11).
            if (parts.Length > 5 && int.TryParse(parts[5], out int crushInterval))
                startEntry.CrushInterval = crushInterval;
            if (parts.Length > 6 && double.TryParse(parts[6], out double height))
                startEntry.Height = height;
            if (parts.Length > 10 && double.TryParse(parts[10], out double endHeight))
                startEntry.EndHeight = endHeight;
            if (parts.Length > 11)
                startEntry.Tag = parts[11];
        }

        /// <summary>
        /// Appends HISPEED/TIL tags for a CHUNITHM speed-change event.
        /// </summary>
        private static void AddSpeedChangeTags(string[] parts, ChartChuni chart, int currentMeasure, int resolution, int measurePosition, int duration)
        {
            // SLP carries an explicit timeline id in the last column; SFL uses
            // the default timeline 0. The speed change and its 1.0 restore are
            // both emitted on that timeline so UGC can @USETIL into it.
            int timelineId = parts[0] == "SLP" && parts.Length > 5 ? int.Parse(parts[5]) : 0;
            string key = "TIL" + timelineId.ToString();

            string til = chart.Tags.ContainsKey(key) ? chart.Tags[key] + ", " : "";

            chart.Tags[key] = til + FormatTilingPoint(currentMeasure, measurePosition, resolution) + ":" + double.Parse(parts[4]) + ", ";

            int nextMeasure = (int)Math.Floor(((double)currentMeasure * resolution + measurePosition + duration) / resolution);
            int nextPosition = currentMeasure * resolution + measurePosition + duration - nextMeasure * resolution;
            chart.Tags[key] += FormatTilingPoint(nextMeasure, nextPosition, resolution) + ":1.0";
            chart.Tags["HISPEED"] = "00";
        }

        /// <summary>
        /// Appends SPDMOD tags for a CHUNITHM DCM (note speed / overtake) event.
        /// Each DCM emits two points (the speed change and a 1.0 restore at its
        /// end), mirroring AddSpeedChangeTags. The converter later collapses
        /// duplicate bar'tick positions so a contiguous DCM's speed wins over
        /// the preceding restore.
        /// </summary>
        private static void AddDcmTags(string[] parts, ChartChuni chart, int currentMeasure, int resolution, int measurePosition, int duration)
        {
            string spdmod = "";
            if (chart.Tags.ContainsKey("SPDMOD"))
                spdmod = chart.Tags["SPDMOD"] + ", ";

            chart.Tags["SPDMOD"] = spdmod + FormatTilingPoint(currentMeasure, measurePosition, resolution) + ":" + double.Parse(parts[4]) + ", ";

            int nextMeasure = (int)Math.Floor(((double)currentMeasure * resolution + measurePosition + duration) / resolution);
            int nextPosition = currentMeasure * resolution + measurePosition + duration - nextMeasure * resolution;
            chart.Tags["SPDMOD"] += FormatTilingPoint(nextMeasure, nextPosition, resolution) + ":1.0";
        }

        /// <summary>
        /// Adds a stop (STP) event as a start/end pair.
        /// </summary>
        private static void AddStopEvent(string[] parts, ChartChuni chart, int startLinearOffset)
        {
            int endLinearOffset = startLinearOffset + int.Parse(parts[3]);

            EntryChuni entry = new EntryChuni();
            entry.LinearOffset = new Fraction(startLinearOffset, 1);
            entry.Type = EntryTypeChuni.Event;
            entry.Player = 1;
            entry.Value = new Fraction(0, 0);
            chart.Entries.Add(entry);

            EntryChuni freezeEntry = new EntryChuni();
            freezeEntry.LinearOffset = new Fraction(endLinearOffset, 1);
            freezeEntry.Type = EntryTypeChuni.Event;
            freezeEntry.Player = 1;
            freezeEntry.Value = new Fraction(1, 1);
            chart.Entries.Add(freezeEntry);
        }

        /// <summary>
        /// Parses a SLA (soflan attribute region) line. The region assigns a
        /// timeline id to the covered lane/time span so the UGC converter can
        /// switch matching notes onto that timeline (@USETIL).
        /// </summary>
        private static void AddSlaRegion(string[] parts, ReadState state, int notesPosition, int notesWidth)
        {
            // SLA M O Cell Width Duration Timeline
            int duration = parts.Length > 5 ? int.Parse(parts[5]) : 0;
            int timelineId = parts.Length > 6 ? int.Parse(parts[6]) : 0;
            state.SlaRegions.Add(new SlaRegion
            {
                Measure = state.CurrentMeasure,
                Offset = int.Parse(parts[2]),
                Column = notesPosition,
                Width = Math.Max(1, notesWidth),
                Duration = duration,
                TimelineId = timelineId
            });
        }

        /// <summary>
        /// Serializes parsed SLA regions into chart.Tags["SLA"] as
        /// "measure,offset,column,width,duration,timelineId;..." for the UGC
        /// converter.
        /// </summary>
        private static void SerializeSlaRegions(ReadState state)
        {
            if (state.SlaRegions.Count == 0)
                return;

            List<string> tokens = new List<string>();
            foreach (SlaRegion region in state.SlaRegions)
            {
                tokens.Add(
                    region.Measure + "," + region.Offset + "," + region.Column + "," +
                    region.Width + "," + region.Duration + "," + region.TimelineId);
            }
            state.Chart.Tags["SLA"] = string.Join(";", tokens);
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
        /// Adds measure entries. Measures are numbered by their chart measure
        /// (0,1,2,...), so the metric-offset pass can build measure boundaries.
        /// </summary>
        private static void AddMeasureEntries(ChartChuni chart, int currentMeasure, int resolution)
        {
            for (int m = 0; m <= currentMeasure + 10; m++)
                EmitMeasure(chart, m * resolution);
        }

        /// <summary>
        /// Adds a single measure entry at the given linear offset.
        /// </summary>
        private static void EmitMeasure(ChartChuni chart, int linearOffset)
        {
            EntryChuni entry = new EntryChuni();
            entry.LinearOffset = new Fraction(linearOffset, 1);
            entry.Type = EntryTypeChuni.Measure;
            entry.Player = 1;
            entry.Value = new Fraction(0, 1);
            chart.Entries.Add(entry);
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