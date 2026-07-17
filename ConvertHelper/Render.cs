using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds;
using Scharfrichter.Common;

using System;
using System.Collections.Generic;
using System.IO;

namespace ConvertHelper
{
    /// <summary>
    /// Renders beatmania-family charts to WAV or auto-tip OGG preview assets.
    /// </summary>
    static public class Render
    {
        /// <summary>
        /// Holds configuration and source data for one render run.
        /// </summary>
        private sealed class RenderContext
        {
            public Configuration Config;
            public Configuration Database;
            public long UnitNumerator;
            public long UnitDenominator;
            public bool UseRenderAutoTip;
            public Dictionary<int, int> Ignore;
            public string OutputFolder;
            public string OutFile;
            public string TargetPath;
            public string DatabaseName;
            public string Title;
            public string Version;
            public Dictionary<string, Sound[]> Sounds;
            public Chart[] Charts;
            public List<byte[]> RenderedData;
            public List<int> RenderedIndexes;
        }

        /// <summary>
        /// Renders matching chart and sound files to disk.
        /// </summary>
        static public void RenderWAV(string[] inArgs, long unitNumerator, long unitDenominator, bool idUseRenderAutoTip)
        {
            RenderContext context = CreateContext(unitNumerator, unitDenominator, idUseRenderAutoTip);
            ShowSplash(context);

            string[] args = PrepareInputArguments(inArgs);
            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }

            InitializeSongIdentity(args[0], context);
            LoadInputFiles(args, context);
            RenderCharts(context);
        }

        /// <summary>
        /// Creates render context and applies force-render configuration.
        /// </summary>
        private static RenderContext CreateContext(long unitNumerator, long unitDenominator, bool useRenderAutoTip)
        {
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            if (config["BMS"].GetBool("ForceRenderAutoTip"))
                useRenderAutoTip = true;

            return new RenderContext
            {
                Config = config,
                Database = Common.LoadDB(),
                UnitNumerator = unitNumerator,
                UnitDenominator = unitDenominator,
                UseRenderAutoTip = useRenderAutoTip,
                Ignore = CreateIgnoreMap(useRenderAutoTip),
                OutputFolder = config["BMS"]["Output"],
                OutFile = "0001",
                TargetPath = null,
                Sounds = new Dictionary<string, Sound[]>(),
                Charts = null,
                RenderedData = new List<byte[]>(),
                RenderedIndexes = new List<int>()
            };
        }

        /// <summary>
        /// Creates the optional auto-tip ignore map.
        /// </summary>
        private static Dictionary<int, int> CreateIgnoreMap(bool useRenderAutoTip)
        {
            Dictionary<int, int> ignore = new Dictionary<int, int>();
            if (useRenderAutoTip)
            {
                ignore.Add(1, 1);
                ignore.Add(2, 2);
            }

            return ignore;
        }

        /// <summary>
        /// Prints the renderer banner and timing information.
        /// </summary>
        private static void ShowSplash(RenderContext context)
        {
            Splash.Show("Render");
            Console.WriteLine("Timing: " + context.UnitNumerator.ToString() + "/" + context.UnitDenominator.ToString());
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
            Console.WriteLine("Usage: Render2DX <files..>");
            Console.WriteLine();
            Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
            Console.WriteLine();
            Console.WriteLine("You must have both the chart file (.1) and the sound file (.2dx).");
            Console.WriteLine("Supported formats:");
            Console.WriteLine("1, 2DX");
        }

