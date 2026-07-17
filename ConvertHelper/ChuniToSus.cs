using DDSReader;
using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace ConvertHelper
{
    /// <summary>
    /// Holds CHUNITHM chart metadata parsed from Music.xml.
    /// </summary>
    public class MusicData
    {
        /// <summary>
        /// Gets or sets the numeric chart type, including WORLD'S END suffix data when present.
        /// </summary>
        public string type { get; set; }

        /// <summary>
        /// Gets or sets the display name for the chart type.
        /// </summary>
        public string typeName { get; set; }

        /// <summary>
        /// Gets or sets the chart level text.
        /// </summary>
        public string level { get; set; }
    }

    /// <summary>
    /// Converts CHUNITHM C2S charts and DDS jackets to SUS output.
    /// </summary>
    static public class ChuniToSus
    {
        /// <summary>
        /// Holds configuration and timing values shared by one conversion run.
        /// </summary>
        private sealed class ConversionContext
        {
            public Configuration Config;
            public long UnitNumerator;
            public long UnitDenominator;
            public int QuantizeMeasure;
        }

        /// <summary>
        /// Holds song-level metadata read from Music.xml.
        /// </summary>
        private sealed class MusicXmlInfo
        {
            public string Id;
            public string Title;
            public string Artist;
            public string Genre;
            public Dictionary<string, MusicData> Charts;
        }

        /// <summary>
        /// Converts supported CHUNITHM input files passed to the ChuniToSus command.
        /// </summary>
        static public void Convert(string[] inArgs, long unitNumerator, long unitDenominator, bool idUseRenderAutoTip = false)
        {
            ConversionContext context = CreateContext(unitNumerator, unitDenominator);
            ShowSplash(context);

            string[] args = PrepareInputArguments(inArgs);
            if (args.Length == 0)
                ShowUsage();

            ProcessFiles(args, context);

            Console.WriteLine("BemaniToBMS finished.");
        }

        /// <summary>
        /// Writes one CHUNITHM chart as a SUS file.
        /// </summary>
        static public bool ConvertChart(ChartChuni chart, Configuration config, string filename, int index, int[] map, string version = "")
        {
            if (config == null)
                config = Configuration.LoadIIDXConfig(Common.configFileName);

            int quantizeNotes = config["BMS"].GetValue("QuantizeNotes");
            int quantizeMeasure = config["BMS"].GetValue("QuantizeMeasure");
            int difficulty = config["IIDX"].GetValue("Difficulty" + index.ToString());
            int outputRank = config["BMS"].GetValue("OutputRank");

            if (quantizeMeasure > 0)
                chart.QuantizeMeasureLengths(quantizeMeasure);

            using (MemoryStream mem = new MemoryStream())
            {
                SUS sus = CreateSus(chart, filename, difficulty, outputRank);
                string output = BuildSusOutputPath(sus.chart, config, filename);

                QuantizeChartNotes(sus.chart, quantizeNotes);
                if (!sus.Write(mem, true))
                    return false;

                File.WriteAllBytes(output, mem.ToArray());
            }

            return true;
        }

        /// <summary>
        /// Creates the shared conversion context from command options and configuration files.
        /// </summary>
        private static ConversionContext CreateContext(long unitNumerator, long unitDenominator)
        {
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            return new ConversionContext
            {
                Config = config,
                UnitNumerator = unitNumerator,
                UnitDenominator = unitDenominator,
                QuantizeMeasure = config["BMS"].GetValue("QuantizeMeasure")
            };
        }

        /// <summary>
        /// Prints the converter banner and timing information.
        /// </summary>
        private static void ShowSplash(ConversionContext context)
        {
            Splash.Show("Chuni to Sus Script");
            Console.WriteLine("Timing: " + context.UnitNumerator.ToString() + "/" + context.UnitDenominator.ToString());
            Console.WriteLine("Measure Quantize: " + context.QuantizeMeasure.ToString());
        }

        /// <summary>
        /// Expands folder arguments and optionally prompts for debug input.
        /// </summary>
        private static string[] PrepareInputArguments(string[] inArgs)
        {
            string[] args = inArgs.Length > 0 ? Subfolder.Parse(inArgs) : inArgs;
            if (System.Diagnostics.Debugger.IsAttached && args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Debugger attached. Input file name:");
                args = new string[] { Console.ReadLine() };
            }

            return args;
        }

        /// <summary>
        /// Prints command usage information.
        /// </summary>
        private static void ShowUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: ChuniToSus <input file>");
            Console.WriteLine();
            Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
            Console.WriteLine();
            Console.WriteLine("Supported formats:");
            Console.WriteLine("C2S");
        }

        /// <summary>
        /// Processes each existing input file.
        /// </summary>
        private static void ProcessFiles(string[] args, ConversionContext context)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (!File.Exists(args[i]))
                    continue;

                Console.WriteLine();
                Console.WriteLine("Processing File: " + args[i]);
                ProcessFile(args[i], context);
            }
        }

        /// <summary>
        /// Dispatches one input file to the converter for its file extension.
        /// </summary>
        private static void ProcessFile(string filename, ConversionContext context)
        {
            switch (Path.GetExtension(filename).ToUpper())
            {
                case @".C2S":
                    ConvertC2S(filename, context);
                    break;
                case @".DDS":
                    ConvertDds(filename, context);
                    break;
            }
        }

        /// <summary>
        /// Reads one C2S chart and writes its SUS output.
        /// </summary>
        private static void ConvertC2S(string filename, ConversionContext context)
        {
            MusicXmlInfo musicInfo = LoadMusicXml(filename);
            using (StreamReader file = new StreamReader(filename))
            {
                ChuniC2S archive = ChuniC2S.Read(file, context.UnitNumerator, context.UnitDenominator);
                ChartChuni chart = archive.chart;
                ApplyMusicTags(chart, Path.GetFileName(filename), musicInfo);
                ConvertChart(chart, context.Config, filename, 1, null, "1");
            }
        }

        /// <summary>
        /// Converts one DDS jacket image to jacket.jpg next to the SUS output.
        /// </summary>
        private static void ConvertDds(string filename, ConversionContext context)
        {
            MusicXmlInfo musicInfo = LoadMusicXml(filename);
            DDSImage img = new DDSImage(filename);
            string dirPath = Path.Combine(context.Config["BMS"]["Output"], Common.nameReplace(musicInfo.Genre), Common.nameReplace(musicInfo.Title), "jacket.jpg");
            img.Save(dirPath);
        }

        /// <summary>
        /// Loads song metadata and chart metadata from the sibling Music.xml file.
        /// </summary>
        private static MusicXmlInfo LoadMusicXml(string filename)
        {
            string c2sDir = Path.GetDirectoryName(filename) + "\\";
            XElement musicXml = XElement.Load(Path.Combine(c2sDir, "Music.xml"));
            return new MusicXmlInfo
            {
                Id = musicXml.Element("name").Element("id").Value,
                Title = musicXml.Element("name").Element("str").Value,
                Artist = musicXml.Element("artistName").Element("str").Value,
                Genre = musicXml.Element("genreNames").Element("list").Element("StringID").Element("str").Value,
                Charts = ReadMusicData(musicXml)
            };
        }

        /// <summary>
        /// Reads per-chart CHUNITHM metadata from the fumens section.
        /// </summary>
        private static Dictionary<string, MusicData> ReadMusicData(XElement musicXml)
        {
            Dictionary<string, MusicData> musicData = new Dictionary<string, MusicData>();
            IEnumerable<XElement> rows = musicXml.Element("fumens").Elements("MusicFumenData");
            foreach (XElement row in rows)
            {
                string path = row.Element("file").Element("path").Value;
                if (path == "")
                    continue;

                musicData.Add(path, CreateMusicData(row, musicXml));
            }

            return musicData;
        }

        /// <summary>
        /// Creates one chart metadata object from a MusicFumenData element.
        /// </summary>
        private static MusicData CreateMusicData(XElement row, XElement musicXml)
        {
            string type = row.Element("type").Element("id").Value;
            if (type == "4")
            {
                return new MusicData
                {
                    type = type + ":" + musicXml.Element("worldsEndTagName").Element("str").Value,
                    typeName = row.Element("type").Element("data").Value,
                    level = musicXml.Element("starDifType").Value
                };
            }

            return new MusicData
            {
                type = type,
                typeName = row.Element("type").Element("data").Value,
                level = row.Element("level").Value
            };
        }

        /// <summary>
        /// Applies Music.xml metadata tags to a parsed C2S chart.
        /// </summary>
        private static void ApplyMusicTags(ChartChuni chart, string fileName, MusicXmlInfo musicInfo)
        {
            MusicData chartData = musicInfo.Charts[fileName];
            chart.Tags["ID"] = musicInfo.Id;
            chart.Tags["TITLE"] = musicInfo.Title;
            chart.Tags["ARTIST"] = musicInfo.Artist;
            chart.Tags["GENRE"] = musicInfo.Genre;
            chart.Tags["PLAYLEVEL"] = chartData.level;
            chart.Tags["TYPE"] = chartData.type;
            chart.Tags["TYPENAME"] = chartData.typeName;
        }

        /// <summary>
        /// Creates a SUS wrapper and initializes standard chart tags.
        /// </summary>
        private static SUS CreateSus(ChartChuni chart, string filename, int difficulty, int outputRank)
        {
            SUS sus = new SUS();
            sus.chart = chart;

            string name = ResolveChartName(chart, filename);
            sus.chart.Tags["TITLE"] = name;
            CopyTag(chart, sus.chart, "ARTIST");
            CopyTag(chart, sus.chart, "GENRE");

            if (difficulty > 0)
                sus.chart.Tags["DIFFICULTY"] = difficulty.ToString();

            sus.chart.Tags["PLAYER"] = sus.chart.Players > 1 ? "3" : "1";
            sus.chart.Tags["RANK"] = outputRank.ToString();
            return sus;
        }

        /// <summary>
        /// Copies a tag when it exists on the source chart.
        /// </summary>
        private static void CopyTag(ChartChuni source, ChartChuni target, string tag)
        {
            if (source.Tags.ContainsKey(tag))
                target.Tags[tag] = source.Tags[tag];
        }

        /// <summary>
        /// Resolves the display name for a SUS chart.
        /// </summary>
        private static string ResolveChartName(ChartChuni chart, string filename)
        {
            if (chart.Tags.ContainsKey("TITLE") && chart.Tags["TITLE"] != "")
                return chart.Tags["TITLE"];

            return Path.GetFileNameWithoutExtension(Path.GetFileName(filename));
        }

        /// <summary>
        /// Builds the final SUS output path and creates its directory.
        /// </summary>
        private static string BuildSusOutputPath(ChartChuni chart, Configuration config, string filename)
        {
            string name = Common.nameReplace(ResolveChartName(chart, filename));
            string genre = Common.nameReplace(chart.Tags["GENRE"]);
            string dirPath = Path.Combine(config["BMS"]["Output"], genre, name);

            if (chart.Tags["TYPENAME"] == "WORLD'S END")
                name += "(" + chart.Tags["TYPENAME"] + " " + chart.Tags["TYPE"].Substring(2) + chart.Tags["PLAYLEVEL"] + ")";
            else
                name += "(" + chart.Tags["TYPENAME"] + ")";

            Common.SafeCreateDirectory(dirPath);
            return Path.Combine(dirPath, name + ".sus");
        }

        /// <summary>
        /// Quantizes note offsets when the configuration asks for it.
        /// </summary>
        private static void QuantizeChartNotes(ChartChuni chart, int quantizeNotes)
        {
            if (quantizeNotes <= 0)
                return;

            try
            {
                chart.quantizeNotes = quantizeNotes;
                chart.QuantizeNoteOffsets();
            }
            catch (Exception)
            {
                // Keep legacy behavior by ignoring quantize failures.
            }
        }
    }
}