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

namespace ConvertHelper
{
    static public class BemaniToBMS
    {
        static public void Convert(string[] inArgs, long unitNumerator, long unitDenominator)
        {
            // configuration
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            Configuration db = Common.LoadDB();
            int quantizeMeasure = config["BMS"].GetValue("QuantizeMeasure");
            int quantizeNotes = config["BMS"].GetValue("QuantizeNotes");
            bool idUseRenderAutoTip = config["BMS"].GetBool("IsUseRenderAutoTip");

            // splash
            Splash.Show("Bemani to BeMusic Script");
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
                Console.WriteLine("Usage: BemaniToBMS <input file>");
                Console.WriteLine();
                Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
                Console.WriteLine();
                Console.WriteLine("Supported formats:");
                Console.WriteLine("1, 2DX, S3P, CS, SD9, SSP");
            }

            // process files
            for (int i = 0; i < args.Length; i++)
            {
                if (File.Exists(args[i]))
                {
                    Console.WriteLine();
                    Console.WriteLine("Processing File: " + args[i]);
                    string filename = args[i];

                    string IIDXDBName = Path.GetFileNameWithoutExtension(filename);
                    string version = IIDXDBName.Substring(0, 2);
                    bool isPre2DX = false;
                    string INDEX = null;
                    if (IIDXDBName.Contains("pre"))
                    {
                        isPre2DX = true;
                        IIDXDBName = IIDXDBName.Substring(0, 5);
                    }
                    if (IIDXDBName.Length > 5)
                    {
                        INDEX = IIDXDBName.Substring(5);
                        IIDXDBName = IIDXDBName.Substring(0, 5);
                    }
                    while (IIDXDBName.StartsWith("0"))
                        IIDXDBName = IIDXDBName.Substring(1);

                    byte[] data = File.ReadAllBytes(args[i]);
                    switch (Path.GetExtension(args[i]).ToUpper())
                    {
                        case @".1":
                            using (MemoryStream source = new MemoryStream(data))
                            {
                                Dictionary<int, int> ignore = new Dictionary<int, int>();
                                if (idUseRenderAutoTip)
                                {
                                    Console.WriteLine("Convert AutoTips");
                                    Console.WriteLine(args[i].Remove(args[i].Length - 8));
                                    string[] files = System.IO.Directory.GetFiles(args[i].Remove(args[i].Length - 8), "*", SearchOption.AllDirectories);
                                    Render.RenderWAV(files, 1, 1000);

                                    ignore.Add(3, 3);
                                }
                                Bemani1 archive = Bemani1.Read(source, unitNumerator, unitDenominator, ignore);

                                if (db[IIDXDBName]["TITLE"] != "")
                                {
                                    for (int j = 0; j < archive.ChartCount; j++)
                                    {
                                        Chart chart = archive.Charts[j];
                                        if (chart != null)
                                        {
                                            chart.Tags["TITLE"] = db[IIDXDBName]["TITLE"];
                                            chart.Tags["ARTIST"] = db[IIDXDBName]["ARTIST"];
                                            chart.Tags["GENRE"] = db[IIDXDBName]["GENRE"];
                                            chart.Tags["VIDEO"] = db[IIDXDBName]["VIDEO"];
                                            chart.Tags["VIDEODELAY"] = db[IIDXDBName]["VIDEODELAY"];
                                            if (j < 6)
                                            {
                                                chart.Tags["PLAYLEVEL"] = db[IIDXDBName]["DIFFICULTYSP" + config["IIDX"]["DIFFICULTY" + j.ToString()]];
                                                chart.Tags["KEYSET"] = db[IIDXDBName]["KEYSETSP" + config["IIDX"]["DIFFICULTY" + j.ToString()]];
                                                chart.Tags["ISUSERENDERAUTOTIP"] = idUseRenderAutoTip.ToString();
                                            }
                                            else if (j < 12)
                                            {
                                                chart.Tags["PLAYLEVEL"] = db[IIDXDBName]["DIFFICULTYDP" + config["IIDX"]["DIFFICULTY" + j.ToString()]];
                                                chart.Tags["KEYSET"] = db[IIDXDBName]["KEYSETDP" + config["IIDX"]["DIFFICULTY" + j.ToString()]];
                                                chart.Tags["ISUSERENDERAUTOTIP"] = idUseRenderAutoTip.ToString();
                                            }
                                        }
                                    }
                                }

                                ConvertArchive(archive, config, args[i], version);
                            }
                            break;
                        case @".2DX":
                            using (MemoryStream source = new MemoryStream(data))
                            {
                                Console.WriteLine("Converting Samples");
                                Bemani2DX archive = Bemani2DX.Read(source);

                                string output = config["BMS"]["Output"];
                                float volume = 0.6f;
                                string title = "";
                                if (db[IIDXDBName]["TITLE"] != "")
                                {
                                    volume = float.Parse(db[IIDXDBName]["VOLUME"]) / 127.0f;
                                    title = db[IIDXDBName]["TITLE"];
                                }
                                ConvertSounds(archive.Sounds, filename, volume, INDEX, output, title, isPre2DX, version);
                            }
                            break;
                        case @".S3P":
                            using (MemoryStream source = new MemoryStream(data))
                            {
                                Console.WriteLine("Converting Samples");
                                BemaniS3P archive = BemaniS3P.Read(source);

                                string output = config["BMS"]["Output"];
                                float volume = 0.6f;
                                string title = "";
                                if (db[IIDXDBName]["TITLE"] != "")
                                {
                                    volume = float.Parse(db[IIDXDBName]["VOLUME"]) / 127.0f;
                                    title = db[IIDXDBName]["TITLE"];
                                }
                                ConvertSounds(archive.Sounds, filename, volume, INDEX, output, title, isPre2DX, version);
                            }
                            break;
                        case @".CS":
                            using (MemoryStream source = new MemoryStream(data))
                                ConvertChart(BeatmaniaIIDXCSNew.Read(source), config, filename, -1, null);
                            break;
                        case @".CS2":
                            using (MemoryStream source = new MemoryStream(data))
                                ConvertChart(BeatmaniaIIDXCSOld.Read(source), config, filename, -1, null);
                            break;
                        case @".CS5":
                            using (MemoryStream source = new MemoryStream(data))
                                ConvertChart(Beatmania5Key.Read(source), config, filename, -1, null);
                            break;
                        case @".CS9":
                            break;
                        case @".SD9":
                            using (MemoryStream source = new MemoryStream(data))
                            {
                                Sound sound = BemaniSD9.Read(source);
                                string targetFile = Path.GetFileNameWithoutExtension(filename);
                                string targetPath = Path.Combine(Path.GetDirectoryName(filename), targetFile) + ".wav";
                                sound.WriteFile(targetPath, 1.0f);
                            }
                            break;
                        case @".SSP":
                            using (MemoryStream source = new MemoryStream(data))
                                ConvertSounds(BemaniSSP.Read(source).Sounds, filename, 1.0f);
                            break;
                    }
                }
            }