        /// <summary>
        /// Resolves database key, display title, and output version from the first input file.
        /// </summary>
        private static void InitializeSongIdentity(string firstArg, RenderContext context)
        {
            string databaseName = Path.GetFileNameWithoutExtension(firstArg);
            string title = databaseName;
            string version = databaseName.Substring(0, 2);

            if (databaseName.Contains("pre"))
                databaseName = databaseName.Substring(0, 5);
            if (databaseName.Length > 5)
                databaseName = databaseName.Substring(0, 5);
            while (databaseName.StartsWith("0"))
                databaseName = databaseName.Substring(1);

            if (context.Database[databaseName]["TITLE"] != "")
            {
                title = context.Database[databaseName]["TITLE"];
                title = Common.nameReplace(title);
            }

            context.DatabaseName = databaseName;
            context.Title = title;
            context.Version = version;
        }

        /// <summary>
        /// Loads chart and sound archives from the provided file list.
        /// </summary>
        private static void LoadInputFiles(string[] args, RenderContext context)
        {
            foreach (string filename in args)
            {
                if (!File.Exists(filename))
                    continue;

                string extension = Path.GetExtension(filename).ToUpper();
                string index = GetSoundIndex(filename);
                if (index == null)
                    continue;

                switch (extension)
                {
                    case @".1":
                        LoadCharts(filename, context);
                        break;
                    case @".2DX":
                        Load2DXSounds(filename, index, context);
                        break;
                    case @".S3P":
                        LoadS3PSounds(filename, index, context);
                        break;
                }
            }
        }

        /// <summary>
        /// Resolves the sound set index from an archive filename, or null for preview archives.
        /// </summary>
        private static string GetSoundIndex(string filename)
        {
            string tmp = Path.GetFileNameWithoutExtension(filename);
            if (tmp.Contains("pre"))
                return null;

            if (tmp.Length > 5)
                return tmp.Substring(5);

            return "0";
        }

