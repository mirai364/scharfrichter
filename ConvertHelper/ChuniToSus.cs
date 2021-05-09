using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds;
using Scharfrichter.Common;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Xml.Linq;

namespace ConvertHelper
{
    public class MusicData
    {
        public string type { get; set; }
        public string typeName { get; set; }
        public string level { get; set; }
    }

    static public class ChuniToSus
    {

        static public void Convert(string[] inArgs, long unitNumerator, long unitDenominator, bool idUseRenderAutoTip = false)
        {
            // configuration
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            Configuration db = Common.LoadDB();
            int quantizeMeasure = config["BMS"].GetValue("QuantizeMeasure");
            int quantizeNotes = config["BMS"].GetValue("QuantizeNotes");

            // splash
            Splash.Show("Chuni to Sus Script");
            Console.WriteLine("Timing: " + unitNumerator.ToString() + "/" + unitDenominator.ToString());
            Console.WriteLine("Measure Quantize: " + quantizeMeasure.ToString());

            // args
            string[] args;
            if (inArgs.Length > 0)
                args = Subfolder.Parse(inArgs);
            else
                args = inArgs;

            // debug args (if applicable)
            if (System.Diagnostics.Debugger.IsAttached && args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Debugger attached. Input file name:");
                args = new string[] { Console.ReadLine() };
            }

            // show usage if no args provided
            if (args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Usage: ChuniToSus <input file>");
                Console.WriteLine();
                Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
                Console.WriteLine();
                Console.WriteLine("Supported formats:");
                Console.WriteLine("C2S");
            }

            string output = config["BMS"]["Output"];

            // process files
            for (int i = 0; i < args.Length; i++)
            {
                if (File.Exists(args[i]))
                {
                    Console.WriteLine();
                    Console.WriteLine("Processing File: " + args[i]);
                    string filename = args[i];

                    byte[] data = File.ReadAllBytes(args[i]);
                    switch (Path.GetExtension(args[i]).ToUpper())
                    {
                        case @".C2S":
                            // Find ID
                            string fileName = Path.GetFileName(filename);
                            // Read Music file 
                            string C2sDir = Path.GetDirectoryName(filename) + "\\";
                            XElement musicXml = XElement.Load(Path.Combine(C2sDir, "Music.xml"));
                            string id = musicXml.Element("name").Element("id").Value;
                            string title = musicXml.Element("name").Element("str").Value;
                            string artist = musicXml.Element("artistName").Element("str").Value;
                            string genre = musicXml.Element("genreNames").Element("list").Element("StringID").Element("str").Value;
                            string previewStartTime = musicXml.Element("previewStartTime").Value;
                            string previewEndTime = musicXml.Element("previewEndTime").Value;
                            var boxedLunchRow = musicXml.Element("fumens").Elements("MusicFumenData");

                            Dictionary<string, MusicData> musicData = new Dictionary<string, MusicData>();
                            foreach (XElement boxedLunchElement in boxedLunchRow)
                            {
                                musicData.Add(boxedLunchElement.Element("file").Element("path").Value, new MusicData() { type = boxedLunchElement.Element("type").Element("id").Value, typeName = boxedLunchElement.Element("type").Element("data").Value, level = boxedLunchElement.Element("level").Value });
                            }

                            System.IO.StreamReader file = new System.IO.StreamReader(args[i]);
                            ChuniC2S archive = ChuniC2S.Read(file, unitNumerator, unitDenominator);
                                ChartChuni chart = archive.chart;
                                chart.Tags["ID"] = id;
                                chart.Tags["TITLE"] = title;
                                chart.Tags["ARTIST"] = artist;
                                chart.Tags["GENRE"] = genre;
                                chart.Tags["PLAYLEVEL"] = musicData[fileName].level;
                                chart.Tags["TYPE"] = musicData[fileName].type;
                                chart.Tags["TYPENAME"] = musicData[fileName].typeName;

                            ConvertChart(chart, config, filename, 1, null, "1");
                            break;
                    }
                }
            }

            // wrap up
            Console.WriteLine("BemaniToBMS finished.");
        }

        static public bool ConvertChart(ChartChuni chart, Configuration config, string filename, int index, int[] map, string version = "")
        {
            if (config == null)
            {
                config = Configuration.LoadIIDXConfig(Common.configFileName);
            }

            int quantizeNotes = config["BMS"].GetValue("QuantizeNotes");
            int quantizeMeasure = config["BMS"].GetValue("QuantizeMeasure");
            int difficulty = config["IIDX"].GetValue("Difficulty" + index.ToString());
            int outputRank = config["BMS"].GetValue("OutputRank");

            if (quantizeMeasure > 0)
                chart.QuantizeMeasureLengths(quantizeMeasure);

            using (MemoryStream mem = new MemoryStream())
            {
                SUS sus = new SUS();
                sus.chart = chart ;

                string name = "";
                if (chart.Tags.ContainsKey("TITLE"))
                    name = chart.Tags["TITLE"];
                if (name == "")
                    name = Path.GetFileNameWithoutExtension(Path.GetFileName(filename)); //ex: "1204 [1P Another]"

                // write some tags
                sus.chart.Tags["TITLE"] = name;
                if (chart.Tags.ContainsKey("ARTIST"))
                    sus.chart.Tags["ARTIST"] = chart.Tags["ARTIST"];
                if (chart.Tags.ContainsKey("GENRE"))
                    sus.chart.Tags["GENRE"] = chart.Tags["GENRE"];

                if (difficulty > 0)
                    sus.chart.Tags["DIFFICULTY"] = difficulty.ToString();

                if (sus.chart.Players > 1)
                    sus.chart.Tags["PLAYER"] = "3";
                else
                    sus.chart.Tags["PLAYER"] = "1";

                // create RANK metadata
                sus.chart.Tags["RANK"] = outputRank.ToString();

                // replace prohibited characters
                name = Common.nameReplace(name);

                string dirPath = Path.Combine(config["BMS"]["Output"], name);
                name += "(" + sus.chart.Tags["TYPENAME"] + ")";

                Common.SafeCreateDirectory(dirPath);
                string output = Path.Combine(dirPath, name + ".sus");

                if (quantizeNotes > 0)
                {
                    try
                    {
                        sus.chart.quantizeNotes = quantizeNotes;
                        sus.chart.QuantizeNoteOffsets();
                    }
                    catch (Exception)
                    {
                        // something weird happened
                    }
                }
                bool isSucces = sus.Write(mem, true);
                if (!isSucces)
                    return false;

                //File.WriteAllBytes(output, mem.ToArray());
                File.WriteAllText(output, Encoding.UTF8.GetString(mem.ToArray()), Encoding.GetEncoding(932));
            }
            return true;
        }
    }
}
