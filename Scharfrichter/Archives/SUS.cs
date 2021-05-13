using Scharfrichter.Codec.Charts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Scharfrichter.Codec.Archives
{
    public class SUS : Archive
    {
        public ChartChuni chart;

        public SUS() { }

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

        public bool Write(Stream target, bool enableBackspinScratch)
        {
            int DelayPoint = 0;
            Dictionary<int, Fraction> bpmMap = new Dictionary<int, Fraction>();
            BinaryWriter writer = new BinaryWriter(target, Encoding.GetEncoding(932));
            MemoryStream header = new MemoryStream();
            MemoryStream shortNote = new MemoryStream();
            MemoryStream hold = new MemoryStream();
            MemoryStream slide = new MemoryStream();
            MemoryStream airHold = new MemoryStream();
            MemoryStream air = new MemoryStream();

            StreamWriter headerWriter = new StreamWriter(header);
            StreamWriter shortNoteWriter = new StreamWriter(shortNote); shortNoteWriter.WriteLine("");  shortNoteWriter.WriteLine("ShortNote");
            StreamWriter holdWriter = new StreamWriter(hold);           holdWriter.WriteLine("");       holdWriter.WriteLine("Hold");
            StreamWriter slideWriter = new StreamWriter(slide);         slideWriter.WriteLine("");      slideWriter.WriteLine("Slide");
            StreamWriter airHoldWriter = new StreamWriter(airHold);     airHoldWriter.WriteLine("");    airHoldWriter.WriteLine("AirHold");
            StreamWriter airWriter = new StreamWriter(air);             airWriter.WriteLine("");        airWriter.WriteLine("Air");

            // create BPM metadata
            chart.Tags["BPM"] = Math.Round((double)(chart.DefaultBPM), 3).ToString();
            // note count header. this can assist people tagging.
            string WAVE = "music.wav";
            string WAVEOFFSET = "0";
            string JACKET = "jacket.jpg";

            headerWriter.WriteLine("Music info");
            headerWriter.WriteLine("#TITLE \"" + chart.Tags["TITLE"] + "\"");
            headerWriter.WriteLine("#ARTIST \"" + chart.Tags["ARTIST"] + "\"");
            headerWriter.WriteLine("#DESIGNER \"" + chart.Tags["DESIGNER"] + "\"");
            headerWriter.WriteLine("#DIFFICULTY " + chart.Tags["TYPE"]);
            headerWriter.WriteLine("#PLAYLEVEL " + chart.Tags["PLAYLEVEL"]);
            headerWriter.WriteLine("#SONGID \"" + chart.Tags["ID"] + "\"");
            headerWriter.WriteLine("#WAVE \"" + WAVE + "\"");
            headerWriter.WriteLine("#WAVEOFFSET " + WAVEOFFSET);
            headerWriter.WriteLine("#JACKET \"" + JACKET + "\"");
            headerWriter.WriteLine("#BASEBPM " + chart.Tags["BPM"]);
            headerWriter.WriteLine("");
            headerWriter.WriteLine("Request");
            headerWriter.WriteLine("#REQUEST \"mertonome enabled\"");
            headerWriter.WriteLine("#REQUEST \"ticks_per_beat 480\"");
            headerWriter.WriteLine("");
            headerWriter.WriteLine("BPM");

            // iterate through all events
            int currentMeasure = 0;
            int currentOperation = 0;
            int measureCount = chart.Measures;
            int bpmCount = 0;
            bool repeat = false;
            List<EntryChuni> measureEntries = new List<EntryChuni>();
            List<EntryChuni> entries = new List<EntryChuni>();
            EntryTypeChuni currentType = EntryTypeChuni.Invalid;
            int currentColumn = -1;
            int currentPlayer = -1;
            string laneString = "";
            string measureString = "";

            while (currentMeasure < measureCount)
            {
                bool write = false;

                if (!repeat)
                {
                    entries.Clear();
                    currentType = EntryTypeChuni.Invalid;
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

                            foreach (var entry in chart.Entries)
                            {
                                if (entry.MetricMeasure == currentMeasure)
                                    measureEntries.Add(entry);
                                else if (entry.MetricMeasure > currentMeasure)
                                    break;
                            }

                            break;
                        // ShortNote
                        case 1: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 0; laneString = "10"; break;
                        case 2: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 1; laneString = "11"; break;
                        case 3: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 2; laneString = "12"; break;
                        case 4: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 3; laneString = "13"; break;
                        case 5: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 4; laneString = "14"; break;
                        case 6: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 5; laneString = "15"; break;
                        case 7: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 6; laneString = "16"; break;
                        case 8: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 7; laneString = "17"; break;
                        case 9: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 8; laneString = "18"; break;
                        case 10: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 9; laneString = "19"; break;
                        case 11: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 10; laneString = "1A"; break;
                        case 12: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 11; laneString = "1B"; break;
                        case 13: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 12; laneString = "1C"; break;
                        case 14: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 13; laneString = "1D"; break;
                        case 15: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 14; laneString = "1E"; break;
                        case 16: currentType = EntryTypeChuni.Marker; currentPlayer = 1; currentColumn = 15; laneString = "1F"; break;
                        // Hold
                        case 17: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 0; laneString = "20"; break;
                        case 18: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 1; laneString = "21"; break;
                        case 19: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 2; laneString = "22"; break;
                        case 20: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 3; laneString = "23"; break;
                        case 21: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 4; laneString = "24"; break;
                        case 22: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 5; laneString = "25"; break;
                        case 23: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 6; laneString = "26"; break;
                        case 24: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 7; laneString = "27"; break;
                        case 25: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 8; laneString = "28"; break;
                        case 26: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 9; laneString = "29"; break;
                        case 27: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 10; laneString = "2A"; break;
                        case 28: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 11; laneString = "2B"; break;
                        case 29: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 12; laneString = "2C"; break;
                        case 30: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 13; laneString = "2D"; break;
                        case 31: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 14; laneString = "2E"; break;
                        case 32: currentType = EntryTypeChuni.Marker; currentPlayer = 2; currentColumn = 15; laneString = "2F"; break;
                        // Slide
                        case 33: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 0; laneString = "30"; break;
                        case 34: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 1; laneString = "31"; break;
                        case 35: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 2; laneString = "32"; break;
                        case 36: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 3; laneString = "33"; break;
                        case 37: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 4; laneString = "34"; break;
                        case 38: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 5; laneString = "35"; break;
                        case 39: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 6; laneString = "36"; break;
                        case 40: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 7; laneString = "37"; break;
                        case 41: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 8; laneString = "38"; break;
                        case 42: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 9; laneString = "39"; break;
                        case 43: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 10; laneString = "3A"; break;
                        case 44: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 11; laneString = "3B"; break;
                        case 45: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 12; laneString = "3C"; break;
                        case 46: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 13; laneString = "3D"; break;
                        case 47: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 14; laneString = "3E"; break;
                        case 48: currentType = EntryTypeChuni.Marker; currentPlayer = 3; currentColumn = 15; laneString = "3F"; break;
                        // AirHold
                        case 49: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 0; laneString = "40"; break;
                        case 50: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 1; laneString = "41"; break;
                        case 51: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 2; laneString = "42"; break;
                        case 52: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 3; laneString = "43"; break;
                        case 53: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 4; laneString = "44"; break;
                        case 54: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 5; laneString = "45"; break;
                        case 55: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 6; laneString = "46"; break;
                        case 56: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 7; laneString = "47"; break;
                        case 57: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 8; laneString = "48"; break;
                        case 58: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 9; laneString = "49"; break;
                        case 59: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 10; laneString = "4A"; break;
                        case 60: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 11; laneString = "4B"; break;
                        case 61: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 12; laneString = "4C"; break;
                        case 62: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 13; laneString = "4D"; break;
                        case 63: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 14; laneString = "4E"; break;
                        case 64: currentType = EntryTypeChuni.Marker; currentPlayer = 4; currentColumn = 15; laneString = "4F"; break;
                        // Air
                        case 65: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 0; laneString = "50"; break;
                        case 66: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 1; laneString = "51"; break;
                        case 67: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 2; laneString = "52"; break;
                        case 68: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 3; laneString = "53"; break;
                        case 69: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 4; laneString = "54"; break;
                        case 70: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 5; laneString = "55"; break;
                        case 71: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 6; laneString = "56"; break;
                        case 72: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 7; laneString = "57"; break;
                        case 73: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 8; laneString = "58"; break;
                        case 74: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 9; laneString = "59"; break;
                        case 75: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 10; laneString = "5A"; break;
                        case 76: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 11; laneString = "5B"; break;
                        case 77: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 12; laneString = "5C"; break;
                        case 78: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 13; laneString = "5D"; break;
                        case 79: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 14; laneString = "5E"; break;
                        case 80: currentType = EntryTypeChuni.Marker; currentPlayer = 5; currentColumn = 15; laneString = "5F"; break;
                        case 81: currentType = EntryTypeChuni.Tempo; currentPlayer = 0; currentColumn = 0; laneString = "08"; break;
                        default: currentOperation = 0; currentMeasure++; continue;
                    }

                    // separate events we'll use
                    foreach (var entry in measureEntries)
                    {
                        if (entry.MetricMeasure == currentMeasure &&
                            entry.Player == currentPlayer &&
                            entry.Type == currentType &&
                            entry.Column == currentColumn &&
                            !entry.Used)
                        {
                            entries.Add(entry);
                        }
                    }
                }

                repeat = false;

                // build a line if necessary
                if (entries.Count > 0)
                {
                    int loopCount = 1;
                    if (currentPlayer != 1 && currentPlayer != 5)
                    {
                        loopCount = 35;
                    }
                    for (int loop = 0;loop < loopCount; loop++)
                    {
                        List<EntryChuni> entriesTmp = new List<EntryChuni>();
                        string laneStringTmp = laneString;
                        if (currentPlayer != 1 && currentPlayer != 5 && currentPlayer != 0)
                            laneStringTmp += Util.ConvertToBMEString(loop, 1);

                        // separate events we'll use
                        foreach (var entry in entries)
                        {
                            if (entry.Identifier == loop)
                            {
                                entriesTmp.Add(entry);
                            }
                        }
                        if (entriesTmp.Count <= 0)
                        {
                            continue;
                        }

                        // get common denominator
                        long common = 1;

                        for (int i = 0; i < 2; i++)
                        {
                            foreach (var entry in entriesTmp)
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

                        if (currentType == EntryTypeChuni.Marker && currentPlayer != 0)
                        {
                            // player key
                            foreach (var entry in entriesTmp)
                            {
                                long multiplier = common / entry.MetricOffset.Denominator;
                                long offset = (entry.MetricOffset.Numerator * multiplier) / commonDivisor;
                                int count = values.Length;
                                int entryMapIndex = (int)(double)entry.Value;

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
                        else if (currentType == EntryTypeChuni.Tempo)
                        {
                            foreach (var entry in entries)
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
                                            headerWriter.WriteLine("#BPM" + bpmCount.ToString("00") + ":" + (Math.Round((double)(entry.Value), 3)).ToString());
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
                            foreach (var entry in entriesTmp)
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
                            builder.Append("#" + measureString + laneStringTmp + ":");

                            for (int i = 0; i < length; i++)
                            {
                                builder.Append(values[i].ToString("00"));
                            }

                            switch (currentPlayer)
                            {
                                case 0:
                                    headerWriter.WriteLine(builder.ToString()); break;
                                case 1:
                                    shortNoteWriter.WriteLine(builder.ToString()); break;
                                case 2:
                                    holdWriter.WriteLine(builder.ToString()); break;
                                case 3:
                                    slideWriter.WriteLine(builder.ToString()); break;
                                case 4:
                                    airHoldWriter.WriteLine(builder.ToString()); break;
                                case 5:
                                    airWriter.WriteLine(builder.ToString()); break;
                            }
                            
                        }
                    }
                }

                if (!repeat)
                    currentOperation++;
            }

            headerWriter.WriteLine("");
            headerWriter.WriteLine("Measure's pulse");
            headerWriter.WriteLine("#00002: 4");

            if (chart.Tags.ContainsKey("TIL00"))
            {
                headerWriter.WriteLine("");
                headerWriter.WriteLine("#TIL00: " + "\"" + chart.Tags["TIL00"] + "\"");
                headerWriter.WriteLine("#HISPEED 00");
            }

            // finalize data and dump to stream
            headerWriter.Flush();
            shortNoteWriter.Flush();
            holdWriter.Flush();
            slideWriter.Flush();
            airHoldWriter.Flush();
            airWriter.Flush();

            writer.Write(header.ToArray());
            writer.Write(shortNote.ToArray());
            writer.Write(hold.ToArray());
            writer.Write(slide.ToArray());
            writer.Write(airHold.ToArray());
            writer.Write(air.ToArray());
            writer.Flush();
            return true;
        }
    }
}
