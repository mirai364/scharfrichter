using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds;
using Scharfrichter.Codec.Sounds.Encoders;
using Scharfrichter.Common;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ConvertHelper
{
    /// <summary>
    /// Converts pop'n music sound archives and charts to PMS-compatible BMS output.
    /// </summary>
    static public class PopnToBMS
    {
        private const float DefaultSampleVolume = 0.6f;
        private static readonly ParallelOptions SampleEncodingParallelOptions = CreateSampleEncodingParallelOptions();

        /// <summary>
        /// Holds configuration and runtime state shared by one conversion run.
        /// </summary>
        private sealed class ConversionContext
        {
            public Configuration Config;
            public Configuration Database;
            public long UnitNumerator;
            public long UnitDenominator;
            public int Version;
            public string OutputFolder;
            public string Category;
            public string SoundOutputFormat;
        }

        /// <summary>
        /// Describes a pop'n input set derived from a dropped 2DX file.
        /// </summary>
        private sealed class SongInput
        {
            public string Filename;
            public string InputFolder;
            public string Title;
            public DateTime UpdateTime;
            public bool IsPreview;
        }

        /// <summary>
        /// Describes one chart file and the target difficulty slot.
        /// </summary>
        private sealed class ChartInput
        {
            public string Filename;
            public int DifficultyIndex;
        }

        /// <summary>
        /// Holds PMS chart-writing options read from configuration.
        /// </summary>
        private sealed class ChartOptions
        {
            public int QuantizeNotes;
            public int QuantizeMeasure;
            public int Difficulty;
            public string Title;
            public int OutputRank;
            public bool EnableCommonBell;
            public string CommonBellPath;
            public string SoundOutputFormat;
        }

        /// <summary>
        /// Converts pop'n music files passed to the PopnToBMS command.
        /// </summary>
        static public void Convert(string[] inArgs, long unitNumerator, long unitDenominator, int version)
        {
            ConversionContext context = CreateContext(unitNumerator, unitDenominator, version);
            Splash.Show("Popn to BeMusic Script");

            string[] args = PrepareInputArguments(inArgs);
            if (args.Length == 0)
                ShowUsage();

            ProcessFiles(args, context);

            Console.WriteLine("PopnToBMS finished.");
            Console.WriteLine();
            Console.WriteLine();
        }

        /// <summary>
        /// Writes one pop'n chart as a PMS file with metadata and quantization settings applied.
        /// </summary>
        static public bool ConvertChart(Chart chart, Configuration config, string filename, int index, int[] map, DateTime updateTime, string version = "", string dirPath = "")
        {
            if (config == null)
                config = Configuration.LoadIIDXConfig(Common.configFileName);

            ChartOptions options = LoadChartOptions(config, index);
            if (options.QuantizeMeasure > 0)
                chart.QuantizeMeasureLengths(options.QuantizeMeasure);

            ApplyCommonBell(chart, options);

            using (MemoryStream mem = new MemoryStream())
            {
                BMS bms = CreateBms(chart, options, filename);
                bms.SoundExtension = SoundEncoderFactory.GetFileExtension(options.SoundOutputFormat);
                string name = ResolveChartName(chart, filename);
                string output = BuildChartOutputPath(config, filename, version, dirPath, ref name, options.Title);

                ConfigureSampleMap(bms, map);
                QuantizeChartNotes(bms.Charts[0], options.QuantizeNotes);

                if (!bms.Write(mem, true))
                    return false;

                WritePmsFile(output, mem, updateTime);
            }

            return true;
        }

        /// <summary>
        /// Encodes pop'n samples as OGG files and returns the longest sample index.
        /// </summary>
        static public int ConvertSounds(Sound[] sounds, string filename, float volume, DateTime updateTime, string INDEX = null, string outputFolder = "", string nameInfo = "", bool isPre2DX = false, string version = "", string soundOutputFormat = SoundEncoderFactory.DefaultFormat)
        {
            string name = GetSoundSetName(filename);
            string targetPath = Path.Combine(outputFolder, version, name);
            Common.SafeCreateDirectory(targetPath);

            if (isPre2DX)
            {
                ConvertPreviewSound(sounds, targetPath, volume, updateTime, soundOutputFormat);
                return -1;
            }

            return ConvertSampleSounds(sounds, targetPath, INDEX, volume, updateTime, soundOutputFormat);
        }

        /// <summary>
        /// Creates the shared conversion context from command options and configuration files.
        /// </summary>
        private static ConversionContext CreateContext(long unitNumerator, long unitDenominator, int version)
        {
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            return new ConversionContext
            {
                Config = config,
                Database = Common.LoadDB("PopnDB"),
                UnitNumerator = unitNumerator,
                UnitDenominator = unitDenominator,
                Version = version,
                OutputFolder = config["BMS"]["Output"],
                Category = "",
                SoundOutputFormat = config["POPN"].GetString("SoundOutputFormat", SoundEncoderFactory.DefaultFormat)
            };
        }

        /// <summary>
        /// Expands folder arguments into file arguments.
        /// </summary>
        private static string[] PrepareInputArguments(string[] inArgs)
        {
            if (inArgs.Length > 0)
                return Subfolder.Parse(inArgs);

            return inArgs;
        }

        /// <summary>
        /// Prints command usage information.
        /// </summary>
        private static void ShowUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: PopnToBMS <input file>");
            Console.WriteLine();
            Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
            Console.WriteLine();
            Console.WriteLine("Supported formats:");
            Console.WriteLine("2DX");
        }

        /// <summary>
        /// Processes all existing input files.
        /// </summary>
        private static void ProcessFiles(string[] args, ConversionContext context)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (!File.Exists(args[i]))
                    continue;

                Console.WriteLine("Processing File: " + args[i]);
                if (Path.GetExtension(args[i]).ToUpper() != @".2DX")
                {
                    ShowUsage();
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.WriteLine();
                    continue;
                }

                if (!Process2DXInput(args[i], context))
                    return;
            }
        }

        /// <summary>
        /// Converts a dropped 2DX file as either a preview archive or a full song set.
        /// </summary>
        private static bool Process2DXInput(string filename, ConversionContext context)
        {
            SongInput input = CreateSongInput(filename, context);
            if (input.IsPreview)
                return ConvertPreview2DX(input, context);

            return ConvertFullSongSet(input, context);
        }

        /// <summary>
        /// Creates pop'n input metadata from a 2DX file path.
        /// </summary>
        private static SongInput CreateSongInput(string filename, ConversionContext context)
        {
            string title = Path.GetFileNameWithoutExtension(filename);
            bool isPreview = title.Length > 4 && title.Substring(title.Length - 4, 4) == "_pre";
            if (isPreview)
                title = title.Substring(0, title.Length - 4);

            string inputFolder = Path.GetDirectoryName(filename) + "\\";
            if (context.OutputFolder == "")
                context.OutputFolder = inputFolder;

            return new SongInput
            {
                Filename = filename,
                InputFolder = inputFolder,
                Title = title,
                UpdateTime = File.GetLastWriteTime(filename),
                IsPreview = isPreview
            };
        }

        /// <summary>
        /// Converts a pop'n preview 2DX archive to the configured preview audio format.
        /// </summary>
        private static bool ConvertPreview2DX(SongInput input, ConversionContext context)
        {
            try
            {
                using (MemoryStream source = new MemoryStream(File.ReadAllBytes(input.Filename)))
                {
                    Console.WriteLine("Converting Samples");
                    Bemani2DX archive = Bemani2DX.Read(source);
                    string title = ResolveDatabaseTitle(input.Title, context);
                    ConvertSounds(archive.Sounds, input.Filename, DefaultSampleVolume, input.UpdateTime, null, context.OutputFolder, title, true, context.Category, context.SoundOutputFormat);
                }
            }
            catch (Exception e)
            {
                PrintConversionException(e);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Converts a normal pop'n 2DX archive and all matching chart files.
        /// </summary>
        private static bool ConvertFullSongSet(SongInput input, ConversionContext context)
        {
            int maxIndex = -1;
            for (int slot = 0; slot < 5; slot++)
            {
                ChartInput chartInput = ResolveChartInput(input, slot);
                if (chartInput == null)
                    continue;

                try
                {
                    string extension = Path.GetExtension(chartInput.Filename).ToUpper();
                    if (extension == @".2DX")
                        maxIndex = ConvertMainSoundArchive(chartInput.Filename, input.Title, context);
                    else if (extension == @".BIN")
                        ConvertPopnChartFile(chartInput, input, maxIndex, context);
                }
                catch (Exception e)
                {
                    PrintConversionException(e);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Resolves the file path and difficulty index for one pop'n chart slot.
        /// </summary>
        private static ChartInput ResolveChartInput(SongInput input, int slot)
        {
            switch (slot)
            {
                case 0:
                    return new ChartInput { Filename = input.Filename, DifficultyIndex = 0 };
                case 1:
                    return CreateExistingChartInput(input, "_ep.bin", 3);
                case 2:
                    return CreateExistingChartInput(input, "_np.bin", 1);
                case 3:
                    return CreateExistingChartInput(input, "_hp.bin", 0);
                case 4:
                    return CreateExistingChartInput(input, "_op.bin", 2);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Creates a chart input when the expected chart file exists.
        /// </summary>
        private static ChartInput CreateExistingChartInput(SongInput input, string suffix, int difficultyIndex)
        {
            string filename = input.InputFolder + input.Title + suffix;
            if (!File.Exists(filename))
                return null;

            Console.WriteLine("Processing File: " + filename);
            return new ChartInput { Filename = filename, DifficultyIndex = difficultyIndex };
        }

        /// <summary>
        /// Converts the main 2DX sound archive and returns the longest sample index.
        /// </summary>
        private static int ConvertMainSoundArchive(string filename, string title, ConversionContext context)
        {
            using (MemoryStream source = new MemoryStream(File.ReadAllBytes(filename)))
            {
                Console.WriteLine("Converting Samples");
                Bemani2DX archive = Bemani2DX.Read(source);
                string databaseTitle = ResolveDatabaseTitle(title, context);
                return ConvertSounds(archive.Sounds, filename, DefaultSampleVolume, File.GetLastWriteTime(filename), null, context.OutputFolder, databaseTitle, false, context.Category, context.SoundOutputFormat);
            }
        }

        /// <summary>
        /// Converts one pop'n BIN chart file to PMS.
        /// </summary>
        private static void ConvertPopnChartFile(ChartInput chartInput, SongInput songInput, int maxIndex, ConversionContext context)
        {
            using (MemoryStream source = new MemoryStream(File.ReadAllBytes(chartInput.Filename)))
            {
                Popn archive = Popn.Read(source, context.UnitNumerator, context.UnitDenominator, maxIndex, context.Version);
                if (!ApplyDatabaseMetadata(archive.Charts[0], songInput.Title, chartInput.DifficultyIndex, context))
                    return;

                ConvertChart(archive.Charts[0], context.Config, songInput.Title, chartInput.DifficultyIndex, null, File.GetLastWriteTime(chartInput.Filename), context.Category, context.OutputFolder);
            }
        }

        /// <summary>
        /// Applies database metadata to one chart and returns false when the difficulty is disabled.
        /// </summary>
        private static bool ApplyDatabaseMetadata(Chart chart, string title, int difficultyIndex, ConversionContext context)
        {
            if (context.Database[title]["TITLE"] == "")
                return true;

            chart.Tags["TITLE"] = context.Database[title]["TITLE"];
            chart.Tags["ARTIST"] = context.Database[title]["ARTIST"];
            chart.Tags["GENRE"] = context.Database[title]["GENRE"];
            context.Category = String.Format("{0:00}", context.Database[title]["CATEGORY"]);

            string playerLevel = context.Database[title]["DIFFICULTYDP" + context.Config["IIDX"]["DIFFICULTY" + difficultyIndex.ToString()]];
            if (Int32.Parse(playerLevel) <= 0)
                return false;

            chart.Tags["PLAYLEVEL"] = playerLevel;
            return true;
        }

        /// <summary>
        /// Resolves the display title from the pop'n database and updates the category state.
        /// </summary>
        private static string ResolveDatabaseTitle(string title, ConversionContext context)
        {
            if (context.Database[title]["TITLE"] == "")
                return title;

            context.Category = String.Format("{0:00}", context.Database[title]["CATEGORY"]);
            return context.Database[title]["TITLE"];
        }

        /// <summary>
        /// Prints a conversion exception using the command's legacy formatting.
        /// </summary>
        private static void PrintConversionException(Exception e)
        {
            Console.WriteLine(e);
            Console.WriteLine();
            Console.WriteLine();
        }

        /// <summary>
        /// Reads PMS chart conversion options from configuration.
        /// </summary>
        private static ChartOptions LoadChartOptions(Configuration config, int index)
        {
            int difficulty = config["IIDX"].GetValue("Difficulty" + index.ToString());
            string title = config["BMS"]["Players" + config["IIDX"]["Players" + index.ToString()]] + " " + config["POPN"]["Difficulty" + difficulty.ToString()];
            if (index == 4)
                title = "BATTLE (3 BUTTON)";

            return new ChartOptions
            {
                QuantizeNotes = config["BMS"].GetValue("QuantizeNotes"),
                QuantizeMeasure = config["BMS"].GetValue("QuantizeMeasure"),
                Difficulty = difficulty,
                Title = title.Trim(),
                OutputRank = config["POPN"].GetValue("OutputRank"),
                EnableCommonBell = config["POPN"].GetBool("EnableCommonBell"),
                CommonBellPath = config["POPN"].GetString("CommonBellPath"),
                SoundOutputFormat = config["POPN"].GetString("SoundOutputFormat", SoundEncoderFactory.DefaultFormat)
            };
        }

        /// <summary>
        /// Applies the configured common bell path when enabled.
        /// </summary>
        private static void ApplyCommonBell(Chart chart, ChartOptions options)
        {
            if (options.EnableCommonBell)
                chart.Tags["COMMONBELLPATH"] = options.CommonBellPath;
        }

        /// <summary>
        /// Creates a BMS wrapper and initializes standard PMS tags.
        /// </summary>
        private static BMS CreateBms(Chart chart, ChartOptions options, string filename)
        {
            BMS bms = new BMS();
            bms.Charts = new Chart[] { chart };

            string name = ResolveChartName(chart, filename);
            bms.Charts[0].Tags["TITLE"] = name;
            CopyTag(chart, bms.Charts[0], "ARTIST");
            CopyTag(chart, bms.Charts[0], "GENRE");

            if (options.Difficulty > 0)
                bms.Charts[0].Tags["DIFFICULTY"] = options.Difficulty.ToString();

            bms.Charts[0].Tags["PLAYER"] = bms.Charts[0].Players > 1 ? "3" : "1";
            bms.Charts[0].Tags["RANK"] = options.OutputRank.ToString();
            return bms;
        }

        /// <summary>
        /// Copies a tag when it exists on the source chart.
        /// </summary>
        private static void CopyTag(Chart source, Chart target, string tag)
        {
            if (source.Tags.ContainsKey(tag))
                target.Tags[tag] = source.Tags[tag];
        }

        /// <summary>
        /// Resolves the display name for a PMS chart.
        /// </summary>
        private static string ResolveChartName(Chart chart, string filename)
        {
            if (chart.Tags.ContainsKey("TITLE") && chart.Tags["TITLE"] != "")
                return chart.Tags["TITLE"];

            return Path.GetFileNameWithoutExtension(Path.GetFileName(filename));
        }

        /// <summary>
        /// Builds the final PMS output path and creates its directory.
        /// </summary>
        private static string BuildChartOutputPath(Configuration config, string filename, string version, string dirPath, ref string name, string title)
        {
            string dirName = Path.GetFileNameWithoutExtension(Path.GetFileName(filename));
            dirPath = Path.Combine(dirPath, version, dirName);

            if (title != null && title.Length > 0)
                name += " [" + title + "]";

            name = Common.nameReplace(name);
            Common.SafeCreateDirectory(dirPath);
            return Path.Combine(dirPath, @"@" + name + ".pms");
        }

        /// <summary>
        /// Generates or assigns the PMS sample map.
        /// </summary>
        private static void ConfigureSampleMap(BMS bms, int[] map)
        {
            if (map == null)
                bms.GenerateSampleMap();
            else
                bms.SampleMap = map;
        }

        /// <summary>
        /// Quantizes note offsets when the configuration asks for it.
        /// </summary>
        private static void QuantizeChartNotes(Chart chart, int quantizeNotes)
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

        /// <summary>
        /// Writes a PMS file and preserves the source timestamp.
        /// </summary>
        private static void WritePmsFile(string output, MemoryStream mem, DateTime updateTime)
        {
            File.WriteAllBytes(output, mem.ToArray());
            SetFileTimes(output, updateTime);
        }

        /// <summary>
        /// Resolves the folder name used for converted sound output.
        /// </summary>
        private static string GetSoundSetName(string filename)
        {
            string name = Path.GetFileNameWithoutExtension(Path.GetFileName(filename));
            if (name.IndexOf("_pre") >= 0)
                name = name.Substring(0, name.Length - 4);

            return name;
        }

        /// <summary>
        /// Writes preview audio for a pre-2DX sound archive.
        /// </summary>
        private static void ConvertPreviewSound(Sound[] sounds, string targetPath, float volume, DateTime updateTime, string soundOutputFormat)
        {
            string output = Path.Combine(targetPath, @"preview" + @"." + SoundEncoderFactory.GetFileExtension(soundOutputFormat));
            ISoundEncoder encoder = SoundEncoderFactory.Create(soundOutputFormat);
            encoder.EncodeToFile(sounds[0], output, volume);
            SetFileTimes(output, updateTime);
        }

        /// <summary>
        /// Writes numbered OGG sample files and returns the longest sample index.
        /// </summary>
        private static int ConvertSampleSounds(Sound[] sounds, string targetPath, string index, float volume, DateTime updateTime, string soundOutputFormat)
        {
            targetPath = BuildSampleTargetPath(targetPath, index);
            int maxIndex = FindLongestSampleIndex(sounds);

            Parallel.For(0, sounds.Length, SampleEncodingParallelOptions, i =>
            {
                EncodeSampleSound(sounds[i], i + 1, targetPath, volume, updateTime, soundOutputFormat);
            });

            SetDirectoryTimes(targetPath, updateTime);
            return maxIndex;
        }

        /// <summary>
        /// Finds the one-based index of the longest decoded sample.
        /// </summary>
        private static int FindLongestSampleIndex(Sound[] sounds)
        {
            int maxIndex = -1;
            int maxLength = 0;
            for (int i = 0; i < sounds.Length; i++)
            {
                if (sounds[i].Data.Length > maxLength)
                {
                    maxIndex = i + 1;
                    maxLength = sounds[i].Data.Length;
                }
            }

            return maxIndex;
        }

        /// <summary>
        /// Encodes one numbered OGG sample file.
        /// </summary>
        private static void EncodeSampleSound(Sound sound, int sampleIndex, string targetPath, float volume, DateTime updateTime, string soundOutputFormat)
        {
            string sampleName = Util.ConvertToBMEString(sampleIndex, 4);
            string output = Path.Combine(targetPath, sampleName + @"." + SoundEncoderFactory.GetFileExtension(soundOutputFormat));
            try
            {
                ISoundEncoder encoder = SoundEncoderFactory.Create(soundOutputFormat);
                encoder.EncodeToFile(sound, output, volume);
                SetFileTimes(output, updateTime);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Failed to encode sample " + sampleName + ".", e);
            }
        }

        /// <summary>
        /// Creates the parallel encoding options used for independent sample files.
        /// </summary>
        private static ParallelOptions CreateSampleEncodingParallelOptions()
        {
            return new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
            };
        }

        /// <summary>
        /// Creates and returns the output directory for numbered sample files.
        /// </summary>
        private static string BuildSampleTargetPath(string targetPath, string index)
        {
            targetPath += "\\sounds";
            if (index != null)
                targetPath += "_" + index;

            Common.SafeCreateDirectory(targetPath);
            return targetPath;
        }

        /// <summary>
        /// Sets all file timestamps to match the source asset.
        /// </summary>
        private static void SetFileTimes(string path, DateTime updateTime)
        {
            File.SetCreationTime(path, updateTime);
            File.SetLastWriteTime(path, updateTime);
            File.SetLastAccessTime(path, updateTime);
        }

        /// <summary>
        /// Sets all directory timestamps to match the source asset.
        /// </summary>
        private static void SetDirectoryTimes(string path, DateTime updateTime)
        {
            Directory.SetCreationTime(path, updateTime);
            Directory.SetLastWriteTime(path, updateTime);
            Directory.SetLastAccessTime(path, updateTime);
        }
    }
}