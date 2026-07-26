using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds;
using Scharfrichter.Codec.Sounds.Encoders;
using Scharfrichter.Common;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const int DefaultBmsObjectBase = 62;
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
            public int BmsObjectBase;
            public string OutputFormat;
        }

        /// <summary>
        /// Converts pop'n music files passed to the PopnToBMS command.
        /// </summary>
        static public void Convert(string[] inArgs, long unitNumerator, long unitDenominator)
        {
            PopnToBMS_PackBmson.Clear();
            ConversionContext context = CreateContext(unitNumerator, unitDenominator);
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
        /// Supports bmson output when OutputFormat is set to "bmson" in config.
        /// </summary>
        static public bool ConvertChart(Chart chart, Configuration config, string filename, int index, int[] map, DateTime updateTime, string version = "", string dirPath = "", BmsonSoundLayout bmsonLayout = null)
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
                bms.SoundFolder = GetSoundSetName(filename);
                string name = ResolveChartName(chart, filename);
                string output = BuildChartOutputPath(config, filename, version, dirPath, ref name, options.Title, options.OutputFormat);

                if (IsBmsonOutput(options.OutputFormat))
                {
                    Bmson bmson = new Bmson();
                    bmson.SoundExtension = SoundEncoderFactory.GetFileExtension(options.SoundOutputFormat);
                    bmson.Charts = bms.Charts;
                    if (!bmson.Write(mem, bmsonLayout))
                        return false;
                }
                else
                {
                    ConfigureSampleMap(bms, map);
                    QuantizeChartNotes(bms.Charts[0], options.QuantizeNotes);

                    if (!bms.Write(mem, true))
                        return false;
                }

                WritePmsFile(output, mem, updateTime);
            }

            return true;
        }

        /// <summary>
        /// Encodes pop'n samples as OGG files and returns the longest sample index.
        /// Supports bmson output format for proper folder structure.
        /// </summary>
        static public int ConvertSounds(Sound[] sounds, string filename, float volume, DateTime updateTime, string INDEX = null, string outputFolder = "", string nameInfo = "", bool isPre2DX = false, string version = "", string soundOutputFormat = SoundEncoderFactory.DefaultFormat, string outputFormat = "bms")
        {
            string name = GetSoundSetName(filename, nameInfo);
            // Build output path: version\[DB_TITLE]\[ファイル名]
            // For previews, the filename subdirectory is omitted
            // e.g. "unknown\pops\pops" (not in DB, regular sounds)
            // e.g. "unknown\pops" (not in DB, preview)
            string targetPath;
            if (!string.IsNullOrEmpty(nameInfo))
            {
                string safeTitle = Common.nameReplace(nameInfo);
                if (!string.IsNullOrEmpty(version))
                    targetPath = Path.Combine(outputFolder, version, safeTitle);
                else
                    targetPath = Path.Combine(outputFolder, safeTitle);
                if (!isPre2DX)
                    targetPath = Path.Combine(targetPath, name);
            }
            else
            {
                if (!string.IsNullOrEmpty(version))
                    targetPath = Path.Combine(outputFolder, version, name);
                else
                    targetPath = Path.Combine(outputFolder, name);
            }
            Common.SafeCreateDirectory(targetPath);

            if (isPre2DX)
            {
                ConvertPreviewSound(sounds, targetPath, volume, updateTime, soundOutputFormat);
                return -1;
            }

            return ConvertSampleSounds(sounds, targetPath, INDEX, volume, updateTime, soundOutputFormat, outputFormat);
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
                Database = Common.LoadDB("PopnDB"),
                UnitNumerator = unitNumerator,
                UnitDenominator = unitDenominator,
                Version = 1,
                OutputFolder = config["POPN"]["Output"],
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
            Console.WriteLine("IFS");
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
                string ext = Path.GetExtension(args[i]).ToUpper();
                if (ext == @".IFS")
                {
                    ProcessIfsInput(args[i], context);
                }
                else if (ext == @".2DX")
                {
                    if (!Process2DXInput(args[i], context))
                        return;
                }
                else
                {
                    ShowUsage();
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.WriteLine();
                    continue;
                }
            }
        }

        /// <summary>
        /// Reads an IFS archive and processes its .2DX and .BIN entries.
        /// </summary>
        private static void ProcessIfsInput(string filename, ConversionContext context)
        {
            string inputFolder = Path.GetDirectoryName(filename) + "\\";
            DateTime archiveTime = File.GetLastWriteTime(filename);

            using (FileStream source = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                BemaniIFS archive = BemaniIFS.Read(source);

                // First pass: find the .2DX (main and preview) and .BIN entries
                byte[] mainTwoDXData = null;
                byte[] preTwoDXData = null;
                string twoDXName = null;
                Dictionary<string, byte[]> binEntries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

                foreach (BemaniIFS.Entry entry in archive.Entries)
                {
                    string ext = Path.GetExtension(entry.FullPath).ToUpper();
                    string name = Path.GetFileNameWithoutExtension(entry.FullPath);
                    if (ext == ".2DX")
                    {
                        bool isPreview = name.Length > 4 && name.Substring(name.Length - 4, 4) == "_pre";
                        if (isPreview)
                        {
                            preTwoDXData = entry.Data;
                        }
                        else if (mainTwoDXData == null)
                        {
                            mainTwoDXData = entry.Data;
                            twoDXName = name;
                        }
                    }
                    else if (ext == ".BIN")
                    {
                        binEntries[name] = entry.Data;
                    }
                }

                if (mainTwoDXData == null && preTwoDXData != null)
                {
                    // Only a preview 2DX exists; handle as a preview archive
                    Console.WriteLine("Converting Preview");
                    string baseName = twoDXName ?? Path.GetFileNameWithoutExtension(filename);
                    // Strip _pre suffix to get base name
                    if (baseName.Length > 4 && baseName.Substring(baseName.Length - 4, 4) == "_pre")
                        baseName = baseName.Substring(0, baseName.Length - 4);
                    string previewDbTitle = ResolveDatabaseTitle(baseName, context);
                    using (MemoryStream preStream = new MemoryStream(preTwoDXData))
                    {
                        Bemani2DX preArchive = Bemani2DX.Read(preStream);
                        ConvertSounds(preArchive.Sounds, baseName + "_pre.2dx", DefaultSampleVolume, archiveTime, null, context.OutputFolder, previewDbTitle, true, context.Category, context.SoundOutputFormat);
                    }
                    return;
                }

                if (mainTwoDXData == null)
                {
                    Console.WriteLine("Warning: No .2DX file found in IFS archive.");
                    return;
                }

                // Process the main .2DX as sound archive
                Console.WriteLine("Converting Samples");
                string title = twoDXName;
                string databaseTitle = ResolveDatabaseTitle(title, context);
                int maxIndex;
                using (MemoryStream soundStream = new MemoryStream(mainTwoDXData))
                {
                    Bemani2DX soundArchive = Bemani2DX.Read(soundStream);
                    maxIndex = ConvertSounds(soundArchive.Sounds, twoDXName, DefaultSampleVolume, archiveTime, null, context.OutputFolder, databaseTitle, false, context.Category, context.SoundOutputFormat);
                }

                // Process preview .2DX if present
                if (preTwoDXData != null)
                {
                    Console.WriteLine("Converting Preview");
                    using (MemoryStream preStream = new MemoryStream(preTwoDXData))
                    {
                        Bemani2DX preArchive = Bemani2DX.Read(preStream);
                        ConvertSounds(preArchive.Sounds, twoDXName + "_pre.2dx", DefaultSampleVolume, archiveTime, null, context.OutputFolder, databaseTitle, true, context.Category, context.SoundOutputFormat);
                    }
                }

                // Process each .BIN entry
                foreach (var kv in binEntries)
                {
                    string binTitle = kv.Key;
                    byte[] binData = kv.Value;

                    // Determine the difficulty suffix from the bin entry name
                    ChartInput chartInput = ResolveChartInputFromBinName(twoDXName, binTitle);
                    if (chartInput == null)
                        continue;

                    Console.WriteLine("Processing IFS Entry: " + binTitle + ".bin");
                    using (MemoryStream chartStream = new MemoryStream(binData))
                    {
                        // Use twoDXName (base name) for DB lookups and output path, not binTitle
                        ResolveCategoryAndVersion(twoDXName, context);
                        Popn popnArchive = Popn.Read(chartStream, context.UnitNumerator, context.UnitDenominator, maxIndex, context.Version);
                        if (!ApplyDatabaseMetadata(popnArchive.Charts[0], twoDXName, chartInput.DifficultyIndex, context))
                            continue;

                        ConvertChart(popnArchive.Charts[0], context.Config, twoDXName, chartInput.DifficultyIndex, null, archiveTime, context.Category, context.OutputFolder);
                    }
                }
            }
        }

        /// <summary>
        /// Maps a .BIN entry name (e.g. "pops_ep", "pops_np") to a difficulty slot.
        /// Returns null if the suffix is not recognized.
        /// </summary>
        private static ChartInput ResolveChartInputFromBinName(string baseName, string binTitle)
        {
            // The binTitle may include the baseName prefix; extract the suffix
            string suffix = "";
            if (binTitle.Length > baseName.Length && binTitle.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                suffix = binTitle.Substring(baseName.Length);
            else
                suffix = binTitle;

            switch (suffix.ToUpper())
            {
                case "_EP":
                case "EP":
                    return new ChartInput { Filename = binTitle + ".bin", DifficultyIndex = 3 };
                case "_NP":
                case "NP":
                    return new ChartInput { Filename = binTitle + ".bin", DifficultyIndex = 1 };
                case "_HP":
                case "HP":
                    return new ChartInput { Filename = binTitle + ".bin", DifficultyIndex = 0 };
                case "_OP":
                case "OP":
                    return new ChartInput { Filename = binTitle + ".bin", DifficultyIndex = 2 };
                case "_BP":
                case "BP":
                    return new ChartInput { Filename = binTitle + ".bin", DifficultyIndex = 4 };
                default:
                    return null;
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
            string outputFormat = context.Config["POPN"].GetString("OutputFormat", "bms");
            bool optimizeBmson = IsBmsonOutput(outputFormat) && context.Config["POPN"].GetBool("OptimizeBmsonSounds");

            Sound[] sounds = null;
            int maxIndex = -1;
            DateTime soundUpdateTime = input.UpdateTime;
            string databaseTitle = ResolveDatabaseTitle(input.Title, context);

            // First pass: process 2DX to get sounds
            for (int slot = 0; slot < 5; slot++)
            {
                ChartInput chartInput = ResolveChartInput(input, slot);
                if (chartInput == null)
                    continue;

                try
                {
                    string extension = Path.GetExtension(chartInput.Filename).ToUpper();
                    if (extension == @".2DX")
                    {
                        Console.WriteLine("Converting Samples");
                        using (MemoryStream source = new MemoryStream(File.ReadAllBytes(chartInput.Filename)))
                        {
                            Bemani2DX archive = Bemani2DX.Read(source);
                            sounds = archive.Sounds;
                            soundUpdateTime = File.GetLastWriteTime(chartInput.Filename);
                            if (!optimizeBmson)
                            {
                                maxIndex = ConvertSounds(sounds, chartInput.Filename, DefaultSampleVolume, soundUpdateTime, null, context.OutputFolder, databaseTitle, false, context.Category, context.SoundOutputFormat);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    PrintConversionException(e);
                    return false;
                }
            }

            // Second pass: process BIN charts
            for (int slot = 0; slot < 5; slot++)
            {
                ChartInput chartInput = ResolveChartInput(input, slot);
                if (chartInput == null)
                    continue;

                try
                {
                    string extension = Path.GetExtension(chartInput.Filename).ToUpper();
                    if (extension == @".BIN")
                    {
                        ResolveCategoryAndVersion(input.Title, context);
                        using (MemoryStream source = new MemoryStream(File.ReadAllBytes(chartInput.Filename)))
                        {
                            Popn archive = Popn.Read(source, context.UnitNumerator, context.UnitDenominator, maxIndex, context.Version);
                            if (!ApplyDatabaseMetadata(archive.Charts[0], input.Title, chartInput.DifficultyIndex, context))
                                continue;

                            if (optimizeBmson && sounds != null)
                            {
                                PopnToBMS_PackBmson.Register(archive.Charts[0], context.Config, input.Title,
                                    chartInput.DifficultyIndex, File.GetLastWriteTime(chartInput.Filename),
                                    context.Category, context.OutputFolder);
                            }
                            else
                            {
                                ConvertChart(archive.Charts[0], context.Config, input.Title,
                                    chartInput.DifficultyIndex, null,
                                    File.GetLastWriteTime(chartInput.Filename), context.Category, context.OutputFolder);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    PrintConversionException(e);
                    return false;
                }
            }

            // Finalize packed bmson
            if (optimizeBmson && sounds != null && PopnToBMS_PackBmson.PendingCount > 0)
            {
                PopnToBMS_PackBmson.Finalize(sounds, DefaultSampleVolume, soundUpdateTime, context.SoundOutputFormat);
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
        /// Resolves the category string from the database and updates the context version from it.
        /// When the song is not in the database, the category is set to "unknown" and version to 0.
        /// </summary>
        private static void ResolveCategoryAndVersion(string title, ConversionContext context)
        {
            if (context.Database[title]["TITLE"] == "")
            {
                context.Category = "unknown";
                context.Version = 0; // auto-detect
                return;
            }

            string rawCategory = context.Database[title]["CATEGORY"];
            int categoryValue;
            if (Int32.TryParse(rawCategory, out categoryValue) && categoryValue > 0)
            {
                context.Version = categoryValue;
            }
            else
            {
                context.Version = 1;
            }
            context.Category = String.Format("{0:00}", categoryValue > 0 ? categoryValue : 1);
        }

        /// <summary>
        /// Converts one pop'n BIN chart file to PMS.
        /// </summary>
        private static void ConvertPopnChartFile(ChartInput chartInput, SongInput songInput, int maxIndex, ConversionContext context)
        {
            // Resolve version from category before reading the chart
            ResolveCategoryAndVersion(songInput.Title, context);

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
        /// When FILEEASY/FILENORMAL/FILEHYPER/FILEEX entries exist in the database,
        /// only charts whose source filename matches the FILE* value are output.
        /// </summary>
        private static bool ApplyDatabaseMetadata(Chart chart, string title, int difficultyIndex, ConversionContext context)
        {
            if (context.Database[title]["TITLE"] == "")
                return true;

            // Check FILE* filtering: if the DB has FILE* entries, only output matching difficulties
            if (!FilterByFileEntry(title, difficultyIndex, context))
                return false;

            chart.Tags["TITLE"] = context.Database[title]["TITLE"];
            chart.Tags["ARTIST"] = context.Database[title]["ARTIST"];
            chart.Tags["GENRE"] = context.Database[title]["GENRE"];

            string playerLevel = context.Database[title]["DIFFICULTYDP" + context.Config["IIDX"]["DIFFICULTY" + difficultyIndex.ToString()]];
            if (playerLevel == "" || Int32.Parse(playerLevel) <= 0)
                return false;

            chart.Tags["PLAYLEVEL"] = playerLevel;
            return true;
        }

        /// <summary>
        /// Maps a difficulty index (0-4) to the corresponding FILE* key name in PopnDB.
        /// Returns null for battle (index 4) or unrecognized indices.
        /// </summary>
        private static string GetFileKeyForDifficulty(ConversionContext context, int difficultyIndex)
        {
            // difficultyIndex -> difficulty value -> POPN difficulty name -> FILE* key
            string diffNum = context.Config["IIDX"]["DIFFICULTY" + difficultyIndex.ToString()];
            if (string.IsNullOrEmpty(diffNum))
                return null;

            string difficultyName = context.Config["POPN"]["Difficulty" + diffNum];
            if (string.IsNullOrEmpty(difficultyName))
                return null;

            // FILEEASY, FILENORMAL, FILEHYPER, FILEEX
            return "FILE" + difficultyName.ToUpper();
        }

        /// <summary>
        /// When the PopnDB entry for a song contains FILE* entries, only charts whose
        /// filename matches the FILE* value's last path component are allowed.
        /// If no FILE* entries exist in the DB, all difficulties pass (return true).
        /// </summary>
        private static bool FilterByFileEntry(string title, int difficultyIndex, ConversionContext context)
        {
            // Check if any FILE* entry exists for this song
            bool hasAnyFileEntry = false;
            string[] fileKeys = { "FILEEASY", "FILENORMAL", "FILEHYPER", "FILEEX" };
            foreach (string key in fileKeys)
            {
                if (context.Database[title][key] != "")
                {
                    hasAnyFileEntry = true;
                    break;
                }
            }

            // If no FILE* entries exist at all, allow all difficulties
            if (!hasAnyFileEntry)
                return true;

            // Get the FILE* key for this difficulty
            string fileKey = GetFileKeyForDifficulty(context, difficultyIndex);
            if (fileKey == null)
                return true; // BATTLE or unknown difficulty - allow by default

            string fileValue = context.Database[title][fileKey];
            if (fileValue == "")
                return false; // This difficulty has an empty FILE* entry -> skip

            // Extract the filename portion (last path component) from the FILE* value
            // e.g. "popn1/pops" -> "pops"
            string expectedFilename = Path.GetFileName(fileValue);

            // Compare case-insensitively against the source title (filename without extension)
            return String.Equals(expectedFilename, title, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves the display title from the pop'n database and updates the category and version state.
        /// </summary>
        private static string ResolveDatabaseTitle(string title, ConversionContext context)
        {
            ResolveCategoryAndVersion(title, context);
            if (context.Database[title]["TITLE"] == "")
                return title;

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
                SoundOutputFormat = config["POPN"].GetString("SoundOutputFormat", SoundEncoderFactory.DefaultFormat),
                BmsObjectBase = GetBmsObjectBase(config),
                OutputFormat = config["POPN"].GetString("OutputFormat", "bms"),
            };
        }

        /// <summary>
        /// Returns the BMS object identifier base from configuration.
        /// </summary>
        private static int GetBmsObjectBase(Configuration config)
        {
            if (config == null)
                return DefaultBmsObjectBase;

            int configuredBase = config["POPN"].GetValue("BmsObjectBase", DefaultBmsObjectBase);
            return configuredBase == 36 ? 36 : 62;
        }

        /// <summary>
        /// Returns true when the configured chart output format is bmson.
        /// </summary>
        private static bool IsBmsonOutput(string outputFormat)
        {
            return String.Equals(outputFormat, "bmson", StringComparison.OrdinalIgnoreCase);
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
            bms.BmsObjectBase = options.BmsObjectBase;
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
        /// Uses the chart's display name (TITLE tag) as the output folder name.
        /// </summary>
        private static string BuildChartOutputPath(Configuration config, string filename, string version, string dirPath, ref string name, string title, string outputFormat)
        {
            // Use the display name (DB TITLE) for the folder name instead of the filename stem
            string dirName = Common.nameReplace(name);
            dirPath = Path.Combine(dirPath, version, dirName);

            if (title != null && title.Length > 0)
                name += " [" + title + "]";

            name = Common.nameReplace(name);
            Common.SafeCreateDirectory(dirPath);

            string extension = IsBmsonOutput(outputFormat) ? ".bmson" : ".pms";
            return Path.Combine(dirPath, @"@" + name + extension);
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
        /// For pop'n music, the sound folder is always named after the source filename
        /// (e.g. pops.ifs -> "pops", pops_2nd.ifs -> "pops_2nd") rather than the database title.
        /// Strips the "_pre" suffix for preview archives.
        /// </summary>
        private static string GetSoundSetName(string filename, string nameInfo = "")
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
        private static int ConvertSampleSounds(Sound[] sounds, string targetPath, string index, float volume, DateTime updateTime, string soundOutputFormat, string outputFormat = "bms")
        {
            targetPath = BuildSampleTargetPath(targetPath, index, outputFormat);
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
        /// For pop'n music, numbered sample files (0001.ogg etc.) are placed directly
        /// in the target path without a "sounds" subdirectory.
        /// </summary>
        private static string BuildSampleTargetPath(string targetPath, string index, string outputFormat = "bms")
        {
            if (IsBmsonOutput(outputFormat))
            {
                targetPath = Path.Combine(targetPath, Bmson.GetSoundFolder(index));
            }

            // For pop'n BMS output, place samples directly in targetPath (no \sounds subdirectory).
            // The targetPath already points to the filename-based folder (e.g. pops/).
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