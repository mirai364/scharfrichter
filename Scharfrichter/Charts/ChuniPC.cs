using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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

        public static ChartChuni Read(StreamReader source)
        {
            ChartChuni chart = new ChartChuni();
            ChartChuni metChart = new ChartChuni();
            Fraction[,] lastSample = new Fraction[9, 2];
            string line;
            int resolution = 0;
            int currentMeasure = 0;
            Dictionary<int, int> holdResetPoint = new Dictionary<int, int>();
            Dictionary<int, int> slideResetPoint = new Dictionary<int, int>();
            Dictionary<int, int> airHoldResetPoint = new Dictionary<int, int>();

            Dictionary<Point, List<int>> holdDic = new Dictionary<Point, List<int>>();
            Dictionary<Point, List<int>> slideDic = new Dictionary<Point, List<int>>();
            Dictionary<Point, List<int>> airHoldDic = new Dictionary<Point, List<int>>();
            while ((line = source.ReadLine()) != null)
            {
                string[] parts = line.Split('\t');
                if (parts[0] == "RESOLUTION")
                {
                    resolution = int.Parse(parts[1]);
                    chart.Tags["RESOLUTION"] = resolution.ToString();
                }
                if (parts[0] == "CREATOR")
                {
                    chart.Tags["DESIGNER"] = parts[1];
                }
                if (parts[0].Length != 3)
                {
                    continue;
                }
                if (parts[0] == "CLK")
                {
                    continue;
                }
                currentMeasure = int.Parse(parts[1]);
                int measurePosition = int.Parse(parts[2]);
                int notesPosition = 0;
                int notesWidth = 0;
                if (parts.Count() > 4)
                {
                    notesPosition = int.Parse(parts[3]);
                    int tmp;
                    if (int.TryParse(parts[4], out tmp) == true)
                        notesWidth = tmp;
                }


                List<int> idList = holdResetPoint.Keys.ToList();
                foreach (int id in idList)
                {
                    if (holdResetPoint[id] < currentMeasure * resolution + measurePosition)
                        holdResetPoint.Remove(id);
                }
                idList = slideResetPoint.Keys.ToList();
                foreach (int id in idList)
                {
                    if (slideResetPoint[id] < currentMeasure * resolution + measurePosition)
                        slideResetPoint.Remove(id);
                }
                idList = airHoldResetPoint.Keys.ToList();
                foreach (int id in idList)
                {
                    if (airHoldResetPoint[id] < currentMeasure * resolution + measurePosition)
                        airHoldResetPoint.Remove(id);
                }

                EntryChuni entry = new EntryChuni();
                int eventOffset = 0;
                entry.LinearOffset = new Fraction(eventOffset, 1);
                entry.Value = new Fraction(0, 1);

                switch (parts[0])
                {
                    case "MET":
                        entry.Type = EntryTypeChuni.Event;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Value = new Fraction(int.Parse(parts[4]), int.Parse(parts[3]));
                        chart.Entries.Add(entry);
                        metChart.Entries.Add(entry);
                        break;
                    case "BPM":
                        entry.Type = EntryTypeChuni.Tempo;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Value = new Fraction((int)(double.Parse(parts[3])*1000), 1000);
                        chart.Entries.Add(entry);
                        break;
                    case "TAP":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 1;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(100 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "CHR":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 1;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(200 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "HLD":
                        int startLinearOffset = currentMeasure * resolution + measurePosition;
                        int endLinearOffset = startLinearOffset + int.Parse(parts[5]);
                        int endResetPoint = (int)((Math.Ceiling(endLinearOffset / (double)resolution) + 1) * resolution);

                        int currentIdentifierTmp = 0;
                        for (int i=1; i< 26; i++)
                        {
                            if (!holdResetPoint.ContainsKey(i))
                            {
                                holdResetPoint[i] = endResetPoint;
                                currentIdentifierTmp = i;
                                break;
                            }
                        }

                        Point tmp = new Point() { linearOffset = startLinearOffset, position = notesPosition };
                        if (holdDic.ContainsKey(tmp))
                        {
                            var list = holdDic[tmp];
                            var key = list[0]; list.Remove(key);
                            entry = chart.Entries[key];
                            entry.Value = new Fraction(entry.Value.Numerator + 100, 1);
                            chart.Entries[key] = entry;
                            if (list.Count <= 0)
                            {
                                holdDic.Remove(tmp);
                            } else
                            {
                                holdDic[tmp] = list;
                            }
                            holdResetPoint.Remove(currentIdentifierTmp);
                            currentIdentifierTmp = entry.Identifier;
                        } else
                        {
                            entry.Type = EntryTypeChuni.Marker;
                            entry.Player = 2;
                            entry.LinearOffset = new Fraction(startLinearOffset, 1);
                            entry.Column = notesPosition;
                            entry.Identifier = currentIdentifierTmp;
                            entry.Value = new Fraction(100 + notesWidth, 1);
                            chart.Entries.Add(entry);
                        }

                        // freeze
                        EntryChuni freezeEntry = new EntryChuni();
                        freezeEntry.Type = EntryTypeChuni.Marker;
                        freezeEntry.Player = 2;
                        freezeEntry.LinearOffset = new Fraction(endLinearOffset, 1);
                        freezeEntry.Column = notesPosition;
                        freezeEntry.Identifier = currentIdentifierTmp;
                        freezeEntry.Value = new Fraction(200 + notesWidth, 1);
                        chart.Entries.Add(freezeEntry);
                        tmp = new Point() { linearOffset = endLinearOffset, position = notesPosition};
                        if (holdDic.ContainsKey(tmp))
                        {
                            var list = holdDic[tmp];
                            list.Add(chart.Entries.Count() - 1);
                            holdDic[tmp] = list;
                        }
                        else
                        {
                            var list =new List<int> { chart.Entries.Count() - 1 };
                            holdDic[tmp] = list;
                        }
                        break;
                    case "FLK":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 1;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(300 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "AIR":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(100 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "AUL":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(300 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "AUR":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(400 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "ADW":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(200 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "ADL":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(500 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "ADR":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(600 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "AHD":
                        startLinearOffset = currentMeasure * resolution + measurePosition;
                        if (parts[5] != "AHD")
                        {
                            var anotherEntry = new EntryChuni();
                            anotherEntry.Type = EntryTypeChuni.Marker;
                            anotherEntry.Player = 5;
                            anotherEntry.LinearOffset = new Fraction(startLinearOffset, 1);
                            anotherEntry.Column = notesPosition;
                            anotherEntry.Value = new Fraction(100 + notesWidth, 1);
                            chart.Entries.Add(anotherEntry);
                        }
                        endLinearOffset = startLinearOffset + int.Parse(parts[6]);
                        endResetPoint = (int)((Math.Ceiling(endLinearOffset / (double)resolution) + 1) * resolution);

                        currentIdentifierTmp = 0;
                        for (int i = 1; i < 26; i++)
                        {
                            if (!airHoldResetPoint.ContainsKey(i))
                            {
                                airHoldResetPoint[i] = endResetPoint;
                                currentIdentifierTmp = i;
                                break;
                            }
                        }

                        tmp = new Point() { linearOffset = startLinearOffset, position = notesPosition };
                        if (airHoldDic.ContainsKey(tmp))
                        {
                            var list = airHoldDic[tmp];
                            var key = list[0]; list.Remove(key);
                            entry = chart.Entries[key];
                            entry.Value = new Fraction(entry.Value.Numerator + 100, 1);
                            chart.Entries[key] = entry;
                            if (list.Count <= 0)
                            {
                                airHoldDic.Remove(tmp);
                            }
                            else
                            {
                                airHoldDic[tmp] = list;
                            }
                            airHoldResetPoint.Remove(currentIdentifierTmp);
                            currentIdentifierTmp = entry.Identifier;
                        }
                        else
                        {
                            entry.Type = EntryTypeChuni.Marker;
                            entry.Player = 4;
                            entry.LinearOffset = new Fraction(startLinearOffset, 1);
                            entry.Column = notesPosition;
                            entry.Identifier = currentIdentifierTmp;
                            entry.Value = new Fraction(100 + notesWidth, 1);
                            chart.Entries.Add(entry);
                        }

                        // freeze
                        freezeEntry = new EntryChuni();
                        freezeEntry.Type = EntryTypeChuni.Marker;
                        freezeEntry.Player = 4;
                        freezeEntry.LinearOffset = new Fraction(endLinearOffset, 1);
                        freezeEntry.Column = notesPosition;
                        freezeEntry.Identifier = currentIdentifierTmp;
                        freezeEntry.Value = new Fraction(200 + notesWidth, 1);
                        chart.Entries.Add(freezeEntry);
                        tmp = new Point() { linearOffset = endLinearOffset, position = notesPosition };
                        if (airHoldDic.ContainsKey(tmp))
                        {
                            var list = airHoldDic[tmp];
                            list.Add(chart.Entries.Count() - 1);
                            airHoldDic[tmp] = list;
                        }
                        else
                        {
                            var list = new List<int> { chart.Entries.Count() - 1 };
                            airHoldDic[tmp] = list;
                        }
                        break;
                    case "MNE":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 1;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(400 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "SLC":
                        startLinearOffset = currentMeasure * resolution + measurePosition;
                        endLinearOffset = startLinearOffset + int.Parse(parts[5]);
                        endResetPoint = (int)((Math.Ceiling(endLinearOffset / (double)resolution) + 1) * resolution);

                        currentIdentifierTmp = 0;
                        for (int i = 1; i < 26; i++)
                        {
                            if (!slideResetPoint.ContainsKey(i))
                            {
                                slideResetPoint[i] = endResetPoint;
                                currentIdentifierTmp = i;
                                break;
                            }
                        }

                        tmp = new Point() { linearOffset = startLinearOffset, position = notesPosition };
                        if (slideDic.ContainsKey(tmp))
                        {
                            var list = slideDic[tmp];
                            var key = list[0]; list.Remove(key);
                            entry = chart.Entries[key];
                            entry.Value = new Fraction(entry.Value.Numerator + 100, 1);
                            chart.Entries[key] = entry;
                            if (list.Count <= 0)
                            {
                                slideDic.Remove(tmp);
                            }
                            else
                            {
                                slideDic[tmp] = list;
                            }
                            slideResetPoint.Remove(currentIdentifierTmp);
                            currentIdentifierTmp = entry.Identifier;
                        }
                        else
                        {
                            entry.Type = EntryTypeChuni.Marker;
                            entry.Player = 3;
                            entry.LinearOffset = new Fraction(startLinearOffset, 1);
                            entry.Column = notesPosition;
                            entry.Identifier = currentIdentifierTmp;
                            entry.Value = new Fraction(100 + notesWidth, 1);
                            chart.Entries.Add(entry);
                        }

                        // freeze
                        freezeEntry = new EntryChuni();
                        freezeEntry.Type = EntryTypeChuni.Marker;
                        freezeEntry.Player = 3;
                        freezeEntry.LinearOffset = new Fraction(endLinearOffset, 1);
                        freezeEntry.Column = int.Parse(parts[6]);
                        freezeEntry.Identifier = currentIdentifierTmp;
                        if (parts.Count() > 7)
                        {
                            freezeEntry.Value = new Fraction(400 + int.Parse(parts[7]), 1);
                        } else
                        {
                            freezeEntry.Value = new Fraction(400 + notesWidth, 1);
                        }
                        chart.Entries.Add(freezeEntry);
                        tmp = new Point() { linearOffset = endLinearOffset, position = int.Parse(parts[6]) };
                        if (slideDic.ContainsKey(tmp))
                        {
                            var list = slideDic[tmp];
                            list.Add(chart.Entries.Count() - 1);
                            slideDic[tmp] = list;
                        }
                        else
                        {
                            var list = new List<int> { chart.Entries.Count() - 1 };
                            slideDic[tmp] = list;
                        }
                        break;
                    case "SLD":
                        startLinearOffset = currentMeasure * resolution + measurePosition;
                        endLinearOffset = startLinearOffset + int.Parse(parts[5]);
                        endResetPoint = (int)((Math.Ceiling(endLinearOffset / (double)resolution) + 1) * resolution);

                        currentIdentifierTmp = 0;
                        for (int i = 1; i < 26; i++)
                        {
                            if (!slideResetPoint.ContainsKey(i))
                            {
                                slideResetPoint[i] = endResetPoint;
                                currentIdentifierTmp = i;
                                break;
                            }
                        }

                        tmp = new Point() { linearOffset = startLinearOffset, position = notesPosition };
                        if (slideDic.ContainsKey(tmp))
                        {
                            var list = slideDic[tmp];
                            var key = list[0]; list.Remove(key);
                            entry = chart.Entries[key];
                            entry.Value = new Fraction(entry.Value.Numerator + 100, 1);
                            chart.Entries[key] = entry;
                            if (list.Count <= 0)
                            {
                                slideDic.Remove(tmp);
                            }
                            else
                            {
                                slideDic[tmp] = list;
                            }
                            slideResetPoint.Remove(currentIdentifierTmp);
                            currentIdentifierTmp = entry.Identifier;
                        }
                        else
                        {
                            entry.Type = EntryTypeChuni.Marker;
                            entry.Player = 3;
                            entry.LinearOffset = new Fraction(startLinearOffset, 1);
                            entry.Column = notesPosition;
                            entry.Identifier = currentIdentifierTmp;
                            entry.Value = new Fraction(100 + notesWidth, 1);
                            chart.Entries.Add(entry);
                        }

                        // freeze
                        freezeEntry = new EntryChuni();
                        freezeEntry.Type = EntryTypeChuni.Marker;
                        freezeEntry.Player = 3;
                        freezeEntry.LinearOffset = new Fraction(endLinearOffset, 1);
                        freezeEntry.Column = int.Parse(parts[6]);
                        freezeEntry.Identifier = currentIdentifierTmp;
                        if (parts.Count() > 7)
                        {
                            freezeEntry.Value = new Fraction(200 + int.Parse(parts[7]), 1);
                        }
                        else
                        {
                            freezeEntry.Value = new Fraction(200 + notesWidth, 1);
                        }
                        chart.Entries.Add(freezeEntry);
                        tmp = new Point() { linearOffset = endLinearOffset, position = int.Parse(parts[6]) };
                        if (slideDic.ContainsKey(tmp))
                        {
                            var list = slideDic[tmp];
                            list.Add(chart.Entries.Count() - 1);
                            slideDic[tmp] = list;
                        }
                        else
                        {
                            var list = new List<int> { chart.Entries.Count() - 1 };
                            slideDic[tmp] = list;
                        }
                        break;
                    case "SFL":
                        startLinearOffset = currentMeasure * resolution + measurePosition;
                        endLinearOffset = startLinearOffset + notesPosition;
                        entry = new EntryChuni();
                        entry.LinearOffset = new Fraction(startLinearOffset, 1);
                        entry.Type = EntryTypeChuni.Event;
                        entry.Player = 1;
                        double speed = double.Parse(parts[4]);
                        if (speed == 0.0f)
                        {
                            entry.Value = new Fraction(0, 0);
                        } else
                        {
                            entry.Value = new Fraction((long)(speed * 1000000), 1000000);
                        }
                        chart.Entries.Add(entry);

                        freezeEntry = new EntryChuni();
                        freezeEntry.LinearOffset = new Fraction(endLinearOffset, 1);
                        freezeEntry.Type = EntryTypeChuni.Event;
                        freezeEntry.Player = 1;
                        freezeEntry.Value = new Fraction(1, 1);
                        chart.Entries.Add(freezeEntry);
                        break;
                    case "STP":
                        startLinearOffset = currentMeasure * resolution + measurePosition;
                        endLinearOffset = startLinearOffset + int.Parse(parts[3]);
                        entry = new EntryChuni();
                        entry.LinearOffset = new Fraction(startLinearOffset, 1);
                        entry.Type = EntryTypeChuni.Event;
                        entry.Player = 1;
                        entry.Value = new Fraction(0, 0);
                        chart.Entries.Add(entry);

                        freezeEntry = new EntryChuni();
                        freezeEntry.LinearOffset = new Fraction(endLinearOffset, 1);
                        freezeEntry.Type = EntryTypeChuni.Event;
                        freezeEntry.Player = 1;
                        freezeEntry.Value = new Fraction(1, 1);
                        chart.Entries.Add(freezeEntry);
                        break;
                    default:
                        Console.WriteLine("There is a sign that has not been defined: " + parts[0]);
                        break;
                }
            }

            // calc measure
            int calcMesure = 0;
            float calcbeat = 1.0f;
            int nextChartCount = 0;
            for (int i = 0; i <= currentMeasure + metChart.Entries.Count + 10; i++)
            {
                if (metChart.Entries.Count > nextChartCount && (int)(double)metChart.Entries[nextChartCount].LinearOffset == calcMesure)
                {
                    if (metChart.Entries[nextChartCount].Value.Denominator != 0 &&
                        metChart.Entries[nextChartCount].Value.Numerator != 0)
                    {
                        calcbeat = (float)metChart.Entries[nextChartCount].Value;
                    }
                    else
                    {
                        calcbeat = 1.0f;
                    }
                    nextChartCount++;
                }
                EntryChuni entry = new EntryChuni();
                calcMesure += (int)(resolution * calcbeat);
                entry.LinearOffset = new Fraction(calcMesure, 1);
                entry.Type = EntryTypeChuni.Measure;
                entry.Player = 1;
                entry.Value = new Fraction(0, 1);
                chart.Entries.Add(entry);
            }

            // sort entries
            chart.Entries.Sort();

            // find the default bpm
            foreach (var entry in chart.Entries)
            {
                if (entry.Type == EntryTypeChuni.Tempo)
                {
                    chart.DefaultBPM = entry.Value;
                    break;
                }
            }
            return chart;
        }

        public static void Write(Stream target, ChartChuni chart)
        {
            // Unsupported
        }
    }
}