        /// <summary>
        /// Loads charts from the first .1 archive in the input list.
        /// </summary>
        private static void LoadCharts(string filename, RenderContext context)
        {
            if (context.Charts != null)
                return;

            Console.WriteLine();
            Console.WriteLine("Valid charts:");
            if (!context.UseRenderAutoTip)
                context.OutFile = Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename));

            using (MemoryStream mem = new MemoryStream(File.ReadAllBytes(filename)))
            {
                context.Charts = Bemani1.Read(mem, context.UnitNumerator, context.UnitDenominator, context.Ignore).Charts;
                for (int i = 0; i < context.Charts.Length; i++)
                {
                    if (context.Charts[i] != null)
                        Console.Write(i.ToString() + "  ");
                }
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Loads a 2DX sound archive when its key has not already been loaded.
        /// </summary>
        private static void Load2DXSounds(string filename, string index, RenderContext context)
        {
            if (context.Sounds.ContainsKey(index))
                return;

            using (MemoryStream mem = new MemoryStream(File.ReadAllBytes(filename)))
            {
                context.Sounds.Add(index, Bemani2DX.Read(mem).Sounds);
            }
        }

        /// <summary>
        /// Loads an S3P sound archive when its key has not already been loaded.
        /// </summary>
        private static void LoadS3PSounds(string filename, string index, RenderContext context)
        {
            if (context.Sounds.ContainsKey(index))
                return;

            using (MemoryStream mem = new MemoryStream(File.ReadAllBytes(filename)))
            {
                context.Sounds.Add(index, BemaniS3P.Read(mem).Sounds);
            }
        }

        /// <summary>
        /// Renders every loaded chart with its matching sound set.
        /// </summary>
        private static void RenderCharts(RenderContext context)
        {
            if (context.Sounds == null || context.Charts == null)
                return;

            for (int k = 0; k < context.Charts.Length; k++)
            {
                Chart chart = context.Charts[k];
                if (chart == null)
                    continue;

                Console.WriteLine("Rendering " + k.ToString());
                string keySet = ResolveKeySet(k, context);
                Sound[] sounds = ResolveSoundSet(keySet, context);
                if (sounds == null)
                    continue;

                byte[] data = RenderChart(chart, sounds, context.UseRenderAutoTip);
                int matchIndex = FindRenderedMatch(data, context);
                WriteRenderedData(k, keySet, data, matchIndex, context);
            }
        }

        /// <summary>
        /// Resolves the sound keyset for a chart index from the database.
        /// </summary>
        private static string ResolveKeySet(int chartIndex, RenderContext context)
        {
            if (chartIndex < 6)
                return context.Database[context.DatabaseName]["KEYSETSP" + context.Config["IIDX"]["DIFFICULTY" + chartIndex.ToString()]];
            if (chartIndex < 12)
                return context.Database[context.DatabaseName]["KEYSETDP" + context.Config["IIDX"]["DIFFICULTY" + chartIndex.ToString()]];

            return "0";
        }

        /// <summary>
        /// Finds the requested sound set or falls back to keyset 0.
        /// </summary>
        private static Sound[] ResolveSoundSet(string keySet, RenderContext context)
        {
            Sound[] sounds;
            if (context.Sounds.TryGetValue(keySet, out sounds))
                return sounds;

            Console.WriteLine("not found keySet");
            if (context.Sounds.TryGetValue("0", out sounds))
                return sounds;

            Console.WriteLine("not found sounds \n continue");
            return null;
        }

        /// <summary>
        /// Renders one chart to OGG or WAV bytes.
        /// </summary>
        private static byte[] RenderChart(Chart chart, Sound[] sounds, bool useRenderAutoTip)
        {
            if (useRenderAutoTip)
                return ChartRenderer.RenderAsFormat(chart, sounds, "ogg");

            return ChartRenderer.RenderAsFormat(chart, sounds, "wav");
        }

        /// <summary>
        /// Finds a previously rendered byte-identical chart, or returns -1.
        /// </summary>
        private static int FindRenderedMatch(byte[] data, RenderContext context)
        {
            for (int i = 0; i < context.RenderedData.Count; i++)
            {
                if (AreBytesEqual(context.RenderedData[i], data))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Compares two byte arrays for exact equality.
        /// </summary>
        private static bool AreBytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Writes unique render data or reports the chart it matches.
        /// </summary>
        private static void WriteRenderedData(int chartIndex, string keySet, byte[] data, int matchIndex, RenderContext context)
        {
            bool match = matchIndex >= 0;
            if (context.UseRenderAutoTip)
            {
                context.TargetPath = BuildAutoTipTargetPath(keySet, context);
                match = false;
            }

            if (!match)
            {
                WriteUniqueRender(chartIndex, data, context);
                context.RenderedData.Add(data);
                context.RenderedIndexes.Add(chartIndex);
            }
            else
            {
                Console.WriteLine("Matches " + context.RenderedIndexes[matchIndex].ToString());
            }
        }

        /// <summary>
        /// Builds the destination folder for auto-tip OGG render output.
        /// </summary>
        private static string BuildAutoTipTargetPath(string keySet, RenderContext context)
        {
            string targetFolder = "sounds";
            if (keySet != "0")
                targetFolder = "sounds_" + keySet;

            return Path.Combine(context.OutputFolder, context.Version, context.Title, targetFolder);
        }

        /// <summary>
        /// Writes one unique render output file.
        /// </summary>
        private static void WriteUniqueRender(int chartIndex, byte[] data, RenderContext context)
        {
            if (context.UseRenderAutoTip)
            {
                string difficulty = (chartIndex < 6 ? 1 : 3) + context.Config["IIDX"].GetValue("DIFFICULTY" + chartIndex.ToString()).ToString();
                Console.WriteLine("Writing unique " + difficulty);
                Common.SafeCreateDirectory(context.TargetPath);
                File.WriteAllBytes(context.TargetPath + "\\" + context.OutFile + "-" + difficulty + ".ogg", data);
            }
            else
            {
                Console.WriteLine("Writing unique " + chartIndex.ToString());
                File.WriteAllBytes(context.OutFile + " -" + Util.ConvertToDecimalString(chartIndex, 2) + ".wav", data);
            }
        }
    }
}