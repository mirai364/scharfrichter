using System;
using System.Collections.Generic;

namespace Scharfrichter.Codec.Charts
{
    public class Chart
    {
        private List<Entry> entries = new List<Entry>();
        private Dictionary<int, Fraction> lengths = new Dictionary<int, Fraction>();
        private Dictionary<string, string> tags = new Dictionary<string, string>();

        public Fraction DefaultBPM = new Fraction(0, 1);
        public Fraction TickRate = new Fraction(0, 1);

        public int quantizeNotes { get; set; }
        public bool isSameFolderMovie { get; set; }
        public bool useMovie { get; set; }
        public string movieFolder { get; set; }


        // Add judgement entries to the list. It's unsure if these are needed, but
        // it can be used if there are compatibility issues with converted arcade data.
        // IIDX: F0, FA, FF, 03, 08, 12
        // 5key: F4, FC, FF, 03, 06, 0E
        public void AddJudgements()
        {
            int[] judgementValues = new int[] { 0xF0, 0xFA, 0xFF, 0x03, 0x08, 0x12 };
            int judgementCount = judgementValues.Length;
            int playerCount = Players;

            for (int j = 0; j < playerCount; j++)
            {
                for (int i = 0; i < judgementCount; i++)
                {
                    Entry entry = new Entry();
                    entry.Column = 0;
                    entry.LinearOffset = new Fraction(0, 1);
                    entry.MetricMeasure = 0;
                    entry.MetricOffset = new Fraction(0, 1);
                    entry.Parameter = i;
                    entry.Player = j + 1;
                    entry.Type = EntryType.Judgement;
                    entry.Value = new Fraction(judgementValues[i], 1);
                    entries.Add(entry);
                }
            }
        }

        // Add measure line entries to the list. Can only be used when
        // metric data is present.
        public void AddMeasureLines()
        {
            int measureCount = -1;
            int playerCount = Players;

            // verify all required metric info is present
            foreach (Entry entry in entries)
                if (!entry.MetricOffsetInitialized)
                    throw new Exception("Measure lines can't be added because at least one entry is missing Metric offset information.");

            // clear up existing ones
            RemoveMeasureLines();

            // find the highest measure index
            foreach (Entry entry in entries)
            {
                if (entry.MetricMeasure >= measureCount)
                    measureCount = entry.MetricMeasure + 1;
            }

            // add measure lines for each measure
            for (int i = 0; i < measureCount; i++)
            {
                for (int j = 0; j < playerCount; j++)
                {
                    Entry entry = new Entry();
                    entry.Column = 0;
                    entry.MetricMeasure = i;
                    entry.MetricOffset = new Fraction(0, 1);
                    entry.Player = j + 1;
                    entry.Type = EntryType.Measure;
                    entry.Value = new Fraction(0, 1);
                    entries.Add(entry);
                }
            }

            // add end of song marker
            if (measureCount >= 0)
            {
                for (int j = 0; j < playerCount; j++)
                {
                    Entry entry = new Entry();
                    entry.Column = 0;
                    entry.MetricMeasure = measureCount;
                    entry.MetricOffset = new Fraction(0, 1);
                    entry.Player = j + 1;
                    entry.Type = EntryType.EndOfSong;
                    entry.Value = new Fraction(0, 1);
                    entries.Add(entry);
                }
            }
        }

