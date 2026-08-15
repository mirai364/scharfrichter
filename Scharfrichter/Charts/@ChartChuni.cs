using System;
using System.Collections.Generic;

namespace Scharfrichter.Codec.Charts
{
    public class ChartChuni
    {
        private List<EntryChuni> entries = new List<EntryChuni>();
        private Dictionary<int, Fraction> lengths = new Dictionary<int, Fraction>();
        private Dictionary<string, string> tags = new Dictionary<string, string>();

        public Fraction DefaultBPM = new Fraction(0, 1);
        public Fraction TickRate = new Fraction(0, 1);

        /// <summary>
        /// Converts linear offsets into metric offsets for the entry list.
        /// </summary>
        public void CalculateMetricOffsets()
        {
            ValidateLinearOffsets();
            ClearMetricOffsets();
            entries.Sort();
            lengths.Clear();

            Dictionary<int, Fraction> lengthList = BuildLinearMeasureLengthList();
            ApplyMetricOffsets(lengthList);
        }

        /// <summary>
        /// Ensures every entry has linear timing before metric offsets are calculated.
        /// </summary>
        private void ValidateLinearOffsets()
        {
            foreach (EntryChuni entry in entries)
            {
                if (!entry.LinearOffsetInitialized)
                    throw new Exception("Metric offsets can't be calculated because at least one entry is missing Linear offset information.");
            }
        }

        /// <summary>
        /// Builds measured linear lengths for measures that do not contain tempo changes.
        /// </summary>
        private Dictionary<int, Fraction> BuildLinearMeasureLengthList()
        {
            Dictionary<int, Fraction> lengthList = new Dictionary<int, Fraction>();
            Fraction lastMeasureOffset = new Fraction(0, 1);
            int measure = 0;
            bool tempoChanged = false;

            foreach (EntryChuni entry in entries)
            {
                if (entry.Type == EntryTypeChuni.Measure || entry.Type == EntryTypeChuni.EndOfSong)
                {
                    if (entry.LinearOffset != lastMeasureOffset)
                    {
                        if (!tempoChanged)
                            AddLinearMeasureLength(lengthList, measure, entry.LinearOffset - lastMeasureOffset);
                        lastMeasureOffset = entry.LinearOffset;
                        measure++;
                        tempoChanged = false;
                    }
                }
                else if (entry.Type == EntryTypeChuni.Tempo && (entry.LinearOffset - lastMeasureOffset).Numerator != 0)
                {
                    tempoChanged = true;
                }
            }

            return lengthList;
        }

        /// <summary>
        /// Adds one linear measure length after validating that it moves forward in time.
        /// </summary>
        private static void AddLinearMeasureLength(Dictionary<int, Fraction> lengthList, int measure, Fraction distance)
        {
            if ((double)distance < 0)
                throw new Exception("INTERNAL ERROR DAMMIT.");
            lengthList.Add(measure, distance);
        }

        /// <summary>
        /// Applies metric offsets to every entry using the calculated measure lengths.
        /// </summary>
        private void ApplyMetricOffsets(Dictionary<int, Fraction> lengthList)
        {
            Fraction bpm = DefaultBPM;
            Fraction lastMeasureOffset = new Fraction(0, 1);
            Fraction lastTempoOffset = new Fraction(0, 1);
            int measure = 0;
            Fraction measureLength = new Fraction(0, 1);
            Fraction rate = Util.CalculateMeasureRate(bpm);
            Fraction tickMeasureLength = GetMeasureTickLength(lengthList, 0);
            List<EntryChuni> entryList = new List<EntryChuni>();
            List<EntryChuni> measureEntryList = new List<EntryChuni>();

            foreach (EntryChuni entry in entries)
            {
                if (IsMetricRateBoundary(entry))
                {
                    Fraction measureDistance = ((entry.LinearOffset - lastTempoOffset) * TickRate) / rate;
                    ApplyPendingTempoEntries(entryList, measureEntryList, measure, measureLength, measureDistance, lastTempoOffset, entry.LinearOffset);
                    measureLength += measureDistance;

                    if (entry.Type == EntryTypeChuni.Measure || entry.Type == EntryTypeChuni.EndOfSong)
                        ApplyMeasureBoundary(entry, measureEntryList, ref measure, ref measureLength, ref lastMeasureOffset);
                    else if (entry.Type == EntryTypeChuni.Tempo)
                        rate = Util.CalculateMeasureRate(entry.Value);

                    lastTempoOffset = entry.LinearOffset;
                    tickMeasureLength = GetMeasureTickLength(lengthList, measure);
                    entryList.Clear();
                }

                ApplyEntryMetricOffset(entry, entryList, tickMeasureLength, lastTempoOffset, measure);
            }
        }

        /// <summary>
        /// Determines whether an entry changes metric timing state.
        /// </summary>
        private static bool IsMetricRateBoundary(EntryChuni entry)
        {
            return entry.Type == EntryTypeChuni.Measure || entry.Type == EntryTypeChuni.Tempo || entry.Type == EntryTypeChuni.EndOfSong;
        }

