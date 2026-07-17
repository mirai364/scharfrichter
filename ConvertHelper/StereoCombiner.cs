using Scharfrichter.Codec;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds;
using System;
using System.Collections.Generic;

namespace ConvertHelper
{
    /// <summary>
    /// Combines compatible split stereo BGM samples into a single centered sample.
    /// </summary>
    static public class StereoCombiner
    {
        /// <summary>
        /// Detects paired BGM samples and merges them in-place.
        /// </summary>
        static public void Process(Sound[] sounds, Chart[] charts, float amplification = 1.0f)
        {
            SampleUsage usage = AnalyzeSampleUsage(charts);
            CombineEligibleSamples(sounds, usage, amplification);
        }

        /// <summary>
        /// Scans charts to find samples that are used only as BGM.
        /// </summary>
        private static SampleUsage AnalyzeSampleUsage(Chart[] charts)
        {
            SampleUsage usage = new SampleUsage();
            foreach (Chart chart in charts)
            {
                foreach (Entry entry in chart.Entries)
                    RegisterEntryUsage(entry, usage);
            }
            return usage;
        }

        /// <summary>
        /// Records sample usage information for one chart entry.
        /// </summary>
        private static void RegisterEntryUsage(Entry entry, SampleUsage usage)
        {
            if (entry.Type != EntryType.Sample && entry.Type != EntryType.Marker)
                return;
            if (entry.Value.Denominator != 1 || entry.Value.Numerator <= 0)
                return;

            int noteValue = (int)entry.Value.Numerator;
            if (!usage.KeysoundsUsed.Contains(noteValue))
            {
                usage.KeysoundsUsed.Add(noteValue);
                if (!usage.BgmKeysounds.Contains(noteValue))
                    usage.BgmKeysounds.Add(noteValue);
            }

            if (!usage.KeysoundOccurrences.ContainsKey(noteValue))
                usage.KeysoundOccurrences[noteValue] = 0;
            usage.KeysoundOccurrences[noteValue]++;

            if (!usage.FirstKeysoundOccurrence.ContainsKey(noteValue))
                usage.FirstKeysoundOccurrence[noteValue] = entry.Offset;

            if (entry.Player != 0)
                usage.BgmKeysounds.Remove(noteValue);
        }

        /// <summary>
        /// Merges all sample pairs that pass the stereo-pair heuristics.
        /// </summary>
        private static void CombineEligibleSamples(Sound[] sounds, SampleUsage usage, float amplification)
        {
            int count = sounds.Length;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (CanCombineSamples(sounds, usage, i, j))
                        CombineSamplePair(sounds, i, j, amplification);
                }
            }
        }

        /// <summary>
        /// Checks whether two samples look like opposite-panned BGM halves.
        /// </summary>
        private static bool CanCombineSamples(Sound[] sounds, SampleUsage usage, int leftIndex, int rightIndex)
        {
            int leftSample = leftIndex + 1;
            int rightSample = rightIndex + 1;
            if (!usage.BgmKeysounds.Contains(leftSample) || !usage.BgmKeysounds.Contains(rightSample))
                return false;

            return Math.Abs(sounds[leftIndex].Data.Length - sounds[rightIndex].Data.Length) <= (sounds[leftIndex].Data.Length / 100)
                && Math.Abs(sounds[leftIndex].Panning - sounds[rightIndex].Panning) == 1
                && usage.KeysoundOccurrences[leftSample] == usage.KeysoundOccurrences[rightSample]
                && usage.FirstKeysoundOccurrence[leftSample] == usage.FirstKeysoundOccurrence[rightSample];
        }

        /// <summary>
        /// Renders, sums, and replaces a stereo sample pair.
        /// </summary>
        private static void CombineSamplePair(Sound[] sounds, int leftIndex, int rightIndex, float amplification)
        {
            byte[] render0 = sounds[leftIndex].Render(1.0f);
            byte[] render1 = sounds[rightIndex].Render(1.0f);
            byte[] output = Util.Sum16(render0, render1);
            sounds[leftIndex].SetSound(output, sounds[leftIndex].Format);
            sounds[rightIndex].SetSound(new byte[] { }, sounds[rightIndex].Format);
            sounds[leftIndex].Panning = 0.5f;
            sounds[leftIndex].Volume = amplification;
        }

        private sealed class SampleUsage
        {
            public readonly List<int> KeysoundsUsed = new List<int>();
            public readonly List<int> BgmKeysounds = new List<int>();
            public readonly Dictionary<int, int> KeysoundOccurrences = new Dictionary<int, int>();
            public readonly Dictionary<int, Fraction> FirstKeysoundOccurrence = new Dictionary<int, Fraction>();
        }
    }
}