        // Convert Metric offsets to Linear offsets for the entry list.
        public void CalculateLinearOffsets()
        {
            // verify all required metric info is present
            foreach (Entry entry in entries)
                if (!entry.MetricOffsetInitialized)
                    throw new Exception("Linear offsets can't be calculated because at least one entry is missing Metric offset information.");

            // delete all linear offset data
            ClearLinearOffsets();

            // make sure everything is sorted before we begin
            entries.Sort();

            // initialization
            Fraction baseLinear = new Fraction(0, 1);
            Fraction bpm = DefaultBPM;
            Fraction lastMetric = new Fraction(0, 1);
            Fraction length = new Fraction(0, 1);
            int measure = -1;
            Fraction measureRate = new Fraction(0, 1);
            Fraction rate = new Fraction(0, 1);

            // BPM into seconds per measure
            measureRate = Util.CalculateMeasureRate(bpm);

            foreach (Entry entry in entries)
            {
                // on measures, update rate information
                if (entry.Type == EntryType.Measure)
                {
                    baseLinear += rate;
                    measure = entry.MetricMeasure;

                    if (lengths.ContainsKey(measure))
                    {
                        length = lengths[measure];
                        rate = length * measureRate;
                    }
                    else
                    {
                        length = new Fraction(1, 1);
                        rate = measureRate;
                    }
                    lastMetric = new Fraction(0, 1);
                }

                // calculate linear offset
                Fraction entryOffset = entry.MetricOffset;
                entryOffset -= lastMetric;
                entryOffset *= rate;
                entryOffset += baseLinear;
                entry.LinearOffset = entryOffset;

                // on tempo change, update rate information
                if (entry.Type == EntryType.Tempo)
                {
                    measureRate = Util.CalculateMeasureRate(bpm);
                    rate = length * measureRate;
                    lastMetric = entry.MetricOffset;
                }
            }
        }

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
            foreach (Entry entry in entries)
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

