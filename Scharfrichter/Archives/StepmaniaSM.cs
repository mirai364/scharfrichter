using Scharfrichter.Codec.Charts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Scharfrichter.Codec.Archives
{
    public class StepmaniaSM : Archive
    {
        public Dictionary<string, string> Tags = new Dictionary<string, string>();

        private class SMNoteEntry
        {
            public int Column;
            public int Measure;
            public string NoteChar;
            public int Offset;

            /// <summary>
            /// Creates an empty StepMania note entry.
            /// </summary>
            public SMNoteEntry()
            {
                Measure = 0;
                NoteChar = "0";
                Offset = 0;
            }

            /// <summary>
            /// Converts an internal chart entry into a quantized StepMania note entry.
            /// </summary>
            public SMNoteEntry(Entry source, int quantize)
            {
                if (!source.MetricOffsetInitialized)
                    throw new Exception("Cannot create SM Note entry without metric offset");

                Column = source.Column;
                Measure = source.MetricMeasure;
                Offset = (int)Math.Round((double)(source.MetricOffset * new Fraction(quantize, 1)));
                while (Offset >= quantize)
                {
                    Offset -= quantize;
                    Measure++;
                }

                if (source.Type == EntryType.Mine)
                    NoteChar = "M";
                else if (source.Type == EntryType.Marker)
                    NoteChar = "1";
                else
                    NoteChar = "0";
            }
        }

        /// <summary>
        /// Creates a StepMania NOTES tag from quantized chart entries.
        /// </summary>
        public void CreateStepTag(Entry[] entries, string gameType, string description, string difficulty, string playLevel, string grooveRadar, int panelCount, int quantize)
        {
            string tagName = BuildNotesTagName(gameType, description, difficulty, playLevel, grooveRadar);
            int highestMeasure = entries[entries.Length - 1].MetricMeasure + 2;
            List<SMNoteEntry> noteEntries = BuildNoteEntries(entries, quantize);
            Tags[tagName] = BuildNoteData(noteEntries, highestMeasure, panelCount, quantize);
        }

        /// <summary>
        /// Builds the StepMania NOTES tag name and metadata header.
        /// </summary>
        private static string BuildNotesTagName(string gameType, string description, string difficulty, string playLevel, string grooveRadar)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("NOTES:");
            builder.AppendLine("     " + gameType + ":");
            builder.AppendLine("     " + description + ":");
            builder.AppendLine("     " + difficulty + ":");
            builder.AppendLine("     " + playLevel + ":");
            builder.Append("     " + grooveRadar);
            return builder.ToString();
        }

        /// <summary>
        /// Converts entries into StepMania note entries and links freeze starts to freeze ends.
        /// </summary>
        private static List<SMNoteEntry> BuildNoteEntries(Entry[] entries, int quantize)
        {
            List<SMNoteEntry> noteEntries = new List<SMNoteEntry>();
            Dictionary<int, SMNoteEntry> previousEntries = new Dictionary<int, SMNoteEntry>();

            foreach (Entry entry in entries)
            {
                SMNoteEntry noteEntry = new SMNoteEntry(entry, quantize);
                if (noteEntry.NoteChar == "0")
                    continue;

                UpdateFreezeState(entry, noteEntry, previousEntries);
                noteEntries.Add(noteEntry);
            }

            return noteEntries;
        }

        /// <summary>
        /// Marks matching StepMania freeze starts and ends.
        /// </summary>
        private static void UpdateFreezeState(Entry entry, SMNoteEntry noteEntry, Dictionary<int, SMNoteEntry> previousEntries)
        {
            if (entry.Freeze)
            {
                if (previousEntries.ContainsKey(entry.Column))
                {
                    previousEntries[entry.Column].NoteChar = "2";
                    previousEntries.Remove(entry.Column);
                    noteEntry.NoteChar = "3";
                }
            }
            else
            {
                previousEntries[entry.Column] = noteEntry;
            }
        }

        /// <summary>
        /// Builds all StepMania measure rows for the NOTES tag body.
        /// </summary>
        private static string BuildNoteData(List<SMNoteEntry> noteEntries, int highestMeasure, int panelCount, int quantize)
        {
            StringBuilder builder = new StringBuilder();
            bool firstMeasure = true;

            for (int measure = 0; measure < highestMeasure; measure++)
            {
                List<SMNoteEntry> measureEntries = GetMeasureEntries(noteEntries, measure);
                if (!firstMeasure)
                    builder.Append(",");
                builder.AppendLine("");
                firstMeasure = false;

                if (measureEntries.Count > 0)
                    AppendFilledMeasure(builder, measureEntries, panelCount, quantize);
                else
                    AppendEmptyMeasure(builder, panelCount);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Selects all note entries that belong to one measure.
        /// </summary>
        private static List<SMNoteEntry> GetMeasureEntries(List<SMNoteEntry> noteEntries, int measure)
        {
            List<SMNoteEntry> measureEntries = new List<SMNoteEntry>();
            foreach (SMNoteEntry entry in noteEntries)
            {
                if (entry.Measure == measure)
                    measureEntries.Add(entry);
            }
            return measureEntries;
        }

        /// <summary>
        /// Appends a measure containing one or more notes.
        /// </summary>
        private static void AppendFilledMeasure(StringBuilder builder, List<SMNoteEntry> measureEntries, int panelCount, int quantize)
        {
            int reduction = CalculateMeasureReduction(measureEntries, quantize);
            int subdivisions = Math.Max(quantize / reduction, 1);
            while (subdivisions < 4)
            {
                subdivisions *= 2;
                reduction /= 2;
            }

            string[,] measureChars = CreateMeasureGrid(subdivisions, panelCount);
            foreach (SMNoteEntry entry in measureEntries)
                measureChars[entry.Offset / reduction, entry.Column] = entry.NoteChar;

            AppendMeasureGrid(builder, measureChars, subdivisions, panelCount);
        }

        /// <summary>
        /// Calculates the StepMania row reduction for a measure.
        /// </summary>
        private static int CalculateMeasureReduction(List<SMNoteEntry> measureEntries, int quantize)
        {
            List<int> offsets = new List<int>();
            foreach (SMNoteEntry entry in measureEntries)
                offsets.Add(entry.Offset);
            offsets.Add(quantize);
            return Util.GetLineReductionDivisor(offsets.ToArray());
        }

        /// <summary>
        /// Creates a zero-filled StepMania measure grid.
        /// </summary>
        private static string[,] CreateMeasureGrid(int subdivisions, int panelCount)
        {
            string[,] measureChars = new string[subdivisions, panelCount];
            for (int i = 0; i < subdivisions; i++)
                for (int j = 0; j < panelCount; j++)
                    measureChars[i, j] = "0";
            return measureChars;
        }

        /// <summary>
        /// Appends all rows from a StepMania measure grid.
        /// </summary>
        private static void AppendMeasureGrid(StringBuilder builder, string[,] measureChars, int subdivisions, int panelCount)
        {
            for (int i = 0; i < subdivisions; i++)
            {
                for (int j = 0; j < panelCount; j++)
                    builder.Append(measureChars[i, j]);
                builder.AppendLine();
            }
        }

        /// <summary>
        /// Appends a four-row empty StepMania measure.
        /// </summary>
        private static void AppendEmptyMeasure(StringBuilder builder, int panelCount)
        {
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < panelCount; j++)
                    builder.Append("0");
                builder.AppendLine();
            }
        }

        /// <summary>
        /// Creates DISPLAYBPM, BPMS, and STOPS tags from tempo entries.
        /// </summary>
        public void CreateTempoTags(Entry[] entries)
        {
            StringBuilder bpmTag = new StringBuilder();
            StringBuilder stopTag = new StringBuilder();
            double lowBPM = double.MaxValue;
            double highBPM = double.MinValue;

            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                double offset = Math.Round(((double)entry.MetricOffset + (double)entry.MetricMeasure) * 4f, 3);
                double value = Math.Round((double)entry.Value, 3);

                if (value > 0)
                    AddBpmTagValue(bpmTag, value, offset, ref lowBPM, ref highBPM);
                else if (i < entries.Length - 1)
                    AddStopTagValue(stopTag, entries, i, offset);
            }

            ApplyDisplayBpm(lowBPM, highBPM);
            Tags["BPMS"] = bpmTag.ToString();
            if (stopTag.Length > 0)
                Tags["STOPS"] = stopTag.ToString();
        }

        /// <summary>
        /// Appends one BPM value and updates the display BPM range.
        /// </summary>
        private void AddBpmTagValue(StringBuilder bpmTag, double value, double offset, ref double lowBPM, ref double highBPM)
        {
            if (bpmTag.Length > 0)
            {
                bpmTag.Append(",");
                if (value < lowBPM)
                    lowBPM = Math.Round(value);
                if (value > highBPM)
                    highBPM = Math.Round(value);
            }
            else
            {
                Tags["DISPLAYBPM"] = Math.Round(value).ToString();
            }

            bpmTag.Append(offset.ToString());
            bpmTag.Append("=");
            bpmTag.Append(value.ToString());
        }

        /// <summary>
        /// Appends one STOP value from adjacent tempo entries.
        /// </summary>
        private static void AddStopTagValue(StringBuilder stopTag, Entry[] entries, int index, double offset)
        {
            double stopLength = Math.Abs(Math.Round((double)(entries[index + 1].LinearOffset - entries[index].LinearOffset), 3));
            if (stopTag.Length > 0)
                stopTag.Append(",");
            stopTag.Append(offset.ToString());
            stopTag.Append("=");
            stopTag.Append(stopLength.ToString());
        }

        /// <summary>
        /// Writes a ranged display BPM when multiple positive BPM values were found.
        /// </summary>
        private void ApplyDisplayBpm(double lowBPM, double highBPM)
        {
            if (lowBPM < highBPM)
            {
                string bpmResult = lowBPM != highBPM ? lowBPM.ToString() + ":" + highBPM.ToString() : lowBPM.ToString();
                if (lowBPM != highBPM)
                    Tags["DISPLAYBPM"] = bpmResult;
            }
        }

        /// <summary>
        /// Writes the StepMania file contents to a stream.
        /// </summary>
        public void Write(Stream target)
        {
            StreamWriter writer = new StreamWriter(target);
            foreach (KeyValuePair<string, string> tag in Tags)
            {
                string val = "#" + tag.Key + ":" + tag.Value + ";";

                if (val.Contains("SongID"))
                    val = "//----- song ID: " + tag.Value + " -----//";

                if (val.Contains("#NOTES"))
                    writer.WriteLine("");

                writer.WriteLine(val);
            }
            writer.Flush();
        }

        /// <summary>
        /// Writes the StepMania file contents to disk.
        /// </summary>
        public void WriteFile(string filename)
        {
            using (MemoryStream mem = new MemoryStream())
            {
                Write(mem);
                File.WriteAllBytes(filename, mem.ToArray());
            }
        }
    }
}
