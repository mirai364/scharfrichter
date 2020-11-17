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

        private Chart[] charts = new Chart[] { null };
        private int[] sampleMap;
        private Dictionary<int, int> reSampleMap = new Dictionary<int, int>();

        public BMS()
        {
            ResetSampleMap();
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

        public void GenerateSampleMap()
        {
            int[] usedSamples = charts[0].UsedSamples();
            SampleMap = usedSamples;
        }

        public bool GenerateReSampleTags(string keyset = "0", string rendarWavName = "")
        {
            Chart chart = charts[0];
            string targetFolder;
            if (keyset == "0")
            {
                targetFolder = "sounds\\";
            }
            else
            {
                targetFolder = "sounds_" + keyset + "\\";
            }

            foreach (KeyValuePair<int, int> pair in reSampleMap)
            {
                if (pair.Value > 1293)
                {
                    Console.WriteLine("WARNING: More than 1293 samples");
                    return false;
                }
                chart.Tags["WAV" + Util.ConvertToBMEString(pair.Value + 1, 2)] = targetFolder + Util.ConvertToBMEString(pair.Key, 4) + ".wav";
                //Console.WriteLine("WAV" + Util.ConvertToBMEString(pair.Value + 1, 2) + " " + targetFolder + Util.ConvertToBMEString(pair.Key, 4) + ".wav");
            }

            if (rendarWavName.Length > 0)
            {
                chart.Tags["WAV01"] = targetFolder + rendarWavName + ".wav";
            }
            return true;
        }

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
                            case "07": currentPlayer = 0; currentType = EntryType.BGA; currentColumn = 3; break;
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

        public void ResetSampleMap()
        {
            sampleMap = new int[1295];
            for (int i = 0; i < 1295; i++)
            {
                sampleMap[i] = i;
            }
        }

        public int[] SampleMap
        {
            get
            {
                return sampleMap;
            }
            set
            {
                int usedSampleCount = value.Length;

                if (usedSampleCount > 1293)
                    usedSampleCount = 1293;

                Array.Copy(value, 0, sampleMap, 1, usedSampleCount);
                for (int i = usedSampleCount + 1; i < 1294; i++)
                {
                    sampleMap[i] = 0;
                }
            }
        }

        private int getCommonDivisor(int value, int quantizeNotes)
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

        public bool Write(Stream target, bool enableBackspinScratch)
        {
            int DelayPoint = 0;
            bool idUseRenderAutoTip = false;
            string commonBellPath = "";
            Dictionary<int, Fraction> bpmMap = new Dictionary<int, Fraction>();
            BinaryWriter writer = new BinaryWriter(target, Encoding.GetEncoding(932));
            Chart chart = charts[0];
            if (chart.Tags.ContainsKey("COMMONBELLPATH"))
            {
                commonBellPath = chart.Tags["COMMONBELLPATH"];
            }

            MemoryStream header = new MemoryStream();
            MemoryStream expansion = new MemoryStream();
            MemoryStream body = new MemoryStream();

            StreamWriter headerWriter = new StreamWriter(header);
            StreamWriter expansionWriter = new StreamWriter(expansion);
            StreamWriter bodyWriter = new StreamWriter(body);

            // note count header. this can assist people tagging.
            headerWriter.WriteLine("; 1P = " + chart.NoteCount(1).ToString());
            headerWriter.WriteLine("; 2P = " + chart.NoteCount(2).ToString());
            headerWriter.WriteLine("");
            headerWriter.WriteLine("");
            headerWriter.WriteLine("* ----------------------HEADER FIELD");
            headerWriter.WriteLine("");

            // create BPM metadata
            chart.Tags["BPM"] = Math.Round((double)(chart.DefaultBPM), 3).ToString();
            // write all metadata
            chart.Tags["LNOBJ"] = "ZZ";

            // EXPANSION FIELD
            expansionWriter.WriteLine("");
            expansionWriter.WriteLine("");
            expansionWriter.WriteLine("*---------------------- EXPANSION FIELD");
            expansionWriter.WriteLine("#PREVIEW preview.wav");
            if (chart.Tags.ContainsKey("VIDEO"))
            {
                if (chart.isSameFolderMovie)
                {
                    expansionWriter.WriteLine("#BMP01 " + chart.Tags["VIDEO"] + ".wmv");
                }
                else
                {
                    expansionWriter.WriteLine("#BMP01 ..\\..\\Movie\\" + chart.Tags["VIDEO"] + ".wmv");
                }
                if (chart.Tags.ContainsKey("VIDEODELAY"))
                {
                    double videoDelay = Int32.Parse(chart.Tags["VIDEODELAY"]);
                    int section = 0;
                    double BPM = Math.Round((double)(chart.DefaultBPM), 3);
                    if (videoDelay < 0)
                    {
                        section = (int)Math.Round(BPM * videoDelay / chart.quantizeNotes * (chart.quantizeNotes / 192.0f) * 2.25f * 1.3125f, MidpointRounding.AwayFromZero) + chart.quantizeNotes * 2;
                        DelayPoint = 2;
                        if (section >= chart.quantizeNotes)
                        {
                            section -= chart.quantizeNotes;
                            DelayPoint = 1;
                        }
                    }
                    else
                    {
                        section = (int)Math.Round(BPM * videoDelay / chart.quantizeNotes * (chart.quantizeNotes / 192.0f) * 2.25f, MidpointRounding.AwayFromZero);
                    }
                    string BGAstringData = "#00004:";
                    int commonDivisor = getCommonDivisor(section, chart.quantizeNotes);
                    int num = chart.quantizeNotes / commonDivisor;
                    int sec = section;
                    if (section != 0) sec = section / commonDivisor;
                    for (int i = 0; i < num; i++)
                    {
                        if (sec == i)
                        {
                            BGAstringData += "01";
                        }
                        else
                        {
                            BGAstringData += "00";
                        }

                    }
                    bodyWriter.WriteLine(BGAstringData);
                }
            }
            expansionWriter.WriteLine("");
            expansionWriter.WriteLine("");

            if (chart.Tags.ContainsKey("ISUSERENDERAUTOTIP"))
            {
                idUseRenderAutoTip = System.Convert.ToBoolean(chart.Tags["ISUSERENDERAUTOTIP"]);
            }
            chart.ClearUsed();

            // iterate through all events
            int currentMeasure = 0;
            int currentOperation = 0;
            int measureCount = chart.Measures;
            int bpmCount = 0;
            bool repeat = false;
            List<Entry> measureEntries = new List<Entry>();
            List<Entry> entries = new List<Entry>();
            EntryType currentType = EntryType.Invalid;
            int currentColumn = -1;
            int currentPlayer = -1;
            string laneString = "";
            string measureString = "";
            string rendarWavName = "";
            if (idUseRenderAutoTip)
            {
                reSampleMap.Add(1, 0);
                int tmpCurrentMeasure = currentMeasure + DelayPoint;
                measureString = tmpCurrentMeasure.ToString();
                while (measureString.Length < 3)
                    measureString = "0" + measureString;
                bodyWriter.WriteLine("#" + measureString + "01:01");
                measureString = "";
                rendarWavName = "0001-" + chart.Tags["PLAYER"] + chart.Tags["DIFFICULTY"];
            }

            while (currentMeasure < measureCount)
            {
                bool write = false;

                if (!repeat)
                {
                    entries.Clear();
                    currentType = EntryType.Invalid;
                    currentColumn = 0;
                    currentPlayer = 0;
                    laneString = "00";

                    switch (currentOperation)
                    {
                        case 00:
                            measureEntries.Clear();

                            int tmpCurrentMeasure = currentMeasure + DelayPoint;
                            measureString = tmpCurrentMeasure.ToString();
                            while (measureString.Length < 3)
                                measureString = "0" + measureString;

                            foreach (Entry entry in chart.Entries)
                            {
                                if (entry.MetricMeasure == currentMeasure)
                                    measureEntries.Add(entry);
                                else if (entry.MetricMeasure > currentMeasure)
                                    break;
                            }

                            break;
                        case 01: currentType = EntryType.Tempo; currentPlayer = 0; currentColumn = 0; laneString = "08"; break;
                        case 02: currentType = EntryType.BGA; currentPlayer = 0; currentColumn = 0; laneString = "04"; break;
                        case 03: currentType = EntryType.BGA; currentPlayer = 0; currentColumn = 1; laneString = "05"; break;
                        case 04: currentType = EntryType.BGA; currentPlayer = 0; currentColumn = 2; laneString = "06"; break;
                        case 05: currentType = EntryType.BGA; currentPlayer = 0; currentColumn = 3; laneString = "07"; break;
                        case 06: currentType = EntryType.Marker; currentPlayer = 0; currentColumn = 0; laneString = "01"; break;
                        case 07: currentOperation++; continue; // placeholders
                        case 08: currentOperation++; continue;
                        case 09: currentOperation++; continue;
                        case 10: currentType = EntryType.Marker; currentPlayer = 1; currentColumn = 0; laneString = "11"; break;
                        case 11: currentType = EntryType.Marker; currentPlayer = 1; currentColumn = 1; laneString = "12"; break;
                        case 12: currentType = EntryType.Marker; currentPlayer = 1; currentColumn = 2; laneString = "13"; break;
                        case 13: currentType = EntryType.Marker; currentPlayer = 1; currentColumn = 3; laneString = "14"; break;
                        case 14: currentType = EntryType.Marker; currentPlayer = 1; currentColumn = 4; laneString = "15"; break;
                        case 15: currentType = EntryType.Marker; currentPlayer = 1; currentColumn = 5; laneString = "18"; break;
                        case 16: currentType = EntryType.Marker; currentPlayer = 1; currentColumn = 6; laneString = "19"; break;
                        case 17: currentType = EntryType.Marker; currentPlayer = 1; currentColumn = 7; laneString = "16"; break;
                        case 18: currentType = EntryType.Marker; currentPlayer = 1; currentColumn = 8; laneString = "17"; break;
                        case 19: currentType = EntryType.Marker; currentPlayer = 2; currentColumn = 0; laneString = "21"; break;
                        case 20: currentType = EntryType.Marker; currentPlayer = 2; currentColumn = 1; laneString = "22"; break;
                        case 21: currentType = EntryType.Marker; currentPlayer = 2; currentColumn = 2; laneString = "23"; break;
                        case 22: currentType = EntryType.Marker; currentPlayer = 2; currentColumn = 3; laneString = "24"; break;
                        case 23: currentType = EntryType.Marker; currentPlayer = 2; currentColumn = 4; laneString = "25"; break;
                        case 24: currentType = EntryType.Marker; currentPlayer = 2; currentColumn = 5; laneString = "28"; break;
                        case 25: currentType = EntryType.Marker; currentPlayer = 2; currentColumn = 6; laneString = "29"; break;
                        case 26: currentType = EntryType.Marker; currentPlayer = 2; currentColumn = 7; laneString = "26"; break;
                        case 27: currentType = EntryType.Marker; currentPlayer = 2; currentColumn = 8; laneString = "27"; break;
                        default: currentOperation = 0; currentMeasure++; continue;
                    }

                    // separate events we'll use
                    foreach (Entry entry in measureEntries)
                    {
                        if (entry.MetricMeasure == currentMeasure &&
                            entry.Player == currentPlayer &&
                            entry.Type == currentType &&
                            entry.Column == currentColumn &&
                            !entry.Used)
                        {
                            entries.Add(entry);

#if (false)
                            // a little hack for backspin scratches
                            if (enableBackspinScratch && entry.Column == 7 && entry.Type == EntryType.Marker && entry.Freeze == true)
                            {
                                Entry backspinEntry = new Entry();
                                backspinEntry.Column = entry.Column;
                                backspinEntry.Freeze = false;
                                backspinEntry.MetricMeasure = entry.MetricMeasure;
                                backspinEntry.MetricOffset = entry.MetricOffset;
                                backspinEntry.Parameter = 0;
                                backspinEntry.Player = entry.Player;
                                backspinEntry.Type = EntryType.Marker;
                                backspinEntry.Used = false;
                                backspinEntry.Value = new Fraction(1295, 1);
                                entries.Add(backspinEntry);
                            }
#endif
                        }
                    }
                }

                repeat = false;

                // build a line if necessary
                if (entries.Count > 0)
                {
                    // get common denominator
                    long common = 1;

                    for (int i = 0; i < 2; i++)
                    {
                        foreach (Entry entry in entries)
                        {
                            if (common % entry.MetricOffset.Denominator != 0 && common <= int.MaxValue)
                            {
                                common *= entry.MetricOffset.Denominator;
                            }
                        }
                    }

                    // prevent outrageous common denominator values here
                    long commonDivisor = 1;
                    long divisorLimit;

                    // use reasonable quantization to prevent crazy values
                    divisorLimit = 7680;

                    while (true)
                    {
                        if ((common / commonDivisor) <= divisorLimit)
                            break;
                        commonDivisor *= 2;
                    }

                    // build line
                    int[] values = new int[common / commonDivisor];

                    if (currentType == EntryType.Marker && currentPlayer != 0)
                    {
                        // player key
                        foreach (Entry entry in entries)
                        {
                            long multiplier = common / entry.MetricOffset.Denominator;
                            long offset = (entry.MetricOffset.Numerator * multiplier) / commonDivisor;
                            int count = values.Length;
                            int entryMapIndex = (int)(double)entry.Value;

                            if (!reSampleMap.ContainsKey(entryMapIndex))
                                reSampleMap.Add(entryMapIndex, reSampleMap.Count());
                            reSampleMap.TryGetValue(entryMapIndex, out entryMapIndex);
                            ++entryMapIndex;

                            if (offset >= 0 && offset < count && !entry.Used)
                            {
                                if (values[offset] != 0)
                                {
                                    repeat = true;
                                }
                                else
                                {
                                    if (entry.Freeze)
                                        values[offset] = 1295;
                                    else
                                        values[offset] = entryMapIndex;
                                    write = true;
                                    entry.Used = true;
                                }
                            }
                        }
                    }
                    else if (currentType == EntryType.Marker && currentPlayer == 0)
                    {
                        // bgm
                        foreach (Entry entry in entries)
                        {
                            long multiplier = common / entry.MetricOffset.Denominator;
                            long offset = (entry.MetricOffset.Numerator * multiplier) / commonDivisor;
                            int count = values.Length;
                            int entryMapIndex = (int)(double)entry.Value;

                            if (!reSampleMap.ContainsKey(entryMapIndex))
                                reSampleMap.Add(entryMapIndex, reSampleMap.Count());
                            reSampleMap.TryGetValue(entryMapIndex, out entryMapIndex);
                            ++entryMapIndex;

                            if (offset >= 0 && offset < count && !entry.Used)
                            {
                                if (values[offset] == 0)
                                {
                                    values[offset] = entryMapIndex;
                                    entry.Used = true;
                                    write = true;
                                }
                                else
                                {
                                    repeat = true;
                                }
                            }
                        }
                    }
                    else if (currentType == EntryType.Tempo)
                    {
                        foreach (Entry entry in entries)
                        {
                            long multiplier = common / entry.MetricOffset.Denominator;
                            long offset = (entry.MetricOffset.Numerator * multiplier) / commonDivisor;
                            //long offset = (entry.MetricOffset.Numerator * common) / entry.MetricOffset.Denominator;
                            int count = values.Length;

                            if (offset >= 0 && offset < count && !entry.Used)
                            {
                                if (values[offset] == 0)
                                {
                                    int entryIndex = -1;

                                    foreach (KeyValuePair<int, Fraction> bpmEntry in bpmMap)
                                    {
                                        if (bpmEntry.Value == entry.Value)
                                        {
                                            entryIndex = bpmEntry.Key;
                                            break;
                                        }
                                    }

                                    if (entryIndex <= 0)
                                    {
                                        bpmCount++;

                                        // this is a hack to make the numbers decimal
                                        if (bpmCount % 36 == 10)
                                            bpmCount += 26;

                                        headerWriter.WriteLine("#BPM" + Util.ConvertToBMEString(bpmCount, 2) + " " + (Math.Round((double)(entry.Value), 3)).ToString());
                                        entryIndex = bpmCount;
                                        bpmMap[entryIndex] = entry.Value;
                                    }
                                    values[offset] = entryIndex;
                                    entry.Used = true;
                                    write = true;
                                }
                                else
                                {
                                    repeat = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (Entry entry in entries)
                        {
                            long multiplier = common / entry.MetricOffset.Denominator;
                            long offset = (entry.MetricOffset.Numerator * multiplier) / commonDivisor;
                            //long offset = (entry.MetricOffset.Numerator * common) / entry.MetricOffset.Denominator;
                            int count = values.Length;

                            if (offset >= 0 && offset < count && !entry.Used)
                            {
                                if (values[offset] == 0)
                                {
                                    values[offset] = (int)(entry.Value.Numerator / entry.Value.Denominator);
                                    entry.Used = true;
                                    write = true;
                                }
                                else
                                {
                                    repeat = true;
                                }
                            }
                        }
                    }

                    if (write)
                    {
                        StringBuilder builder = new StringBuilder();
                        values = Reduce(values);
                        int length = values.Length;
                        builder.Append("#" + measureString + laneString + ":");

                        for (int i = 0; i < length; i++)
                        {
                            builder.Append(Util.ConvertToBMEString(values[i], 2));
                        }

                        bodyWriter.WriteLine(builder.ToString());
                    }
                }

                if (!repeat)
                    currentOperation++;
            }

            string keyset = "0";
            if (chart.Tags.ContainsKey("KEYSET"))
                keyset = chart.Tags["KEYSET"];
            bool isSucces = GenerateReSampleTags(keyset, rendarWavName);
            if (!isSucces)
                return false;

            foreach (KeyValuePair<string, string> tag in chart.Tags)
            {
                if (tag.Value != null && tag.Value.Length > 0)
                {
                    if (tag.Key == "VIDEO" || tag.Key == "VIDEODELAY" || tag.Key == "KEYSET" || tag.Key == "ISUSERENDERAUTOTIP")
                        continue;
                    if (commonBellPath != "" && tag.Value.Contains("0000.wav"))
                    {
                        headerWriter.WriteLine("#" + tag.Key + " " + commonBellPath);
                        continue;
                    }

                    headerWriter.WriteLine("#" + tag.Key + " " + tag.Value);
                }
                else
                {
                    headerWriter.WriteLine("#" + tag.Key);
                }
            }

            expansionWriter.WriteLine("*---------------------- MAIN DATA FIELD");
            expansionWriter.WriteLine("");
            expansionWriter.WriteLine("");
            // write measure lengths
            foreach (KeyValuePair<int, Fraction> ml in chart.MeasureLengths)
            {
                if ((double)ml.Value != 1)
                {
                    int tmpCurrentMeasure = ml.Key + DelayPoint;
                    string line = tmpCurrentMeasure.ToString();
                    while (line.Length < 3)
                        line = "0" + line;

                    line = "#" + line + "02:" + ((double)ml.Value).ToString();
                    expansionWriter.WriteLine(line);
                }
            }
            
            // finalize data and dump to stream
            headerWriter.Flush();
            expansionWriter.Flush();
            bodyWriter.Flush();
            writer.Write(header.ToArray());
            writer.Write(expansion.ToArray());
            writer.Write(body.ToArray());
            writer.Flush();
            return true;
        }
    }
}
