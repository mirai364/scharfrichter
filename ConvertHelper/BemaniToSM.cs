using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds.Encoders;
using Scharfrichter.Common;

using System;
using System.IO;

namespace ConvertHelper
{
    /// <summary>
    /// Converts Bemani DDR assets to StepMania-compatible output.
    /// </summary>
    static public class BemaniToSM
    {
        private const string configFileName = "Convert";
        private const string databaseFileName = "DDRDB";

        /// <summary>
        /// Holds optional metadata typed by the user for one simfile conversion run.
        /// </summary>
        private sealed class ManualMetadata
        {
            public string Title = "";
            public string Artist = "";
            public string TitleTranslit = "";
            public string ArtistTranslit = "";
            public string CDTitle = "";
        }

        /// <summary>
        /// Converts SSQ charts and XWB sound banks passed to the BemaniToSM command.
        /// </summary>
        static public void Convert(string[] inArgs)
        {
            Configuration config = LoadConfig();
            Splash.Show("Bemani To Stepmania");

            string[] args = PrepareInputArguments(inArgs);
            if (args.Length == 0)
                ShowUsage();

            string manualSelect = PromptManualFillIfNeeded(args);
            ProcessFiles(args, config, manualSelect);
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
            Console.WriteLine("Usage: BemaniToSM <input file>");
            Console.WriteLine();
            Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
            Console.WriteLine();
            Console.WriteLine("Supported formats:");
            Console.WriteLine("SSQ, XWB");
        }

        /// <summary>
        /// Prompts once for optional simfile metadata when at least one SSQ file is present.
        /// </summary>
        private static string PromptManualFillIfNeeded(string[] args)
        {
            foreach (string filename in args)
            {
                if (File.Exists(filename) && Path.GetExtension(filename).ToUpper() == ".SSQ")
                {
                    Console.WriteLine();
                    Console.Write("At least one ssq files detected.");
                    Console.WriteLine();
                    Console.Write("Enable manual fill-up simfile data?");
                    Console.WriteLine();
                    Console.Write("Input y for Yes, ENTER for No: ");
                    return Console.ReadLine();
                }
            }

            return "";
        }

        /// <summary>
        /// Processes all existing input files.
        /// </summary>
        private static void ProcessFiles(string[] args, Configuration config, string manualSelect)
        {
            foreach (string filename in args)
            {
                if (!File.Exists(filename))
                    continue;

                Console.WriteLine();
                Console.WriteLine("Processing File: " + filename);
                ProcessFile(filename, config, manualSelect);
            }
        }

        /// <summary>
        /// Dispatches one input file to the converter for its file extension.
        /// </summary>
        private static void ProcessFile(string filename, Configuration config, string manualSelect)
        {
            switch (Path.GetExtension(filename).ToUpper())
            {
                case @".XWB":
                    ConvertXwb(filename, config);
                    break;
                case @".SSQ":
                    ConvertSsq(filename, config, manualSelect);
                    break;
            }
        }

