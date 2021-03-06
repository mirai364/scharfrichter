﻿using System;
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
            Fraction[,] lastSample = new Fraction[9, 2];
            string line;
            int resolution = 0;
            int currentMeasure = 0;
            resetPoint holdResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            resetPoint slideResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };
            resetPoint airHolddResetPoint = new resetPoint() { resetLinearOffset = 0, currentIdentifier = 0 };

            Dictionary<Point, List<int>> holdDic = new Dictionary<Point, List<int>>();
            Dictionary<Point, List<int>> slideDic = new Dictionary<Point, List<int>>();
            Dictionary<Point, List<int>> airHoldDic = new Dictionary<Point, List<int>>();
            while ((line = source.ReadLine()) != null)
            {
                string[] parts = line.Split('\t');
                if (parts[0] == "RESOLUTION")
                {
                    resolution = int.Parse(parts[1]);
                }
                if (parts[0] == "CREATOR")
                {
                    chart.Tags["DESIGNER"] = parts[1];
                }
                if (parts[0].Length != 3)
                {
                    continue;
                }
                if (parts[0] == "MET")
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


                EntryChuni entry = new EntryChuni();
                int eventOffset = 0;
                entry.LinearOffset = new Fraction(eventOffset, 1);
                entry.Value = new Fraction(0, 1);

                switch (parts[0])
                {
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
                        entry.Value = new Fraction(10 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "CHR":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 1;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(20 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "HLD":
                        int startLinearOffset = currentMeasure * resolution + measurePosition;
                        int endLinearOffset = startLinearOffset + int.Parse(parts[5]);
                        int endResetPoint = endLinearOffset; // (int)((Math.Ceiling(endLinearOffset / (double)resolution) + 1) * resolution);
                        if (startLinearOffset <= holdResetPoint.resetLinearOffset)
                        {
                            holdResetPoint.currentIdentifier++;
                            holdResetPoint.resetLinearOffset = Math.Max(holdResetPoint.resetLinearOffset, endResetPoint);
                        }
                        else
                        {
                            holdResetPoint.currentIdentifier = 0;
                            holdResetPoint.resetLinearOffset = endResetPoint;
                        }
                        int currentIdentifierTmp = holdResetPoint.currentIdentifier;

                        Point tmp = new Point() { linearOffset = startLinearOffset, position = notesPosition };
                        if (holdDic.ContainsKey(tmp))
                        {
                            var list = holdDic[tmp];
                            var key = list[0]; list.Remove(key);
                            entry = chart.Entries[key];
                            entry.Value = new Fraction(entry.Value.Numerator + 10, 1);
                            chart.Entries[key] = entry;
                            if (list.Count <= 0)
                            {
                                holdDic.Remove(tmp);
                            } else
                            {
                                holdDic[tmp] = list;
                            }
                            currentIdentifierTmp = entry.Identifier;
                            holdResetPoint.currentIdentifier--;
                        } else
                        {
                            entry.Type = EntryTypeChuni.Marker;
                            entry.Player = 2;
                            entry.LinearOffset = new Fraction(startLinearOffset, 1);
                            entry.Column = notesPosition;
                            entry.Identifier = currentIdentifierTmp;
                            entry.Value = new Fraction(10 + notesWidth, 1);
                            chart.Entries.Add(entry);
                        }

                        // freeze
                        EntryChuni freezeEntry = new EntryChuni();
                        freezeEntry.Type = EntryTypeChuni.Marker;
                        freezeEntry.Player = 2;
                        freezeEntry.LinearOffset = new Fraction(endLinearOffset, 1);
                        freezeEntry.Column = notesPosition;
                        freezeEntry.Identifier = currentIdentifierTmp;
                        freezeEntry.Value = new Fraction(20 + notesWidth, 1);
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
                        entry.Value = new Fraction(30 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "AIR":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(10 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "AUL":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(30 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "AUR":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(40 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "ADW":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(20 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "ADL":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(50 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "ADR":
                        entry.Type = EntryTypeChuni.Marker;
                        entry.Player = 5;
                        entry.LinearOffset = new Fraction(currentMeasure * resolution + measurePosition, 1);
                        entry.Column = notesPosition;
                        entry.Value = new Fraction(60 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "AHD":
                        startLinearOffset = currentMeasure * resolution + measurePosition;
                        endLinearOffset = startLinearOffset + int.Parse(parts[6]);
                        endResetPoint = endLinearOffset; // (int)((Math.Ceiling(endLinearOffset / (double)resolution) + 1) * resolution);
                        if (startLinearOffset <= airHolddResetPoint.resetLinearOffset)
                        {
                            airHolddResetPoint.currentIdentifier++;
                            airHolddResetPoint.resetLinearOffset = Math.Max(airHolddResetPoint.resetLinearOffset, endResetPoint);
                        }
                        else
                        {
                            airHolddResetPoint.currentIdentifier = 0;
                            airHolddResetPoint.resetLinearOffset = endResetPoint;
                        }
                        currentIdentifierTmp = airHolddResetPoint.currentIdentifier;

                        tmp = new Point() { linearOffset = startLinearOffset, position = notesPosition };
                        if (airHoldDic.ContainsKey(tmp))
                        {
                            var list = airHoldDic[tmp];
                            var key = list[0]; list.Remove(key);
                            entry = chart.Entries[key];
                            entry.Value = new Fraction(entry.Value.Numerator + 10, 1);
                            chart.Entries[key] = entry;
                            if (list.Count <= 0)
                            {
                                airHoldDic.Remove(tmp);
                            }
                            else
                            {
                                airHoldDic[tmp] = list;
                            }
                            currentIdentifierTmp = entry.Identifier;
                            airHolddResetPoint.currentIdentifier--;
                        }
                        else
                        {
                            entry.Type = EntryTypeChuni.Marker;
                            entry.Player = 4;
                            entry.LinearOffset = new Fraction(startLinearOffset, 1);
                            entry.Column = notesPosition;
                            entry.Identifier = currentIdentifierTmp;
                            entry.Value = new Fraction(10 + notesWidth, 1);
                            chart.Entries.Add(entry);
                        }

                        // freeze
                        freezeEntry = new EntryChuni();
                        freezeEntry.Type = EntryTypeChuni.Marker;
                        freezeEntry.Player = 4;
                        freezeEntry.LinearOffset = new Fraction(endLinearOffset, 1);
                        freezeEntry.Column = notesPosition;
                        freezeEntry.Identifier = currentIdentifierTmp;
                        freezeEntry.Value = new Fraction(20 + notesWidth, 1);
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
                        entry.Value = new Fraction(40 + notesWidth, 1);
                        chart.Entries.Add(entry);
                        break;
                    case "SLC":
                        startLinearOffset = currentMeasure * resolution + measurePosition;
                        endLinearOffset = startLinearOffset + int.Parse(parts[5]);
                        endResetPoint = endLinearOffset; // (int)((Math.Ceiling(endLinearOffset / (double)resolution) + 1) * resolution);
                        if (startLinearOffset <= slideResetPoint.resetLinearOffset)
                        {
                            slideResetPoint.currentIdentifier++;
                            slideResetPoint.resetLinearOffset = Math.Max(slideResetPoint.resetLinearOffset, endResetPoint);
                        }
                        else
                        {
                            slideResetPoint.currentIdentifier = 0;
                            slideResetPoint.resetLinearOffset = endResetPoint;
                        }
                        currentIdentifierTmp = slideResetPoint.currentIdentifier;

                        tmp = new Point() { linearOffset = startLinearOffset, position = notesPosition };
                        if (slideDic.ContainsKey(tmp))
                        {
                            var list = slideDic[tmp];
                            var key = list[0]; list.Remove(key);
                            entry = chart.Entries[key];
                            entry.Value = new Fraction(entry.Value.Numerator + 10, 1);
                            chart.Entries[key] = entry;
                            if (list.Count <= 0)
                            {
                                slideDic.Remove(tmp);
                            }
                            else
                            {
                                slideDic[tmp] = list;
                            }
                            currentIdentifierTmp = entry.Identifier;
                            slideResetPoint.currentIdentifier--;
                        }
                        else
                        {
                            entry.Type = EntryTypeChuni.Marker;
                            entry.Player = 3;
                            entry.LinearOffset = new Fraction(startLinearOffset, 1);
                            entry.Column = notesPosition;
                            entry.Identifier = currentIdentifierTmp;
                            entry.Value = new Fraction(10 + notesWidth, 1);
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
                            freezeEntry.Value = new Fraction(40 + int.Parse(parts[7]), 1);
                        } else
                        {
                            freezeEntry.Value = new Fraction(40 + notesWidth, 1);
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
                        endResetPoint = endLinearOffset; // (int)((Math.Ceiling(endLinearOffset / (double)resolution) + 1) * resolution);
                        if (startLinearOffset <= slideResetPoint.resetLinearOffset)
                        {
                            slideResetPoint.currentIdentifier++;
                            slideResetPoint.resetLinearOffset = Math.Max(slideResetPoint.resetLinearOffset, endResetPoint);
                        }
                        else
                        {
                            slideResetPoint.currentIdentifier = 0;
                            slideResetPoint.resetLinearOffset = endResetPoint;
                        }
                        currentIdentifierTmp = slideResetPoint.currentIdentifier;

                        tmp = new Point() { linearOffset = startLinearOffset, position = notesPosition };
                        if (slideDic.ContainsKey(tmp))
                        {
                            var list = slideDic[tmp];
                            var key = list[0]; list.Remove(key);
                            entry = chart.Entries[key];
                            entry.Value = new Fraction(entry.Value.Numerator + 10, 1);
                            chart.Entries[key] = entry;
                            if (list.Count <= 0)
                            {
                                slideDic.Remove(tmp);
                            }
                            else
                            {
                                slideDic[tmp] = list;
                            }
                            currentIdentifierTmp = entry.Identifier;
                            slideResetPoint.currentIdentifier--;
                        }
                        else
                        {
                            entry.Type = EntryTypeChuni.Marker;
                            entry.Player = 3;
                            entry.LinearOffset = new Fraction(startLinearOffset, 1);
                            entry.Column = notesPosition;
                            entry.Identifier = currentIdentifierTmp;
                            entry.Value = new Fraction(10 + notesWidth, 1);
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
                            freezeEntry.Value = new Fraction(20 + int.Parse(parts[7]), 1);
                        }
                        else
                        {
                            freezeEntry.Value = new Fraction(20 + notesWidth, 1);
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
                        string TIL00 = "";
                        if (chart.Tags.ContainsKey("TIL00"))
                        {
                            TIL00 = chart.Tags["TIL00"] + ", ";
                        }

                        if (measurePosition == 0)
                        {
                            chart.Tags["TIL00"] = TIL00 + currentMeasure + "'0:" + double.Parse(parts[4]) + ", ";
                        } else
                        {
                            int calc = (int)((480.0 * 4) * ((double)measurePosition / (double)resolution));
                            chart.Tags["TIL00"] = TIL00 + currentMeasure + "'" + calc.ToString() + ":" +double.Parse(parts[4]) + ", ";
                        }

                        int nextMeasure = (int)Math.Floor(((double)currentMeasure * (double)resolution + (double)measurePosition + (double)notesPosition) / (double)resolution);
                        int culcPosition = currentMeasure * resolution + measurePosition + notesPosition - nextMeasure * resolution;
                        if (culcPosition == 0)
                        {
                            chart.Tags["TIL00"] += nextMeasure + "'0:1.0";
                        }
                        else
                        {
                            int calc = (int)((480.0 * 4) * ((double)culcPosition / (double)resolution));
                            chart.Tags["TIL00"] += nextMeasure + "'" + calc.ToString() + ":1.0";
                        }
                        chart.Tags["HISPEED"] = "00";
                        break;
                    default:
                        Console.WriteLine("There is a sign that has not been defined");
                        break;
                }
            }

            for (int i=0; i <= currentMeasure + 3; i++)
            {
                EntryChuni entry = new EntryChuni();
                int eventOffset = i * resolution;
                entry.LinearOffset = new Fraction(eventOffset, 1);
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