            // wrap up
            Console.WriteLine("BemaniToBMS finished.");
        }

        static public void ConvertArchive(Archive archive, Configuration config, string filename, string version = "")
        {
            for (int j = 0; j < archive.ChartCount; j++)
            {
                if (archive.Charts[j] != null)
                {
                    Console.WriteLine("Converting Chart " + j.ToString());
                    ConvertChart(archive.Charts[j], config, filename, j, null, version);
                }
            }
        }

        static public void ConvertChart(Chart chart, Configuration config, string filename, int index, int[] map, string version = "")
        {
            if (config == null)
            {
                config = Configuration.LoadIIDXConfig(Common.configFileName);
            }

            int quantizeNotes = config["BMS"].GetValue("QuantizeNotes");
            int quantizeMeasure = config["BMS"].GetValue("QuantizeMeasure");
            int difficulty = config["IIDX"].GetValue("Difficulty" + index.ToString());
            string title = config["BMS"]["Players" + config["IIDX"]["Players" + index.ToString()]] + " " + config["BMS"]["Difficulty" + difficulty.ToString()];
            title = title.Trim();
            string movieFolder = config["BMS"]["MovieFolder"];
            string outputFolder = config["BMS"]["Output"] + version + "\\";
            bool isSameFolderMovie = config["BMS"].GetBool("IsSameFolderMovie");

            if (quantizeMeasure > 0)
                chart.QuantizeMeasureLengths(quantizeMeasure);

            using (MemoryStream mem = new MemoryStream())
            {
                BMS bms = new BMS();
                bms.Charts = new Chart[] { chart };

                string name = "";
                if (chart.Tags.ContainsKey("TITLE"))
                    name = chart.Tags["TITLE"];
                if (name == "")
                    name = Path.GetFileNameWithoutExtension(Path.GetFileName(filename)); //ex: "1204 [1P Another]"

                // write some tags
                bms.Charts[0].Tags["TITLE"] = name;
                if (chart.Tags.ContainsKey("ARTIST"))
                    bms.Charts[0].Tags["ARTIST"] = chart.Tags["ARTIST"];
                if (chart.Tags.ContainsKey("GENRE"))
                    bms.Charts[0].Tags["GENRE"] = chart.Tags["GENRE"];

                if (difficulty > 0)
                    bms.Charts[0].Tags["DIFFICULTY"] = difficulty.ToString();

                if (bms.Charts[0].Players > 1)
                    bms.Charts[0].Tags["PLAYER"] = "3";
                else
                    bms.Charts[0].Tags["PLAYER"] = "1";
     

                // replace prohibited characters
                name = Common.nameReplace(name);

                string dirPath = outputFolder + name;

                if (title != null && title.Length > 0)
                {
                    if (bms.Charts[0].Players > 2)
                        title = title + " " + bms.Charts[0].Players + "P";
                    else if (bms.Charts[0].Players > 1)
                        title = title + " DP";
                    name += " [" + title + "]";
                }

                Common.SafeCreateDirectory(dirPath);
                string output = Path.Combine(dirPath, @"@" + name + ".bms");

                bms.Charts[0].isSameFolderMovie = isSameFolderMovie;
                if (chart.Tags.ContainsKey("VIDEO") && isSameFolderMovie)
                {
                    string BGA = chart.Tags["VIDEO"];
                    string movieFile = movieFolder + BGA + ".wmv";
                    if (System.IO.File.Exists(movieFile))
                    {
                        string copyPath = dirPath + "\\" + BGA + ".wmv";
                        if (!System.IO.File.Exists(copyPath))
                        {
                            Console.WriteLine(copyPath);
                            File.Copy(movieFile, copyPath);
                        }
                    }
                }

                if (map == null)
                    bms.GenerateSampleMap();
                else
                    bms.SampleMap = map;

                if (quantizeNotes > 0)
                {
                    try
                    {
                        bms.Charts[0].quantizeNotes = quantizeNotes;
                        bms.Charts[0].QuantizeNoteOffsets();
                    }
                    catch (Exception)
                    {
                        // something weird happened
                    }
                }
                bms.Write(mem, true);

                File.WriteAllBytes(output, mem.ToArray());
            }
        }

        static public void ConvertSounds(Sound[] sounds, string filename, float volume, string INDEX = null, string outputFolder = "", string nameInfo = "", bool isPre2DX = false, string version = "")
        {
            string name;
            if (nameInfo.Length == 0)
            {
                name = Path.GetFileNameWithoutExtension(Path.GetFileName(filename));
            }
            else
            {
                name = Common.nameReplace(nameInfo);
            }
            string targetPath = Path.Combine(outputFolder, version, name);
            Common.SafeCreateDirectory(targetPath);

            if (isPre2DX)
            {
                sounds[0].WriteFile(Path.Combine(targetPath, @"preview" + @".wav"), volume);
            }
            else
            {
                targetPath += "\\sounds";
                if (INDEX != null)
                {
                    targetPath += "_" + INDEX;
                }
                Common.SafeCreateDirectory(targetPath);
                int count = sounds.Length;

                for (int j = 0; j < count; j++)
                {
                    int sampleIndex = j + 1;
                    sounds[j].WriteFile(Path.Combine(targetPath, Scharfrichter.Codec.Util.ConvertToBMEString(sampleIndex, 4) + @".wav"), volume);
                }
            }
        }
    }
}
