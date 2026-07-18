using Scharfrichter.Codec.Charts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scharfrichter.Codec.Archives
{
    /// <summary>
    /// Be-Music Script Object Notation writer.
    /// </summary>
    public class Bmson : Archive
    {
        public const int Resolution = 240;
        private const int PulsesPerMeasure = Resolution * 4;
        private Chart[] charts = new Chart[] { null };
        private string soundExtension = "ogg";

        public string SoundExtension
        {
            get
            {
                return soundExtension;
            }
            set
            {
                soundExtension = String.IsNullOrWhiteSpace(value) ? "ogg" : value.Trim().TrimStart('.').ToLowerInvariant();
            }
        }

        public override Chart[] Charts
        {
            get
            {
                return charts;
            }
            set
            {
                if (value != null && value.Length > 0)
                    charts[0] = value[0];
            }
        }

        public override int ChartCount
        {
            get
            {
                return (charts[0] != null) ? 1 : 0;
            }
        }

        public static string GetSoundFolder(string keyset)
        {
            if (keyset == null || keyset == "" || keyset == "0")
                return "sounds";

            return "sounds_" + keyset;
        }

        public string GetSoundFileName(int sample)
        {
            return Util.ConvertToBMEString(sample, 4) + "." + SoundExtension;
        }

        public bool Write(Stream target)
        {
            return Write(target, null);
        }

        public bool Write(Stream target, BmsonSoundLayout soundLayout)
        {
            if (charts[0] == null)
                return false;

            Chart chart = charts[0];
            BmsonRoot root = CreateRoot(chart, soundLayout);
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(root, options);
            target.Write(json, 0, json.Length);
            return true;
        }

        private BmsonRoot CreateRoot(Chart chart, BmsonSoundLayout soundLayout)
        {
            List<int> measureStarts = BuildMeasureStarts(chart);

            return new BmsonRoot
            {
                Version = "1.0.0",
                Info = CreateInfo(chart),
                Lines = CreateLines(chart, measureStarts),
                BpmEvents = CreateBpmEvents(chart, measureStarts),
                StopEvents = CreateStopEvents(chart, measureStarts),
                SoundChannels = soundLayout != null ? CreatePackedSoundChannels(chart, measureStarts, soundLayout) : CreateSoundChannels(chart, measureStarts),
                Bga = CreateBga(chart, measureStarts)
            };
        }

        private static BmsonInfo CreateInfo(Chart chart)
        {
            BmsonInfo info = new BmsonInfo
            {
                Title = GetTag(chart, "TITLE"),
                Subtitle = "",
                Artist = GetTag(chart, "ARTIST"),
                Genre = GetTag(chart, "GENRE"),
                ModeHint = GetModeHint(chart),
                ChartName = GetChartName(chart),
                Level = GetUnsignedTag(chart, "PLAYLEVEL"),
                InitBpm = Math.Round((double)chart.DefaultBPM, 3),
                JudgeRank = GetUnsignedTag(chart, "RANK", 100),
                Total = CalculateTotalGauge(chart.NoteTotal),
                Resolution = Resolution
            };

            if (info.InitBpm <= 0 && chart.Tags.ContainsKey("BPM"))
                info.InitBpm = Math.Round(Convert.ToDouble(chart.Tags["BPM"]), 3);

            return info;
        }

        private static string GetModeHint(Chart chart)
        {
            if (chart.Players > 1)
                return "beat-14k";

            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type == EntryType.Marker && entry.Player > 0 && (entry.Column == 5 || entry.Column == 6))
                    return "beat-7k";
            }

            return "beat-5k";
        }

        private static string GetChartName(Chart chart)
        {
            if (chart.Tags.ContainsKey("CHARTNAME"))
                return chart.Tags["CHARTNAME"];

            if (chart.Tags.ContainsKey("DIFFICULTY"))
                return chart.Tags["DIFFICULTY"];

            return "";
        }

        private static string GetTag(Chart chart, string tag)
        {
            if (chart.Tags.ContainsKey(tag))
                return chart.Tags[tag];

            return "";
        }

        private static uint GetUnsignedTag(Chart chart, string tag)
        {
            return GetUnsignedTag(chart, tag, 0);
        }

        private static uint GetUnsignedTag(Chart chart, string tag, uint defaultValue)
        {
            uint value;
            if (chart.Tags.ContainsKey(tag) && UInt32.TryParse(chart.Tags[tag], out value))
                return value;

            return defaultValue;
        }

        private static double CalculateTotalGauge(int noteCount)
        {
            if (noteCount < 1)
                return 0;
            if (noteCount < 400)
                return Math.Floor(200.0 + (noteCount / 5.0));
            if (noteCount < 600)
                return Math.Floor(280.0 + ((noteCount - 400.0) / 2.5));

            return Math.Floor(360.0 + ((noteCount - 600.0) / 5.0));
        }

        private static List<int> BuildMeasureStarts(Chart chart)
        {
            int measureCount = chart.Measures;
            List<int> starts = new List<int>(measureCount + 1);
            double currentPulse = 0;

            for (int i = 0; i <= measureCount; i++)
            {
                starts.Add((int)Math.Round(currentPulse));
                currentPulse += GetMeasureLength(chart, i) * PulsesPerMeasure;
            }

            return starts;
        }

        private static double GetMeasureLength(Chart chart, int measure)
        {
            if (chart.MeasureLengths.ContainsKey(measure))
                return (double)chart.MeasureLengths[measure];

            return 1.0;
        }

        private static int GetPulse(Chart chart, Entry entry, List<int> measureStarts)
        {
            int measure = Math.Max(0, entry.MetricMeasure);
            while (measure >= measureStarts.Count)
                measureStarts.Add(measureStarts[measureStarts.Count - 1] + PulsesPerMeasure);

            double measurePulseLength = GetMeasureLength(chart, measure) * PulsesPerMeasure;
            double pulse = measureStarts[measure] + ((double)entry.MetricOffset * measurePulseLength);
            return (int)Math.Round(pulse);
        }

        private static BmsonBarLine[] CreateLines(Chart chart, List<int> measureStarts)
        {
            List<BmsonBarLine> lines = new List<BmsonBarLine>();
            int maxEventPulse = GetMaxEventPulse(chart, measureStarts);
            int lastLinePulse = maxEventPulse + (PulsesPerMeasure * 3);

            for (int i = 0; i < measureStarts.Count; i++)
            {
                if (measureStarts[i] <= lastLinePulse)
                    lines.Add(new BmsonBarLine { Y = (uint)measureStarts[i] });
            }

            return lines.ToArray();
        }

        private static int GetMaxEventPulse(Chart chart, List<int> measureStarts)
        {
            int maxPulse = 0;
            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type != EntryType.Marker && entry.Type != EntryType.Tempo && entry.Type != EntryType.Stop && entry.Type != EntryType.BGA)
                    continue;

                maxPulse = Math.Max(maxPulse, GetPulse(chart, entry, measureStarts));
            }

            return maxPulse;
        }

        private static BmsonBpmEvent[] CreateBpmEvents(Chart chart, List<int> measureStarts)
        {
            List<BmsonBpmEvent> events = new List<BmsonBpmEvent>();
            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type != EntryType.Tempo)
                    continue;

                events.Add(new BmsonBpmEvent
                {
                    Y = (uint)GetPulse(chart, entry, measureStarts),
                    Bpm = Math.Round((double)entry.Value, 3)
                });
            }

            events.Sort((a, b) => a.Y.CompareTo(b.Y));
            return events.ToArray();
        }

        private static BmsonStopEvent[] CreateStopEvents(Chart chart, List<int> measureStarts)
        {
            List<BmsonStopEvent> events = new List<BmsonStopEvent>();
            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type != EntryType.Stop)
                    continue;

                events.Add(new BmsonStopEvent
                {
                    Y = (uint)GetPulse(chart, entry, measureStarts),
                    Duration = (uint)Math.Max(0, (int)Math.Round((double)entry.Value))
                });
            }

            events.Sort((a, b) => a.Y.CompareTo(b.Y));
            return events.ToArray();
        }

        private BmsonSoundChannel[] CreateSoundChannels(Chart chart, List<int> measureStarts)
        {
            Dictionary<int, int> sampleOrder = new Dictionary<int, int>();
            Dictionary<int, List<BmsonNote>> notesBySample = new Dictionary<int, List<BmsonNote>>();
            Dictionary<Entry, Entry> longNoteEnds = BuildLongNoteEndMap(chart);
            HashSet<Entry> consumedLongNoteEnds = new HashSet<Entry>(longNoteEnds.Values);

            foreach (Entry entry in GetSortedEntries(chart))
            {
                if (entry.Type != EntryType.Marker || consumedLongNoteEnds.Contains(entry))
                    continue;

                int sample = (int)((double)entry.Value);
                if (sample <= 0)
                    continue;

                if (!sampleOrder.ContainsKey(sample))
                    sampleOrder[sample] = sampleOrder.Count;

                List<BmsonNote> notes;
                if (!notesBySample.TryGetValue(sample, out notes))
                {
                    notes = new List<BmsonNote>();
                    notesBySample[sample] = notes;
                }

                int y = GetPulse(chart, entry, measureStarts);
                Entry endEntry;
                int length = 0;
                if (longNoteEnds.TryGetValue(entry, out endEntry))
                    length = Math.Max(0, GetPulse(chart, endEntry, measureStarts) - y);

                notes.Add(new BmsonNote
                {
                    X = GetLane(entry),
                    Y = (uint)y,
                    L = (uint)length,
                    C = false
                });
            }

            List<int> samples = new List<int>(notesBySample.Keys);
            samples.Sort((a, b) => sampleOrder[a].CompareTo(sampleOrder[b]));

            List<BmsonSoundChannel> channels = new List<BmsonSoundChannel>();
            foreach (int sample in samples)
            {
                List<BmsonNote> notes = notesBySample[sample];
                notes.Sort((a, b) => a.Y.CompareTo(b.Y));
                channels.Add(new BmsonSoundChannel
                {
                    Name = GetSampleName(chart, sample),
                    Notes = notes.ToArray()
                });
            }

            return channels.ToArray();
        }

        private static BmsonSoundChannel[] CreatePackedSoundChannels(Chart chart, List<int> measureStarts, BmsonSoundLayout soundLayout)
        {
            List<List<BmsonNote>> notesByTrack = new List<List<BmsonNote>>();
            for (int i = 0; i < soundLayout.Tracks.Count; i++)
                notesByTrack.Add(new List<BmsonNote>());

            Dictionary<Entry, Entry> longNoteEnds = BuildLongNoteEndMap(chart);
            HashSet<Entry> consumedLongNoteEnds = new HashSet<Entry>(longNoteEnds.Values);

            foreach (Entry entry in GetSortedEntries(chart))
            {
                if (entry.Type != EntryType.Marker || consumedLongNoteEnds.Contains(entry))
                    continue;

                BmsonPackedNote packedNote;
                if (!soundLayout.Notes.TryGetValue(entry, out packedNote))
                    continue;

                int y = GetPulse(chart, entry, measureStarts);
                Entry endEntry;
                int length = 0;
                if (longNoteEnds.TryGetValue(entry, out endEntry))
                    length = Math.Max(0, GetPulse(chart, endEntry, measureStarts) - y);

                notesByTrack[packedNote.TrackIndex].Add(new BmsonNote
                {
                    X = GetLane(entry),
                    Y = (uint)y,
                    L = (uint)length,
                    C = packedNote.Continue
                });
            }

            List<BmsonSoundChannel> channels = new List<BmsonSoundChannel>();
            for (int i = 0; i < soundLayout.Tracks.Count; i++)
            {
                if (notesByTrack[i].Count == 0)
                    continue;

                notesByTrack[i].Sort((a, b) => a.Y.CompareTo(b.Y));
                channels.Add(new BmsonSoundChannel
                {
                    Name = soundLayout.Tracks[i].Name,
                    Notes = notesByTrack[i].ToArray()
                });
            }

            return channels.ToArray();
        }
        private static List<Entry> GetSortedEntries(Chart chart)
        {
            List<Entry> entries = new List<Entry>(chart.Entries);
            entries.Sort();
            return entries;
        }

        private static Dictionary<Entry, Entry> BuildLongNoteEndMap(Chart chart)
        {
            Dictionary<Entry, Entry> result = new Dictionary<Entry, Entry>();
            Dictionary<string, Entry> previousMarkers = new Dictionary<string, Entry>();

            foreach (Entry entry in GetSortedEntries(chart))
            {
                if (entry.Type != EntryType.Marker || entry.Player <= 0)
                    continue;

                string key = entry.Player.ToString() + ":" + entry.Column.ToString();
                if (entry.Freeze)
                {
                    Entry startEntry;
                    if (previousMarkers.TryGetValue(key, out startEntry))
                        result[startEntry] = entry;

                    previousMarkers.Remove(key);
                }
                else
                {
                    previousMarkers[key] = entry;
                }
            }

            return result;
        }

        private static int GetLane(Entry entry)
        {
            if (entry.Player <= 0)
                return 0;

            int lane;
            switch (entry.Column)
            {
                case 8:
                    lane = 8;
                    break;
                case 5:
                    lane = 6;
                    break;
                case 6:
                    lane = 7;
                    break;
                default:
                    lane = entry.Column + 1;
                    break;
            }

            if (entry.Player > 1)
                lane += 8;

            return lane;
        }

        private string GetSampleName(Chart chart, int sample)
        {
            string keyset = "0";
            if (chart.Tags.ContainsKey("KEYSET"))
                keyset = chart.Tags["KEYSET"];

            return GetSoundFolder(keyset) + "/" + GetSoundFileName(sample);
        }

        private static BmsonBga CreateBga(Chart chart, List<int> measureStarts)
        {
            BmsonBga bga = new BmsonBga
            {
                BgaHeader = CreateBgaHeaders(chart),
                BgaEvents = CreateBgaEvents(chart, measureStarts, 0),
                LayerEvents = CreateBgaEvents(chart, measureStarts, 1),
                PoorEvents = CreateBgaEvents(chart, measureStarts, 2)
            };

            return bga;
        }

        private static BmsonBgaHeader[] CreateBgaHeaders(Chart chart)
        {
            List<BmsonBgaHeader> headers = new List<BmsonBgaHeader>();
            foreach (KeyValuePair<string, string> tag in chart.Tags)
            {
                if (!tag.Key.StartsWith("BMP") || tag.Key.Length != 5)
                    continue;

                int id = ParseBase36(tag.Key.Substring(3, 2));
                if (id <= 0)
                    continue;

                headers.Add(new BmsonBgaHeader
                {
                    Id = (uint)id,
                    Name = tag.Value
                });
            }

            headers.Sort((a, b) => a.Id.CompareTo(b.Id));
            return headers.ToArray();
        }

        private static BmsonBgaEvent[] CreateBgaEvents(Chart chart, List<int> measureStarts, int column)
        {
            List<BmsonBgaEvent> events = new List<BmsonBgaEvent>();
            foreach (Entry entry in GetSortedEntries(chart))
            {
                if (entry.Type != EntryType.BGA || entry.Column != column)
                    continue;

                int id = (int)((double)entry.Value);
                if (id <= 0)
                    continue;

                events.Add(new BmsonBgaEvent
                {
                    Y = (uint)GetPulse(chart, entry, measureStarts),
                    Id = (uint)id
                });
            }

            return events.ToArray();
        }

        private static int ParseBase36(string value)
        {
            int result = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = Char.ToUpperInvariant(value[i]);
                int digit;
                if (c >= '0' && c <= '9')
                    digit = c - '0';
                else if (c >= 'A' && c <= 'Z')
                    digit = c - 'A' + 10;
                else
                    return -1;

                result = (result * 36) + digit;
            }

            return result;
        }
        private sealed class BmsonRoot
        {
            [JsonPropertyName("version")]
            public string Version { get; set; }

            [JsonPropertyName("info")]
            public BmsonInfo Info { get; set; }

            [JsonPropertyName("lines")]
            public BmsonBarLine[] Lines { get; set; }

            [JsonPropertyName("bpm_events")]
            public BmsonBpmEvent[] BpmEvents { get; set; }

            [JsonPropertyName("stop_events")]
            public BmsonStopEvent[] StopEvents { get; set; }

            [JsonPropertyName("sound_channels")]
            public BmsonSoundChannel[] SoundChannels { get; set; }

            [JsonPropertyName("bga")]
            public BmsonBga Bga { get; set; }
        }

        private sealed class BmsonInfo
        {
            [JsonPropertyName("title")]
            public string Title { get; set; }

            [JsonPropertyName("subtitle")]
            public string Subtitle { get; set; }

            [JsonPropertyName("artist")]
            public string Artist { get; set; }

            [JsonPropertyName("subartists")]
            public string[] Subartists { get; set; } = new string[0];

            [JsonPropertyName("genre")]
            public string Genre { get; set; }

            [JsonPropertyName("mode_hint")]
            public string ModeHint { get; set; }

            [JsonPropertyName("chart_name")]
            public string ChartName { get; set; }

            [JsonPropertyName("level")]
            public uint Level { get; set; }

            [JsonPropertyName("init_bpm")]
            public double InitBpm { get; set; }

            [JsonPropertyName("judge_rank")]
            public double JudgeRank { get; set; }

            [JsonPropertyName("total")]
            public double Total { get; set; }

            [JsonPropertyName("resolution")]
            public uint Resolution { get; set; }
        }

        private sealed class BmsonBarLine
        {
            [JsonPropertyName("y")]
            public uint Y { get; set; }
        }

        private sealed class BmsonBpmEvent
        {
            [JsonPropertyName("y")]
            public uint Y { get; set; }

            [JsonPropertyName("bpm")]
            public double Bpm { get; set; }
        }

        private sealed class BmsonStopEvent
        {
            [JsonPropertyName("y")]
            public uint Y { get; set; }

            [JsonPropertyName("duration")]
            public uint Duration { get; set; }
        }

        private sealed class BmsonSoundChannel
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("notes")]
            public BmsonNote[] Notes { get; set; }
        }

        private sealed class BmsonNote
        {
            [JsonPropertyName("x")]
            public int X { get; set; }

            [JsonPropertyName("y")]
            public uint Y { get; set; }

            [JsonPropertyName("l")]
            public uint L { get; set; }

            [JsonPropertyName("c")]
            public bool C { get; set; }
        }

        private sealed class BmsonBga
        {
            [JsonPropertyName("bga_header")]
            public BmsonBgaHeader[] BgaHeader { get; set; } = new BmsonBgaHeader[0];

            [JsonPropertyName("bga_events")]
            public BmsonBgaEvent[] BgaEvents { get; set; } = new BmsonBgaEvent[0];

            [JsonPropertyName("layer_events")]
            public BmsonBgaEvent[] LayerEvents { get; set; } = new BmsonBgaEvent[0];

            [JsonPropertyName("poor_events")]
            public BmsonBgaEvent[] PoorEvents { get; set; } = new BmsonBgaEvent[0];
        }

        private sealed class BmsonBgaHeader
        {
            [JsonPropertyName("id")]
            public uint Id { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }
        }

        private sealed class BmsonBgaEvent
        {
            [JsonPropertyName("y")]
            public uint Y { get; set; }

            [JsonPropertyName("id")]
            public uint Id { get; set; }
        }
    }
}
