using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scharfrichter.Codec.Charts
{
    public static class PopnPC
    {
        public static Chart Read(Stream source, int maxIndex, int version)
        {
            Chart chart = new Chart();
            BinaryReader memReader = new BinaryReader(source);
            Fraction[,] lastSample = new Fraction[9, 2];

            while (true)
            {
                Entry entry = new Entry();
                if (memReader.BaseStream.Length == memReader.BaseStream.Position)
                {
                    break;
                }

                long eventOffset = memReader.ReadInt32();
                entry.LinearOffset = new Fraction(eventOffset, 1);
                entry.Value = new Fraction(0, 1);

                int eventType = memReader.ReadInt16();
                if (eventType == 0x0645)
                {
                    entry.Type = EntryType.EndOfSong;
                    entry.Player = 1;
                    chart.Entries.Add(entry);
                    break;
                }
                int eventValue = memReader.ReadByte();
                int eventParameter = memReader.ReadByte();
                int scoreLength = 0;
                if (version >= 24)
                {
                    // Addition of long notes from Usaneko
                    scoreLength = memReader.ReadInt32();
                }

                int overflow = eventParameter & 0b00001111;
                //if (eventOffset < 0)
                //{
                //    Console.WriteLine("[" + eventOffset + "] " + eventType.ToString("x4") + " : " + eventValue + "(" + eventParameter + "[" + (eventParameter & 0b00001111) + "])" + "  -  "  + scoreLength);
                //}
                switch (eventType)
                {
                    case 0x0145:
                        entry.Type = EntryType.Marker;
                        eventValue &= 0b00001111;
                        entry.Player = eventValue > 4 ? 2 : 1;
                        entry.Column = eventValue > 4 ? eventValue - 4 : eventValue;
                        entry.Value = lastSample[entry.Column, entry.Player - 1];
                        break;
                    case 0x0245:
                        eventParameter = (eventParameter >> 4);
                        entry.Type = EntryType.Sample;
                        entry.Player = eventParameter > 4 ? 2 : 1;
                        entry.Column = eventParameter > 4 ? eventParameter - 4 : eventParameter;
                        entry.Value = new Fraction(eventValue + overflow * 256, 1);
                        lastSample[entry.Column, entry.Player - 1] = entry.Value;
                        break;
                    case 0x0345:
                        entry.Type = EntryType.Marker;
                        entry.Player = 0;
                        entry.Value = new Fraction(maxIndex == -1 ? 1 : maxIndex, 1);
                        entry.Parameter = (eventParameter >> 4);
                        entry.Column = (eventParameter >> 4);
                        break;
                    case 0x0445:
                        entry.Type = EntryType.Tempo;
                        entry.Value = new Fraction(eventValue, 1);
                        break;
                    case 0x0745:
                        entry.Type = EntryType.Marker;
                        entry.Player = 0;
                        entry.Value = new Fraction(eventValue + overflow * 256, 1);
                        entry.Parameter = (eventParameter >> 4);
                        entry.Column = 0;
                        break;
                    case 0x0845:
                        entry.Type = EntryType.Judgement;
                        entry.Player = 0;
                        entry.Value = new Fraction(eventValue, 1);
                        entry.Parameter = (eventParameter >> 4);
                        break;
                    case 0x0B00:
                        entry.Type = EntryType.Measure;
                        entry.Player = eventParameter + 1;
                        break;
                    default: entry.Type = EntryType.Invalid; break;
                }

                if (entry.Type != EntryType.Invalid)
                    chart.Entries.Add(entry);

                // if there is a value in a marker, it is a freeze
                if (entry.Type == EntryType.Marker && entry.Player > 0 && scoreLength > 0)
                {
                    Entry freezeEntry = new Entry();
                    freezeEntry.Type = EntryType.Marker;
                    freezeEntry.Freeze = true;
                    freezeEntry.Player = entry.Player;
                    freezeEntry.LinearOffset = entry.LinearOffset + new Fraction(scoreLength, 1);
                    freezeEntry.Column = entry.Column;
                    freezeEntry.Value = new Fraction(0, 1);
                    chart.Entries.Add(freezeEntry);
                }
            }

            // sort entries
            chart.Entries.Sort();

            // find the default bpm
            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type == EntryType.Tempo)
                {
                    chart.DefaultBPM = entry.Value;
                    break;
                }
            }
            return chart;
        }

        public static void Write(Stream target, Chart chart)
        {
            // Unsupported
        }
    }
}
