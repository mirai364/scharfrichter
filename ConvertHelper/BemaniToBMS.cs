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
using System.Xml.Linq;
using System.Threading.Tasks;

namespace ConvertHelper
{
    /// <summary>
    /// Converts beatmania-family chart and sound files to BMS-compatible outputs.
    /// </summary>
    static public class BemaniToBMS
    {
        private const float DefaultSampleVolume = 0.6f;
        private const float FullSampleVolume = 1.0f;
        private const int PlayVideoPoorFlag = 0x02;
        private const int DefaultBmsObjectBase = 62;
        private const double MaxPackedTrackSeconds = 600.0;
        private const double PackedSoundEventGapSeconds = 0.002;
        private const double PackedSoundTailPaddingSeconds = 1.000;
        private static readonly ParallelOptions SampleEncodingParallelOptions = CreateSampleEncodingParallelOptions();
        private static readonly ParallelOptions PackedSoundEncodingParallelOptions = CreatePackedSoundEncodingParallelOptions();

        /// <summary>
        /// Holds configuration and runtime options shared across one conversion run.
        /// </summary>
        private sealed class ConversionContext
        {
            public Configuration Config;
            public Configuration Database;
            public long UnitNumerator;
            public long UnitDenominator;
            public bool UseRenderAutoTip;
            public string OutputFolder;
            public string SoundOutputFormat;
            public Dictionary<string, InputFileInfo> VirtualInputs = new Dictionary<string, InputFileInfo>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Describes a parsed input file and metadata derived from its name.
        /// </summary>
        private sealed class InputFileInfo
        {
            public string Filename;
            public string DatabaseName;
            public string Version;
            public string Index;
            public bool IsPre2DX;
            public bool IsVirtual;
            public DateTime UpdateTime;
            public byte[] Data;
        }

        /// <summary>
        /// Holds BMS chart-writing options read from configuration.
        /// </summary>
        private sealed class PendingBmsonChart
        {
            public Chart Chart;
            public Configuration Config;
            public string Filename;
            public int Index;
            public DateTime UpdateTime;
            public string Version;
        }

        private sealed class PackedSoundEvent
        {
            public List<Entry> Entries = new List<Entry>();
            public int SampleIndex;
            public double StartSeconds;
            public double EndSeconds;
            public int TrackIndex;
        }

        private sealed class PackedSoundTrackBuild
        {
            public List<PackedSoundEvent> Events = new List<PackedSoundEvent>();
            public double EndSeconds;
            public string Name;
        }

        private static readonly List<PendingBmsonChart> PendingBmsonCharts = new List<PendingBmsonChart>();
        private static readonly Dictionary<Chart, BmsonSoundLayout> BmsonSoundLayouts = new Dictionary<Chart, BmsonSoundLayout>();
        private static readonly HashSet<string> ConvertedSoundFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private sealed class ChartOptions
        {
            public int QuantizeNotes;
            public int QuantizeMeasure;
            public int Difficulty;
            public string Title;
            public string MovieFolder;
            public bool IsSameFolderMovie;
            public bool UseMovie;
            public int OutputRank;
            public string OutputFormat;
            public string SoundOutputFormat;
            public bool UseBgaImage;
            public string BgaImageGraphicFolder;
            public string BgaImageOutputName;
            public int BmsObjectBase;
        }

        /// <summary>
        /// Converts all supported input files passed to the BemaniToBMS command.
        /// </summary>
        static public void Convert(string[] inArgs, long unitNumerator, long unitDenominator, bool idUseRenderAutoTip = false)
        {
            ConversionContext context = CreateContext(unitNumerator, unitDenominator, idUseRenderAutoTip);
            ShowSplash(context);

            string[] args = PrepareInputArguments(inArgs);
            if (args.Length == 0)
                ShowUsage();

            ProcessFiles(args, context);

            Console.WriteLine("BemaniToBMS finished.");
        }

        /// <summary>
        /// Converts every chart in an archive and retries with auto-tip rendering when sample limits are exceeded.
        /// </summary>
        static public void ConvertArchive(Archive archive, Configuration config, string filename, DateTime updateTime, string version = "", bool idUseRenderAutoTip = false)
        {
            int chartIndex;
            int sampleCount;
            int sampleLimit = GetBmsObjectLimit(config);
            if (!IsBmsonOutput(config) && !idUseRenderAutoTip && ArchiveExceedsBmsSampleLimit(archive, sampleLimit, out chartIndex, out sampleCount))
            {
                RetryWithRenderAutoTip(filename, chartIndex, sampleCount, sampleLimit);
                return;
            }

            bool isSuccess = ConvertArchiveCharts(archive, config, filename, updateTime, version);
            if (!isSuccess && !idUseRenderAutoTip)
                RetryWithRenderAutoTip(filename, -1, -1, sampleLimit);
        }