        /// <summary>
        /// Extracts every sound from an XWB bank as WAV files.
        /// </summary>
        private static void ConvertXwb(string filename, Configuration config)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Console.WriteLine("Reading XWB bank");
                MicrosoftXWB bank = MicrosoftXWB.Read(fs);
                string outPath = Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename));
                Directory.CreateDirectory(outPath);

                for (int i = 0; i < bank.SoundCount; i++)
                    WriteXwbSound(bank, i, outPath, config["DDR"].GetString("SoundOutputFormat", SoundEncoderFactory.DefaultFormat));
            }
        }

        /// <summary>
        /// Writes one sound from an XWB bank as a WAV file.
        /// </summary>
        private static void WriteXwbSound(MicrosoftXWB bank, int index, string outPath, string soundOutputFormat)
        {
            string outFileName = String.IsNullOrEmpty(bank.Sounds[index].Name)
                ? Util.ConvertToHexString(index, 4)
                : bank.Sounds[index].Name;

            string soundExtension = SoundEncoderFactory.GetFileExtension(soundOutputFormat);
            string outFile = Path.Combine(outPath, outFileName + "." + soundExtension);
            Console.WriteLine("Writing " + outFile);
            ISoundEncoder encoder = SoundEncoderFactory.Create(soundOutputFormat);
            encoder.EncodeToFile(bank.Sounds[index], outFile, 1.0f);
        }

        /// <summary>
        /// Converts one SSQ chart file to a StepMania SM file.
        /// </summary>
        private static void ConvertSsq(string filename, Configuration config, string manualSelect)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                BemaniSSQ ssq = BemaniSSQ.Read(fs, 0x1000);
                StepmaniaSM sm = new StepmaniaSM();
                ManualMetadata metadata = ReadManualMetadata(manualSelect);

                InitializeStepmaniaTags(sm, filename, metadata, manualSelect == "y");
                sm.CreateTempoTags(ssq.TempoEntries.ToArray());
                AddStepTags(sm, ssq, config, manualSelect == "y");

                string outTitle = ResolveOutputTitle(filename, metadata);
                sm.WriteFile(Path.Combine(Path.GetDirectoryName(filename), outTitle + ".sm"));
            }
        }

        /// <summary>
        /// Reads optional simfile metadata from the console.
        /// </summary>
        private static ManualMetadata ReadManualMetadata(string manualSelect)
        {
            ManualMetadata metadata = new ManualMetadata();
            if (manualSelect != "y")
                return metadata;

            Console.WriteLine();
            Console.Write("TITLE: ");
            metadata.Title = Console.ReadLine();

            Console.Write("ARTIST: ");
            metadata.Artist = Console.ReadLine();

            Console.Write("TITLETRANSLIT: ");
            metadata.TitleTranslit = Console.ReadLine();

            Console.Write("ARTISTTRANSLIT: ");
            metadata.ArtistTranslit = Console.ReadLine();

            Console.Write("Origin (for CDTitle): ");
            metadata.CDTitle = Console.ReadLine();

            Console.WriteLine();
            Console.WriteLine("Input difficulty ratings for song " + metadata.Title + " below.");
            Console.WriteLine();
            return metadata;
        }

        /// <summary>
        /// Initializes top-level StepMania metadata tags.
        /// </summary>
        private static void InitializeStepmaniaTags(StepmaniaSM sm, string filename, ManualMetadata metadata, bool useManualMetadata)
        {
            sm.Tags["SongID"] = Path.GetFileNameWithoutExtension(@filename);
            sm.Tags["TITLE"] = metadata.Title;
            sm.Tags["ARTIST"] = metadata.Artist;

            if (useManualMetadata)
            {
                if (metadata.TitleTranslit != "")
                    sm.Tags["TITLETRANSLIT"] = metadata.TitleTranslit;
                if (metadata.ArtistTranslit != "")
                    sm.Tags["ARTISTTRANSLIT"] = metadata.ArtistTranslit;
            }
            else
            {
                sm.Tags["TITLETRANSLIT"] = "";
                sm.Tags["ARTISTTRANSLIT"] = "";
            }

            string imageTitle = metadata.TitleTranslit == "" ? metadata.Title : metadata.TitleTranslit;
            sm.Tags["BANNER"] = imageTitle + ".png";
            sm.Tags["BACKGROUND"] = imageTitle + "-bg.png";
            sm.Tags["CDTITLE"] = "./CDTitles/" + metadata.CDTitle + ".png";
            sm.Tags["MUSIC"] = imageTitle + ".ogg";
            sm.Tags["SAMPLESTART"] = "20";
            sm.Tags["SAMPLELENGTH"] = "15";
        }

        /// <summary>
        /// Adds all StepMania step tags from the parsed SSQ charts.
        /// </summary>
        private static void AddStepTags(StepmaniaSM sm, BemaniSSQ ssq, Configuration config, bool promptMeter)
        {
            string[] gameTypes = { "dance-single", "dance-double", "dance-couple", "dance-solo" };
            foreach (string gameTypeName in gameTypes)
            {
                string[] difficultyNames = { "Beginner", "Easy", "Medium", "Hard", "Challenge", "" };
                foreach (string difficultyName in difficultyNames)
                    AddMatchingStepTags(sm, ssq, config, gameTypeName, difficultyName, promptMeter);
            }
        }

        /// <summary>
        /// Adds step tags for charts matching one game type and difficulty label.
        /// </summary>
        private static void AddMatchingStepTags(StepmaniaSM sm, BemaniSSQ ssq, Configuration config, string gameTypeName, string difficultyName, bool promptMeter)
        {
            foreach (Chart chart in ssq.Charts)
            {
                string gameType = config["SM"]["DanceMode" + chart.Tags["Panels"]];
                if (gameTypeName != gameType)
                    continue;

                string difficulty = config["SM"]["Difficulty" + config["DDR"]["Difficulty" + chart.Tags["Difficulty"]]];
                chart.Entries.Sort();

                if (gameType == config["SM"]["DanceMode8"] && difficulty == "")
                    break;
                if (difficultyName != difficulty)
                    continue;

                NormalizeGameType(chart, config, ref gameType, difficulty);
                string meter = PromptMeterIfNeeded(gameType, difficulty, promptMeter);
                string stepDifficulty = difficulty == "" ? "Medium" : difficulty;
                sm.CreateStepTag(chart.Entries.ToArray(), gameType, "", stepDifficulty, meter, "", System.Convert.ToInt32(chart.Tags["Panels"]), config["SM"].GetValue("QuantizeNotes"));
            }
        }

        /// <summary>
        /// Applies solo column conversion or couple-chart detection for one chart.
        /// </summary>
        private static void NormalizeGameType(Chart chart, Configuration config, ref string gameType, string difficulty)
        {
            if (gameType == config["SM"]["DanceMode6"])
            {
                ConvertSoloColumns(chart);
            }
            else if (gameType == config["SM"]["DanceMode4"])
            {
                DetectCoupleChart(chart, config, ref gameType);
            }
        }

        /// <summary>
        /// Converts DDR solo panel column order to StepMania solo order.
        /// </summary>
        private static void ConvertSoloColumns(Chart chart)
        {
            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type != EntryType.Marker)
                    continue;

                switch (entry.Column)
                {
                    case 0: entry.Column = 0; break;
                    case 1: entry.Column = 2; break;
                    case 2: entry.Column = 3; break;
                    case 3: entry.Column = 5; break;
                    case 4: entry.Column = 1; break;
                    case 6: entry.Column = 4; break;
                }
            }
        }

        /// <summary>
        /// Changes a double chart to couple mode when player-two columns are present.
        /// </summary>
        private static void DetectCoupleChart(Chart chart, Configuration config, ref string gameType)
        {
            foreach (Entry entry in chart.Entries)
            {
                if (entry.Type == EntryType.Marker && entry.Column >= 4)
                {
                    gameType = config["SM"]["DanceModeCouple"];
                    chart.Tags["Panels"] = "8";
                    break;
                }
            }
        }

        /// <summary>
        /// Prompts for a chart meter when manual metadata entry is enabled.
        /// </summary>
        private static string PromptMeterIfNeeded(string gameType, string difficulty, bool promptMeter)
        {
            string meter = "";
            string displayDifficulty = GetStepmaniaDifficultyText(difficulty);
            if (promptMeter)
            {
                Console.Write(ToUpperFirstLetter(gameType.Replace("dance-", "")) + "-" + displayDifficulty + ": ");
                meter = Console.ReadLine();
            }

            if (meter == "")
                meter = "0";

            return meter;
        }

        /// <summary>
        /// Converts StepMania difficulty text to the label shown during manual meter entry.
        /// </summary>
        private static string GetStepmaniaDifficultyText(string difficulty)
        {
            switch (difficulty)
            {
                case "Easy": return "Basic";
                case "Medium": return "Difficult";
                case "Hard": return "Expert";
                case "": return "Difficult";
                default: return difficulty;
            }
        }

        /// <summary>
        /// Resolves the SM output file title from manual metadata or the input filename.
        /// </summary>
        private static string ResolveOutputTitle(string filename, ManualMetadata metadata)
        {
            if (metadata.TitleTranslit != "")
                return metadata.TitleTranslit;
            if (metadata.Title != "")
                return metadata.Title;

            return Path.GetFileNameWithoutExtension(@filename);
        }

        /// <summary>
        /// Returns a copy of the string with its first character upper-cased.
        /// </summary>
        static private string ToUpperFirstLetter(this string source)
        {
            if (string.IsNullOrEmpty(source))
                return string.Empty;

            char[] letters = source.ToCharArray();
            letters[0] = char.ToUpper(letters[0]);
            return new string(letters);
        }

        /// <summary>
        /// Loads StepMania conversion configuration with default values populated.
        /// </summary>
        static private Configuration LoadConfig()
        {
            Configuration config = Configuration.ReadFile(configFileName);
            config["SM"].SetDefaultValue("QuantizeNotes", 192);
            config["SM"].SetDefaultString("DanceMode4", "dance-single");
            config["SM"].SetDefaultString("DanceMode6", "dance-solo");
            config["SM"].SetDefaultString("DanceMode8", "dance-double");
            config["SM"].SetDefaultString("DanceModeCouple", "dance-couple");
            config["SM"].SetDefaultString("Difficulty0", "Challenge");
            config["SM"].SetDefaultString("Difficulty1", "Easy");
            config["SM"].SetDefaultString("Difficulty2", "Medium");
            config["SM"].SetDefaultString("Difficulty3", "Hard");
            config["SM"].SetDefaultString("Difficulty4", "Beginner");
            config["SM"].SetDefaultString("Difficulty5", "Edit");
            config["DDR"].SetDefaultString("Difficulty1", "1");
            config["DDR"].SetDefaultString("Difficulty2", "2");
            config["DDR"].SetDefaultString("Difficulty3", "3");
            config["DDR"].SetDefaultString("Difficulty4", "4");
            config["DDR"].SetDefaultString("Difficulty6", "0");
            config["DDR"].SetDefaultString("SoundOutputFormat", SoundEncoderFactory.DefaultFormat);
            return config;
        }

        /// <summary>
        /// Loads the DDR metadata database.
        /// </summary>
        static private Configuration LoadDB()
        {
            Configuration config = Configuration.ReadFile(databaseFileName);
            return config;
        }
    }
}