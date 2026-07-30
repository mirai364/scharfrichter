using Scharfrichter.Codec.Charts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scharfrichter.Codec.Archives
{
    // Be-Music Source File.

    public class BMS : Archive
    {
        private enum ValueCoding
        {
            BME,
            Hex,
            Decimal,
            BPMTable
        }

        private const int DefaultBmsObjectBase = 36;
        private Chart[] charts = new Chart[] { null };
        private int[] sampleMap;
        private Dictionary<int, int> reSampleMap = new Dictionary<int, int>();
        private string soundExtension = "ogg";
        private string soundFolder = "sounds";
        private int bmsObjectBase = DefaultBmsObjectBase;

        /// <summary>
        /// Initializes a BMS archive with the default one-to-one sample map.
        /// </summary>
        public BMS()
        {
            ResetSampleMap();
        }

        /// <summary>
        /// Gets or sets the single chart stored by this BMS archive.
        /// </summary>
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

        /// <summary>
        /// Gets the number of charts currently stored in this archive.
        /// </summary>
        public override int ChartCount
        {
            get
            {
                return (charts[0] != null) ? 1 : 0;
            }
        }

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

        /// <summary>
        /// Gets or sets the folder prefix used for #WAV sample paths.
        /// Defaults to "sounds" for IIDX compatibility. Pop'n sets this to the filename stem.
        /// </summary>
        public string SoundFolder
        {
            get
            {
                return soundFolder;
            }
            set
            {
                soundFolder = String.IsNullOrWhiteSpace(value) ? "sounds" : value.TrimEnd('\\', '/');
            }
        }

        /// <summary>
        /// Gets or sets the numeric base used for two-character BMS object identifiers.
        /// </summary>
        public int BmsObjectBase
        {
            get
            {
                return bmsObjectBase;
            }
            set
            {
                bmsObjectBase = value == 36 ? 36 : 62;
                ResetSampleMap();
            }
        }

        /// <summary>
        /// Gets the largest non-zero object identifier value available in the selected base.
        /// </summary>
        public int MaxBmsObjectIndex
        {
            get
            {
                return (bmsObjectBase * bmsObjectBase) - 1;
            }
        }

        /// <summary>
        /// Calculates the BMS #TOTAL gauge value from the number of playable notes.
        /// </summary>
        private int CalculateTotalGauge(int noteCount)
        {
            double gauge = 0;

            if (noteCount < 1)
            {
                gauge = 0;
            }
            else if (noteCount < 400)
            {
                gauge = 200.0 + (noteCount / 5.0);
            }
            else if (noteCount < 600)
            {
                gauge = 280.0 + ((noteCount - 400.0) / 2.5);
            }
            else // noteCount >= 600
            {
                gauge = 360.0 + ((noteCount - 600.0) / 5.0);
            }

            return (int)Math.Floor(gauge);
        }

        /// <summary>
        /// Builds the sample map from the samples referenced by the current chart.
        /// </summary>
        public void GenerateSampleMap()
        {
            int[] usedSamples = charts[0].UsedSamples();
            SampleMap = usedSamples;
        }

        /// <summary>
        /// Generates remapped #WAV tags for all samples that were written to BMS channels.
        /// </summary>
        public bool GenerateReSampleTags(string keyset = "0", string rendarWavName = "")
        {
            Chart chart = charts[0];
            string targetFolder;
            if (keyset == "0")
            {
                targetFolder = soundFolder + "\\";
            }
            else
            {
                targetFolder = soundFolder + "_" + keyset + "\\";
            }

            foreach (KeyValuePair<int, int> pair in reSampleMap)
            {
                if (pair.Value >= MaxBmsObjectIndex)
                {
                    Console.WriteLine("WARNING: More than " + MaxBmsObjectIndex.ToString() + " samples");
                    return false;
                }
                chart.Tags["WAV" + Util.ConvertToBMSObjectString(pair.Value + 1, 2, bmsObjectBase)] = targetFolder + Util.ConvertToBMEString(pair.Key, 4) + "." + SoundExtension;
                //Console.WriteLine("WAV" + Util.ConvertToBMSObjectString(pair.Value + 1, 2, bmsObjectBase) + " " + targetFolder + Util.ConvertToBMEString(pair.Key, 4) + "." + SoundExtension);
            }

            if (rendarWavName.Length > 0)
            {
                chart.Tags["WAV01"] = targetFolder + rendarWavName + "." + SoundExtension;
            }
            return true;
        }

        /// <summary>
        /// Reads BMS text data from a stream and converts it into a chart archive.
        /// </summary>
        static public BMS Read(Stream source)
        {
            List<KeyValuePair<string, string>> noteTags = new List<KeyValuePair<string, string>>();

            BMS result = new BMS();
            Chart chart = new Chart();
            StreamReader reader = new StreamReader(source);

            while (!reader.EndOfStream)
            {
                string currentLine = reader.ReadLine();

                if (currentLine.StartsWith("#"))
                {
                    currentLine = currentLine.Substring(1);
                    currentLine = currentLine.Replace("\t", " ");

                    if (currentLine.Contains(" "))
                    {
                        int separatorOffset = currentLine.IndexOf(" ");
                        string val = currentLine.Substring(separatorOffset + 1).Trim();
                        string tag = currentLine.Substring(0, separatorOffset).Trim().ToUpper();
                        if (tag != "")
                            chart.Tags[tag] = val;
                    }
                    else if (currentLine.Contains(":"))
                    {
                        int separatorOffset = currentLine.IndexOf(":");
                        string val = currentLine.Substring(separatorOffset + 1).Trim();
                        string tag = currentLine.Substring(0, separatorOffset).Trim().ToUpper();
                        if (tag != "")
                            noteTags.Add(new KeyValuePair<string, string>(tag, val));
                    }
                }
            }

            if (chart.Tags.ContainsKey("BPM"))
            {
                chart.DefaultBPM = Fraction.Rationalize(Convert.ToDouble(chart.Tags["BPM"]));
            }

            foreach (KeyValuePair<string, string> tag in noteTags)
            {
                if (tag.Key.Length == 5)
                {
                    string measure = tag.Key.Substring(0, 3);
                    string lane = tag.Key.Substring(3, 2);
                    ValueCoding coding = ValueCoding.BME;

                    int currentColumn;
                    int currentMeasure;
                    int currentPlayer;
                    EntryType currentType;

                    if (lane == "02")
                    {
                        chart.MeasureLengths[Convert.ToInt32(measure)] = Fraction.Rationalize(Convert.ToDouble(tag.Value));
                    }
                    else
                    {
                        currentMeasure = Convert.ToInt32(measure);
                        currentColumn = 0;

                        switch (lane)
                        {
                            case "01": currentPlayer = 0; currentType = EntryType.Marker; currentColumn = 0; break;
                            case "03": currentPlayer = 0; currentType = EntryType.Tempo; coding = ValueCoding.Hex; break;
                            case "04": currentPlayer = 0; currentType = EntryType.BGA; currentColumn = 0; break;
                            case "05": currentPlayer = 0; currentType = EntryType.BGA; currentColumn = 1; break;
                            case "06": currentPlayer = 0; currentType = EntryType.BGA; currentColumn = 2; break;
                            case "07": currentPlayer = 0; currentType = EntryType.BGA; currentColumn = 1; break;
                            case "0A": currentPlayer = 0; currentType = EntryType.BGA; currentColumn = 3; break;
                            case "08": currentPlayer = 0; currentType = EntryType.Tempo; coding = ValueCoding.BPMTable; break;
                            case "11": currentPlayer = 1; currentType = EntryType.Marker; currentColumn = 0; break;
                            case "12": currentPlayer = 1; currentType = EntryType.Marker; currentColumn = 1; break;
                            case "13": currentPlayer = 1; currentType = EntryType.Marker; currentColumn = 2; break;
                            case "14": currentPlayer = 1; currentType = EntryType.Marker; currentColumn = 3; break;
                            case "15": currentPlayer = 1; currentType = EntryType.Marker; currentColumn = 4; break;
                            case "16": currentPlayer = 1; currentType = EntryType.Marker; currentColumn = 5; break;
                            case "17": currentPlayer = 1; currentType = EntryType.Marker; currentColumn = 8; break;
                            case "18": currentPlayer = 1; currentType = EntryType.Marker; currentColumn = 6; break;
                            case "19": currentPlayer = 1; currentType = EntryType.Marker; currentColumn = 7; break;
                            case "21": currentPlayer = 2; currentType = EntryType.Marker; currentColumn = 0; break;
                            case "22": currentPlayer = 2; currentType = EntryType.Marker; currentColumn = 1; break;
                            case "23": currentPlayer = 2; currentType = EntryType.Marker; currentColumn = 2; break;
                            case "24": currentPlayer = 2; currentType = EntryType.Marker; currentColumn = 3; break;
                            case "25": currentPlayer = 2; currentType = EntryType.Marker; currentColumn = 4; break;
                            case "26": currentPlayer = 2; currentType = EntryType.Marker; currentColumn = 5; break;
                            case "27": currentPlayer = 2; currentType = EntryType.Marker; currentColumn = 8; break;
                            case "28": currentPlayer = 2; currentType = EntryType.Marker; currentColumn = 6; break;
                            case "29": currentPlayer = 2; currentType = EntryType.Marker; currentColumn = 7; break;
                            case "31": currentPlayer = 1; currentType = EntryType.Sample; currentColumn = 0; break;
                            case "32": currentPlayer = 1; currentType = EntryType.Sample; currentColumn = 1; break;
                            case "33": currentPlayer = 1; currentType = EntryType.Sample; currentColumn = 2; break;
                            case "34": currentPlayer = 1; currentType = EntryType.Sample; currentColumn = 3; break;
                            case "35": currentPlayer = 1; currentType = EntryType.Sample; currentColumn = 4; break;
                            case "36": currentPlayer = 1; currentType = EntryType.Sample; currentColumn = 5; break;
                            case "37": currentPlayer = 1; currentType = EntryType.Sample; currentColumn = 8; break;
                            case "38": currentPlayer = 1; currentType = EntryType.Sample; currentColumn = 6; break;
                            case "39": currentPlayer = 1; currentType = EntryType.Sample; currentColumn = 7; break;
                            case "41": currentPlayer = 2; currentType = EntryType.Sample; currentColumn = 0; break;
                            case "42": currentPlayer = 2; currentType = EntryType.Sample; currentColumn = 1; break;
                            case "43": currentPlayer = 2; currentType = EntryType.Sample; currentColumn = 2; break;
                            case "44": currentPlayer = 2; currentType = EntryType.Sample; currentColumn = 3; break;
                            case "45": currentPlayer = 2; currentType = EntryType.Sample; currentColumn = 4; break;
                            case "46": currentPlayer = 2; currentType = EntryType.Sample; currentColumn = 5; break;
                            case "47": currentPlayer = 2; currentType = EntryType.Sample; currentColumn = 8; break;
                            case "48": currentPlayer = 2; currentType = EntryType.Sample; currentColumn = 6; break;
                            case "49": currentPlayer = 2; currentType = EntryType.Sample; currentColumn = 7; break;
                            default: chart.Tags[tag.Key + ":" + tag.Value] = ""; continue; // a little hack to preserve unknown lines
                        }

                        // determine the alphabet used to decode this line
                        string alphabet;
                        int alphabetLength;
                        switch (coding)
                        {
                            case ValueCoding.Hex: alphabet = Util.alphabetHex; break;
                            case ValueCoding.Decimal: alphabet = Util.alphabetDec; break;
                            default: alphabet = Util.alphabetBME; break;
                        }
                        alphabetLength = alphabet.Length;

                        // decode the line
                        int valueLength = (tag.Value.Length | 1) ^ 1; // make an even number
                        for (int i = 0; i < valueLength; i += 2)
                        {
                            string pair = tag.Value.Substring(i, 2);
                            int index0 = alphabet.IndexOf(pair.Substring(0, 1));
                            int index1 = alphabet.IndexOf(pair.Substring(1, 1));
                            int val = 0;

                            if (index0 > 0)
                                val += (index0 * alphabetLength);
                            if (index1 > 0)
                                val += index1;

                            if (val > 0)
                            {
                                Entry entry = new Entry();
                                entry.Column = currentColumn;
                                entry.Player = currentPlayer;
                                entry.MetricMeasure = currentMeasure;
                                entry.Type = currentType;
                                entry.MetricOffset = new Fraction(i, valueLength);

                                if (coding == ValueCoding.BPMTable)
                                {
                                    if (chart.Tags.ContainsKey("BPM" + pair))
                                    {
                                        string bpmValue = chart.Tags["BPM" + pair];
                                        entry.Value = Fraction.Rationalize(Convert.ToDouble(bpmValue));
                                    }
                                    else
                                    {
                                        entry.Type = EntryType.Invalid;
                                    }
                                }
                                else
                                {
                                    entry.Value = new Fraction(val, 1);
                                }

                                if (entry.Type != EntryType.Invalid)
                                    chart.Entries.Add(entry);
                            }
                        }
                    }
                }
            }

            chart.AddMeasureLines();
            chart.AddJudgements();
            chart.CalculateLinearOffsets();

            result.charts = new Chart[] { chart };
            return result;
        }

        /// <summary>
        /// Removes empty evenly-spaced slots from a BMS channel value array.
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
                            if (j % p != 0)
                            {
                                if (result[j] != 0)
                                {
                                    fail = true;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        fail = true;
                    }

                    if (!fail)
                    {
                        int newCount = count / p;
                        int[] newResult = new int[newCount];
                        int index = 0;

                        for (int j = 0; j < count; j += p)
                        {
                            newResult[index] = result[j];
                            index++;
                        }

                        result = newResult;
                        count = newCount;
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Resets the sample map so every BMS object index maps to the same sample index.
        /// </summary>
        public void ResetSampleMap()
        {
            sampleMap = new int[MaxBmsObjectIndex + 1];
            for (int i = 0; i < sampleMap.Length; i++)
            {
                sampleMap[i] = i;
            }
        }

        /// <summary>
        /// Gets or sets the mapping from BMS object indexes to source sample indexes.
        /// </summary>
        public int[] SampleMap
        {
            get
            {
                return sampleMap;
            }
            set
            {
                int usedSampleCount = value.Length;

                if (usedSampleCount > MaxBmsObjectIndex)
                    usedSampleCount = MaxBmsObjectIndex;

                Array.Copy(value, 0, sampleMap, 1, usedSampleCount);
                for (int i = usedSampleCount + 1; i < sampleMap.Length; i++)
                {
                    sampleMap[i] = 0;
                }
            }
        }

        /// <summary>
        /// Calculates the largest divisor shared by a timing section and the chart quantization.
        /// </summary>
        private static int GetCommonDivisor(int value, int quantizeNotes)
        {
            if (value == 0) return quantizeNotes;
            int a = value;
            int b = quantizeNotes;
            int c;
            while (true)
            {
                c = b % a; if (c == 0) break;
                b = a;
                a = c;
            }
            return a;
        }

        /// <summary>
        /// Finds marker entries that should be emitted through LNTYPE 1 long-note channels.
        /// </summary>
        private static HashSet<Entry> BuildLongNoteEntrySet(Chart chart)
        {
            HashSet<Entry> longNoteEntries = new HashSet<Entry>();
            Dictionary<string, Entry> previousMarkers = new Dictionary<string, Entry>();
            List<Entry> sortedEntries = new List<Entry>(chart.Entries);
            sortedEntries.Sort();

            foreach (Entry entry in sortedEntries)
            {
                if (entry.Type != EntryType.Marker || entry.Player <= 0)
                    continue;

                string key = entry.Player.ToString() + ":" + entry.Column.ToString();
                if (entry.Freeze)
                {
                    Entry startEntry;
                    if (previousMarkers.TryGetValue(key, out startEntry))
                        longNoteEntries.Add(startEntry);
                    longNoteEntries.Add(entry);
                    previousMarkers.Remove(key);
                }
                else
                {
                    previousMarkers[key] = entry;
                }
            }

            return longNoteEntries;
        }

        /// <summary>
        /// Determines whether a BMS channel string belongs to an LNTYPE 1 long-note lane.
        /// </summary>
        private static bool IsLongNoteLane(string laneString)
        {
            if (laneString.Length != 2)
                return false;

            char laneGroup = laneString[0];
            return laneGroup == '5' || laneGroup == '6';
        }

        /// <summary>
        /// Registers a fractional BPM or STOP value and returns its BMS table index.
        /// </summary>
        private static int RegisterValue(Dictionary<int, Fraction> valueMap, Fraction value)
        {
            foreach (KeyValuePair<int, Fraction> entry in valueMap)
            {
                if (entry.Value == value)
                    return entry.Key;
            }

            int index = valueMap.Count + 1;
            if (index % 36 == 10)
                index += 26;
            valueMap[index] = value;
            return index;
        }

        /// <summary>
        /// Writes a BMS header tag and applies quoting when the value requires it.
        /// </summary>
        private static void WriteHeaderTag(StreamWriter writer, string key, string value)
        {
            if (value != null && value.Length > 0)
                writer.WriteLine("#" + key + " " + FormatHeaderValue(key, value));
            else
                writer.WriteLine("#" + key);
        }

        /// <summary>
        /// Formats a header value so text fields remain parseable by BMS readers.
        /// </summary>
        private static string FormatHeaderValue(string key, string value)
        {
            if (!IsTextHeaderTag(key) || !NeedsQuotedHeaderValue(value))
                return value;

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// Determines whether a header key represents free text that may need quoting.
        /// </summary>
        private static bool IsTextHeaderTag(string key)
        {
            switch (key.ToUpperInvariant())
            {
                case "TITLE":
                case "SUBTITLE":
                case "ARTIST":
                case "SUBARTIST":
                case "GENRE":
                case "COMMENT":
                case "MAKER":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Determines whether a header value contains characters that should be quoted.
        /// </summary>
        private static bool NeedsQuotedHeaderValue(string value)
        {
            return value.Contains("//") || value.Contains("/*") || value.Contains("*/") || value.Contains("\"") || value.Contains("\\");
        }

        /// <summary>
        /// Writes the comment header and the beginning of the BMS header field.
        /// </summary>
        private static void WriteInitialHeader(StreamWriter writer, Chart chart)
        {
            writer.WriteLine("; 1P = " + chart.NoteCount(1).ToString());
            writer.WriteLine("; 2P = " + chart.NoteCount(2).ToString());
            writer.WriteLine("");
            writer.WriteLine("");
            writer.WriteLine("* ----------------------HEADER FIELD");
            writer.WriteLine("");
        }

        /// <summary>
        /// Normalizes generated header tags that must be controlled by the writer.
        /// </summary>
        private static void PrepareGeneratedHeaderTags(Chart chart, bool useLongNoteChannels)
        {
            chart.Tags["BPM"] = Math.Round((double)(chart.DefaultBPM), 3).ToString();
            chart.Tags.Remove("LNOBJ");
            chart.Tags.Remove("LNTYPE");
            if (useLongNoteChannels)
                chart.Tags["LNTYPE"] = "1";
        }

        /// <summary>
        /// Writes expansion metadata such as gauge total and preview audio.
        /// </summary>
        private void WriteExpansionHeader(StreamWriter writer, Chart chart)
        {
            writer.WriteLine("");
            writer.WriteLine("");
            writer.WriteLine("*---------------------- EXPANSION FIELD");
            int noteCount = chart.NoteCount(1) + chart.NoteCount(2);
            double gauge = CalculateTotalGauge(noteCount);
            writer.WriteLine("#TOTAL " + gauge);
            writer.WriteLine("#PREVIEW preview." + SoundExtension);
        }

        /// <summary>
        /// Writes BMS movie tags and emits the preload BGA event used for video delay.
        /// </summary>
        private static void WriteVideoTagsAndDelay(Chart chart, StreamWriter expansionWriter, StreamWriter bodyWriter, ref int delayPoint)
        {
            if (!chart.Tags.ContainsKey("VIDEO") || !chart.useMovie)
                return;

            string bga = chart.Tags["VIDEO"];
            string[] extensions = { ".wmv", ".mp4" };
            foreach (string extension in extensions)
            {
                string movieFile = chart.movieFolder + bga + extension;
                if (File.Exists(movieFile))
                {
                    string bgaPath = chart.isSameFolderMovie ? bga + extension : "..\\..\\movie\\" + bga + extension;
                    expansionWriter.WriteLine("#BMP01 " + bgaPath);
                    expansionWriter.WriteLine("#VIDEOFILE " + bgaPath);
                    expansionWriter.WriteLine("#MOVIE " + bgaPath);
                }
            }

            if (!chart.Tags.ContainsKey("VIDEODELAY"))
                return;

            double videoDelay = Int32.Parse(chart.Tags["VIDEODELAY"]);
            expansionWriter.WriteLine("#VIDEODLY " + videoDelay.ToString());
            int section = 0;
            double bpm = Math.Round((double)(chart.DefaultBPM), 3);
            if (videoDelay < 0)
            {
                section = (int)Math.Round(bpm * videoDelay / chart.quantizeNotes * (chart.quantizeNotes / 192.0f) * 2.25f * 1.3125f, MidpointRounding.AwayFromZero) + chart.quantizeNotes * 2;
                delayPoint = 2;
                if (section >= chart.quantizeNotes)
                {
                    section -= chart.quantizeNotes;
                    delayPoint = 1;
                }
            }
            else
            {
                section = (int)Math.Round(bpm * videoDelay / chart.quantizeNotes * (chart.quantizeNotes / 192.0f) * 2.25f, MidpointRounding.AwayFromZero);
            }

            string bgaStringData = "#00004:";
            int commonDivisor = GetCommonDivisor(section, chart.quantizeNotes);
            int num = chart.quantizeNotes / commonDivisor;
            int sec = section;
            if (section != 0)
                sec = section / commonDivisor;
            for (int i = 0; i < num; i++)
            {
                bgaStringData += sec == i ? "01" : "00";
            }

            bodyWriter.WriteLine(bgaStringData);
        }

        /// <summary>
        /// Writes the optional render auto tip preview event and returns the generated WAV tag prefix.
        /// </summary>
        private static string WriteRenderAutoTipPreview(Chart chart, StreamWriter bodyWriter, int measure)
        {
            if (!chart.Tags.ContainsKey("ISUSERENDERAUTOTIP") || !System.Convert.ToBoolean(chart.Tags["ISUSERENDERAUTOTIP"]))
                return "";

            bodyWriter.WriteLine("#" + FormatMeasureNumber(measure) + "01:01");
            return "0001-" + chart.Tags["PLAYER"] + chart.Tags["DIFFICULTY"];
        }

        /// <summary>
        /// Applies the legacy MSS conversion that keeps very short scratch spans representable in BMS.
        /// </summary>
        private static void ApplyMssSupport(Chart chart)
        {
            List<Entry> newList = new List<Entry>();
            List<Entry> mssList = new List<Entry>();
            foreach (Entry entry in chart.Entries)
            {
                if (entry.IsMss)
                    mssList.Add(entry);
                else
                    newList.Add(entry);
            }
            if (mssList.Count > 0)
            {
                mssList.Sort();
                Entry previous = mssList.First();
                mssList.RemoveAt(0);

                foreach (Entry entry in mssList)
                {
                    Fraction linearOffset = entry.LinearOffset - previous.LinearOffset;
                    if (((double)linearOffset) <= 1)
                    {
                        Fraction pak = (entry.LinearOffset / (new Fraction(entry.MetricMeasure, 1) + entry.MetricOffset) / new Fraction(192, 1)) * new Fraction(4, 3);
                        if (entry.Freeze)
                            entry.LinearOffset = previous.LinearOffset - pak;
                        else
                            previous.LinearOffset = entry.LinearOffset - pak;

                        newList.Add(previous);
                    }
                    else
                    {
                        newList.Add(previous);
                    }
                    previous = entry;
                }
                newList.Add(previous);

                chart.Entries = newList;
                chart.CalculateMetricOffsets();
            }
        }

        /// <summary>
        /// Copies entries for one measure into a reusable buffer.
        /// </summary>
        private static void CollectMeasureEntries(Chart chart, int currentMeasure, List<Entry> measureEntries)
        {
            measureEntries.Clear();
            foreach (Entry entry in chart.Entries)
            {
                if (entry.MetricMeasure == currentMeasure)
                    measureEntries.Add(entry);
                else if (entry.MetricMeasure > currentMeasure)
                    break;
            }
        }

        /// <summary>
        /// Formats a BMS measure number as a three-digit string.
        /// </summary>
        private static string FormatMeasureNumber(int measure)
        {
            string measureString = measure.ToString();
            while (measureString.Length < 3)
                measureString = "0" + measureString;
            return measureString;
        }

        /// <summary>
        /// Writes chart tags after generated WAV/BMP/BPM/STOP metadata has been prepared.
        /// </summary>
        private static void WriteChartHeaderTags(StreamWriter headerWriter, Chart chart, string commonBellPath, string soundExtension)
        {
            foreach (KeyValuePair<string, string> tag in chart.Tags)
            {
                if (tag.Value != null && tag.Value.Length > 0)
                {
                    if (tag.Key == "VIDEO" || tag.Key == "VIDEODELAY" || tag.Key == "KEYSET" || tag.Key == "ISUSERENDERAUTOTIP")
                        continue;
                    if (commonBellPath != "" && tag.Value.Contains("0000." + soundExtension))
                    {
                        WriteHeaderTag(headerWriter, tag.Key, commonBellPath);
                        continue;
                    }

                    WriteHeaderTag(headerWriter, tag.Key, tag.Value);
                }
                else
                {
                    WriteHeaderTag(headerWriter, tag.Key, null);
                }
            }
        }

        /// <summary>
        /// Writes non-standard measure lengths while applying any video delay offset.
        /// </summary>
        private static void WriteMeasureLengths(StreamWriter writer, Chart chart, int delayPoint)
        {
            foreach (KeyValuePair<int, Fraction> ml in chart.MeasureLengths)
            {
                if ((double)ml.Value != 1)
                {
                    string line = FormatMeasureNumber(ml.Key + delayPoint);
                    line = "#" + line + "02:" + ((double)ml.Value).ToString();
                    writer.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Flushes the buffered BMS sections to the target stream in header, expansion, and body order.
        /// </summary>
        private static void FlushBmsOutput(BinaryWriter writer, StreamWriter headerWriter, StreamWriter expansionWriter, StreamWriter bodyWriter, MemoryStream header, MemoryStream expansion, MemoryStream body)
        {
            headerWriter.Flush();
            expansionWriter.Flush();
            bodyWriter.Flush();
            writer.Write(header.ToArray());
            writer.Write(expansion.ToArray());
            writer.Write(body.ToArray());
            writer.Flush();
        }

        private struct BmsLaneDefinition
        {
            public EntryType Type;
            public int Player;
            public int Column;
            public string Lane;
        }

        /// <summary>
        /// Determines whether an operation index is a reserved placeholder channel.
        /// </summary>
        private static bool IsSkippedLaneOperation(int operation)
        {
            return operation == 8 || operation == 9;
        }

        /// <summary>
        /// Converts the BMS writer operation index into an entry filter and channel string.
        /// </summary>
        private static bool TryGetBmsLaneDefinition(int operation, out BmsLaneDefinition lane)
        {
            lane = new BmsLaneDefinition() { Type = EntryType.Invalid, Player = 0, Column = 0, Lane = "00" };
            if (operation == 1)
                return SetLane(out lane, EntryType.Tempo, 0, 0, "08");
            if (operation == 2)
                return SetLane(out lane, EntryType.Stop, 0, 0, "09");
            if (operation >= 3 && operation <= 6)
                return SetLane(out lane, EntryType.BGA, 0, operation - 3, GetBgaLane(operation - 3));
            if (operation == 7)
                return SetLane(out lane, EntryType.Marker, 0, 0, "01");
            if (operation >= 10 && operation <= 45)
                return TryGetPlayerMarkerLane(operation, out lane);
            return false;
        }

        /// <summary>
        /// Creates a BMS lane definition value.
        /// </summary>
        private static bool SetLane(out BmsLaneDefinition lane, EntryType type, int player, int column, string laneString)
        {
            lane = new BmsLaneDefinition() { Type = type, Player = player, Column = column, Lane = laneString };
            return true;
        }

        /// <summary>
        /// Gets the BMS BGA channel used by the internal BGA column.
        /// </summary>
        private static string GetBgaLane(int column)
        {
            string[] lanes = { "04", "07", "06", "0A" };
            return lanes[column];
        }

        /// <summary>
        /// Converts player marker operation indexes into visible or long-note lane definitions.
        /// </summary>
        private static bool TryGetPlayerMarkerLane(int operation, out BmsLaneDefinition lane)
        {
            int group = (operation - 10) / 9;
            int index = (operation - 10) % 9;
            int player = (group % 2) + 1;
            bool longNote = group >= 2;
            int[] columns = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            string[] visible1P = { "11", "12", "13", "14", "15", "18", "19", "16", "17" };
            string[] visible2P = { "21", "22", "23", "24", "25", "28", "29", "26", "27" };
            string[] long1P = { "51", "52", "53", "54", "55", "58", "59", "56", "57" };
            string[] long2P = { "61", "62", "63", "64", "65", "68", "69", "66", "67" };
            string laneString = SelectPlayerLane(player, longNote, index, visible1P, visible2P, long1P, long2P);
            return SetLane(out lane, EntryType.Marker, player, columns[index], laneString);
        }

        /// <summary>
        /// Selects the BMS channel string for a player marker lane.
        /// </summary>
        private static string SelectPlayerLane(int player, bool longNote, int index, string[] visible1P, string[] visible2P, string[] long1P, string[] long2P)
        {
            if (player == 1)
                return longNote ? long1P[index] : visible1P[index];
            return longNote ? long2P[index] : visible2P[index];
        }

        /// <summary>
        /// Adds entries from the current measure that match the selected BMS lane.
        /// </summary>
        private static void CollectMatchingLaneEntries(List<Entry> measureEntries, List<Entry> entries, int currentMeasure, BmsLaneDefinition lane, bool useLongNoteChannels, HashSet<Entry> longNoteEntries)
        {
            foreach (Entry entry in measureEntries)
            {
                if (entry.MetricMeasure == currentMeasure && entry.Player == lane.Player && entry.Type == lane.Type && entry.Column == lane.Column && !entry.Used)
                {
                    if (ShouldSkipForLongNoteLane(entry, lane.Lane, useLongNoteChannels, longNoteEntries))
                        continue;
                    entries.Add(entry);
                }
            }
        }

        /// <summary>
        /// Determines whether an entry belongs to the opposite visible/long-note lane family.
        /// </summary>
        private static bool ShouldSkipForLongNoteLane(Entry entry, string laneString, bool useLongNoteChannels, HashSet<Entry> longNoteEntries)
        {
            if (!useLongNoteChannels || entry.Type != EntryType.Marker || entry.Player <= 0)
                return false;
            bool isLongNoteLane = IsLongNoteLane(laneString);
            bool isLongNoteEntry = longNoteEntries.Contains(entry);
            return isLongNoteLane != isLongNoteEntry;
        }
        /// <summary>
        /// Writes one BMS channel line for the selected lane entries and reports collisions that need a repeat pass.
        /// </summary>
        private bool WriteLaneEntries(List<Entry> entries, EntryType currentType, int currentPlayer, string laneString, string measureString, Dictionary<int, Fraction> bpmMap, Dictionary<int, Fraction> stopMap, StreamWriter headerWriter, StreamWriter bodyWriter, ref int bpmCount)
        {
            long common = GetCommonDenominator(entries);
            long commonDivisor = GetLimitedCommonDivisor(common, 7680);
            int[] values = new int[common / commonDivisor];
            bool repeat = false;
            bool write = FillLaneValues(values, entries, currentType, common, commonDivisor, bpmMap, stopMap, headerWriter, ref bpmCount, ref repeat);
            if (write)
                WriteBmsChannelLine(bodyWriter, measureString, laneString, values, bmsObjectBase);
            return repeat;
        }

        /// <summary>
        /// Finds the shared denominator needed to represent all entry offsets in a BMS channel.
        /// </summary>
        private static long GetCommonDenominator(List<Entry> entries)
        {
            long common = 1;
            for (int i = 0; i < 2; i++)
                foreach (Entry entry in entries)
                {
                    long denom = entry.MetricOffset.Denominator;
                    if (denom <= 0)
                        continue;
                    if (common % denom != 0 && common <= int.MaxValue)
                    {
                        long newCommon = common * denom;
                        if (newCommon < common)
                            return common;
                        common = newCommon;
                    }
                }
            return common;
        }

        /// <summary>
        /// Calculates a divisor that keeps generated channel arrays within a practical length.
        /// </summary>
        private static long GetLimitedCommonDivisor(long common, long divisorLimit)
        {
            long commonDivisor = 1;
            while ((common / commonDivisor) > divisorLimit)
                commonDivisor *= 2;
            return commonDivisor;
        }

        /// <summary>
        /// Fills the channel value array for marker, STOP, BPM, or raw BMS entries.
        /// </summary>
        private bool FillLaneValues(int[] values, List<Entry> entries, EntryType currentType, long common, long commonDivisor, Dictionary<int, Fraction> bpmMap, Dictionary<int, Fraction> stopMap, StreamWriter headerWriter, ref int bpmCount, ref bool repeat)
        {
            if (currentType == EntryType.Marker)
                return FillMarkerValues(values, entries, common, commonDivisor, ref repeat);
            if (currentType == EntryType.Stop)
                return FillStopValues(values, entries, common, commonDivisor, stopMap, headerWriter, bmsObjectBase, ref repeat);
            if (currentType == EntryType.Tempo)
                return FillTempoValues(values, entries, common, commonDivisor, bpmMap, headerWriter, bmsObjectBase, ref bpmCount, ref repeat);
            return FillRawValues(values, entries, common, commonDivisor, ref repeat);
        }

        /// <summary>
        /// Fills sample marker values and registers their remapped #WAV indexes.
        /// </summary>
        private bool FillMarkerValues(int[] values, List<Entry> entries, long common, long commonDivisor, ref bool repeat)
        {
            bool write = false;
            foreach (Entry entry in entries)
            {
                int offset = GetValueOffset(entry, common, commonDivisor);
                int entryMapIndex = RegisterSampleValue((int)(double)entry.Value);
                if (TryPlaceValue(values, offset, entryMapIndex, entry, ref repeat))
                    write = true;
            }
            return write;
        }

        /// <summary>
        /// Fills STOP values and emits #STOP table definitions for new values.
        /// </summary>
        private static bool FillStopValues(int[] values, List<Entry> entries, long common, long commonDivisor, Dictionary<int, Fraction> stopMap, StreamWriter headerWriter, int bmsObjectBase, ref bool repeat)
        {
            bool write = false;
            foreach (Entry entry in entries)
            {
                int offset = GetValueOffset(entry, common, commonDivisor);
                int entryIndex = RegisterValue(stopMap, entry.Value);
                headerWriter.WriteLine("#STOP" + Util.ConvertToBMSObjectString(entryIndex, 2, bmsObjectBase) + " " + Math.Round((double)entry.Value, 6).ToString());
                if (TryPlaceValue(values, offset, entryIndex, entry, ref repeat))
                    write = true;
            }
            return write;
        }

        /// <summary>
        /// Fills BPM table values and emits #BPM table definitions for new values.
        /// </summary>
        private static bool FillTempoValues(int[] values, List<Entry> entries, long common, long commonDivisor, Dictionary<int, Fraction> bpmMap, StreamWriter headerWriter, int bmsObjectBase, ref int bpmCount, ref bool repeat)
        {
            bool write = false;
            foreach (Entry entry in entries)
            {
                int offset = GetValueOffset(entry, common, commonDivisor);
                int entryIndex = GetOrRegisterBpm(bpmMap, headerWriter, entry.Value, bmsObjectBase);
                bpmCount = Math.Max(bpmCount, entryIndex);
                if (TryPlaceValue(values, offset, entryIndex, entry, ref repeat))
                    write = true;
            }
            return write;
        }

        /// <summary>
        /// Fills direct numeric channel values for BGA and other non-table entries.
        /// </summary>
        private static bool FillRawValues(int[] values, List<Entry> entries, long common, long commonDivisor, ref bool repeat)
        {
            bool write = false;
            foreach (Entry entry in entries)
            {
                int offset = GetValueOffset(entry, common, commonDivisor);
                int value = (int)(entry.Value.Numerator / entry.Value.Denominator);
                if (TryPlaceValue(values, offset, value, entry, ref repeat))
                    write = true;
            }
            return write;
        }

        /// <summary>
        /// Registers a sample value and returns the BMS object index used in channel data.
        /// </summary>
        private int RegisterSampleValue(int sampleIndex)
        {
            if (!reSampleMap.ContainsKey(sampleIndex))
                reSampleMap.Add(sampleIndex, reSampleMap.Count());
            reSampleMap.TryGetValue(sampleIndex, out sampleIndex);
            return sampleIndex + 1;
        }

        /// <summary>
        /// Gets an existing BPM table index or emits a new #BPM definition.
        /// </summary>
        private static int GetOrRegisterBpm(Dictionary<int, Fraction> bpmMap, StreamWriter headerWriter, Fraction value, int bmsObjectBase)
        {
            foreach (KeyValuePair<int, Fraction> bpmEntry in bpmMap)
                if (bpmEntry.Value == value)
                    return bpmEntry.Key;
            int entryIndex = RegisterValue(bpmMap, value);
            headerWriter.WriteLine("#BPM" + Util.ConvertToBMSObjectString(entryIndex, 2, bmsObjectBase) + " " + Math.Round((double)value, 3).ToString());
            return entryIndex;
        }

        /// <summary>
        /// Converts an entry metric offset into a channel array index.
        /// </summary>
        private static int GetValueOffset(Entry entry, long common, long commonDivisor)
        {
            long multiplier = common / entry.MetricOffset.Denominator;
            return (int)((entry.MetricOffset.Numerator * multiplier) / commonDivisor);
        }

        /// <summary>
        /// Places a channel value unless another event already occupies the same position.
        /// </summary>
        private static bool TryPlaceValue(int[] values, int offset, int value, Entry entry, ref bool repeat)
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
        /// Reduces and writes a BMS channel line to the body stream.
        /// </summary>
        private static void WriteBmsChannelLine(StreamWriter bodyWriter, string measureString, string laneString, int[] values, int bmsObjectBase)
        {
            StringBuilder builder = new StringBuilder();
            values = Reduce(values);
            builder.Append("#" + measureString + laneString + ":");
            for (int i = 0; i < values.Length; i++)
                builder.Append(Util.ConvertToBMSObjectString(values[i], 2, bmsObjectBase));
            bodyWriter.WriteLine(builder.ToString());
        }
        /// <summary>
        /// Writes all measure channel data and returns the optional render auto tip WAV name.
        /// </summary>
        private string WriteChannelData(Chart chart, int delayPoint, Dictionary<int, Fraction> bpmMap, Dictionary<int, Fraction> stopMap, HashSet<Entry> longNoteEntries, bool useLongNoteChannels, StreamWriter headerWriter, StreamWriter bodyWriter, ref int bpmCount)
        {
            chart.ClearUsed();
            int currentMeasure = 0;
            int currentOperation = 0;
            int measureCount = chart.Measures;
            bool repeat = false;
            List<Entry> measureEntries = new List<Entry>();
            List<Entry> entries = new List<Entry>();
            EntryType currentType = EntryType.Invalid;
            int currentColumn = -1;
            int currentPlayer = -1;
            string laneString = "";
            string measureString = "";
            string rendarWavName = WriteRenderAutoTipPreview(chart, bodyWriter, currentMeasure + delayPoint);
            if (rendarWavName.Length > 0)
                reSampleMap.Add(1, 0);

            ApplyMssSupport(chart);

            while (currentMeasure < measureCount)
            {
                if (!repeat)
                {
                    entries.Clear();
                    currentType = EntryType.Invalid;
                    currentColumn = 0;
                    currentPlayer = 0;
                    laneString = "00";

                    if (currentOperation == 0)
                    {
                        measureString = FormatMeasureNumber(currentMeasure + delayPoint);
                        CollectMeasureEntries(chart, currentMeasure, measureEntries);
                    }
                    else if (IsSkippedLaneOperation(currentOperation))
                    {
                        currentOperation++;
                        continue;
                    }
                    else
                    {
                        BmsLaneDefinition lane;
                        if (!TryGetBmsLaneDefinition(currentOperation, out lane))
                        {
                            currentOperation = 0;
                            currentMeasure++;
                            continue;
                        }

                        currentType = lane.Type;
                        currentPlayer = lane.Player;
                        currentColumn = lane.Column;
                        laneString = lane.Lane;
                        CollectMatchingLaneEntries(measureEntries, entries, currentMeasure, lane, useLongNoteChannels, longNoteEntries);
                    }
                }

                repeat = false;
                if (entries.Count > 0)
                    repeat = WriteLaneEntries(entries, currentType, currentPlayer, laneString, measureString, bpmMap, stopMap, headerWriter, bodyWriter, ref bpmCount);

                if (!repeat)
                    currentOperation++;
            }

            return rendarWavName;
        }

        /// <summary>
        /// Writes the chart as a BMS-compatible text stream.
        /// </summary>
        public bool Write(Stream target, bool enableBackspinScratch)
        {
            reSampleMap.Clear();
            int DelayPoint = 0;
            string commonBellPath = "";
            Dictionary<int, Fraction> bpmMap = new Dictionary<int, Fraction>();
            Dictionary<int, Fraction> stopMap = new Dictionary<int, Fraction>();
            BinaryWriter writer = new BinaryWriter(target, Encoding.GetEncoding(932));
            Chart chart = charts[0];
            HashSet<Entry> longNoteEntries = BuildLongNoteEntrySet(chart);
            bool useLongNoteChannels = longNoteEntries.Count > 0;
            if (chart.Tags.ContainsKey("COMMONBELLPATH"))
                commonBellPath = chart.Tags["COMMONBELLPATH"];

            MemoryStream header = new MemoryStream();
            MemoryStream expansion = new MemoryStream();
            MemoryStream body = new MemoryStream();

            Encoding outputEncoding = Encoding.GetEncoding(932);
            StreamWriter headerWriter = new StreamWriter(header, outputEncoding);
            StreamWriter expansionWriter = new StreamWriter(expansion, outputEncoding);
            StreamWriter bodyWriter = new StreamWriter(body, outputEncoding);

            WriteInitialHeader(headerWriter, chart);
            PrepareGeneratedHeaderTags(chart, useLongNoteChannels);
            WriteExpansionHeader(expansionWriter, chart);
            WriteVideoTagsAndDelay(chart, expansionWriter, bodyWriter, ref DelayPoint);
            expansionWriter.WriteLine("");
            expansionWriter.WriteLine("");

            int bpmCount = 0;
            string rendarWavName = WriteChannelData(chart, DelayPoint, bpmMap, stopMap, longNoteEntries, useLongNoteChannels, headerWriter, bodyWriter, ref bpmCount);

            string keyset = "0";
            if (chart.Tags.ContainsKey("KEYSET"))
                keyset = chart.Tags["KEYSET"];
            bool isSucces = GenerateReSampleTags(keyset, rendarWavName);
            if (!isSucces)
                return false;

            WriteChartHeaderTags(headerWriter, chart, commonBellPath, SoundExtension);
            expansionWriter.WriteLine("*---------------------- MAIN DATA FIELD");
            expansionWriter.WriteLine("");
            expansionWriter.WriteLine("");
            WriteMeasureLengths(expansionWriter, chart, DelayPoint);
            FlushBmsOutput(writer, headerWriter, expansionWriter, bodyWriter, header, expansion, body);
            return true;
        }
    }
}