        /// <summary>
        /// Checks whether any chart needs more BMS WAV tags than the format can represent.
        /// </summary>
        private static bool ArchiveExceedsBmsSampleLimit(Archive archive, int sampleLimit, out int chartIndex, out int sampleCount)
        {
            chartIndex = -1;
            sampleCount = 0;

            for (int i = 0; i < archive.ChartCount; i++)
            {
                Chart chart = archive.Charts[i];
                if (chart == null)
                    continue;

                int usedSamples = CountUniqueMarkerSamples(chart);
                if (usedSamples > sampleLimit)
                {
                    chartIndex = i;
                    sampleCount = usedSamples;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Counts the unique sound IDs that are actually triggered by marker entries.
        /// </summary>
        private static int CountUniqueMarkerSamples(Chart chart)
        {
            HashSet<int> usedSamples = new HashSet<int>();
            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type != EntryType.Marker)
                    continue;

                int value = (int)((double)entry.Value);
                if (value > 0)
                    usedSamples.Add(value);
            }

            return usedSamples.Count;
        }

        /// <summary>
        /// Re-runs conversion with rendered auto-tip samples after the normal BMS sample limit is exceeded.
        /// </summary>
        private static void RetryWithRenderAutoTip(string filename, int chartIndex, int sampleCount, int sampleLimit)
        {
            Console.WriteLine("");
            Console.WriteLine("");
            if (chartIndex >= 0)
                Console.WriteLine("Chart " + chartIndex.ToString() + " uses " + sampleCount.ToString() + " sound sources, which is larger than the BMS limit of " + sampleLimit.ToString() + ".");
            else
                Console.WriteLine("Because the number of sound sources is larger than " + sampleLimit.ToString() + ", change the setting and re-execute.");
            Console.WriteLine("*------------------------------------------------------*");
            Convert(new string[] { filename }, ConverterTiming.StandardNumerator, ConverterTiming.StandardDenominator, true);
        }

        /// <summary>
        /// Writes one chart as a BMS file using the configured metadata, sample map, and quantization settings.
        /// </summary>
        static public bool ConvertChart(Chart chart, Configuration config, string filename, int index, int[] map, DateTime updateTime, string version = "")
        {
            if (config == null)
                config = Configuration.LoadIIDXConfig(Common.configFileName);

            ChartOptions options = LoadChartOptions(config, index);
            if (options.QuantizeMeasure > 0)
                chart.QuantizeMeasureLengths(options.QuantizeMeasure);

            using (MemoryStream mem = new MemoryStream())
            {
                BMS bms = CreateBms(chart, options, filename);
                bms.SoundExtension = SoundEncoderFactory.GetFileExtension(options.SoundOutputFormat);
                string name = GetChartName(chart, filename);
                string dirPath = BuildChartDirectory(config, version, name);
                string output = BuildChartOutputPath(dirPath, ref name, options.Title, bms.Charts[0], options.OutputFormat);

                ConfigureMovieOutput(bms.Charts[0], chart, options, dirPath);
                QuantizeChartNotes(bms.Charts[0], options.QuantizeNotes);

                if (IsBmsonOutput(options.OutputFormat))
                {
                    Bmson bmson = new Bmson();
                    bmson.SoundExtension = SoundEncoderFactory.GetFileExtension(options.SoundOutputFormat);
                    bmson.Charts = bms.Charts;
                    if (!bmson.Write(mem, GetBmsonSoundLayout(chart)))
                        return false;
                }
                else
                {
                    ConfigureSampleMap(bms, map);
                    if (!bms.Write(mem, true))
                        return false;
                }

                WriteChartFile(output, mem, updateTime);
            }

            return true;
        }

        /// <summary>
        /// Encodes a sound set as OGG samples or a preview file for pre-2DX assets.
        /// </summary>
        static public void ConvertSounds(Sound[] sounds, string filename, float volume, DateTime updateTime, string INDEX = null, string outputFolder = "", string nameInfo = "", bool isPre2DX = false, string version = "", string outputFormat = "bms", string soundOutputFormat = SoundEncoderFactory.DefaultFormat)
        {
            string name = GetSoundSetName(filename, nameInfo);
            string targetPath = Path.Combine(outputFolder, version, name);
            Common.SafeCreateDirectory(targetPath);

            if (isPre2DX)
            {
                ConvertPreviewSound(sounds, targetPath, volume, updateTime, soundOutputFormat);
                return;
            }

            ConvertSampleSounds(sounds, targetPath, INDEX, volume, updateTime, outputFormat, soundOutputFormat);
        }

        /// <summary>
        /// Creates the shared conversion context from command options and configuration files.
        /// </summary>
        private static ConversionContext CreateContext(long unitNumerator, long unitDenominator, bool useRenderAutoTip)
        {
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            return new ConversionContext
            {
                Config = config,
                Database = Common.LoadDB(),
                UnitNumerator = unitNumerator,
                UnitDenominator = unitDenominator,
                UseRenderAutoTip = useRenderAutoTip,
                OutputFolder = null,
                SoundOutputFormat = config["IIDX"].GetString("SoundOutputFormat", SoundEncoderFactory.DefaultFormat)
            };
        }

        /// <summary>
        /// Prints the converter banner and timing information.
        /// </summary>
        private static void ShowSplash(ConversionContext context)
        {
            int quantizeMeasure = context.Config["BMS"].GetValue("QuantizeMeasure");

            Splash.Show("Bemani to BeMusic Script");
            Console.WriteLine("Timing: " + context.UnitNumerator.ToString() + "/" + context.UnitDenominator.ToString());
            Console.WriteLine("Measure Quantize: " + quantizeMeasure.ToString());
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

        private static int ParsePositiveInt(string value, string name)
        {
            int result;
            if (!Int32.TryParse(value, out result) || result <= 0)
                throw new ArgumentException("Invalid " + name + ": " + value);

            return result;
        }

        /// <summary>
        /// Prints command usage information.
        /// </summary>
        private static void ShowUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: BemaniToBMS <input file>");
            Console.WriteLine();
            Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
            Console.WriteLine();
            Console.WriteLine("Supported formats:");
            Console.WriteLine("1, 2DX, S3P, CS, SD9, SSP");
        }

        /// <summary>
        /// Processes each existing input file while isolating per-file errors.
        /// </summary>
        private static void ProcessFiles(string[] args, ConversionContext context)
        {
            for (int i = 0; i < args.Length; i++)
            {
                try
                {
                    if (File.Exists(args[i]))
                        ProcessFile(args[i], context);
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0} Exception caught." + args[i], e);
                }
            }
        }

        /// <summary>
        /// Dispatches one input file to the converter for its file extension.
        /// </summary>
        private static void ProcessFile(string filename, ConversionContext context)
        {
            Console.WriteLine();
            Console.WriteLine("Processing File: " + filename);

            if (String.Equals(Path.GetExtension(filename), ".ifs", StringComparison.OrdinalIgnoreCase))
            {
                ProcessIfsFile(filename, context);
                return;
            }

            InputFileInfo input = CreateInputFileInfo(filename, context);
            ProcessInput(input, context);
        }

        /// <summary>
        /// Dispatches one prepared input payload to the converter for its file extension.
        /// </summary>
        private static void ProcessInput(InputFileInfo input, ConversionContext context)
        {
            string filename = input.Filename;
            using (MemoryStream source = new MemoryStream(input.Data))
            {
                switch (Path.GetExtension(filename).ToUpper())
                {
                    case @".1":
                        ConvertBemani1Archive(source, input, context);
                        break;
                    case @".2DX":
                        if (ConvertedSoundFiles.Contains(GetInputIdentity(input)))
                            return;
                        EnsurePendingChartForSound(input, context);
                        if (ConvertedSoundFiles.Contains(GetInputIdentity(input)))
                            return;
                        Convert2DXSamples(source, input, context);
                        ConvertedSoundFiles.Add(GetInputIdentity(input));
                        break;
                    case @".S3P":
                        if (ConvertedSoundFiles.Contains(GetInputIdentity(input)))
                            return;
                        EnsurePendingChartForSound(input, context);
                        if (ConvertedSoundFiles.Contains(GetInputIdentity(input)))
                            return;
                        ConvertS3PSamples(source, input, context);
                        ConvertedSoundFiles.Add(GetInputIdentity(input));
                        break;
                    case @".CS":
                        ConvertChart(BeatmaniaIIDXCSNew.Read(source), context.Config, filename, -1, null, input.UpdateTime);
                        break;
                    case @".CS2":
                        ConvertChart(BeatmaniaIIDXCSOld.Read(source), context.Config, filename, -1, null, input.UpdateTime);
                        break;
                    case @".CS5":
                        ConvertChart(Beatmania5Key.Read(source), context.Config, filename, -1, null, input.UpdateTime);
                        break;
                    case @".CS9":
                        break;
                    case @".SD9":
                        ConvertSD9Sound(source, input, context.SoundOutputFormat);
                        break;
                    case @".SSP":
                        ConvertSounds(BemaniSSP.Read(source).Sounds, filename, FullSampleVolume, input.UpdateTime, null, "", "", false, "", context.Config["IIDX"].GetString("OutputFormat", "bms"), context.SoundOutputFormat);
                        break;
                }
            }
        }

        /// <summary>
        /// Reads an IFS archive and converts supported chart and sound entries without extracting them.
        /// </summary>
        private static void ProcessIfsFile(string filename, ConversionContext context)
        {
            EnsureOutputFolder(filename, context);

            List<InputFileInfo> inputs = ReadIfsInputs(filename, context);
            if (inputs.Count == 0)
                return;

            foreach (InputFileInfo input in inputs)
                context.VirtualInputs[GetInputIdentity(input)] = input;

            foreach (InputFileInfo input in SortIfsInputs(inputs))
            {
                try
                {
                    Console.WriteLine();
                    Console.WriteLine("Processing IFS Entry: " + input.Filename);
                    ProcessInput(input, context);
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0} Exception caught." + input.Filename, e);
                }
            }
        }

        /// <summary>
        /// Creates virtual converter inputs for supported files stored in an IFS archive.
        /// </summary>
        private static List<InputFileInfo> ReadIfsInputs(string filename, ConversionContext context)
        {
            List<InputFileInfo> inputs = new List<InputFileInfo>();
            DateTime archiveUpdateTime = File.GetLastWriteTime(filename);
            string archiveDirectory = Path.GetDirectoryName(filename);

            using (FileStream source = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                BemaniIFS archive = BemaniIFS.Read(source);
                foreach (BemaniIFS.Entry entry in archive.Entries)
                {
                    if (!IsSupportedInputExtension(entry.FullPath))
                        continue;

                    string virtualFilename = Path.Combine(archiveDirectory, entry.FullPath);
                    DateTime updateTime = entry.TimeStamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(entry.TimeStamp).LocalDateTime : archiveUpdateTime;
                    inputs.Add(CreateInputFileInfo(virtualFilename, entry.Data, updateTime, true, context));
                }
            }

            return inputs;
        }