        /// <summary>
        /// Applies provisional offsets to entries collected inside a tempo-changing measure.
        /// </summary>
        private static void ApplyPendingTempoEntries(List<EntryChuni> entryList, List<EntryChuni> measureEntryList, int measure, Fraction measureLength, Fraction measureDistance, Fraction lastTempoOffset, Fraction entryLinearOffset)
        {
            foreach (EntryChuni tempoEntry in entryList)
            {
                tempoEntry.MetricOffset = Fraction.Shrink(measureLength + (((tempoEntry.LinearOffset - lastTempoOffset) / (entryLinearOffset - lastTempoOffset)) * measureDistance));
                tempoEntry.MetricMeasure = measure;
                measureEntryList.Add(tempoEntry);
            }
        }

        /// <summary>
        /// Finalizes entries and length metadata at a measure or end-of-song boundary.
        /// </summary>
        private void ApplyMeasureBoundary(EntryChuni entry, List<EntryChuni> measureEntryList, ref int measure, ref Fraction measureLength, ref Fraction lastMeasureOffset)
        {
            if (entry.LinearOffset != lastMeasureOffset)
            {
                NormalizeTempoMeasureEntries(measureEntryList, measureLength);
                MeasureLengths[measure] = measureLength;
                measure++;
                lastMeasureOffset = Fraction.Shrink(entry.LinearOffset);
                measureLength = new Fraction(0, 1);
            }
            entry.MetricOffset = new Fraction(0, 1);
            entry.MetricMeasure = measure;
        }

        /// <summary>
        /// Scales entries collected during a tempo-changing measure into measure-relative offsets.
        /// </summary>
        private static void NormalizeTempoMeasureEntries(List<EntryChuni> measureEntryList, Fraction measureLength)
        {
            foreach (EntryChuni measureEntry in measureEntryList)
            {
                Fraction temp = measureEntry.MetricOffset;
                temp /= measureLength;
                measureEntry.MetricOffset = Fraction.Shrink(temp);
                NormalizeMetricOverflow(measureEntry);
            }
            measureEntryList.Clear();
        }

        /// <summary>
        /// Applies a direct metric offset or delays the entry until a tempo boundary is known.
        /// </summary>
        private static void ApplyEntryMetricOffset(EntryChuni entry, List<EntryChuni> pendingEntries, Fraction tickMeasureLength, Fraction lastTempoOffset, int measure)
        {
            if (tickMeasureLength.Numerator > 0)
            {
                entry.MetricOffset = Fraction.Shrink((entry.LinearOffset - lastTempoOffset) / tickMeasureLength);
                entry.MetricMeasure = measure;
                NormalizeMetricOverflow(entry);
            }
            else
            {
                pendingEntries.Add(entry);
            }
        }

        /// <summary>
        /// Moves offsets greater than or equal to one into following measures.
        /// </summary>
        private static void NormalizeMetricOverflow(EntryChuni entry)
        {
            while ((double)entry.MetricOffset >= 1)
            {
                Fraction offs = entry.MetricOffset;
                entry.MetricMeasure++;
                offs.Numerator -= offs.Denominator;
                entry.MetricOffset = Fraction.Shrink(offs);
            }
        }

        /// <summary>
        /// Gets a measured tick length or zero when the measure needs tempo-boundary calculation.
        /// </summary>
        private static Fraction GetMeasureTickLength(Dictionary<int, Fraction> lengthList, int measure)
        {
            if (lengthList.ContainsKey(measure))
                return lengthList[measure];
            return new Fraction(0, 1);
        }

        public void ClearLinearOffsets()
        {
            foreach (var entry in entries)
            {
                entry.LinearOffset = new Fraction(0, 1);
                entry.LinearOffsetInitialized = false;
            }
        }

        public void ClearMetricOffsets()
        {
            foreach (var entry in entries)
            {
                entry.MetricOffset = new Fraction(0, 1);
                entry.MetricMeasure = 0;
                entry.MetricOffsetInitialized = false;
            }
        }

        // Entries property
        public List<EntryChuni> Entries
        {
            get
            {
                return entries;
            }
        }

        // MeasureLengths property
        public Dictionary<int, Fraction> MeasureLengths
        {
            get
            {
                return lengths;
            }
        }

        // Tags property
        public Dictionary<string, string> Tags
        {
            get
            {
                return tags;
            }
        }
    }

    public class EntryChuni : IComparable<EntryChuni>
    {
        private Fraction linearOffset;
        private Fraction metricOffset;
        private Fraction value;

        public bool LinearOffsetInitialized;
        public int MetricMeasure;
        public bool MetricOffsetInitialized;
        public bool ValueInitialized;

        public int Column;
        public int Identifier = 0;
        public int Parameter;
        public int Player;
        public EntryTypeChuni Type;

