using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds;
using Scharfrichter.Codec.Sounds.Encoders;
using Scharfrichter.Common;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConvertHelper
{
    /// <summary>
    /// bmson packed-sound optimization support for PopnToBMS.
    /// Each unique sample gets its own track containing just that sample.
    /// This maps Pop'n's individual sample-per-note model to packed track layout
    /// while reducing the track count compared to time-based packing.
    /// </summary>
    internal static class PopnToBMS_PackBmson
    {
        internal sealed class PendingChart
        {
            public Chart Chart;
            public Configuration Config;
            public string Filename;
            public int Index;
            public DateTime UpdateTime;
            public string Version;
            public string DirPath;
        }

        private static readonly List<PendingChart> PendingCharts = new List<PendingChart>();

        internal static int PendingCount => PendingCharts.Count;

        internal static void Clear()
        {
            PendingCharts.Clear();
        }

        internal static void Register(Chart chart, Configuration config, string filename, int index, DateTime updateTime, string version, string dirPath)
        {
            PendingCharts.Add(new PendingChart
            {
                Chart = chart,
                Config = config,
                Filename = filename,
                Index = index,
                UpdateTime = updateTime,
                Version = version,
                DirPath = dirPath,
            });
        }

        /// <summary>
        /// Builds packed bmson sound tracks: one track per unique sample used across all pending charts.
        /// </summary>
        internal static void Finalize(Sound[] sounds, float volume, DateTime updateTime, string soundOutputFormat)
        {
            if (PendingCharts.Count == 0)
                return;

            // Collect all unique sample IDs used across all charts
            var usedSamples = new HashSet<int>();
            foreach (var pending in PendingCharts)
            {
                foreach (Entry entry in pending.Chart.Entries)
                {
                    if (entry.Type != EntryType.Marker || entry.Player <= 0)
                        continue;
                    int sampleIndex = (int)((double)entry.Value);
                    if (sampleIndex > 0 && sampleIndex <= sounds.Length)
                        usedSamples.Add(sampleIndex);
                }
            }

            List<int> sampleList = usedSamples.OrderBy(s => s).ToList();
            Console.WriteLine("[BMSON_PACK] {0} charts, {1} unique samples used, optimizing...", PendingCharts.Count, sampleList.Count);

            // Determine output path from first pending chart
            var first = PendingCharts[0];
            string name = first.Chart.Tags.ContainsKey("TITLE") && first.Chart.Tags["TITLE"] != ""
                ? first.Chart.Tags["TITLE"]
                : first.Filename;
            name = Common.nameReplace(name);
            string targetPath = Path.Combine(first.DirPath, first.Version, name);
            string soundFolder = Bmson.GetSoundFolder("0");
            string soundPath = Path.Combine(targetPath, soundFolder);
            Common.SafeCreateDirectory(soundPath);

            // Map sample -> track index
            Dictionary<int, int> sampleToTrack = new Dictionary<int, int>();
            string soundExt = SoundEncoderFactory.GetFileExtension(soundOutputFormat);

            foreach (int sampleIndex in sampleList)
            {
                Sound sourceSound = sounds[sampleIndex - 1];
                string sampleName = Util.ConvertToBMEString(sampleIndex, 4);
                string output = Path.Combine(soundPath, sampleName + "." + soundExt);

                try
                {
                    ISoundEncoder encoder = SoundEncoderFactory.Create(soundOutputFormat);
                    encoder.EncodeToFile(sourceSound, output, volume);
                    SetFileTimes(output, updateTime);
                }
                catch
                {
                }
            }

            // Build BmsonSoundLayout per chart: each sample maps to a track
            foreach (var pending in PendingCharts)
            {
                BmsonSoundLayout layout = new BmsonSoundLayout();
                Dictionary<int, int> chartSampleToTrack = new Dictionary<int, int>();
                int nextTrackIdx = 0;

                foreach (Entry entry in pending.Chart.Entries)
                {
                    if (entry.Type != EntryType.Marker || entry.Player <= 0)
                        continue;

                    int sampleIndex = (int)((double)entry.Value);
                    if (sampleIndex <= 0 || sampleIndex > sounds.Length)
                        continue;

                    if (!chartSampleToTrack.ContainsKey(sampleIndex))
                    {
                        chartSampleToTrack[sampleIndex] = nextTrackIdx;
                        layout.Tracks.Add(new BmsonSoundTrack
                        {
                            Name = Util.ConvertToBMEString(sampleIndex, 4),
                            Index = nextTrackIdx,
                        });
                        nextTrackIdx++;
                    }

                    layout.Notes[entry] = new BmsonPackedNote
                    {
                        TrackIndex = chartSampleToTrack[sampleIndex],
                        Continue = entry.Freeze,
                    };
                }

                Console.WriteLine("[BMSON_PACK] Chart uses {0} tracks", layout.Tracks.Count);

                // Write chart with layout
                PopnToBMS.ConvertChart(pending.Chart, pending.Config, pending.Filename, pending.Index, null, pending.UpdateTime, pending.Version, pending.DirPath, layout);
            }

            PendingCharts.Clear();
            SetDirectoryTimes(soundPath, updateTime);
            Console.WriteLine("[BMSON_PACK] Done. Total samples: {0}", sampleList.Count);
        }

        private static void SetFileTimes(string path, DateTime updateTime)
        {
            File.SetCreationTime(path, updateTime);
            File.SetLastWriteTime(path, updateTime);
            File.SetLastAccessTime(path, updateTime);
        }

        private static void SetDirectoryTimes(string path, DateTime updateTime)
        {
            Directory.SetCreationTime(path, updateTime);
            Directory.SetLastWriteTime(path, updateTime);
            Directory.SetLastAccessTime(path, updateTime);
        }
    }
}