        /// <summary>
        /// Orders IFS payloads so charts register before sample archives in bmson mode.
        /// </summary>
        private static List<InputFileInfo> SortIfsInputs(List<InputFileInfo> inputs)
        {
            List<InputFileInfo> result = new List<InputFileInfo>(inputs);
            result.Sort(delegate (InputFileInfo left, InputFileInfo right)
            {
                int leftOrder = GetIfsInputOrder(left.Filename);
                int rightOrder = GetIfsInputOrder(right.Filename);
                int orderCompare = leftOrder.CompareTo(rightOrder);
                if (orderCompare != 0)
                    return orderCompare;

                return StringComparer.OrdinalIgnoreCase.Compare(left.Filename, right.Filename);
            });
            return result;
        }

        private static int GetIfsInputOrder(string filename)
        {
            string extension = Path.GetExtension(filename).ToUpper();
            if (extension == ".1")
                return 0;
            if (extension == ".CS" || extension == ".CS2" || extension == ".CS5")
                return 1;
            if (extension == ".2DX" || extension == ".S3P" || extension == ".SSP" || extension == ".SD9")
                return 2;
            return 3;
        }

        private static bool IsSupportedInputExtension(string filename)
        {
            switch (Path.GetExtension(filename).ToUpper())
            {
                case @".1":
                case @".2DX":
                case @".S3P":
                case @".CS":
                case @".CS2":
                case @".CS5":
                case @".SD9":
                case @".SSP":
                    return true;
            }

            return false;
        }
        /// <summary>
        /// Reads file bytes and parses database keys, version, index, and pre-2DX state from the name.
        /// </summary>
        private static InputFileInfo CreateInputFileInfo(string filename, ConversionContext context)
        {
            EnsureOutputFolder(filename, context);
            return CreateInputFileInfo(filename, File.ReadAllBytes(filename), File.GetLastWriteTime(filename), false, context);
        }

        private static InputFileInfo CreateInputFileInfo(string filename, byte[] data, DateTime updateTime, bool isVirtual, ConversionContext context)
        {
            string databaseName = Path.GetFileNameWithoutExtension(filename);
            string version = databaseName.Substring(0, 2);
            bool isPre2DX = false;
            string index = null;

            if (databaseName.Contains("pre"))
            {
                isPre2DX = true;
                databaseName = databaseName.Substring(0, 5);
            }

            if (databaseName.Length > 5)
            {
                index = databaseName.Substring(5);
                databaseName = databaseName.Substring(0, 5);
            }

            while (databaseName.StartsWith("0"))
                databaseName = databaseName.Substring(1);

            return new InputFileInfo
            {
                Filename = filename,
                DatabaseName = databaseName,
                Version = version,
                Index = index,
                IsPre2DX = isPre2DX,
                IsVirtual = isVirtual,
                UpdateTime = updateTime,
                Data = data
            };
        }

        private static string GetInputIdentity(InputFileInfo input)
        {
            if (input.IsVirtual)
                return input.Filename;

            return Path.GetFullPath(input.Filename);
        }
        /// <summary>
        /// Resolves the output folder from configuration or the input file directory.
        /// </summary>
        private static void EnsureOutputFolder(string filename, ConversionContext context)
        {
            if (context.OutputFolder == null)
                context.OutputFolder = context.Config["IIDX"]["Output"];

            if (context.OutputFolder == "")
                context.OutputFolder = Path.GetDirectoryName(filename) + "\\";
        }

        /// <summary>
        /// Reads and converts a .1 chart archive.
        /// </summary>
        private static void ConvertBemani1Archive(MemoryStream source, InputFileInfo input, ConversionContext context)
        {
            Dictionary<int, int> ignore = CreateIgnoreMapForAutoTip(input, context);
            Bemani1 archive = Bemani1.Read(source, context.UnitNumerator, context.UnitDenominator, ignore);

            ApplyDatabaseMetadata(archive, input.DatabaseName, context.Config, context.Database, context.UseRenderAutoTip);
            ConvertArchive(archive, context.Config, input.Filename, input.UpdateTime, input.Version, context.UseRenderAutoTip);
            ConvertRequiredSoundFiles(archive, input, context);
        }

        private static void ConvertRequiredSoundFiles(Archive archive, InputFileInfo chartInput, ConversionContext context)
        {
            if (!IsBmsonOutput(context.Config) || chartInput.IsPre2DX)
                return;

            HashSet<string> soundSets = GetRequiredSoundSets(archive);
            foreach (string soundSet in soundSets)
            {
                InputFileInfo soundInput = ResolveSoundInput(chartInput, soundSet, context);
                if (soundInput == null)
                {
                    Console.WriteLine("Warning: Required sound file was not found for " + Path.GetFileName(chartInput.Filename) + " keyset " + soundSet + ". Expected a .s3p or .2dx file in the same folder or IFS archive.");
                    continue;
                }

                ProcessInput(soundInput, context);
            }
        }

        private static HashSet<string> GetRequiredSoundSets(Archive archive)
        {
            HashSet<string> soundSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < archive.ChartCount; i++)
            {
                Chart chart = archive.Charts[i];
                if (chart != null)
                    soundSets.Add(GetChartSoundSet(chart));
            }

            if (soundSets.Count == 0)
                soundSets.Add("0");

            return soundSets;
        }

        private static InputFileInfo ResolveSoundInput(InputFileInfo chartInput, string soundSet, ConversionContext context)
        {
            string directory = Path.GetDirectoryName(chartInput.Filename);
            string baseName = Path.GetFileNameWithoutExtension(chartInput.Filename);
            string suffix = soundSet == "0" ? "" : soundSet;
            string[] extensions = { ".s3p", ".2dx" };

            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(directory, baseName + suffix + extension);
                InputFileInfo input = ResolveInput(candidate, chartInput.IsVirtual, context);
                if (input != null)
                    return input;
            }