            foreach (Entry entry in entries)
            {
                if (entry.Type == EntryType.Measure || entry.Type == EntryType.EndOfSong)
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
                else if (entry.Type == EntryType.Tempo && (entry.LinearOffset - lastMeasureOffset).Numerator != 0)
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
            List<Entry> entryList = new List<Entry>();
            List<Entry> measureEntryList = new List<Entry>();

            foreach (Entry entry in entries)
            {
                if (IsMetricRateBoundary(entry))
                {
                    Fraction measureDistance = ((entry.LinearOffset - lastTempoOffset) * TickRate) / rate;
                    ApplyPendingTempoEntries(entryList, measureEntryList, measure, measureLength, measureDistance, lastTempoOffset, entry.LinearOffset);
                    measureLength += measureDistance;

                    if (entry.Type == EntryType.Measure || entry.Type == EntryType.EndOfSong)
                        ApplyMeasureBoundary(entry, measureEntryList, ref measure, ref measureLength, ref lastMeasureOffset);
                    else if (entry.Type == EntryType.Tempo)
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
        private static bool IsMetricRateBoundary(Entry entry)
        {
            return entry.Type == EntryType.Measure || entry.Type == EntryType.Tempo || entry.Type == EntryType.EndOfSong;
        }

        /// <summary>
        /// Applies provisional offsets to entries collected inside a tempo-changing measure.
        /// </summary>
        private static void ApplyPendingTempoEntries(List<Entry> entryList, List<Entry> measureEntryList, int measure, Fraction measureLength, Fraction measureDistance, Fraction lastTempoOffset, Fraction entryLinearOffset)
        {
            foreach (Entry tempoEntry in entryList)
            {
                tempoEntry.MetricOffset = Fraction.Shrink(measureLength + (((tempoEntry.LinearOffset - lastTempoOffset) / (entryLinearOffset - lastTempoOffset)) * measureDistance));
                tempoEntry.MetricMeasure = measure;
                measureEntryList.Add(tempoEntry);
            }
        }

        /// <summary>
        /// Finalizes entries and length metadata at a measure or end-of-song boundary.
        /// </summary>
        private void ApplyMeasureBoundary(Entry entry, List<Entry> measureEntryList, ref int measure, ref Fraction measureLength, ref Fraction lastMeasureOffset)
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
        private static void NormalizeTempoMeasureEntries(List<Entry> measureEntryList, Fraction measureLength)
        {
            foreach (Entry measureEntry in measureEntryList)
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
        private static void ApplyEntryMetricOffset(Entry entry, List<Entry> pendingEntries, Fraction tickMeasureLength, Fraction lastTempoOffset, int measure)
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
        private static void NormalizeMetricOverflow(Entry entry)
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
            foreach (Entry entry in entries)
            {
                entry.LinearOffset = new Fraction(0, 1);
                entry.LinearOffsetInitialized = false;
            }
        }

        public void ClearMetricOffsets()
        {
            foreach (Entry entry in entries)
            {
                entry.MetricOffset = new Fraction(0, 1);
                entry.MetricMeasure = 0;
                entry.MetricOffsetInitialized = false;
            }
        }

        // clear the Used flag on all entries
        public void ClearUsed()
        {
            foreach (Entry entry in entries)
                entry.Used = false;
        }

        // Entries property
        public List<Entry> Entries
        {
            get
            {
                return entries;
            }
            set
            {
                entries = value;
            }
        }

        // Measures property
        public int Measures
        {
            get
            {
                int measureCount = -1;

                // find the highest measure index
                foreach (Entry entry in entries)
                {
                    if (entry.MetricMeasure >= measureCount)
                        measureCount = entry.MetricMeasure + 1;
                }
                return measureCount + 1;
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

        // determine the number of notes a player must play
        public int NoteCount(int player)
        {
            int result = 0;

            foreach (Entry entry in entries)
                if (entry.Type == EntryType.Marker && entry.Player == player)
                    result++;

            return result;
        }

        // determine the number of non-bgm notes
        public int NoteTotal
        {
            get
            {
                int result = 0;

                foreach (Entry entry in entries)
                    if (entry.Type == EntryType.Marker && entry.Player > 0)
                        result++;

                return result;
            }
        }

        // determine the number of players
        public int Players
        {
            get
            {
                int result = 0;
                foreach (Entry entry in entries)
                {
                    if ((entry.Type == EntryType.Marker || entry.Type == EntryType.Sample) && (entry.Player > result))
                        result = entry.Player;
                }
                return result;
            }
        }

        // quantize measure lengths so that they are easier to decipher.
        // many people use BMSE, and BMSE doesn't like measure lengths that are not a multiple of 1/64,
        // so this is here to please the people that still use it. (I hate you guys.)
        public void QuantizeMeasureLengths(int quantizeValue)
        {
            // verify all required metric info is present
            foreach (Entry entry in entries)
                if (!entry.MetricOffsetInitialized)
                    throw new Exception("Measure lengths can't be quantized because at least one entry is missing Metric offset information.");

            double quantizationFloat = quantizeValue;
            int measureCount = Measures;
            Fraction lengthBefore = Fraction.Rationalize(TotalMeasureLength);

            // quantize the measure lengths
            for (int i = 0; i < measureCount; i++)
            {
                if (lengths.ContainsKey(i))
                {
                    if (lengths[i].Denominator != quantizeValue)
                        lengths[i] = new Fraction((long)(Math.Round((double)Fraction.Shrink(lengths[i]) * quantizationFloat)), quantizeValue);
                }
            }

#if (true)
            // since we adjusted measure lengths, we also need to adjust BPMs
            Fraction lengthAfter = Fraction.Rationalize(TotalMeasureLength);
            Fraction ratio = lengthAfter / lengthBefore;

            foreach (Entry entry in entries)
            {
                if (entry.Type == EntryType.Tempo)
                {
                    entry.Value *= ratio;
                }
            }

            DefaultBPM *= ratio;
#endif

#if (false) // disabled for now because it is a little buggy and we need to get a release out
            // regenerate linear offsets because the values could have changed
            CalculateLinearOffsets();
#endif
        }

        // quantize Metric note offsets. This is useful for reducing the size of a
        // converted BMS file.
        public void QuantizeNoteOffsets()
        {
            int quantizeValue = this.quantizeNotes;
            // verify all required metric info is present
            foreach (Entry entry in entries)
                if (!entry.MetricOffsetInitialized)
                    throw new Exception("Metric note offsets can't be quantized because at least one entry is missing Metric offset information.");

            int measure = 0;
            Fraction lastMeasure = new Fraction(0, 1);
            long quantize = 0;

            // quantize each event
            foreach (Entry entry in entries)
            {
                if (entry.Type == EntryType.Measure || quantize == 0)
                {
                    if (entry.Type == EntryType.Measure && entry.MetricOffset != lastMeasure)
                        measure++;

                    if (lengths.ContainsKey(measure))
                        quantize = quantizeValue * (long)(Math.Round((double)lengths[measure]));
                    else
                        quantize = quantizeValue;
                }

                if (quantize == 0)
                    quantize = 192;

                if (entry.Type == EntryType.Marker && entry.MetricOffset.Denominator > quantize)
                    entry.MetricOffset = Fraction.Quantize(entry.MetricOffset, quantize);
                else
                    entry.MetricOffset = Fraction.Reduce(entry.MetricOffset);
            }
        }

        // remove all judgement information from the chart.
        public void RemoveJudgements()
        {
            foreach (Entry entry in entries)
            {
                if (entry.Type == EntryType.Judgement)
                    entry.Type = EntryType.Invalid;
            }
        }

        // remove all measure lines from the chart.
        public void RemoveMeasureLines()
        {
            foreach (Entry entry in entries)
            {
                if (entry.Type == EntryType.Measure || entry.Type == EntryType.EndOfSong)
                    entry.Type = EntryType.Invalid;
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

        // get the sum of all measure lengths.
        public double TotalMeasureLength
        {
            get
            {
                double result = 0;
                int measureCount = Measures;
                for (int i = 0; i < measureCount; i++)
                {
                    if (lengths.ContainsKey(i))
                    {
                        result += (double)lengths[i];
                    }
                    else
                    {
                        result += 1;
                    }
                }
                return result;
            }
        }

        // get a list of samples used in the chart. It can be used as a sample map.
        public int[] UsedSamples()
        {
            List<int> result = new List<int>();
            foreach (Entry entry in entries)
            {
                if (entry.Type == EntryType.Marker || entry.Type == EntryType.Sample)
                {
                    int val = (int)((double)entry.Value);
                    if (val > 0)
                    {
                        if (!result.Contains(val))
                        {
                            result.Add(val);
                        }
                    }
                }
            }

            result.Sort();
            return result.ToArray();
        }
    }

    public class Entry : IComparable<Entry>
    {
        private Fraction linearOffset;
        private Fraction metricOffset;
        private Fraction value;

        public bool LinearOffsetInitialized;
        public int MetricMeasure;
        public bool MetricOffsetInitialized;
        public bool ValueInitialized;

        public int Column;
        public bool Freeze;
        public int Parameter;
        public int Player;
        public EntryType Type;
        public bool Used;
        public bool IsMss = false;

        /// <summary>
        /// Compares entries by timing, lane metadata, and special event ordering.
        /// </summary>
        public int CompareTo(Entry other)
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
        private int CompareTiming(Entry other)
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
        private int CompareLaneMetadata(Entry other)
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
        private int CompareEventPriority(Entry other)
        {
            int order = ComparePreferredFirst(Type, other.Type, EntryType.Measure);
            if (order != 0)
                return order;
            order = ComparePreferredFirst(Type, other.Type, EntryType.Tempo);
            if (order != 0)
                return order;
            order = ComparePreferredFirst(Type, other.Type, EntryType.Sample);
            if (order != 0)
                return order;
            return ComparePreferredLast(Type, other.Type, EntryType.EndOfSong);
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
        private static int ComparePreferredFirst(EntryType current, EntryType other, EntryType preferred)
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
        private static int ComparePreferredLast(EntryType current, EntryType other, EntryType preferred)
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

        // some functions don't really care what kind of offset there is as long as there's some way
        // to sort them by time- this property should remain read-only for this reason
        public Fraction Offset
        {
            get
            {
                if (linearOffset.Denominator != 0)
                {
                    return linearOffset;
                }
                else if (metricOffset.Denominator != 0)
                {
                    return metricOffset;
                }
                else
                {
                    return new Fraction(0, 1);
                }
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

    public enum EntryType
    {
        Invalid,
        Marker,
        Sample,
        Stop,
        Tempo,
        Measure,
        Mine,
        Event,
        Judgement,
        BGA,
        EndOfSong
    }
}


