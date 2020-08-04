using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds;
using Scharfrichter.Common;

using System;
using System.IO;
using System.Text;

namespace ConvertHelper
{
    static public class PopnToBMS
    {
        static public void Convert(string[] inArgs, long unitNumerator, long unitDenominator, int version)
        {
            // configuration
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            Configuration db = Common.LoadDB("PopnDB");

            // splash
            Splash.Show("Popn to BeMusic Script");

            // args
            string[] args;
            if (inArgs.Length > 0)
                args = Subfolder.Parse(inArgs);
            else
                args = inArgs;

            // show usage if no args provided
            if (args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Usage: PopnToBMS <input file>");
                Console.WriteLine();
                Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
                Console.WriteLine();
                Console.WriteLine("Supported formats:");
                Console.WriteLine("2DX");
            }

            // process files
            for (int i = 0; i < args.Length; i++)
            {
                if (File.Exists(args[i]))
                {
                    Console.WriteLine("Processing File: " + args[i]);
                    string filename = args[i];
                    if (Path.GetExtension(filename).ToUpper() != @".2DX")
                    {
                        Console.WriteLine();
                        Console.WriteLine("Usage: PopnToBMS <input file>");
                        Console.WriteLine();
                        Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
                        Console.WriteLine();
                        Console.WriteLine("Supported formats:");
                        Console.WriteLine("2DX");
                        Console.WriteLine();
                        Console.WriteLine();
                        Console.WriteLine();
                        continue;
                    }

                    string title = Path.GetFileNameWithoutExtension(filename);
                    string output = Path.GetDirectoryName(filename) + "\\";
                    string suffix = "";
                    if (title.Length > 4)
                    {
                        suffix = title.Substring(title.Length - 4, 4);
                    }
                    if (suffix == "_pre")
                    {
                        title = title.Substring(0, title.Length - 4);

                        try
                        {
                            byte[] data = File.ReadAllBytes(filename);
                            using (MemoryStream source = new MemoryStream(data))
                            {
                                Console.WriteLine("Converting Samples");
                                Bemani2DX archive = Bemani2DX.Read(source);

                                float volume = 0.6f;
                                ConvertSounds(archive.Sounds, filename, volume, null, output, title, true, "");
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            Console.WriteLine();
                            Console.WriteLine();
                            return;
                        }
                    } else
                    {
                        int maxIndex = -1;
                        int difficaltyIndex = 0;
                        for (int j = 0; j < 5; j++)
                        {
                            switch (j)
                            {
                                case 0:
                                    // Convert Sounds
                                    break;
                                case 1:
                                    // EASY
                                    filename = output + title + "_ep.bin";
                                    if (!File.Exists(filename))
                                    {
                                        continue;
                                    }
                                    Console.WriteLine("Processing File: " + filename);
                                    difficaltyIndex = 3;
                                    break;
                                case 2:
                                    // NOERMAL
                                    filename = output + title + "_np.bin";
                                    if (!File.Exists(filename))
                                    {
                                        continue;
                                    }
                                    Console.WriteLine("Processing File: " + filename);
                                    difficaltyIndex = 1;
                                    break;
                                case 3:
                                    // HYPER
                                    filename = output + title + "_hp.bin";
                                    if (!File.Exists(filename))
                                    {
                                        continue;
                                    }
                                    Console.WriteLine("Processing File: " + filename);
                                    difficaltyIndex = 0;
                                    break;
                                case 4:
                                    // EX
                                    filename = output + title + "_op.bin";
                                    if (!File.Exists(filename))
                                    {
                                        continue;
                                    }
                                    Console.WriteLine("Processing File: " + filename);
                                    difficaltyIndex = 2;
                                    break;
                                //case 5:
                                //    // Battle
                                //    filename = output + title + "_bp.bin";
                                //    if (!File.Exists(filename))
                                //    {
                                //        continue;
                                //    }
                                //    Console.WriteLine("Processing File: " + filename);
                                //    difficaltyIndex = 99;
                                //    break;
                            }

                            byte[] data = File.ReadAllBytes(filename);

                            try
                            {
                                switch (Path.GetExtension(filename).ToUpper())
                                {
                                    case @".BIN":
                                        using (MemoryStream source = new MemoryStream(data))
                                        {
                                            Popn archive = Popn.Read(source, unitNumerator, unitDenominator, maxIndex, version);
                                            if (db[title]["TITLE"] != "")
                                            {
                                                Chart chart = archive.Charts[0];
                                                chart.Tags["TITLE"] = db[title]["TITLE"];
                                                chart.Tags["ARTIST"] = db[title]["ARTIST"];
                                                chart.Tags["GENRE"] = db[title]["GENRE"];
                                                chart.Tags["PLAYLEVEL"] = db[title]["DIFFICULTYDP" + config["IIDX"]["DIFFICULTY" + difficaltyIndex.ToString()]];
                                            }
                                            ConvertChart(archive.Charts[0], config, title, difficaltyIndex, null, "", output);

                                        }
                                        break;
                                    case @".2DX":
                                        using (MemoryStream source = new MemoryStream(data))
                                        {
                                            Console.WriteLine("Converting Samples");
                                            Bemani2DX archive = Bemani2DX.Read(source);

                                            float volume = 0.6f;
                                            maxIndex = ConvertSounds(archive.Sounds, filename, volume, null, output, title, false, "");
                                        }
                                        break;
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                Console.WriteLine();
                                Console.WriteLine();
                                return;
                            }
                        }

                    }
                }
            }

            // wrap up
            Console.WriteLine("PopnToBMS finished.");
            Console.WriteLine();
            Console.WriteLine();
        }

        static public bool ConvertChart(Chart chart, Configuration config, string filename, int index, int[] map, string version = "", string dirPath = "")
        {
            if (config == null)
            {
                config = Configuration.LoadIIDXConfig(Common.configFileName);
            }

            int quantizeNotes = config["BMS"].GetValue("QuantizeNotes");
            int quantizeMeasure = config["BMS"].GetValue("QuantizeMeasure");
            int difficulty = config["IIDX"].GetValue("Difficulty" + index.ToString());
            string title = config["BMS"]["Players" + config["IIDX"]["Players" + index.ToString()]] + " " + config["POPN"]["Difficulty" + difficulty.ToString()];
            if (index == 4)
            {
                title = "BATTLE (3 BUTTON)";
            }
            title = title.Trim();
            int outputRank = config["POPN"].GetValue("OutputRank");

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

                // create RANK metadata
                bms.Charts[0].Tags["RANK"] = outputRank.ToString();

                // replace prohibited characters
                name = Common.nameReplace(name);

                if (title != null && title.Length > 0)
                {
                    name += " [" + title + "]";
                }

                Common.SafeCreateDirectory(dirPath);
                string output = Path.Combine(dirPath, @"@" + name + ".pms");

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
                bool isSucces = bms.Write(mem, true);
                if (!isSucces)
                    return false;

                File.WriteAllText(output, Encoding.UTF8.GetString(mem.ToArray()), Encoding.GetEncoding(932));
            }
            return true;
        }

        static public int ConvertSounds(Sound[] sounds, string filename, float volume, string INDEX = null, string outputFolder = "", string nameInfo = "", bool isPre2DX = false, string version = "")
        {
            string targetPath = Path.Combine(outputFolder, version);
            Common.SafeCreateDirectory(targetPath);

            int maxIndex = -1;
            int maxLength = 0;
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
                    if (sounds[j].Data.Length > maxLength)
                    {
                        maxIndex = sampleIndex;
                        maxLength = sounds[j].Data.Length;
                    }
                    sounds[j].WriteFile(Path.Combine(targetPath, Scharfrichter.Codec.Util.ConvertToBMEString(sampleIndex, 4) + @".wav"), volume);
                }
            }
            return maxIndex;
        }
    }
}