            return null;
        }

        private static InputFileInfo ResolveInput(string filename, bool preferVirtual, ConversionContext context)
        {
            InputFileInfo input;
            if (preferVirtual && context.VirtualInputs.TryGetValue(filename, out input))
                return input;
            if (File.Exists(filename))
                return CreateInputFileInfo(filename, context);
            if (context.VirtualInputs.TryGetValue(filename, out input))
                return input;

            return null;
        }
        private static void EnsurePendingChartForSound(InputFileInfo soundInput, ConversionContext context)
        {
            if (!IsBmsonOutput(context.Config))
                return;

            string soundSet = GetSoundSet(soundInput);
            if (HasPendingChartForSound(soundInput, context, soundSet))
                return;

            InputFileInfo chartInput = ResolveChartInputForSound(soundInput, context);
            if (chartInput == null)
            {
                Console.WriteLine("Warning: Sound file " + Path.GetFileName(soundInput.Filename) + " requires a matching .1 file in the same folder or IFS archive for bmson conversion.");
                return;
            }

            ProcessInput(chartInput, context);
            if (!HasPendingChartForSound(soundInput, context, soundSet))
            {
                Console.WriteLine("Warning: Matching .1 file was found, but no chart uses keyset " + soundSet + " for " + Path.GetFileName(soundInput.Filename) + ".");
                return;
            }
        }

        private static bool HasPendingChartForSound(InputFileInfo soundInput, ConversionContext context, string soundSet)
        {
            string title = "";
            if (context.Database[soundInput.DatabaseName]["TITLE"] != "")
                title = context.Database[soundInput.DatabaseName]["TITLE"];

            string name = GetSoundSetName(soundInput.Filename, title);
            string targetPath = Path.Combine(context.OutputFolder, soundInput.Version, name);
            return FindPendingBmsonCharts(targetPath, soundSet).Count > 0;
        }

        private static InputFileInfo ResolveChartInputForSound(InputFileInfo soundInput, ConversionContext context)
        {
            string directory = Path.GetDirectoryName(soundInput.Filename);
            string baseName = Path.GetFileNameWithoutExtension(soundInput.Filename);
            if (baseName.Length > 5)
                baseName = baseName.Substring(0, 5);

            string candidate = Path.Combine(directory, baseName + ".1");
            return ResolveInput(candidate, soundInput.IsVirtual, context);
        }
        private static string GetSoundSet(InputFileInfo input)
        {
            if (String.IsNullOrEmpty(input.Index))
                return "0";

            return input.Index;
        }

        private static string GetChartSoundSet(Chart chart)
        {
            if (chart.Tags.ContainsKey("KEYSET") && chart.Tags["KEYSET"] != "")
                return chart.Tags["KEYSET"];

            return "0";
        }
        /// <summary>
        /// Builds the chart ignore map and renders auto-tip samples when requested.
        /// </summary>
        private static Dictionary<int, int> CreateIgnoreMapForAutoTip(InputFileInfo input, ConversionContext context)
        {
            Dictionary<int, int> ignore = new Dictionary<int, int>();
            if (!context.UseRenderAutoTip)
                return ignore;

            Console.WriteLine("Convert AutoTips");
            Console.WriteLine(input.Filename.Remove(input.Filename.Length - 8));
            string[] files = Directory.GetFiles(input.Filename.Remove(input.Filename.Length - 8), "*", SearchOption.AllDirectories);
            Render.RenderWAV(files, ConverterTiming.StandardNumerator, ConverterTiming.StandardDenominator, true);

            ignore.Add(3, 3);
            return ignore;
        }

        /// <summary>
        /// Applies song metadata from the IIDX database to every chart in an archive.
        /// </summary>
        private static void ApplyDatabaseMetadata(Archive archive, string databaseName, Configuration config, Configuration db, bool useRenderAutoTip)
        {
            if (db[databaseName]["TITLE"] == "")
                return;

            for (int i = 0; i < archive.ChartCount; i++)
            {
                Chart chart = archive.Charts[i];
                if (chart != null)
                    ApplyChartDatabaseMetadata(chart, i, databaseName, config, db, useRenderAutoTip);
            }
        }

        /// <summary>
        /// Applies common metadata and side-specific difficulty data to one chart.
        /// </summary>
        private static void ApplyChartDatabaseMetadata(Chart chart, int chartIndex, string databaseName, Configuration config, Configuration db, bool useRenderAutoTip)
        {
            chart.Tags["TITLE"] = db[databaseName]["TITLE"];
            chart.Tags["ARTIST"] = db[databaseName]["ARTIST"];
            chart.Tags["GENRE"] = db[databaseName]["GENRE"];
            chart.Tags["MUSICID"] = databaseName;
            chart.Tags["VIDEO"] = db[databaseName]["VIDEO"];
            chart.Tags["VIDEODELAY"] = db[databaseName]["VIDEODELAY"];
            chart.Tags["PLAYVIDEOFLAGS"] = db[databaseName]["PLAYVIDEOFLAGS"];
            CopyOverlayTags(chart, databaseName, db);

            if (chartIndex < 6)
            {
                ApplyDifficultyMetadata(chart, chartIndex, "SP", databaseName, config, db, useRenderAutoTip);
            }
            else if (chartIndex < 12)
            {
                ApplyDifficultyMetadata(chart, chartIndex, "DP", databaseName, config, db, useRenderAutoTip);
            }
        }

        private static void CopyOverlayTags(Chart chart, string databaseName, Configuration db)
        {
            for (int i = 0; i < 10; i++)
            {
                string tag = "OVERLAY" + i.ToString();
                string value = db[databaseName][tag];
                if (!String.IsNullOrWhiteSpace(value))
                    chart.Tags[tag] = value;
            }
        }


        /// <summary>
        /// Applies play level, keyset, and auto-tip tags for one chart side.
        /// </summary>
        private static void ApplyDifficultyMetadata(Chart chart, int chartIndex, string playSide, string databaseName, Configuration config, Configuration db, bool useRenderAutoTip)
        {
            string difficulty = config["IIDX"]["DIFFICULTY" + chartIndex.ToString()];
            chart.Tags["PLAYLEVEL"] = db[databaseName]["DIFFICULTY" + playSide + difficulty];
            chart.Tags["KEYSET"] = db[databaseName]["KEYSET" + playSide + difficulty];
            chart.Tags["ISUSERENDERAUTOTIP"] = useRenderAutoTip.ToString();
        }

        /// <summary>
        /// Reads a .2DX sample archive and writes its samples.
        /// </summary>
        private static void Convert2DXSamples(MemoryStream source, InputFileInfo input, ConversionContext context)
        {
            Console.WriteLine("Converting Samples");
            Bemani2DX archive = Bemani2DX.Read(source);
            ConvertSoundsWithDatabaseInfo(archive.Sounds, input, context);
        }

        /// <summary>
        /// Reads an .S3P sample archive and writes its samples.
        /// </summary>
        private static void ConvertS3PSamples(MemoryStream source, InputFileInfo input, ConversionContext context)
        {
            Console.WriteLine("Converting Samples");
            BemaniS3P archive = BemaniS3P.Read(source);
            ConvertSoundsWithDatabaseInfo(archive.Sounds, input, context);
        }

        /// <summary>
        /// Looks up database title and volume metadata before writing samples.
        /// </summary>
        private static void ConvertSoundsWithDatabaseInfo(Sound[] sounds, InputFileInfo input, ConversionContext context)
        {
            float volume = DefaultSampleVolume;
            string title = "";

            if (context.Database[input.DatabaseName]["TITLE"] != "")
            {
                volume = float.Parse(context.Database[input.DatabaseName]["VOLUME"]) / 127.0f;
                title = context.Database[input.DatabaseName]["TITLE"];
            }

            if (TryConvertPackedBmsonSounds(sounds, input, context, volume, title))
                return;

            ConvertSounds(sounds, input.Filename, volume, input.UpdateTime, input.Index, context.OutputFolder, title, input.IsPre2DX, input.Version, context.Config["IIDX"].GetString("OutputFormat", "bms"), context.SoundOutputFormat);
        }

        /// <summary>
        /// Converts one SD9 sound file to WAV.
        /// </summary>
        private static void ConvertSD9Sound(MemoryStream source, InputFileInfo input, string soundOutputFormat)
        {
            Sound sound = BemaniSD9.Read(source);
            string targetFile = Path.GetFileNameWithoutExtension(input.Filename);
            string targetPath = Path.Combine(Path.GetDirectoryName(input.Filename), targetFile) + "." + SoundEncoderFactory.GetFileExtension(soundOutputFormat);
            ISoundEncoder encoder = SoundEncoderFactory.Create(soundOutputFormat);
            encoder.EncodeToFile(sound, targetPath, FullSampleVolume);
        }

        /// <summary>
        /// Converts all non-null charts in an archive.
        /// </summary>
        private static bool ConvertArchiveCharts(Archive archive, Configuration config, string filename, DateTime updateTime, string version)
        {
            bool isSuccess = false;
            for (int i = 0; i < archive.ChartCount; i++)
            {
                if (archive.Charts[i] == null)
                    continue;

                Console.WriteLine("Converting Chart " + i.ToString());
                RegisterPendingBmsonChart(archive.Charts[i], config, filename, i, updateTime, version);
                isSuccess = ConvertChart(archive.Charts[i], config, filename, i, null, updateTime, version);
                if (!isSuccess)
                    break;
            }

            return isSuccess;
        }

        private static void RegisterPendingBmsonChart(Chart chart, Configuration config, string filename, int index, DateTime updateTime, string version)
        {
            if (!IsBmsonOutput(config))
                return;

            PendingBmsonCharts.Add(new PendingBmsonChart
            {
                Chart = chart,
                Config = config,
                Filename = filename,
                Index = index,
                UpdateTime = updateTime,
                Version = version
            });
        }

        private static BmsonSoundLayout GetBmsonSoundLayout(Chart chart)
        {
            BmsonSoundLayout layout;
            if (BmsonSoundLayouts.TryGetValue(chart, out layout))
                return layout;

            return null;
        }
        /// <summary>
        /// Reads chart conversion options from configuration.
        /// </summary>
        private static ChartOptions LoadChartOptions(Configuration config, int index)
        {
            int difficulty = config["IIDX"].GetValue("Difficulty" + index.ToString());
            string title = config["BMS"]["Players" + config["IIDX"]["Players" + index.ToString()]] + " " + config["BMS"]["Difficulty" + difficulty.ToString()];

            return new ChartOptions
            {
                QuantizeNotes = config["BMS"].GetValue("QuantizeNotes"),
                QuantizeMeasure = config["BMS"].GetValue("QuantizeMeasure"),
                Difficulty = difficulty,
                Title = title.Trim(),
                MovieFolder = config["IIDX"]["MovieFolder"],
                IsSameFolderMovie = config["IIDX"].GetBool("IsSameFolderMovie"),
                UseMovie = config["IIDX"].GetBool("UseMovie"),
                OutputRank = config["IIDX"].GetValue("OutputRank"),
                OutputFormat = config["IIDX"].GetString("OutputFormat", "bms"),
                SoundOutputFormat = config["IIDX"].GetString("SoundOutputFormat", SoundEncoderFactory.DefaultFormat),
                UseBgaImage = config["IIDX"].GetBool("UseBgaImage"),
                BgaImageGraphicFolder = config["IIDX"].GetString("BgaImageGraphicFolder", ""),
                BgaImageOutputName = config["IIDX"].GetString("BgaImageOutputName", "bga_image"),
                BmsObjectBase = GetBmsObjectBase(config),
            };
        }

        /// <summary>
        /// Returns the BMS object identifier base from configuration.
        /// </summary>
        private static int GetBmsObjectBase(Configuration config)
        {
            if (config == null)
                return DefaultBmsObjectBase;

            int configuredBase = config["IIDX"].GetValue("BmsObjectBase", DefaultBmsObjectBase);
            return configuredBase == 36 ? 36 : 62;
        }

        /// <summary>
        /// Returns the largest non-zero BMS object identifier available in the configured base.
        /// </summary>
        private static int GetBmsObjectLimit(Configuration config)
        {
            return GetBmsObjectLimit(GetBmsObjectBase(config));
        }

        /// <summary>
        /// Returns the largest non-zero BMS object identifier available in the selected base.
        /// </summary>
        private static int GetBmsObjectLimit(int bmsObjectBase)
        {
            return (bmsObjectBase * bmsObjectBase) - 1;
        }

        /// <summary>
        /// Creates a BMS archive wrapper and initializes standard chart tags.
        /// </summary>
        private static BMS CreateBms(Chart chart, ChartOptions options, string filename)
        {
            BMS bms = new BMS();
            bms.BmsObjectBase = options.BmsObjectBase;
            bms.Charts = new Chart[] { chart };
            Chart targetChart = bms.Charts[0];
            string name = GetChartName(chart, filename);

            targetChart.Tags["TITLE"] = name;
            CopyTag(chart, targetChart, "ARTIST");
            CopyTag(chart, targetChart, "GENRE");

            if (options.Title != null && options.Title.Length > 0)
                targetChart.Tags["CHARTNAME"] = options.Title;

            if (options.Difficulty > 0)
                targetChart.Tags["DIFFICULTY"] = options.Difficulty.ToString();

            targetChart.Tags["PLAYER"] = targetChart.Players > 1 ? "3" : "1";
            targetChart.Tags["RANK"] = options.OutputRank.ToString();

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
        /// Resolves the display name for a chart from tags or the input filename.
        /// </summary>
        private static string GetChartName(Chart chart, string filename)
        {
            string name = "";
            if (chart.Tags.ContainsKey("TITLE"))
                name = chart.Tags["TITLE"];
            if (name == "")
                name = Path.GetFileNameWithoutExtension(Path.GetFileName(filename));
            return name;
        }

        /// <summary>
        /// Creates and returns the output directory for a BMS chart.
        /// </summary>
        private static string BuildChartDirectory(Configuration config, string version, string name)
        {
            string safeName = Common.nameReplace(name);
            string dirPath = Path.Combine(config["IIDX"]["Output"], version, safeName);
            Common.SafeCreateDirectory(dirPath);
            return dirPath;
        }

        /// <summary>
        /// Builds the final BMS file path and appends difficulty text to the filename.
        /// </summary>
        private static string BuildChartOutputPath(string dirPath, ref string name, string title, Chart chart, string outputFormat)
        {
            int players = chart.Players;
            name = Common.nameReplace(name);

            if (title != null && title.Length > 0)
            {
                if (players > 2)
                    title = title + " " + players + "P";
                else if (players > 1)
                    title = title + " DP";

                name += " [" + title + "]";
            }

            string extension = IsBmsonOutput(outputFormat) ? ".bmson" : (ShouldUseBmeExtension(chart) ? ".bme" : ".bms");
            return Path.Combine(dirPath, @"@" + name + extension);
        }

        /// <summary>
        /// Returns true when the configured chart output format is bmson.
        /// </summary>
        private static bool IsBmsonOutput(Configuration config)
        {
            if (config == null)
                return false;

            return IsBmsonOutput(config["IIDX"].GetString("OutputFormat", "bms"));
        }

        /// <summary>
        /// Returns true when the configured chart output format is bmson.
        /// </summary>
        private static bool IsBmsonOutput(string outputFormat)
        {
            return String.Equals(outputFormat, "bmson", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true when the chart uses extended BME lanes or double-play lanes.
        /// </summary>
        private static bool ShouldUseBmeExtension(Chart chart)
        {
            if (chart.Players > 1)
                return true;

            foreach (Entry entry in chart.Entries)
            {
                if ((entry.Type == EntryType.Marker || entry.Type == EntryType.Sample) && entry.Player > 0)
                {
                    if (entry.Column == 5 || entry.Column == 6 || entry.Column == 8)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies movie output settings and copies same-folder movie assets when enabled.
        /// </summary>
        private static void ConfigureMovieOutput(Chart targetChart, Chart sourceChart, ChartOptions options, string dirPath)
        {
            targetChart.isSameFolderMovie = options.IsSameFolderMovie;
            targetChart.useMovie = options.UseMovie;
            targetChart.movieFolder = options.MovieFolder;

            if (sourceChart.Tags.ContainsKey("VIDEO") && options.IsSameFolderMovie && options.UseMovie)
                CopyMovieFiles(sourceChart.Tags["VIDEO"], options.MovieFolder, dirPath);

            ConfigureBgaImage(targetChart, sourceChart, options, dirPath);
        }

        /// <summary>
        /// Renders AFP/GEO assets and registers the result as a BMS BGA image sequence.
        /// </summary>
        private static void ConfigureBgaImage(Chart targetChart, Chart sourceChart, ChartOptions options, string dirPath)
        {
            if (!options.UseBgaImage || String.IsNullOrWhiteSpace(options.BgaImageGraphicFolder))
                return;

            string graphicId = sourceChart.Tags.ContainsKey("MUSICID") ? sourceChart.Tags["MUSICID"] : "";
            string afpName = sourceChart.Tags.ContainsKey("OVERLAY0") ? sourceChart.Tags["OVERLAY0"] : "";
            if (String.IsNullOrWhiteSpace(afpName))
                return;

            string graphicFolder = ResolveBgaImageGraphicFolder(options.BgaImageGraphicFolder, graphicId);
            if (String.IsNullOrWhiteSpace(graphicFolder))
            {
                Console.WriteLine("Warning: BGA image graphic folder was not found for id: " + graphicId);
                return;
            }

            string outputName = GetBgaImageOutputName(options.BgaImageOutputName);
            string frameFolder = Path.Combine(dirPath, outputName);
            bool isPoor = IsPoorOverlay(sourceChart, options.BgaImageGraphicFolder, graphicId);
            string channel = isPoor ? "POOR" : "LAYER";
            try
            {
                AfpBgaFrameRenderer.RenderResult result = AfpBgaFrameRenderer.RenderFrames(graphicFolder, afpName, frameFolder, outputName);
                List<int> bmpIds = RegisterBgaImageFrames(targetChart, dirPath, result.FrameFiles, options.BmsObjectBase);
                AddBgaImageEvents(targetChart, bmpIds, result.Fps, channel);
                Console.WriteLine("BGA image frames written: " + frameFolder);
            }
            catch (Exception e)
            {
                Console.WriteLine("Warning: Failed to render BGA image: " + e.Message);
            }
        }

        private static bool IsPoorOverlay(Chart sourceChart, string graphicRootOrFolder, string graphicId)
        {
            int flags;
            if (sourceChart.Tags.ContainsKey("PLAYVIDEOFLAGS") &&
                Int32.TryParse(sourceChart.Tags["PLAYVIDEOFLAGS"], out flags))
                return (flags & PlayVideoPoorFlag) != 0;

            string graphicRoot = graphicRootOrFolder;
            if (IsBgaImageGraphicFolder(graphicRootOrFolder))
            {
                DirectoryInfo parent = Directory.GetParent(graphicRootOrFolder);
                graphicRoot = parent == null ? "" : parent.FullName;
            }

            DirectoryInfo dataFolder = String.IsNullOrWhiteSpace(graphicRoot) ? null : Directory.GetParent(graphicRoot);
            string videoListFile = dataFolder == null ? "" : Path.Combine(dataFolder.FullName, "info", "0", "video_music_list.xml");
            if (String.IsNullOrWhiteSpace(graphicId) || !File.Exists(videoListFile))
                return true;

            XDocument document = XDocument.Load(videoListFile);
            string normalizedId = graphicId.TrimStart('0');
            XElement music = document.Descendants("music").FirstOrDefault(x =>
                String.Equals((string)x.Attribute("id"), normalizedId, StringComparison.Ordinal));
            XElement value = music?.Element("info")?.Element("play_video_flags");
            return value == null || !Int32.TryParse(value.Value, out flags) || (flags & PlayVideoPoorFlag) != 0;
        }

        private static string GetBgaImageOutputName(string outputName)
        {
            if (String.IsNullOrWhiteSpace(outputName))
                return "bga_image";

            return Common.nameReplace(Path.GetFileNameWithoutExtension(outputName.Trim()));
        }


        private static string ResolveBgaImageGraphicFolder(string graphicRootOrFolder, string graphicId)
        {
            if (String.IsNullOrWhiteSpace(graphicRootOrFolder))
                return "";

            if (IsBgaImageGraphicFolder(graphicRootOrFolder))
                return graphicRootOrFolder;

            if (String.IsNullOrWhiteSpace(graphicId))
                return "";

            string normalizedId = Path.GetFileNameWithoutExtension(graphicId.Trim());
            string candidate = Path.Combine(graphicRootOrFolder, normalizedId);
            if (IsBgaImageGraphicFolder(candidate))
                return candidate;

            candidate = Path.Combine(graphicRootOrFolder, normalizedId.PadLeft(5, '0'));
            return IsBgaImageGraphicFolder(candidate) ? candidate : "";
        }

        private static bool IsBgaImageGraphicFolder(string folder)
        {
            return Directory.Exists(Path.Combine(folder, "afp")) && Directory.Exists(Path.Combine(folder, "geo")) && Directory.Exists(Path.Combine(folder, "tex"));
        }

        /// <summary>
        /// Registers rendered BGA frame files as BMP identifiers in the selected BMS object base.
        /// </summary>
        private static List<int> RegisterBgaImageFrames(Chart chart, string chartFolder, List<string> frameFiles, int bmsObjectBase)
        {
            List<int> bmpIds = new List<int>();
            int nextId = 2;
            foreach (string frameFile in frameFiles)
            {
                nextId = FindAvailableBmpId(chart, nextId, bmsObjectBase);
                string relativePath = Path.GetRelativePath(chartFolder, frameFile);
                chart.Tags["BMP" + Util.ConvertToBMSObjectString(nextId, 2, bmsObjectBase)] = relativePath;
                bmpIds.Add(nextId);
                nextId++;
            }

            return bmpIds;
        }

        /// <summary>
        /// Finds the next unused BMP identifier within the selected BMS object base.
        /// </summary>
        private static int FindAvailableBmpId(Chart chart, int startId, int bmsObjectBase)
        {
            int maxObjectId = GetBmsObjectLimit(bmsObjectBase);
            for (int id = Math.Max(2, startId); id <= maxObjectId; id++)
            {
                string tag = "BMP" + Util.ConvertToBMSObjectString(id, 2, bmsObjectBase);
                if (!chart.Tags.ContainsKey(tag))
                    return id;
            }

            throw new InvalidOperationException("No free BMP slot is available for BGA image.");
        }

        private static void AddBgaImageEvents(Chart chart, List<int> bmpIds, int fps, string channel)
        {
            if (bmpIds.Count == 0)
                return;

            Fraction bpm = chart.DefaultBPM.Numerator > 0 ? chart.DefaultBPM : new Fraction(120, 1);
            for (int i = 0; i < bmpIds.Count; i++)
            {
                Fraction metric = new Fraction(i * bpm.Numerator, fps * 240L * bpm.Denominator);
                int measure = (int)Math.Floor((double)metric);
                Fraction offset = metric - new Fraction(measure, 1);

                Entry entry = new Entry();
                entry.Type = EntryType.BGA;
                entry.Player = 0;
                entry.Column = GetBgaImageColumn(channel);
                entry.Value = new Fraction(bmpIds[i], 1);
                entry.MetricMeasure = measure;
                entry.MetricOffset = offset;
                chart.Entries.Add(entry);
            }

            chart.Entries.Sort();
        }


        private static int GetBgaImageColumn(string channel)
        {
            string normalized = String.IsNullOrWhiteSpace(channel) ? "POOR" : channel.Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "BASE": return 0;
                case "LAYER": return 1;
                case "POOR": return 2;
                case "LAYER2": return 3;
                default:
                    throw new ArgumentException("Invalid BGA image channel: " + channel);
            }
        }
        /// <summary>
        /// Copies supported movie files into the chart output folder.
        /// </summary>
        private static void CopyMovieFiles(string videoName, string movieFolder, string dirPath)
        {
            string[] extensions = { ".wmv", ".mp4" };
            foreach (string extension in extensions)
            {
                string movieFile = movieFolder + videoName + extension;
                if (!File.Exists(movieFile))
                    continue;

                string copyPath = Path.Combine(dirPath, videoName + extension);
                if (File.Exists(copyPath))
                    continue;

                Console.WriteLine(copyPath);
                File.Copy(movieFile, copyPath);
            }
        }

        /// <summary>
        /// Generates or assigns the BMS sample map.
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
                // something weird happened
            }
        }

        /// <summary>
        /// Writes a chart file and preserves the source timestamp.
        /// </summary>
        private static void WriteChartFile(string output, MemoryStream mem, DateTime updateTime)
        {
            File.WriteAllBytes(output, mem.ToArray());
            SetFileTimes(output, updateTime);
        }

        /// <summary>
        /// Resolves the folder name used for converted sound output.
        /// </summary>
        private static string GetSoundSetName(string filename, string nameInfo)
        {
            if (nameInfo.Length == 0)
                return Path.GetFileNameWithoutExtension(Path.GetFileName(filename));

            return Common.nameReplace(nameInfo);
        }

        private static bool TryConvertPackedBmsonSounds(Sound[] sounds, InputFileInfo input, ConversionContext context, float volume, string nameInfo)
        {
            if (!IsBmsonOutput(context.Config) || !context.Config["IIDX"].GetBool("OptimizeBmsonSounds") || input.IsPre2DX || PendingBmsonCharts.Count == 0)
                return false;

            string name = GetSoundSetName(input.Filename, nameInfo);
            string targetPath = Path.Combine(context.OutputFolder, input.Version, name);
            string soundSet = GetSoundSet(input);
            List<PendingBmsonChart> charts = FindPendingBmsonCharts(targetPath, soundSet);
            if (charts.Count == 0)
                return false;

            string soundFolder = Bmson.GetSoundFolder(soundSet);
            string soundPath = Path.Combine(targetPath, soundFolder);
            Common.SafeCreateDirectory(soundPath);

            List<PackedSoundTrackBuild> tracksToEncode = BuildPackedBmsonLayouts(charts, sounds, context, soundFolder);

            Parallel.ForEach(tracksToEncode, PackedSoundEncodingParallelOptions, track =>
            {
                EncodePackedSoundTrack(track, sounds, soundPath, volume, input.UpdateTime, context.SoundOutputFormat);
            });

            foreach (PendingBmsonChart pending in charts)
                ConvertChart(pending.Chart, pending.Config, pending.Filename, pending.Index, null, pending.UpdateTime, pending.Version);

            SetDirectoryTimes(soundPath, input.UpdateTime);
            return true;
        }

        private static List<PendingBmsonChart> FindPendingBmsonCharts(string targetPath, string soundSet)
        {
            List<PendingBmsonChart> result = new List<PendingBmsonChart>();
            string fullTargetPath = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (PendingBmsonChart pending in PendingBmsonCharts)
            {
                string chartName = GetChartName(pending.Chart, pending.Filename);
                string chartPath = BuildChartDirectory(pending.Config, pending.Version, chartName);
                chartPath = Path.GetFullPath(chartPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (String.Equals(chartPath, fullTargetPath, StringComparison.OrdinalIgnoreCase) && String.Equals(GetChartSoundSet(pending.Chart), soundSet, StringComparison.OrdinalIgnoreCase))
                    result.Add(pending);
            }

            return result;
        }

        private static List<PackedSoundTrackBuild> BuildPackedBmsonLayouts(List<PendingBmsonChart> charts, Sound[] sounds, ConversionContext context, string soundFolder)
        {
            Dictionary<Entry, Chart> chartByEntry = new Dictionary<Entry, Chart>();
            Dictionary<string, PackedSoundEvent> eventsBySignature = new Dictionary<string, PackedSoundEvent>();

            foreach (PendingBmsonChart pending in charts)
            {
                EnsureLinearOffsets(pending.Chart);
                foreach (Entry entry in GetSortedMarkerEntries(pending.Chart))
                {
                    PackedSoundEvent soundEvent = CreatePackedSoundEvent(entry, sounds, context);
                    if (soundEvent == null)
                        continue;

                    chartByEntry[entry] = pending.Chart;
                    string signature = BuildPackedEventSignature(soundEvent);
                    PackedSoundEvent existingEvent;
                    if (!eventsBySignature.TryGetValue(signature, out existingEvent))
                    {
                        existingEvent = soundEvent;
                        eventsBySignature[signature] = existingEvent;
                    }

                    existingEvent.Entries.Add(entry);
                }
            }

            List<PackedSoundEvent> events = new List<PackedSoundEvent>(eventsBySignature.Values);
            events.Sort(ComparePackedSoundEvents);

            List<PackedSoundTrackBuild> tracks = new List<PackedSoundTrackBuild>();
            foreach (PackedSoundEvent soundEvent in events)
            {
                PackedSoundTrackBuild targetTrack = null;
                for (int i = 0; i < tracks.Count; i++)
                {
                    if (CanAppendPackedSoundEvent(tracks[i], soundEvent, sounds))
                    {
                        targetTrack = tracks[i];
                        break;
                    }
                }

                if (targetTrack == null)
                {
                    targetTrack = new PackedSoundTrackBuild();
                    tracks.Add(targetTrack);
                }

                soundEvent.TrackIndex = tracks.IndexOf(targetTrack);
                targetTrack.Events.Add(soundEvent);
                targetTrack.EndSeconds = Math.Max(targetTrack.EndSeconds, soundEvent.EndSeconds);
            }

            string soundExtension = SoundEncoderFactory.GetFileExtension(context.SoundOutputFormat);
            for (int i = 0; i < tracks.Count; i++)
                tracks[i].Name = soundFolder + "/" + "bmson_" + (i + 1).ToString("0000") + "." + soundExtension;

            foreach (PendingBmsonChart pending in charts)
                BmsonSoundLayouts[pending.Chart] = BuildChartPackedBmsonLayout(pending.Chart, tracks, chartByEntry);

            return tracks;
        }

        private static PackedSoundEvent CreatePackedSoundEvent(Entry entry, Sound[] sounds, ConversionContext context)
        {
            int sampleIndex = (int)((double)entry.Value);
            if (sampleIndex <= 0 || sampleIndex > sounds.Length || sounds[sampleIndex - 1] == null)
                return null;

            Sound sound = sounds[sampleIndex - 1];
            if (sound.Data == null || sound.Data.Length == 0 || sound.Format == null || sound.Format.AverageBytesPerSecond <= 0)
                return null;

            double startSeconds = GetEntrySeconds(entry, context);
            double durationSeconds = (double)sound.Data.Length / sound.Format.AverageBytesPerSecond;
            return new PackedSoundEvent
            {
                SampleIndex = sampleIndex,
                StartSeconds = startSeconds,
                EndSeconds = startSeconds + durationSeconds
            };
        }

        private static int ComparePackedSoundEvents(PackedSoundEvent left, PackedSoundEvent right)
        {
            int result = left.StartSeconds.CompareTo(right.StartSeconds);
            if (result != 0)
                return result;

            result = left.EndSeconds.CompareTo(right.EndSeconds);
            if (result != 0)
                return result;

            return left.SampleIndex.CompareTo(right.SampleIndex);
        }

        private static bool CanAppendPackedSoundEvent(PackedSoundTrackBuild track, PackedSoundEvent soundEvent, Sound[] sounds)
        {
            if (track.Events.Count == 0)
                return true;

            if (soundEvent.StartSeconds < track.EndSeconds + PackedSoundEventGapSeconds)
                return false;

            if ((soundEvent.EndSeconds + PackedSoundTailPaddingSeconds - track.Events[0].StartSeconds) > MaxPackedTrackSeconds)
                return false;

            Sound sound = sounds[soundEvent.SampleIndex - 1];
            return IsCompatiblePackedTrack(track, sound, sounds);
        }

        private static BmsonSoundLayout BuildChartPackedBmsonLayout(Chart chart, List<PackedSoundTrackBuild> tracks, Dictionary<Entry, Chart> chartByEntry)
        {
            BmsonSoundLayout layout = new BmsonSoundLayout();
            Dictionary<int, int> layoutTrackIndexByGlobalIndex = new Dictionary<int, int>();

            for (int globalTrackIndex = 0; globalTrackIndex < tracks.Count; globalTrackIndex++)
            {
                PackedSoundTrackBuild track = tracks[globalTrackIndex];
                if (track.Events.Count == 0)
                    continue;

                bool hasChartRestart = false;
                foreach (PackedSoundEvent soundEvent in track.Events)
                {
                    foreach (Entry entry in soundEvent.Entries)
                    {
                        Chart entryChart;
                        if (!chartByEntry.TryGetValue(entry, out entryChart) || !Object.ReferenceEquals(entryChart, chart))
                            continue;

                        int layoutTrackIndex;
                        if (!layoutTrackIndexByGlobalIndex.TryGetValue(globalTrackIndex, out layoutTrackIndex))
                        {
                            layoutTrackIndex = layout.Tracks.Count;
                            layoutTrackIndexByGlobalIndex[globalTrackIndex] = layoutTrackIndex;
                            layout.Tracks.Add(new BmsonSoundTrack { Index = layoutTrackIndex, Name = track.Name });
                        }

                        layout.Notes[entry] = new BmsonPackedNote
                        {
                            TrackIndex = layoutTrackIndex,
                            Continue = hasChartRestart
                        };
                        hasChartRestart = true;
                    }
                }
            }

            return layout;
        }

        private static string BuildPackedEventSignature(PackedSoundEvent soundEvent)
        {
            int startMilliseconds = (int)Math.Round(soundEvent.StartSeconds * 1000.0);
            return soundEvent.SampleIndex.ToString() + "@" + startMilliseconds.ToString();
        }

        private static double GetEntrySeconds(Entry entry, ConversionContext context)
        {
            double offset = (double)entry.LinearOffset;
            if (context.UnitDenominator == 0)
                return offset;

            return offset * ((double)context.UnitNumerator / (double)context.UnitDenominator);
        }
        private static void EnsureLinearOffsets(Chart chart)
        {
            foreach (Entry entry in chart.Entries)
            {
                if (!entry.LinearOffsetInitialized)
                {
                    chart.CalculateLinearOffsets();
                    return;
                }
            }
        }

        private static List<Entry> GetSortedMarkerEntries(Chart chart)
        {
            List<Entry> entries = new List<Entry>();
            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type == EntryType.Marker)
                    entries.Add(entry);
            }

            entries.Sort();
            return entries;
        }

        private static bool IsCompatiblePackedTrack(PackedSoundTrackBuild track, Sound sound, Sound[] sounds)
        {
            if (track.Events.Count == 0)
                return true;

            Sound firstSound = sounds[track.Events[0].SampleIndex - 1];
            return AreCompatibleWaveFormats(firstSound.Format, sound.Format);
        }
        private static bool AreCompatibleWaveFormats(NAudio.Wave.WaveFormat left, NAudio.Wave.WaveFormat right)
        {
            if (left == null || right == null)
                return false;

            return left.Encoding == right.Encoding &&
                   left.SampleRate == right.SampleRate &&
                   left.Channels == right.Channels &&
                   left.BitsPerSample == right.BitsPerSample &&
                   left.BlockAlign == right.BlockAlign;
        }
        private static void EncodePackedSoundTrack(PackedSoundTrackBuild track, Sound[] sounds, string soundPath, float volume, DateTime updateTime, string soundOutputFormat)
        {
            Sound firstSound = sounds[track.Events[0].SampleIndex - 1];
            int bytesPerFrame = firstSound.Format.BlockAlign;
            double trackSeconds = track.EndSeconds - track.Events[0].StartSeconds + PackedSoundTailPaddingSeconds;
            if (trackSeconds > MaxPackedTrackSeconds)
                throw new InvalidOperationException("Packed bmson sound track is too long: " + trackSeconds.ToString("0.000") + " seconds.");

            int byteCount = SecondsToByteOffset(trackSeconds, firstSound.Format.AverageBytesPerSecond, bytesPerFrame);
            byte[] buffer = new byte[Math.Max(bytesPerFrame, byteCount + bytesPerFrame)];

            double startSeconds = track.Events[0].StartSeconds;
            foreach (PackedSoundEvent soundEvent in track.Events)
            {
                Sound sourceSound = sounds[soundEvent.SampleIndex - 1];
                byte[] sourceData = AreCompatibleWaveFormats(sourceSound.Format, firstSound.Format) ? sourceSound.Render(volume) : sourceSound.RenderNewFormat(volume, firstSound.Format);
                int offset = SecondsToByteOffset(soundEvent.StartSeconds - startSeconds, firstSound.Format.AverageBytesPerSecond, bytesPerFrame);
                EnsureBufferLength(ref buffer, offset + sourceData.Length, bytesPerFrame);
                MixPcm16(buffer, sourceData, offset);
            }

            Sound packedSound = new Sound
            {
                Data = buffer,
                Format = firstSound.Format
            };

            string output = Path.Combine(soundPath, Path.GetFileName(track.Name));
            ISoundEncoder encoder = SoundEncoderFactory.Create(soundOutputFormat);
            encoder.EncodeToFile(packedSound, output, 1.0f);
            SetFileTimes(output, updateTime);
        }

        private static void EnsureBufferLength(ref byte[] buffer, int requiredLength, int bytesPerFrame)
        {
            if (requiredLength <= buffer.Length)
                return;

            int alignedLength = requiredLength;
            int remainder = alignedLength % bytesPerFrame;
            if (remainder != 0)
                alignedLength += bytesPerFrame - remainder;

            Array.Resize(ref buffer, alignedLength);
        }
        private static int SecondsToByteOffset(double seconds, int averageBytesPerSecond, int bytesPerFrame)
        {
            int offset = (int)Math.Round(seconds * averageBytesPerSecond);
            return offset - (offset % bytesPerFrame);
        }

        private static void MixPcm16(byte[] target, byte[] source, int offset)
        {
            int length = Math.Min(source.Length, target.Length - offset);
            length -= length % 2;
            for (int i = 0; i < length; i += 2)
            {
                int targetSample = BitConverter.ToInt16(target, offset + i);
                int sourceSample = BitConverter.ToInt16(source, i);
                int mixed = Math.Max(Int16.MinValue, Math.Min(Int16.MaxValue, targetSample + sourceSample));
                byte[] bytes = BitConverter.GetBytes((short)mixed);
                target[offset + i] = bytes[0];
                target[offset + i + 1] = bytes[1];
            }
        }
        /// <summary>
        /// Writes the preview OGG for a pre-2DX sound archive.
        /// </summary>
        private static void ConvertPreviewSound(Sound[] sounds, string targetPath, float volume, DateTime updateTime, string soundOutputFormat)
        {
            if (sounds == null || sounds.Length == 0)
                return;

            string output = Path.Combine(targetPath, @"preview" + @"." + SoundEncoderFactory.GetFileExtension(soundOutputFormat));
            ISoundEncoder encoder = SoundEncoderFactory.Create(soundOutputFormat);
            encoder.EncodeToFile(sounds[0], output, volume);
            SetFileTimes(output, updateTime);
        }

        /// <summary>
        /// Writes numbered OGG sample files for a normal sound archive.
        /// </summary>
        private static void ConvertSampleSounds(Sound[] sounds, string targetPath, string index, float volume, DateTime updateTime, string outputFormat, string soundOutputFormat)
        {
            targetPath = BuildSampleTargetPath(targetPath, index, outputFormat);
            Parallel.For(0, sounds.Length, SampleEncodingParallelOptions, i =>
            {
                EncodeSampleSound(sounds[i], i + 1, targetPath, volume, updateTime, outputFormat, soundOutputFormat);
            });

            SetDirectoryTimes(targetPath, updateTime);
        }

        /// <summary>
        /// Encodes one numbered OGG sample file.
        /// </summary>
        private static void EncodeSampleSound(Sound sound, int sampleIndex, string targetPath, float volume, DateTime updateTime, string outputFormat, string soundOutputFormat)
        {
            string sampleName = GetSampleFileName(sampleIndex, outputFormat, soundOutputFormat);
            string output = Path.Combine(targetPath, sampleName);
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
        /// Returns the sample filename used by the selected output format.
        /// </summary>
        private static string GetSampleFileName(int sampleIndex, string outputFormat, string soundOutputFormat)
        {
            return Util.ConvertToBMEString(sampleIndex, 4) + @"." + SoundEncoderFactory.GetFileExtension(soundOutputFormat);
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
        /// Creates the parallel encoding options used for large packed bmson sound files.
        /// </summary>
        private static ParallelOptions CreatePackedSoundEncodingParallelOptions()
        {
            return new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Math.Min(2, Environment.ProcessorCount - 1))
            };
        }

        /// <summary>
        /// Creates and returns the output directory for numbered sample files.
        /// </summary>
        private static string BuildSampleTargetPath(string targetPath, string index, string outputFormat)
        {
            if (IsBmsonOutput(outputFormat))
            {
                targetPath = Path.Combine(targetPath, Bmson.GetSoundFolder(index));
            }
            else
            {
                targetPath += "\\sounds";
                if (index != null)
                    targetPath += "_" + index;
            }

            Common.SafeCreateDirectory(targetPath);
            return targetPath;
        }

        /// <summary>
        /// Sets all file timestamps to match the source asset.
        /// </summary>
        private static void SetFileTimes(string path, DateTime updateTime)
        {
            if (!File.Exists(path))
                return;
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