        // CHUNITHM extended fields (used by ChuniToUgc):
        //   Tag          - CHR/FLK direction, or AIR/AHD/AHX color (C2S string kept as-is)
        //   Height       - ASD/ALD start height (C2S raw value)
        //   EndHeight    - ASD/ALD end height (C2S raw value)
        //   CrushInterval- ALD crush interval (in 384 ticks per measure)
        //   TargetNote   - C2S targetNote column value (ASD/ASC chain reference)
        public string Tag = "";
        public double Height;
        public double EndHeight;
        public int CrushInterval;
        public string TargetNote = "";

        /// <summary>
        /// Compares entries by timing, lane metadata, and special event ordering.
        /// </summary>
        public int CompareTo(EntryChuni other)
        {
            int timingOrder = CompareTiming(other);
            if (timingOrder != 0)
                return timingOrder;

            int laneOrder = CompareLaneMetadata(other);
            if (laneOrder != 0)
                return laneOrder;

            return CompareEventPriority(other);
        }

        /// <summary>
        /// Compares entries by metric timing when available, otherwise by linear timing.
        /// </summary>
        private int CompareTiming(EntryChuni other)
        {
            if (other.MetricOffsetInitialized && MetricOffsetInitialized)
            {
                if (other.MetricMeasure > MetricMeasure)
                    return -1;
                if (other.MetricMeasure < MetricMeasure)
                    return 1;
                return CompareFractions(metricOffset, other.metricOffset);
            }

            if (other.LinearOffsetInitialized && LinearOffsetInitialized)
                return CompareFractions(linearOffset, other.linearOffset);

            return 0;
        }

        /// <summary>
        /// Compares player, column, and parameter fields after timing is equal.
        /// </summary>
        private int CompareLaneMetadata(EntryChuni other)
        {
            int order = CompareDescending(Player, other.Player);
            if (order != 0)
                return order;
            order = CompareDescending(Column, other.Column);
            if (order != 0)
                return order;
            return CompareDescending(Parameter, other.Parameter);
        }

        /// <summary>
        /// Compares event classes that must appear before or after ordinary notes.
        /// </summary>
        private int CompareEventPriority(EntryChuni other)
        {
            int order = ComparePreferredFirst(Type, other.Type, EntryTypeChuni.Measure);
            if (order != 0)
                return order;
            order = ComparePreferredFirst(Type, other.Type, EntryTypeChuni.Tempo);
            if (order != 0)
                return order;
            return ComparePreferredLast(Type, other.Type, EntryTypeChuni.EndOfSong);
        }

        /// <summary>
        /// Compares two fractions using their floating-point values.
        /// </summary>
        private static int CompareFractions(Fraction current, Fraction other)
        {
            double currentFloat = (double)current;
            double otherFloat = (double)other;
            if (otherFloat > currentFloat)
                return -1;
            if (otherFloat < currentFloat)
                return 1;
            return 0;
        }

        /// <summary>
        /// Compares integer fields using the existing descending sort convention.
        /// </summary>
        private static int CompareDescending(int current, int other)
        {
            if (other > current)
                return -1;
            if (other < current)
                return 1;
            return 0;
        }

        /// <summary>
        /// Gives the preferred type an earlier sort position.
        /// </summary>
        private static int ComparePreferredFirst(EntryTypeChuni current, EntryTypeChuni other, EntryTypeChuni preferred)
        {
            if (current == preferred && other != preferred)
                return -1;
            if (current != preferred && other == preferred)
                return 1;
            return 0;
        }

        /// <summary>
        /// Gives the preferred type a later sort position.
        /// </summary>
        private static int ComparePreferredLast(EntryTypeChuni current, EntryTypeChuni other, EntryTypeChuni preferred)
        {
            if (current == preferred && other != preferred)
                return 1;
            if (current != preferred && other == preferred)
                return -1;
            return 0;
        }

        public override string ToString()
        {
            // for debug purposes only
            return ("[M" + MetricMeasure.ToString() + ":" + metricOffset.ToString() + ", L" + linearOffset.ToString() + "] " + Type.ToString() + ": P" + Player.ToString() + ", C" + Column.ToString());
        }

        public Fraction LinearOffset
        {
            get
            {
                return linearOffset;
            }
            set
            {
                linearOffset = value;
                LinearOffsetInitialized = true;
                if (linearOffset.Denominator == 0)
                    LinearOffsetInitialized = false;
            }
        }

        public Fraction MetricOffset
        {
            get
            {
                return metricOffset;
            }
            set
            {
                metricOffset = value;
                MetricOffsetInitialized = true;
                if (metricOffset.Denominator == 0)
                    MetricOffsetInitialized = false;
            }
        }

        public Fraction Value
        {
            get
            {
                return this.value;
            }
            set
            {
                this.value = value;
                ValueInitialized = true;
                if (this.value.Denominator == 0)
                    ValueInitialized = false;
            }
        }
    }

    public enum EntryTypeChuni
    {
        Invalid,
        Marker,
        Tempo,
        Measure,
        Event,
        EndOfSong
    }
}