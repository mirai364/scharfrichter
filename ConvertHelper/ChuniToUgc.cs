using DDSReader;
using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using Scharfrichter.Codec.Sounds.Encoders;
using Scharfrichter.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace ConvertHelper
{
    /// <summary>
    /// Converts CHUNITHM chart data to Umiguri Chart (UGC) v8 format.
    /// Note types are mapped to the corresponding UGC note types:
    ///   TAP -> t, CHR -> x (ExTAP), FLK -> f (FLICK), MNE -> d (DAMAGE),
    ///   HLD/HXD -> h (HOLD) + s child, SLC/SLD -> s (SLIDE) + c intermediate / s end,
    ///   AHD/AHX -> H (AIR-HOLD) + s child, AIR -> a (AIR),
    ///   ASD/ASC -> S (AIR-SLIDE), ALD -> C (AIR-CRUSH).
    /// </summary>
    static public class ChuniToUgc
    {
        private const int TicksPerBeat = 480;
        private const int DefaultBeatsPerMeasure = 4;
        private const int StandardTicksPerMeasure = TicksPerBeat * DefaultBeatsPerMeasure; // 1920

        /// <summary>
        /// Output unit priority at the same timestamp, matching the reference
        /// converter (Margrete) order:
        ///   HOLD (h) -> SLIDE (s) -> single notes (t/x/f/d/a) -> AIR-SLIDE (S)
        ///   -> AIR-HOLD (H) -> AIR-CRUSH (C)
        /// A companion AIR (AUL / ADW / ...) is emitted right after its ground
        /// unit, so the SLIDE-first order places CHR before AHD like the
        /// reference output.
        /// AIR-SLIDE (S) carries a Previous relationship to its TargetNote
        /// (TAP / CHR / SLD / ...), so it must be emitted AFTER the single
        /// notes at the same timestamp (UMIGURI resolves Previous from the
        /// immediately preceding line).
        /// </summary>
        private const int PriorityHold = 0;
        private const int PrioritySlide = 1;
        private const int PrioritySingle = 2;
        private const int PriorityAirSlide = 3;
        private const int PriorityAirHold = 4;
        private const int PriorityAirCrush = 5;

        // Player numbers used by ChuniPC for the note families.
        private const int PlayerTap = 1;
        private const int PlayerHold = 2;
        private const int PlayerSlide = 3;
        private const int PlayerAirHold = 4;
        private const int PlayerAir = 5;
        private const int PlayerAirSlide = 6;
        private const int PlayerAirCrush = 7;

        /// <summary>
        /// C2S CHR direction tag -> UGC ExTAP extra character.
        /// </summary>
        private static readonly Dictionary<string, string> C2UChrExtras = new Dictionary<string, string>
        {
            { "UP", "U" }, { "DW", "D" }, { "CE", "C" }, { "LS", "L" },
            { "RS", "R" }, { "RC", "A" }, { "LC", "W" }, { "BS", "I" },
        };

        /// <summary>
        /// C2S AIR-CRUSH color tag -> UGC color character.
        /// </summary>
        private static readonly Dictionary<string, string> C2UAirCrushColor = new Dictionary<string, string>
        {
            { "DEF", "0" }, { "NON", "Z" }, { "RED", "1" }, { "ORN", "2" },
            { "YEL", "3" }, { "LIM", "4" }, { "GRN", "5" }, { "AQA", "6" },
            { "CYN", "7" }, { "DGR", "8" }, { "BLU", "9" }, { "VLT", "A" },
            { "PPL", "Y" }, { "PNK", "B" }, { "GRY", "C" }, { "BLK", "D" },
        };

        /// <summary>
        /// C2S AIR-CRUSH interval threshold (384 ticks per measure): intervals
        /// longer than 25 measures are serialized as "$" (auto).
        /// </summary>
        private const int AirCrushIntervalAutoThreshold = 9600; // 25 measures * 384

        /// <summary>
        /// Tick length of each UMIGURI measure, built from the CHUNITHM MET
        /// events (4/4 = 1920, 3/4 = 1440, ...). Used to convert fixed-grid
        /// note positions into @BEAT-consistent Bar'Tick positions.
        /// </summary>
        private static Dictionary<int, int> currentMeasureTicks = new Dictionary<int, int>();

        private static readonly Dictionary<string, int> DifficultyMap = new Dictionary<string, int>
        {
            { "0", 0 },  // BASIC
            { "1", 1 },  // ADVANCED
            { "2", 2 },  // EXPERT
            { "3", 3 },  // MASTER
            { "4", 4 },  // WORLD'S END
            { "5", 5 },  // ULTIMA
        };

        /// <summary>A pre-rendered UGC output unit, sorted by time and priority.</summary>
        private sealed class NoteUnit
        {
            public int Measure;
            public double Offset;
            public int Priority;
            public string Text;
            public EntryChuni Entry; // source entry (for companion grouping)
            public int StartAbs;    // absolute tick of the unit start
            public int EndAbs;      // absolute tick of the unit end

            /// <summary>
            /// All lanes touched by a SLIDE chain (start, intermediate and end
            /// segments). A companion AIR / AIR-HOLD may attach to any segment
            /// whose column matches, not only the chain start column.
            /// </summary>
            public int[] ChainColumns;

            /// <summary>
            /// Absolute tick position of each SLIDE / AIR-SLIDE segment end.
            /// Used to place companion AIR / AIR-HOLD notes right after the
            /// segment they attach to (UMIGURI resolves Previous from the
            /// immediately preceding line).
            /// </summary>
            public int[] ChainSegmentEnds;

            /// <summary>
            /// Per-segment output lines of a SLIDE chain (e.g. "#480>s82").
            /// When writing, each segment is followed by any companion AIR /
            /// AIR-HOLD whose time matches the segment end.
            /// </summary>
            public List<string> ChainSegmentLines;
        }

        /// <summary>
        /// Converts supported CHUNITHM input files to UGC output.
        /// </summary>
        static public void Convert(string[] inArgs, long unitNumerator, long unitDenominator, bool idUseRenderAutoTip = false)
        {
            ShowSplash();
            string[] args = PrepareInputArguments(inArgs);
            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }

            foreach (string path in args)
            {
                if (!File.Exists(path))
                    continue;

                Console.WriteLine();
                Console.WriteLine("Processing File: " + path);
                ProcessInput(path, unitNumerator, unitDenominator);
            }

            Console.WriteLine("ChuniToUgc finished.");
        }

        private static void ShowSplash()
        {
            Splash.Show("Chuni to UGC Script");
        }

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

        private static void ShowUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: ChuniToUgc <input file>");
            Console.WriteLine("Supported: Chuni PC tab-separated chart files");
        }

        /// <summary>
        /// Dispatches one input file to the appropriate converter based on its extension.
        ///   .c2s -> UGC chart conversion
        ///   .dds -> jacket image conversion
        ///   .acb / .awb -> ACB/AWB audio extraction (AcbToWav)
        /// </summary>
        private static void ProcessInput(string filename, long unitNumerator, long unitDenominator)
        {
            string ext = Path.GetExtension(filename).ToLowerInvariant();
            switch (ext)
            {
                case ".c2s":
                    ProcessFile(filename, unitNumerator, unitDenominator);
                    break;
                case ".dds":
                    ConvertDds(filename);
                    break;
                case ".acb":
                case ".awb":
                    AcbToWav.Convert(new[] { filename });
                    break;
                default:
                    Console.WriteLine("Unsupported file type: " + ext);
                    break;
            }
        }

        /// <summary>
        /// Converts one DDS jacket image to jacket.jpg next to the UGC output.
        /// Output goes to the CHUNI output folder (or BMS Output as fallback).
        /// </summary>
        private static void ConvertDds(string filename)
        {
            try
            {
                string dir = Path.GetDirectoryName(filename) + "\\";
                MusicMetadata meta = LoadMusicMetadata(dir);
                string title = meta?.Title ?? Path.GetFileNameWithoutExtension(filename);
                string genre = meta?.Genre ?? "CHUNITHM";

                DDSImage img = new DDSImage(filename);

                Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
                string chuniOutput = config["CHUNI"]["Output"];
                string outputRoot = string.IsNullOrEmpty(chuniOutput) ? config["BMS"]["Output"] : chuniOutput;
                if (string.IsNullOrEmpty(outputRoot))
                    outputRoot = "output";

                string dirPath = Path.Combine(outputRoot, Common.nameReplace(genre), Common.nameReplace(title), "jacket.jpg");
                img.Save(dirPath);
                Console.WriteLine("  -> " + dirPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  FAILED: " + ex.Message);
            }
        }

        private static void ProcessFile(string filename, long unitNumerator, long unitDenominator)
        {
            string dir = Path.GetDirectoryName(filename) + "\\";
            MusicMetadata meta = LoadMusicMetadata(dir);

            // Read the chart through ChuniC2S so TickRate is configured.
            ChartChuni chart;
            using (StreamReader reader = new StreamReader(filename))
            {
                chart = ChuniC2S.Read(reader, unitNumerator, unitDenominator).chart;
            }

            if (chart == null)
            {
                Console.WriteLine("  FAILED: empty chart");
                return;
            }

            string output = BuildOutputPath(chart, filename, meta);
            string ugc = WriteUgc(chart, meta, Path.GetFileName(filename));
            File.WriteAllText(output, ugc, Encoding.UTF8);
        }

        private static MusicMetadata LoadMusicMetadata(string dir)
        {
            string musicXmlPath = Path.Combine(dir, "Music.xml");
            if (!File.Exists(musicXmlPath))
                return null;

            try
            {
                XElement xml = XElement.Load(musicXmlPath);
                MusicMetadata meta = new MusicMetadata();
                meta.Id = xml.Element("name").Element("id").Value;
                meta.Title = xml.Element("name").Element("str").Value;
                meta.Artist = xml.Element("artistName").Element("str").Value;
                meta.Genre = xml.Element("genreNames").Element("list").Element("StringID").Element("str").Value;
                // Release date (YYYYMMDD).
                meta.ReleaseDate = xml.Element("releaseDate")?.Value ?? "";

                meta.Charts = new Dictionary<string, MusicChartMeta>();
                IEnumerable<XElement> rows = xml.Element("fumens").Elements("MusicFumenData");
                foreach (XElement row in rows)
                {
                    string path = row.Element("file").Element("path").Value;
                    if (string.IsNullOrEmpty(path))
                        continue;

                    string id = row.Element("type").Element("id").Value;
                    MusicChartMeta chartMeta = new MusicChartMeta();
                    chartMeta.TypeId = id;
                    chartMeta.TypeName = row.Element("type").Element("data").Value;
                    chartMeta.Level = row.Element("levelDecimal").Value != null
                        ? (row.Element("level").Value + (int.Parse(row.Element("levelDecimal").Value) >= 50 ? "+" : ""))
                        : row.Element("level").Value;

                    if (id == "4")
                    {
                        chartMeta.WeAttr = xml.Element("worldsEndTagName")?.Element("str")?.Value ?? "";
                        string star = "";
                        switch (xml.Element("starDifType")?.Value)
                        {
                            case "9": star = "5"; break;
                            case "7": star = "4"; break;
                            case "5": star = "3"; break;
                            case "3": star = "2"; break;
                            case "1": star = "1"; break;
                        }
                        chartMeta.Level = star;
                    }

                    meta.Charts[path] = chartMeta;
                }

                return meta;
            }
            catch
            {
                return null;
            }
        }

        private static MusicChartMeta ResolveChartMeta(string filename, MusicMetadata meta)
        {
            if (meta?.Charts != null)
            {
                string name = Path.GetFileName(filename);
                if (meta.Charts.ContainsKey(name))
                    return meta.Charts[name];
            }
            return new MusicChartMeta { TypeId = "0", TypeName = "BASIC", Level = "1" };
        }

        private static string BuildOutputPath(ChartChuni chart, string filename, MusicMetadata meta)
        {
            // Output to the [CHUNI] OUTPUT folder, or [BMS] OUTPUT as fallback.
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            string chuniOutput = config["CHUNI"]["Output"];
            string outputRoot = string.IsNullOrEmpty(chuniOutput) ? config["BMS"]["Output"] : chuniOutput;
            if (string.IsNullOrEmpty(outputRoot))
                outputRoot = "output";

            string title = meta?.Title ?? Path.GetFileNameWithoutExtension(filename);
            string genre = meta?.Genre ?? "CHUNITHM";
            string name = Common.nameReplace(title);
            string genreClean = Common.nameReplace(genre);
            string dirPath = Path.Combine(outputRoot, genreClean, name);
            Common.SafeCreateDirectory(dirPath);

            MusicChartMeta chartMeta = ResolveChartMeta(Path.GetFileName(filename), meta);
            string outName = name + "(" + chartMeta.TypeName + ")";
            return Path.Combine(dirPath, outName + ".ugc");
        }

        private static string WriteUgc(ChartChuni chart, MusicMetadata meta, string filename)
        {
            MusicChartMeta chartMeta = ResolveChartMeta(filename, meta);
            StringBuilder sb = new StringBuilder();

            // Build the @BEAT-aware measure lengths first so the header
            // (BPM / SPDMOD) and every note line can re-map onto them.
            BuildMeasureTicks(chart);

            WriteHeader(sb, chart, meta, chartMeta);
            WriteNotes(sb, chart);

            return sb.ToString();
        }

        /// <summary>
        /// Writes the complete UGC header section, matching the standard
        /// Margrete / Umiguri Chart v8 header layout.
        /// </summary>
        private static void WriteHeader(StringBuilder sb, ChartChuni chart, MusicMetadata meta, MusicChartMeta chartMeta)
        {
            sb.AppendLine("' Created with ChuniToUgc");

            double mainBpm = (double)chart.DefaultBPM;
            if (mainBpm <= 0) mainBpm = 120;

            sb.AppendLine("@VER\t8");
            sb.AppendLine("@EXVER\t1");

            if (meta != null)
            {
                sb.AppendLine("@TITLE\t" + meta.Title);
                // UGC sort key: generated from the title using the spec rules.
                sb.AppendLine("@SORT\t" + GenerateSortKey(meta.Title));
                if (!string.IsNullOrEmpty(meta.Artist))
                    sb.AppendLine("@ARTIST\t" + meta.Artist);
                if (!string.IsNullOrEmpty(meta.Genre))
                    sb.AppendLine("@GENRE\t" + meta.Genre);
            }

            // Designer from chart tags
            if (chart.Tags.ContainsKey("DESIGNER") && !string.IsNullOrEmpty(chart.Tags["DESIGNER"]))
                sb.AppendLine("@DESIGN\t" + chart.Tags["DESIGNER"]);

            // Difficulty
            if (!string.IsNullOrEmpty(chartMeta.TypeId) && DifficultyMap.ContainsKey(chartMeta.TypeId))
                sb.AppendLine("@DIFF\t" + DifficultyMap[chartMeta.TypeId]);
            else
                sb.AppendLine("@DIFF\t0");

            sb.AppendLine("@LEVEL\t" + (chartMeta.Level ?? "1"));

            if (chartMeta.TypeId == "4" && !string.IsNullOrEmpty(chartMeta.WeAttr))
                sb.AppendLine("@WEATTR\t" + chartMeta.WeAttr);

            sb.AppendLine("@CONST\t0.00000");

            // Song ID
            if (meta != null && !string.IsNullOrEmpty(meta.Id))
                sb.AppendLine("@SONGID\t" + meta.Id);

            // Release date (YYYYMMDD)
            if (meta != null && !string.IsNullOrEmpty(meta.ReleaseDate))
                sb.AppendLine("@RLDATE\t" + meta.ReleaseDate);

            // Audio/jacket defaults
            // BGM extension matches the [CHUNI] SoundOutputFormat codec.
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            string soundFormat = SoundEncoderFactory.NormalizeFormat(config["CHUNI"].GetString("SoundOutputFormat", SoundEncoderFactory.DefaultFormat));
            string bgmExt = SoundEncoderFactory.GetFileExtension(soundFormat);
            sb.AppendLine("@BGM\tmusic." + bgmExt);
            sb.AppendLine("@BGMOFS\t0.00000");
            sb.AppendLine("@BGMPRV\t0.00000\t0.00000");
            sb.AppendLine("@JACKET\tjacket.jpg");
            sb.AppendLine("@BGIMG\t");
            sb.AppendLine("@BGMODE\tPASSIVE\tFALSE");
            sb.AppendLine("@FLDCOL\t-1");
            sb.AppendLine("@FLDIMG\t");

            // Flags
            sb.AppendLine("@FLAG\tDIFFTTL\tFALSE");
            sb.AppendLine("@FLAG\tSOFFSET\tTRUE");
            sb.AppendLine("@FLAG\tCLICK\tTRUE");
            sb.AppendLine("@FLAG\tEXLONG\tFALSE");
            sb.AppendLine("@FLAG\tBGMWCMP\tFALSE");
            sb.AppendLine("@FLAG\tHIPRECISION\tTRUE");

            // Metadata info
            sb.AppendLine("@ATINFO\tAUTHORS\t");
            sb.AppendLine("@ATINFO\tSITES\t");
            sb.AppendLine("@DLURL\t");
            sb.AppendLine("@COPYRIGHT\t");
            sb.AppendLine("@LICENSE\t");

            sb.AppendLine("@TICKS\t480");
            WriteBeatHeader(sb, chart);

            // BPM changes
            WriteBpmHeader(sb, chart, mainBpm);

            // Speed changes from CHUNITHM SFL events.
            WriteTimelineHeader(sb, chart);

            sb.AppendLine("@MAINBPM\t" + mainBpm.ToString("F5"));
            sb.AppendLine("@MAINTIL\t0");
            sb.AppendLine("@ENDHEAD");
            sb.AppendLine();
        }

        /// <summary>
        /// CHUNITHM MET (meter change) event, with its measure-local position
        /// (0..383 on the 384 grid) so multiple METs in one measure can be
        /// ordered, e.g. Arcahv measure 2's tuplet run
        /// (1/384, 1/192, 1/128, 1/96, 64/64).
        /// </summary>
        private struct MetInfo
        {
            public int Measure;
            public int Position;   // 0..383 within the measure
            public int Numerator;
            public int Denominator;
        }

        /// <summary>
        /// Collects MET events from the chart, excluding STP (Player 1) and
        /// zero-valued events, ordered by (measure, position).
        /// </summary>
        private static List<MetInfo> CollectMets(ChartChuni chart)
        {
            List<MetInfo> mets = new List<MetInfo>();
            foreach (EntryChuni entry in chart.Entries)
            {
                if (entry.Type != EntryTypeChuni.Event)
                    continue;
                if (entry.Player != 0)
                    continue; // STP (Player 1) does not affect measure length
                if (entry.Value.Denominator == 0 || entry.Value.Numerator == 0)
                    continue; // stop / other events

                int measure = entry.Parameter != 0 ? entry.Parameter : entry.MetricMeasure;
                int numerator = (int)entry.Value.Numerator;
                int denominator = (int)entry.Value.Denominator;
                if (numerator <= 0 || denominator <= 0)
                    continue;

                // The CHUNITHM grid is 384 ticks per measure. LinearOffset is
                // measure*384 + position.
                int position = (int)((double)entry.LinearOffset % 384);
                mets.Add(new MetInfo { Measure = measure, Position = position, Numerator = numerator, Denominator = denominator });
            }
            mets.Sort((a, b) =>
            {
                int cmp = a.Measure.CompareTo(b.Measure);
                if (cmp != 0) return cmp;
                return a.Position.CompareTo(b.Position);
            });
            return mets;
        }

        /// <summary>
        /// Writes @BEAT definitions. Always emits the initial 4/4 at measure 0,
        /// then one @BEAT per measure using the LAST MET in that measure.
        /// Multiple METs in one measure (CHUNITHM tuplets) cannot be expressed
        /// as per-measure @BEAT changes in UMIGURI, so the measure length is
        /// determined by its final meter.
        /// ChuniPC stores a MET as an EntryTypeChuni.Event whose Value is
        /// Fraction(numerator, denominator); MET 38 0 4 3 becomes
        /// @BEAT 38 3 4.
        /// </summary>
        private static void WriteBeatHeader(StringBuilder sb, ChartChuni chart)
        {
            sb.AppendLine("@BEAT\t0\t4\t4");

            List<MetInfo> mets = CollectMets(chart);
            Dictionary<int, MetInfo> lastByMeasure = new Dictionary<int, MetInfo>();
            foreach (MetInfo met in mets)
                lastByMeasure[met.Measure] = met; // sorted by position, so the last one wins

            List<int> measures = new List<int>(lastByMeasure.Keys);
            measures.Sort();
            foreach (int measure in measures)
            {
                MetInfo met = lastByMeasure[measure];
                if (measure == 0 && met.Numerator == 4 && met.Denominator == 4)
                    continue;
                sb.AppendLine("@BEAT\t" + met.Measure + "\t" + met.Numerator + "\t" + met.Denominator);
            }
        }

        /// <summary>
        /// Writes @TIL timeline definitions from CHUNITHM SFL (speed change)
        /// events. The ChuniPC parser stores these in chart.Tags["TIL00"] as
        /// "bar'tick:speed, bar'tick:speed, ...".
        ///
        /// UMIGURI v8 distinguishes the two speed concepts:
        ///   @TIL    timeline definition (soflan): TimelineId BarTick Speed
        ///   @SPDMOD note speed definition:         BarTick Speed
        /// CHUNITHM SFL scrolls the field like a SOF-LAN, so it is emitted as
        /// @TIL entries on timeline 0 (matching the header @MAINTIL 0).
        ///
        /// Each SFL emits two points (the speed change and the 1.0 restore);
        /// duplicate bar'tick positions are collapsed, and the leading 1.0
        /// initial point (0'0) is skipped.
        /// </summary>
        private static void WriteTimelineHeader(StringBuilder sb, ChartChuni chart)
        {
            if (chart.Tags.ContainsKey("TIL00") && !string.IsNullOrEmpty(chart.Tags["TIL00"]))
            {
                string til = chart.Tags["TIL00"].Trim();
                string[] points = til.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (points.Length > 0)
                {
                    List<string> converted = new List<string>();
                    foreach (string point in points)
                    {
                        string trimmed = point.Trim();
                        // "bar'tick:speed"
                        int colonIdx = trimmed.LastIndexOf(':');
                        if (colonIdx <= 0)
                            continue;
                        string barTickPart = trimmed.Substring(0, colonIdx);
                        string speedPart = trimmed.Substring(colonIdx + 1);
                        double speed;
                        if (!double.TryParse(speedPart, NumberStyles.Float, CultureInfo.InvariantCulture, out speed))
                            continue;

                        int quoteIdx = barTickPart.IndexOf('\'');
                        int bar, tick;
                        if (quoteIdx > 0 && int.TryParse(barTickPart.Substring(0, quoteIdx), out bar) && int.TryParse(barTickPart.Substring(quoteIdx + 1), out tick))
                        {
                            // The SFL bar'tick is on the CHUNITHM fixed grid;
                            // re-map through the MET-aware measure lengths so
                            // the @TIL point aligns with the @BEAT layout.
                            int abs = bar * StandardTicksPerMeasure + tick;
                            int outBar, outTick;
                            ConvertToUmiguriBarTick(abs, out outBar, out outTick);
                            converted.Add(outBar + "'" + outTick + "\t" + speed.ToString("F5", CultureInfo.InvariantCulture));
                        }
                    }

                    // Deduplicate by bar'tick. Each SFL emits two points: the
                    // speed change at its start and a 1.0 restore at its end.
                    // For contiguous SFL segments the restore lands exactly on
                    // the next SFL's speed-change position. The accumulated
                    // TIL00 order is [restore, change] at that position, so the
                    // speed change must win over the 1.0 restore. Keep the LAST
                    // occurrence of each bar'tick while preserving the time order.
                    Dictionary<string, int> indexByKey = new Dictionary<string, int>();
                    List<string> unique = new List<string>();
                    foreach (string entry in converted)
                    {
                        string key = entry.Substring(0, entry.IndexOf('\t'));
                        int existingIndex;
                        if (indexByKey.TryGetValue(key, out existingIndex))
                        {
                            unique[existingIndex] = entry; // last occurrence wins
                        }
                        else
                        {
                            indexByKey[key] = unique.Count;
                            unique.Add(entry);
                        }
                    }
                    // Timeline 0 is the base timeline referenced by @MAINTIL 0.
                    foreach (string entry in unique)
                        sb.AppendLine("@TIL\t0\t" + entry);
                }
            }
        }

        /// <summary>
        /// Writes @BPM header entries for tempo changes.
        /// </summary>
        private static void WriteBpmHeader(StringBuilder sb, ChartChuni chart, double mainBpm)
        {
            sb.AppendLine("@BPM\t0'0\t" + mainBpm.ToString("F5"));

            List<TempoChange> changes = new List<TempoChange>();
            foreach (EntryChuni entry in chart.Entries)
            {
                if (entry.Type == EntryTypeChuni.Tempo)
                {
                    double bpm = (double)entry.Value;
                    string barTick = FormatBarTick(entry);
                    changes.Add(new TempoChange { BarTick = barTick, Bpm = bpm });
                }
            }

            changes.Sort((a, b) =>
            {
                int barA = int.Parse(a.BarTick.Split('\'')[0]);
                int barB = int.Parse(b.BarTick.Split('\'')[0]);
                if (barA != barB) return barA.CompareTo(barB);
                int tickA = int.Parse(a.BarTick.Split('\'')[1]);
                int tickB = int.Parse(b.BarTick.Split('\'')[1]);
                return tickA.CompareTo(tickB);
            });

            foreach (TempoChange change in changes)
            {
                if (change.BarTick != "0'0" && Math.Abs(change.Bpm - mainBpm) > 0.001)
                    sb.AppendLine("@BPM\t" + change.BarTick + "\t" + change.Bpm.ToString("F5"));
            }
        }

        private struct TempoChange
        {
            public string BarTick;
            public double Bpm;
        }

        /// <summary>
        /// Builds the tick length of every UMIGURI measure from the CHUNITHM
        /// MET events. Default 4/4 = 1920 ticks; a MET at measure M switches
        /// the length from M onward (e.g. 3/4 => 1440 ticks, 4/4 => 1920 ticks).
        /// </summary>
        private static Dictionary<int, int> BuildMeasureTicks(ChartChuni chart)
        {
            Dictionary<int, int> ticks = new Dictionary<int, int>();
            currentMeasureTicks = ticks;

            List<MetInfo> mets = CollectMets(chart);

            int lastMeasure = 0;
            foreach (EntryChuni entry in chart.Entries)
            {
                if (entry.MetricMeasure > lastMeasure)
                    lastMeasure = entry.MetricMeasure;
            }
            foreach (MetInfo met in mets)
            {
                if (met.Measure > lastMeasure)
                    lastMeasure = met.Measure;
            }

            int idx = 0;
            int currentLen = StandardTicksPerMeasure;
            for (int m = 0; m <= lastMeasure + 10; m++)
            {
                // When multiple METs (tuplets) exist in the same measure, the
                // last MET determines the measure length (UMIGURI @BEAT is
                // effective per measure).
                // Example: Arcahv measure 2's MET group (1/384, 1/192, 1/128,
                // 1/96, 64/64): the last 64/64 (= 4/4) makes measure 2 1920
                // ticks long.
                while (idx < mets.Count && mets[idx].Measure == m)
                {
                    int length = TicksPerBeat * DefaultBeatsPerMeasure * mets[idx].Numerator / mets[idx].Denominator;
                    if (length > 0)
                        currentLen = length;
                    idx++;
                }
                ticks[m] = currentLen;
            }
            return ticks;
        }

        /// <summary>
        /// Converts an absolute tick on the fixed 1920 grid (CHUNITHM) into a
        /// Bar'Tick position on the @BEAT-aware UMIGURI measure layout. The
        /// absolute tick count is preserved, so real time stays identical;
        /// only the measure boundaries follow the MET changes.
        /// </summary>
        private static void ConvertToUmiguriBarTick(int chuniAbsTick, out int bar, out int tick)
        {
            int m = 0;
            int remaining = chuniAbsTick;
            while (true)
            {
                int len;
                if (!currentMeasureTicks.TryGetValue(m, out len))
                    len = StandardTicksPerMeasure;
                if (remaining < len)
                {
                    bar = m;
                    tick = remaining;
                    return;
                }
                remaining -= len;
                m++;
            }
        }

        /// <summary>
        /// Writes all notes in time order, but emits companion AIR notes
        /// (AUL / ADW / ...) and AIR-HOLDs (AHD) immediately after the
        /// ground-note unit they are connected to (TAP / CHR / HLD / SLIDE /
        /// AHD), matching the reference converter (Margrete / MuConvert)
        /// output order. UMIGURI requires the attached AIR / AIR-HOLD to be
        /// the very next line after its ground note, otherwise it cannot
        /// resolve the Previous relationship.
        /// </summary>
        private static void WriteNotes(StringBuilder sb, ChartChuni chart)
        {
            List<NoteUnit> units = BuildNoteUnits(chart);

            units.Sort(CompareUnit);

            // All AIR (Player 5), AIR-HOLD (Player 4) and AIR-SLIDE (Player 6)
            // units are pulled out into the companion list; they are flushed
            // right after their connected ground unit so UMIGURI can resolve
            // the Previous relationship. AIR/AHD with a TAP/CHR/FLK ground
            // carry Parameter == 0 (MapCompanionCode returns 0 for those).
            // AIR-SLIDE (S) attaches to its TargetNote ground (same column;
            // same tick for instant notes, the hold END tick for HLD/HXD), so
            // it must be flushed immediately after that ground. Keeping the S
            // in the companion list also places it BEFORE any same-tick
            // AIR-CRUSH (C), so UMIGURI never mistakes the S for a decoration
            // of the C (C is previous-less and never precedes the S).
            List<NoteUnit> companionAirs = new List<NoteUnit>();
            List<NoteUnit> normal = new List<NoteUnit>();

            foreach (NoteUnit unit in units)
            {
                bool isCompanion = unit.Entry != null &&
                    (unit.Entry.Player == PlayerAir || unit.Entry.Player == PlayerAirHold ||
                     unit.Entry.Player == PlayerAirSlide);
                if (isCompanion)
                    companionAirs.Add(unit);
                else
                    normal.Add(unit);
            }

            foreach (NoteUnit unit in normal)
            {
                // SLIDE chains are written segment-by-segment so companion
                // AIR / AIR-HOLD notes can be placed immediately after the
                // segment whose end time/column they attach to.
                if (unit.ChainSegmentLines != null && unit.ChainSegmentLines.Count > 0)
                {
                    // First write the parent line (#bar'tick:s...).
                    string parentLine = unit.Text.Substring(0, unit.Text.IndexOf('\n', StringComparison.Ordinal)).TrimEnd('\r');
                    if (parentLine.Length == 0)
                        parentLine = unit.Text;
                    sb.AppendLine(parentLine);

                    // After the chain parent, emit AIR-SLIDE companions that
                    // attach to the chain start (TargetNote = SLD/SLC).
                    // AHD / AIR notes are still placed by the per-segment
                    // EmitCompanionAirsAt logic below.
                    if (unit.Entry != null)
                        EmitAirSlidesAtStart(sb, companionAirs, unit);

                    // Then each segment line + companions matching its end.
                    for (int s = 0; s < unit.ChainSegmentLines.Count; s++)
                    {
                        sb.AppendLine(unit.ChainSegmentLines[s]);
                        if (unit.Entry != null && unit.ChainSegmentEnds != null && s < unit.ChainSegmentEnds.Length)
                        {
                            // ChainSegmentLines[s] is the segment ending at
                            // chain entry s+1, so its end column is the
                            // (s+1)-th entry of ChainColumns.
                            int endColumn = (unit.ChainColumns != null && s + 1 < unit.ChainColumns.Length) ? unit.ChainColumns[s + 1] : -1;
                            EmitCompanionAirsAt(sb, companionAirs, unit, unit.ChainSegmentEnds[s], endColumn);
                        }
                    }
                    continue;
                }

                sb.AppendLine(unit.Text);

                if (unit.Entry != null)
                    EmitCompanionAirs(sb, companionAirs, unit);
            }

            // Any remaining companion notes whose column did not match any
            // SLIDE segment are emitted in time order so UMIGURI treats them
            // as independent AIR / AIR-HOLD notes (Previous-less).
            if (companionAirs.Count > 0)
            {
                companionAirs.Sort(CompareUnit);
                foreach (NoteUnit unit in companionAirs)
                    sb.AppendLine(unit.Text);
            }
        }

        /// <summary>
        /// Emits companion AIR / AIR-HOLD notes whose time exactly matches the
        /// given absolute tick, and which can resolve Previous from the ground
        /// unit (same player + column family).
        /// </summary>
        /// <param name="segmentEndColumn">
        /// For a SLIDE chain, the end column of the specific chain segment that
        /// ends at <paramref name="atAbsTick"/>. UMIGURI resolves the Previous
        /// of an AIR / AIR-HOLD against the immediately preceding segment line,
        /// whose end cell is this column. When negative, falls back to matching
        /// any column in the chain (legacy behavior).
        /// </param>
        /// <param name="allowDifferentColumnAtStart">
        /// When true (used for the SLIDE start point), AIR / AIR-HOLD notes at
        /// the same tick are attached to the SLIDE regardless of column.
        /// CHUNITHM permits the AIR on a same-time SLIDE to use a different
        /// column (e.g. AHD 42 0 6 2 SLD 96 next to SLD 42 0 0 4 96).
        /// </param>
        private static void EmitCompanionAirsAt(StringBuilder sb, List<NoteUnit> pendingAir, NoteUnit ground, int atAbsTick, int segmentEndColumn, bool allowDifferentColumnAtStart = false)
        {
            if (ground.Entry == null)
                return;
            int groundPlayer = ground.Entry.Player;

            for (int i = pendingAir.Count - 1; i >= 0; i--)
            {
                NoteUnit air = pendingAir[i];
                if (air.Entry == null)
                    continue;
                bool matchesGround;
                if (air.Entry.Player == PlayerAirSlide)
                {
                    // AIR-SLIDE attaches to a SLIDE chain segment end: the
                    // TargetNote column (SLD/SLC/SXD/SXC) matches the ground
                    // SLIDE family (Player 3 covers SLD/SLC/SXD/SXC), the
                    // AIR-SLIDE must start at the segment end tick, and its
                    // column must match the segment end column.
                    matchesGround = AirSlideMatchesSegmentEnd(air.Entry, ground.Entry, atAbsTick, segmentEndColumn);
                }
                else
                {
                    int airParam = air.Entry.Parameter;
                    if (groundPlayer == PlayerTap)
                        matchesGround = airParam == 0;
                    else
                        matchesGround = airParam == groundPlayer;
                }
                if (!matchesGround)
                    continue;

                // Column must match the segment that ends at atAbsTick, unless
                // the start-point rule allows any column. UMIGURI resolves the
                // Previous of an AIR / AIR-HOLD on a SLIDE from the immediately
                // preceding segment line, whose end cell is the segment's end
                // column. Matching against every column of the chain would let
                // a chain steal a same-column companion at an unrelated time
                // (e.g. 0933_03.c2s L449 AHD column 8 vs the long chain's
                // earlier column-8 waypoint).
                bool columnMatches = allowDifferentColumnAtStart;
                if (air.Entry.Player == PlayerAirSlide)
                {
                    // Column matching is already checked inside AirSlideMatchesSegmentEnd
                    columnMatches = true;
                }
                else if (!columnMatches && ground.ChainColumns != null)
                {
                    if (segmentEndColumn >= 0)
                    {
                        columnMatches = air.Entry.Column == segmentEndColumn;
                    }
                    else
                    {
                        for (int c = 0; c < ground.ChainColumns.Length; c++)
                        {
                            if (air.Entry.Column == ground.ChainColumns[c])
                            {
                                columnMatches = true;
                                break;
                            }
                        }
                    }
                }
                else if (!columnMatches)
                {
                    columnMatches = air.Entry.Column == ground.Entry.Column;
                }
                if (!columnMatches)
                    continue;

                int airAbs = ToAbsoluteTick(air.Entry);
                if (airAbs != atAbsTick)
                    continue;

                sb.AppendLine(air.Text);
                pendingAir.RemoveAt(i);

                // When multiple SLIDE chains end at the same point (e.g. a fan
                // of 15 SXC/SXD chains all converging to (63,0) col7), each
                // segment end must consume only ONE matching AIR-SLIDE. The
                // remaining AIR-SLIDEs are attached by the subsequent chains
                // that end at the same position, keeping a 1:1 pairing.
                // AHD / AIR notes still emit all matches at the same tick.
                if (air.Entry.Player == PlayerAirSlide)
                    break;
            }
        }

        /// <summary>
        /// Emits AIR-SLIDE (Player 6) companions that attach to the ground
        /// unit's start point (same tick, same column, TargetNote matches the
        /// ground type). Used after a SLIDE chain parent line so the AIR-SLIDE
        /// sits immediately after its ground SLIDE. Unlike EmitCompanionAirs,
        /// this only matches Player 6 (AIR-SLIDE) - AHD / AIR notes are still
        /// placed by the per-segment EmitCompanionAirsAt logic.
        /// </summary>
        private static void EmitAirSlidesAtStart(StringBuilder sb, List<NoteUnit> pendingAir, NoteUnit ground)
        {
            if (ground.Entry == null)
                return;
            int groundStartAbs = ground.StartAbs;

            for (int i = pendingAir.Count - 1; i >= 0; i--)
            {
                NoteUnit air = pendingAir[i];
                if (air.Entry == null || air.Entry.Player != PlayerAirSlide)
                    continue;

                // AIR-SLIDE must start at the same tick as the chain parent
                // and sit on the same column (same rule as
                // AirSlideMatchesGround).
                if (ToAbsoluteTick(air.Entry) != groundStartAbs)
                    continue;

                if (!AirSlideMatchesGround(air.Entry, ground))
                    continue;

                sb.AppendLine(air.Text);
                pendingAir.RemoveAt(i);
            }
        }

        /// <summary>
        /// Returns whether an AIR-SLIDE (Player 6) attaches to a SLIDE chain
        /// segment end. The TargetNote column (SLD/SLC/SXD/SXC) refers to the
        /// SLIDE family (Player 3 covers SLD/SLC/SXD/SXC), so the ground type
        /// check accepts any SLIDE-family entry. The AIR-SLIDE must start at
        /// the segment end tick and its column must match the segment end
        /// column.
        /// </summary>
        private static bool AirSlideMatchesSegmentEnd(EntryChuni air, EntryChuni ground, int atAbsTick, int segmentEndColumn)
        {
            if (air.Player != PlayerAirSlide)
                return false;

            string target = air.TargetNote ?? "";
            switch (target)
            {
                case "SLD":
                case "SLC":
                case "SXD":
                case "SXC":
                    if (ground.Player != PlayerSlide)
                        return false;
                    break;
                default:
                    return false; // TAP / CHR / HLD / AHD / AIR do not attach at a segment end
            }

            // AIR-SLIDE must start at the segment end tick.
            if (ToAbsoluteTick(air) != atAbsTick)
                return false;

            // Same-column 1:1: the AIR-SLIDE column must match the segment end column
            // (UMIGURI resolves Previous from the preceding line's end cell).
            if (segmentEndColumn >= 0 && air.Column != segmentEndColumn)
                return false;

            return true;
        }

        /// <summary>
        /// Returns whether an AIR-SLIDE (Player 6) attaches to the given ground
        /// note. The C2S TargetNote column (TAP / CHR / FLK / MNE / HLD / SLD /
        /// AHD / AIR) names the ground type; UMIGURI resolves the Previous of
        /// the AIR-SLIDE from the immediately preceding line, so the ground
        /// must be on the same column and at the matching tick.
        /// Time matching:
        ///   - Instant notes (TAP / CHR / FLK / MNE): the S attaches at the
        ///     ground START tick.
        ///   - Hold notes (HLD / HXD): the S attaches at the ground END tick
        ///     (the release point), e.g. ASC 82 0 0 4 HLD attaches to the HLD
        ///     whose end lands on (82,0).
        ///   - SLIDE chains: the S attaches at the chain start tick.
        /// </summary>
        private static bool AirSlideMatchesGround(EntryChuni air, NoteUnit ground)
        {
            if (air.Player != PlayerAirSlide || ground.Entry == null)
                return false;

            string target = air.TargetNote ?? "";
            int groundType = (int)(ground.Entry.Value.Numerator / 100);
            bool typeMatches;
            switch (target)
            {
                case "TAP": typeMatches = groundType == 1; break;
                case "CHR": typeMatches = groundType == 2; break;
                case "FLK": typeMatches = groundType == 3; break;
                case "MNE": typeMatches = groundType == 4; break;
                case "HLD":
                case "HXD": typeMatches = ground.Entry.Player == PlayerHold; break;
                case "SLD":
                case "SLC":
                case "SXD":
                case "SXC": typeMatches = ground.Entry.Player == PlayerSlide; break;
                case "AHD":
                case "AHX": typeMatches = ground.Entry.Player == PlayerAirHold; break;
                case "AIR": typeMatches = ground.Entry.Player == PlayerAir; break;
                default: typeMatches = false; break;
            }
            if (!typeMatches)
                return false;

            // Same-column 1:1: the AIR-SLIDE attaches to the ground note on the
            // same lane. UMIGURI resolves Previous from (time, cell, width),
            // so an ASC on lane 8 must attach to the CHR on lane 8, and the
            // AUR on lane 12 must attach to the CHR on lane 12.
            if (air.Column != ground.Entry.Column)
                return false;

            int airAbs = ToAbsoluteTick(air);
            if (ground.Entry.Player == PlayerHold)
            {
                // HLD / HXD: the S is placed at the hold's release (end) tick.
                return airAbs == ground.EndAbs;
            }
            // Instant notes and slide chains: the S starts with the ground.
            return airAbs == ground.StartAbs;
        }

        /// <summary>
        /// Emits and removes companion AIR / AIR-HOLD notes that target the
        /// given ground unit's player AND fall within the unit's time range.
        /// The companion's Parameter stores the connected ground player for
        /// linked grounds:
        ///   2 = HLD, 3 = SLIDE, 4 = AHD.
        /// Ground units map to players: HLD -> 2, SLIDE -> 3, AHD -> 4.
        /// For TAP / CHR / FLK / MNE grounds (Player 1, -4) the companion
        /// carries Parameter == 0 (MapCompanionCode returns 0 for these),
        /// so a Player-1 ground matches any companion whose Parameter is 0
        /// and whose time falls within the ground's range.
        ///
        /// The companion column must match the ground column: UMIGURI
        /// resolves Previous from (time, cell, width), so two AHDs at the
        /// same timing on different columns must attach to their own ground
        /// notes, not both to the first one.
        /// </summary>
        private static void EmitCompanionAirs(StringBuilder sb, List<NoteUnit> pendingAir, NoteUnit ground)
        {
            if (ground.Entry == null)
                return;
            int groundPlayer = ground.Entry.Player;
            int groundColumn = ground.Entry.Column;

            for (int i = pendingAir.Count - 1; i >= 0; i--)
            {
                NoteUnit air = pendingAir[i];
                if (air.Entry == null)
                    continue;

                bool matchesGround;
                if (air.Entry.Player == PlayerAirSlide)
                {
                    matchesGround = AirSlideMatchesGround(air.Entry, ground);
                }
                else
                {
                    int airParam = air.Entry.Parameter;
                    if (groundPlayer == PlayerTap)
                        matchesGround = airParam == 0; // TAP / CHR / FLK / MNE ground
                    else
                        matchesGround = airParam == groundPlayer;
                }

                if (!matchesGround)
                    continue;

                // Column must match: the companion attaches to the ground
                // note on the same lane. For a SLIDE chain, any segment in
                // the chain may serve as the Previous, so match against all
                // chain columns (the column check for AIR-SLIDE is already
                // done inside AirSlideMatchesGround).
                bool columnMatches;
                if (air.Entry.Player == PlayerAirSlide)
                {
                    // AIR-SLIDE: column matching is already checked inside AirSlideMatchesGround
                    columnMatches = true;
                }
                else if (ground.ChainColumns != null)
                {
                    columnMatches = false;
                    for (int c = 0; c < ground.ChainColumns.Length; c++)
                    {
                        if (air.Entry.Column == ground.ChainColumns[c])
                        {
                            columnMatches = true;
                            break;
                        }
                    }
                }
                else
                {
                    columnMatches = air.Entry.Column == groundColumn;
                }

                if (!columnMatches)
                    continue;

                int airAbs = air.Entry != null ? ToAbsoluteTick(air.Entry) : air.StartAbs;
                if (airAbs < ground.StartAbs || airAbs > ground.EndAbs)
                    continue;
                sb.AppendLine(air.Text);
                pendingAir.RemoveAt(i);

                // One ground carries exactly ONE AIR-SLIDE companion (1:1
                // pairing). Air / AIR-HOLD notes still emit all matches at
                // the same tick.
                if (air.Entry.Player == PlayerAirSlide)
                    break;
            }
        }

        /// <summary>
        /// Compares note units by metric time, then by priority (linked notes
        /// before single notes at the same timestamp).
        /// </summary>
        private static int CompareUnit(NoteUnit a, NoteUnit b)
        {
            int cmp = a.Measure.CompareTo(b.Measure);
            if (cmp != 0) return cmp;
            cmp = a.Offset.CompareTo(b.Offset);
            if (cmp != 0) return cmp;
            return a.Priority.CompareTo(b.Priority);
        }

        /// <summary>
        /// Builds all note units from the chart.
        /// HOLD / AIR-HOLD / AIR-CRUSH pairs are matched by column and their
        /// start/end structure; SLIDE chains are grouped by identifier;
        /// all other notes are written singly.
        /// </summary>
        private static List<NoteUnit> BuildNoteUnits(ChartChuni chart)
        {
            List<NoteUnit> units = new List<NoteUnit>();

            // Collect marker entries, skipping AHD companion AIR markers.
            List<EntryChuni> markers = new List<EntryChuni>();
            foreach (EntryChuni entry in chart.Entries)
            {
                if (entry.Type != EntryTypeChuni.Marker || entry.Player <= 0)
                    continue;

                // Skip the companion AIR marker emitted by ChuniPC for AHD lines
                // (value 1000+); it is represented by the AIR-HOLD itself.
                if (entry.Player == PlayerAir && (int)(entry.Value.Numerator / 100) >= 10)
                    continue;

                markers.Add(entry);
            }

            BuildSlideChains(markers, units);
            BuildLinkedPairs(markers, units);

            return units;
        }

        /// <summary>
        /// Groups ground slide (SLD/SLC) and air slide (ASD/ASC) entries into
        /// chains and renders them. Chain grouping uses the (player,
        /// identifier) pair and splits at every type-1 start.
        /// </summary>
        private static void BuildSlideChains(List<EntryChuni> markers, List<NoteUnit> units)
        {
            Dictionary<long, List<EntryChuni>> slideGroups = new Dictionary<long, List<EntryChuni>>();
            foreach (EntryChuni entry in markers)
            {
                // AIR-CRUSH (ALD) also forms identifier-based chains: each end
                // becomes the next start with a different column, so the
                // per-column pendingStart logic cannot pair them.
                if (entry.Player == PlayerSlide || entry.Player == PlayerAirSlide ||
                    entry.Player == PlayerAirCrush)
                {
                    long groupKey = ((long)entry.Player << 32) | (uint)(entry.Identifier & 0x7FFFFFFF);
                    List<EntryChuni> group;
                    if (!slideGroups.TryGetValue(groupKey, out group))
                    {
                        group = new List<EntryChuni>();
                        slideGroups[groupKey] = group;
                    }
                    group.Add(entry);
                }
            }

            foreach (List<EntryChuni> group in slideGroups.Values)
            {
                SortByTime(group);
                List<EntryChuni> currentChain = new List<EntryChuni>();
                foreach (EntryChuni entry in group)
                {
                    int type = (int)(entry.Value.Numerator / 100);
                    if (type == 1)
                    {
                        // A start entry begins a new chain UNLESS it connects
                        // to the previous entry's end point. ChuniPC can reuse
                        // the same identifier for unrelated chains (the
                        // AllocateIdentifier reset is time-based only), so we
                        // must also verify the previous end (time + end column)
                        // matches this start.
                        bool connected = false;
                        if (currentChain.Count > 0)
                        {
                            EntryChuni prev = currentChain[currentChain.Count - 1];
                            connected = (int)((double)prev.LinearOffset) == (int)((double)entry.LinearOffset) &&
                                        prev.Column == entry.Column;
                        }

                        if (currentChain.Count > 0 && !connected)
                        {
                            NoteUnit unit = RenderSlideChain(currentChain);
                            if (unit != null)
                                units.Add(unit);
                            currentChain.Clear();
                        }
                    }
                    currentChain.Add(entry);
                }
                if (currentChain.Count > 0)
                {
                    NoteUnit unit = RenderSlideChain(currentChain);
                    if (unit != null)
                        units.Add(unit);
                }
            }
        }

        /// <summary>
        /// Builds HOLD / AIR-HOLD / AIR-CRUSH pairs and single notes.
        /// </summary>
        private static void BuildLinkedPairs(List<EntryChuni> markers, List<NoteUnit> units)
        {
            // Same (player, column) can carry multiple start entries whose
            // ends all land on the same position (e.g. a HXD/HLD stall where
            // each hold ends at the same cell). Keep a per-column LIST of
            // pending starts and match each end to the latest start that
            // precedes it, so no start entry is lost.
            Dictionary<long, List<EntryChuni>> pendingStarts = new Dictionary<long, List<EntryChuni>>();

            foreach (EntryChuni entry in markers)
            {
                if (entry.Player == PlayerSlide || entry.Player == PlayerAirSlide ||
                    entry.Player == PlayerAirCrush) // ALD handled by BuildSlideChains
                    continue; // handled by BuildSlideChains

                int type = (int)(entry.Value.Numerator / 100);
                int col = entry.Column;

                if (entry.Player == PlayerTap || entry.Player == PlayerAir)
                {
                    // Single note (TAP / CHR / FLK / MNE / AIR)
                    NoteUnit unit = RenderSingle(entry);
                    if (unit != null)
                        units.Add(unit);
                    continue;
                }

                // Linked note: HLD / HXD (2), AHD / AHX (4), ALD (7)
                long pairKey = ((long)entry.Player << 32) | (uint)col;
                List<EntryChuni> list;
                if (!pendingStarts.TryGetValue(pairKey, out list))
                {
                    list = new List<EntryChuni>();
                    pendingStarts[pairKey] = list;
                }

                if (type == 1)
                {
                    list.Add(entry);
                }
                else
                {
                    // Find the latest pending start that precedes this end.
                    int endAbs = ToAbsoluteTick(entry);
                    EntryChuni start = null;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (ToAbsoluteTick(list[i]) < endAbs)
                        {
                            start = list[i];
                            list.RemoveAt(i);
                            break;
                        }
                    }

                    if (start != null)
                    {
                        NoteUnit unit;
                        if (entry.Player == PlayerHold)
                            unit = RenderHold(start, entry);
                        else if (entry.Player == PlayerAirCrush)
                            unit = RenderAirCrush(start, entry);
                        else
                            unit = RenderAirHold(start, entry);
                        if (unit != null)
                            units.Add(unit);
                    }
                    // The end also becomes a candidate start for the next
                    // connected pair.
                    if (list.Count == 0)
                    {
                        list.Add(entry);
                    }
                }
            }
        }

        /// <summary>
        /// Sorts a list of entries by metric time, then by encoded value.
        /// </summary>
        private static void SortByTime(List<EntryChuni> list)
        {
            list.Sort((a, b) =>
            {
                int cmp = a.MetricMeasure.CompareTo(b.MetricMeasure);
                if (cmp != 0) return cmp;
                cmp = ((double)a.MetricOffset).CompareTo((double)b.MetricOffset);
                if (cmp != 0) return cmp;
                return a.Value.Numerator.CompareTo(b.Value.Numerator);
            });
        }

        /// <summary>
        /// Renders a HOLD pair (h parent + offset>s child).
        /// </summary>
        private static NoteUnit RenderHold(EntryChuni start, EntryChuni end)
        {
            StringBuilder sb = new StringBuilder();
            int startTick = ToAbsoluteTick(start);
            AppendBarTick(sb, start);
            sb.Append(":h" + ToBase36(start.Column) + ToBase36(GetWidth(start)));
            AppendChild(sb, ToAbsoluteTick(end) - startTick, ">s");
            return new NoteUnit
            {
                Measure = start.MetricMeasure,
                Offset = (double)start.MetricOffset,
                Priority = PriorityHold,
                Text = sb.ToString(),
                Entry = start,
                StartAbs = ToAbsoluteTick(start),
                EndAbs = ToAbsoluteTick(end)
            };
        }

        /// <summary>
        /// Renders an AIR-HOLD pair (H parent + offset>s child).
        /// The end point always uses ">s" (AIR-ACTION).
        /// </summary>
        private static NoteUnit RenderAirHold(EntryChuni start, EntryChuni end)
        {
            StringBuilder sb = new StringBuilder();
            int startTick = ToAbsoluteTick(start);
            AppendBarTick(sb, start);
            string color = MapAirColor(start);
            sb.Append(":H" + ToBase36(start.Column) + ToBase36(GetWidth(start)) + color);
            AppendChild(sb, ToAbsoluteTick(end) - startTick, ">s");
            return new NoteUnit
            {
                Measure = start.MetricMeasure,
                Offset = (double)start.MetricOffset,
                Priority = PriorityAirHold,
                Text = sb.ToString(),
                Entry = start,
                StartAbs = ToAbsoluteTick(start),
                EndAbs = ToAbsoluteTick(end)
            };
        }

        /// <summary>
        /// Renders one connected SLIDE chain. Ground slide produces
        /// "s" + followers, air slide (Player 6) produces "S" + height +
        /// color and followers with height.
        /// </summary>
        private static NoteUnit RenderSlideChain(List<EntryChuni> chain)
        {
            if (chain.Count == 0)
                return null;

            EntryChuni parent = chain[0];
            if (parent.Player == PlayerAirSlide)
                return RenderAirSlideChain(chain);
            if (parent.Player == PlayerAirCrush)
                return RenderAirCrushChain(chain);

            StringBuilder sb = new StringBuilder();
            int parentTick = ToAbsoluteTick(parent);
            AppendBarTick(sb, parent);
            sb.Append(":s" + ToBase36(parent.Column) + ToBase36(GetWidth(parent)));

            // All columns encountered by the chain (including every segment's
            // start column) and each segment's ending position / line text.
            List<int> chainColumns = new List<int>();
            chainColumns.Add(parent.Column);
            List<int> segmentEnds = new List<int>();
            List<string> segmentLines = new List<string>();
            for (int i = 1; i < chain.Count; i++)
            {
                chainColumns.Add(chain[i].Column);
                segmentEnds.Add(ToAbsoluteTick(chain[i]));
            }

            for (int i = 1; i < chain.Count; i++)
            {
                EntryChuni child = chain[i];
                int offset = ToAbsoluteTick(child) - parentTick;
                int type = (int)(child.Value.Numerator / 100);
                string xw = ToBase36(child.Column) + ToBase36(GetWidth(child));

                string segmentLine;
                if (type == 4 || type == 5)
                {
                    // SLC / SXC (end + connected waypoint) -> control point (always >c)
                    segmentLine = "#" + offset.ToString() + ">c" + xw;
                }
                else
                {
                    // SLD / SXD segment -> slide (including the final point)
                    segmentLine = "#" + offset.ToString() + ">s" + xw;
                }
                segmentLines.Add(segmentLine);
                sb.AppendLine();
                sb.Append(segmentLine);
            }

            return new NoteUnit
            {
                Measure = parent.MetricMeasure,
                Offset = (double)parent.MetricOffset,
                Priority = PrioritySlide,
                Text = sb.ToString(),
                Entry = parent,
                StartAbs = ToAbsoluteTick(parent),
                EndAbs = ToAbsoluteTick(chain[chain.Count - 1]),
                ChainColumns = chainColumns.ToArray(),
                ChainSegmentEnds = segmentEnds.ToArray(),
                ChainSegmentLines = segmentLines
            };
        }

        /// <summary>
        /// Renders an AIR-SLIDE chain: parent "S" + xw + height hh + color,
        /// followers ">s"/">c" + xw + height hh.
        /// </summary>
        private static NoteUnit RenderAirSlideChain(List<EntryChuni> chain)
        {
            EntryChuni parent = chain[0];
            StringBuilder sb = new StringBuilder();
            int parentTick = ToAbsoluteTick(parent);
            AppendBarTick(sb, parent);
            string color = MapAirSlideColor(parent);
            // ASC (Air Slide Control, ChuniPC Parameter==1) is a control point
            // that uses the apex height (EndHeight, C2S col10) for the UGC S
            // line height (e.g. ASC 128 192 1 1 SLD 1.0 24 0 1 19.0 -> parent
            // line height 19.0*15=285="7X"). ASD (Air Slide, Parameter==0)
            // uses the start height (Height, C2S col6).
            double parentHeight = (parent.Parameter == 1 && parent.EndHeight > 0) ? parent.EndHeight : parent.Height;
            sb.Append(":S" + ToBase36(parent.Column) + ToBase36(GetWidth(parent)) +
                      EncodeAirHeight(parentHeight) + color);

            for (int i = 1; i < chain.Count; i++)
            {
                EntryChuni child = chain[i];
                int offset = ToAbsoluteTick(child) - parentTick;
                int type = (int)(child.Value.Numerator / 100);
                string xw = ToBase36(child.Column) + ToBase36(GetWidth(child));
                // The segment height comes from the SEGMENT START entry's
                // EndHeight (C2S apex height col10). ChuniPC stores EndHeight only
                // on the start entry (AddAirSlideMarkerPair sets
                // startEntry.EndHeight), so using child.EndHeight (the end
                // entry) always falls back to parent.Height and renders the
                // ending height too low (e.g. ASC 128 192 1 1 SLD 1.0 24
                // 0 1 19.0 -> follower must be 19.0*15=285="7X", not 1.0).
                double segHeight = chain[i - 1].EndHeight > 0 ? chain[i - 1].EndHeight : chain[i - 1].Height;
                string hh = EncodeAirHeight(segHeight);
                // type 1 = fresh start, type 2 = final end, type 3+ = merged
                // continuation (previous end also starts the next segment).
                // A merged point ends with a control point (no AIR-ACTION) -> ">c";
                // a final end carries AIR-ACTION -> ">s".
                char marker = (type >= 3) ? 'c' : 's';
                AppendChild(sb, offset, ">" + marker.ToString() + xw + hh);
            }

            return new NoteUnit
            {
                Measure = parent.MetricMeasure,
                Offset = (double)parent.MetricOffset,
                Priority = PriorityAirSlide,
                Text = sb.ToString(),
                Entry = parent,
                StartAbs = ToAbsoluteTick(parent),
                EndAbs = ToAbsoluteTick(chain[chain.Count - 1])
            };
        }

        /// <summary>
        /// Renders an AIR-CRUSH chain (C parent + offset>c followers).
        /// Parent format: #BarTick:C x w hh color,interval
        /// Each ALD start/end pair is a segment: the chain start is the parent,
        /// every following entry (which is the previous end and the next start)
        /// becomes a ">c" follower.
        /// </summary>
        private static NoteUnit RenderAirCrushChain(List<EntryChuni> chain)
        {
            EntryChuni parent = chain[0];
            StringBuilder sb = new StringBuilder();
            int parentTick = ToAbsoluteTick(parent);
            AppendBarTick(sb, parent);
            string color = MapAirCrushColor(parent);
            string interval = FormatCrushInterval(parent);
            sb.Append(":C" + ToBase36(parent.Column) + ToBase36(GetWidth(parent)) +
                      EncodeAirHeight(parent.Height) + color + "," + interval);

            for (int i = 1; i < chain.Count; i++)
            {
                EntryChuni child = chain[i];
                int offset = ToAbsoluteTick(child) - parentTick;
                string xw = ToBase36(child.Column) + ToBase36(GetWidth(child));
                double segHeight = chain[i - 1].EndHeight > 0 ? chain[i - 1].EndHeight : chain[i - 1].Height;
                AppendChild(sb, offset, ">c" + xw + EncodeAirHeight(segHeight));
            }

            return new NoteUnit
            {
                Measure = parent.MetricMeasure,
                Offset = (double)parent.MetricOffset,
                Priority = PriorityAirCrush,
                Text = sb.ToString(),
                Entry = parent,
                StartAbs = ToAbsoluteTick(parent),
                EndAbs = ToAbsoluteTick(chain[chain.Count - 1])
            };
        }

        /// <summary>
        /// Renders an AIR-CRUSH pair (C parent + offset>c child).
        /// Parent format: #BarTick:C x w hh color,interval
        /// </summary>
        private static NoteUnit RenderAirCrush(EntryChuni start, EntryChuni end)
        {
            StringBuilder sb = new StringBuilder();
            int startTick = ToAbsoluteTick(start);
            AppendBarTick(sb, start);
            string color = MapAirCrushColor(start);
            string interval = FormatCrushInterval(start);
            sb.Append(":C" + ToBase36(start.Column) + ToBase36(GetWidth(start)) +
                      EncodeAirHeight(start.Height) + color + "," + interval);
            AppendChild(sb, ToAbsoluteTick(end) - startTick, ">c" +
                        ToBase36(end.Column) + ToBase36(GetWidth(end)) +
                        EncodeAirHeight(start.EndHeight > 0 ? start.EndHeight : start.Height));
            return new NoteUnit
            {
                Measure = start.MetricMeasure,
                Offset = (double)start.MetricOffset,
                Priority = PriorityAirCrush,
                Text = sb.ToString(),
                Entry = start,
                StartAbs = ToAbsoluteTick(start),
                EndAbs = ToAbsoluteTick(end)
            };
        }

        /// <summary>
        /// Renders a single TAP / ExTAP / FLICK / DAMAGE / AIR note.
        /// </summary>
        private static NoteUnit RenderSingle(EntryChuni entry)
        {
            StringBuilder sb = new StringBuilder();
            string x = ToBase36(entry.Column);
            string w = ToBase36(GetWidth(entry));
            int type = (int)(entry.Value.Numerator / 100);

            AppendBarTick(sb, entry);
            switch (entry.Player)
            {
                case PlayerTap:
                    switch (type)
                    {
                        case 1: // TAP
                            sb.Append(":t" + x + w);
                            break;
                        case 2: // CHR -> ExTAP with direction
                            sb.Append(":x" + x + w + MapChrExtra(entry));
                            break;
                        case 3: // FLK -> FLICK, direction from tag or auto
                            sb.Append(":f" + x + w + MapFlickExtra(entry));
                            break;
                        case 4: // MNE -> DAMAGE
                            sb.Append(":d" + x + w);
                            break;
                        default:
                            sb.Append(":t" + x + w);
                            break;
                    }
                    break;
                case PlayerAir:
                    sb.Append(":a" + x + w + MapAirDirection(entry) + MapAirColor(entry));
                    break;
                default:
                    sb.Append(":t" + x + w);
                    break;
            }

            return new NoteUnit
            {
                Measure = entry.MetricMeasure,
                Offset = (double)entry.MetricOffset,
                Priority = PrioritySingle,
                Text = sb.ToString(),
                Entry = entry,
                StartAbs = ToAbsoluteTick(entry),
                EndAbs = ToAbsoluteTick(entry)
            };
        }

        /// <summary>
        /// Maps the C2S CHR direction tag to the UGC ExTAP extra character
        /// (defaults to U / up when absent).
        /// </summary>
        private static string MapChrExtra(EntryChuni entry)
        {
            string tag = entry.Tag ?? "";
            string mapped;
            if (C2UChrExtras.TryGetValue(tag, out mapped))
                return mapped;
            return "U";
        }

        /// <summary>
        /// Maps the C2S FLK direction tag to the UGC FLICK direction.
        /// L / R are explicit directions, anything else is A (auto).
        /// </summary>
        private static string MapFlickExtra(EntryChuni entry)
        {
            string tag = entry.Tag ?? "";
            if (tag == "L") return "L";
            if (tag == "R") return "R";
            return "A";
        }

        /// <summary>
        /// Maps a CHUNITHM air entry to the UGC AIR direction code.
        /// </summary>
        private static string MapAirDirection(EntryChuni entry)
        {
            int type = (int)(entry.Value.Numerator / 100);
            switch (type)
            {
                case 1: return "UC"; // AIR (up)
                case 2: return "DC"; // ADW (down)
                case 3: return "UL"; // AUL (up-left)
                case 4: return "UR"; // AUR (up-right)
                case 5: return "DL"; // ADL (down-left)
                case 6: return "DR"; // ADR (down-right)
                default: return "UC";
            }
        }

        /// <summary>
        /// Maps the C2S AIR / AIR-HOLD color to the UGC color character.
        /// Down-direction AIRs (ADW/ADR/ADL) swap green/purple.
        /// </summary>
        private static string MapAirColor(EntryChuni entry)
        {
            string tag = entry.Tag ?? "";
            bool isDown = IsDownDirection(entry);
            switch (tag)
            {
                case "GRN": return isDown ? "I" : "N";
                case "PPL": return isDown ? "N" : "I";
                default: return "N";
            }
        }

        /// <summary>
        /// Maps the C2S AIR-SLIDE color to the UGC color character.
        /// </summary>
        private static string MapAirSlideColor(EntryChuni entry)
        {
            string tag = entry.Tag ?? "";
            switch (tag)
            {
                case "GRN": return "I";
                case "PPL": return "I";
                default: return "N";
            }
        }

        /// <summary>
        /// Maps the C2S AIR-CRUSH color to the UGC color character.
        /// </summary>
        private static string MapAirCrushColor(EntryChuni entry)
        {
            string tag = entry.Tag ?? "";
            string mapped;
            if (C2UAirCrushColor.TryGetValue(tag, out mapped))
                return mapped;
            return "0";
        }

        /// <summary>
        /// Returns whether the AIR entry points downward.
        /// </summary>
        private static bool IsDownDirection(EntryChuni entry)
        {
            int type = (int)(entry.Value.Numerator / 100);
            return type == 2 || type == 5 || type == 6; // ADW / ADL / ADR
        }

        /// <summary>
        /// Serializes an AIR-CRUSH interval. The C2S value is on the 384-per-
        /// measure CHUNITHM grid; UGC interval is in @TICKS(480)-based ticks
        /// (1920 per 4/4 measure), so the value is scaled by the measure ratio
        /// (1920/384 = 5). Intervals longer than 25 measures are "$".
        /// </summary>
        private static string FormatCrushInterval(EntryChuni entry)
        {
            double scale = (double)StandardTicksPerMeasure / 384.0; // 1920/384 = 5
            int interval = (int)Math.Round(entry.CrushInterval * scale);
            if (interval > AirCrushIntervalAutoThreshold * scale)
                return "$";
            return interval.ToString();
        }

        /// <summary>
        /// Encodes a C2S height value into the UGC 2-character base-36 height.
        /// Scaled by 15 so real-device (CHUNITHM) heights match (e.g. C2S
        /// height 5.0 -> 75 -> "23"). Values are clamped to the 2-character
        /// base-36 range 0..1295.
        /// </summary>
        private static string EncodeAirHeight(double c2sHeight)
        {
            double converted = c2sHeight * 15.0;
            int clamped = (int)Math.Round(converted);
            if (clamped < 0) clamped = 0;
            if (clamped > 1295) clamped = 1295;
            return ToBase36(clamped).PadLeft(2, '0');
        }

        /// <summary>
        /// Generates a UGC SORT key from a title using the spec rules:
        /// English letters are uppercased, spaces and symbols are removed,
        /// kana are converted (dakuten removed, small kana enlarged, and the
        /// prolonged sound mark is expanded to the vowel 'u').
        /// </summary>
        private static string GenerateSortKey(string title)
        {
            if (string.IsNullOrEmpty(title))
                return "";

            StringBuilder sb = new StringBuilder();
            foreach (char c in title)
            {
                string katakana = ToSortKana(c);
                if (katakana != null)
                {
                    sb.Append(katakana);
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Converts one character to its sort form for the UGC SORT key.
        /// Rules: hiragana -> katakana, remove dakuten/handakuten, expand
        /// small kana to full kana, and expand the prolonged sound mark
        /// to the vowel 'u'.
        /// </summary>
        private static string ToSortKana(char c)
        {
            int code = (int)c;
            if (code >= 0x3041 && code <= 0x3096)
                code += 0x60;
            if (code == 0x30FC)
                return "ウ";

            if (code < 0x30A1 || code > 0x30FA)
                return null;

            if ((code >= 0x30AC && code <= 0x30BE && (code % 2 == 0)) ||
                (code >= 0x30C0 && code <= 0x30C8 && (code % 2 == 0)) ||
                (code >= 0x30D0 && code <= 0x30D9 && ((code - 0x30D0) % 2 == 0)))
            {
                code--;
            }

            switch ((char)code)
            {
                case 'ァ': code = 'ア'; break;
                case 'ィ': code = 'イ'; break;
                case 'ゥ': code = 'ウ'; break;
                case 'ェ': code = 'エ'; break;
                case 'ォ': code = 'オ'; break;
                case 'ヵ': code = 'カ'; break;
                case 'ヶ': code = 'ケ'; break;
                case 'ッ': code = 'ツ'; break;
                case 'ャ': code = 'ヤ'; break;
                case 'ュ': code = 'ユ'; break;
                case 'ョ': code = 'ヨ'; break;
                case 'ヮ': code = 'ワ'; break;
            }

            return ((char)code).ToString();
        }

        /// <summary>
        /// Converts a metric entry to the tick within its measure (0 .. 1919).
        /// </summary>
        private static int ToMeasureTick(EntryChuni entry)
        {
            return (int)((double)entry.MetricOffset * StandardTicksPerMeasure);
        }

        /// <summary>
        /// Converts a metric entry to an absolute tick count from the start.
        /// </summary>
        private static int ToAbsoluteTick(EntryChuni entry)
        {
            return entry.MetricMeasure * StandardTicksPerMeasure + ToMeasureTick(entry);
        }

        /// <summary>
        /// Formats a Bar'Tick string for an entry's metric position, re-mapped
        /// onto the @BEAT-aware measure layout.
        /// </summary>
        private static string FormatBarTick(EntryChuni entry)
        {
            int abs = entry.MetricMeasure * StandardTicksPerMeasure + ToMeasureTick(entry);
            int bar, tick;
            ConvertToUmiguriBarTick(abs, out bar, out tick);
            return bar.ToString() + "'" + tick.ToString();
        }

        /// <summary>
        /// Appends "#Bar'Tick" for an entry, re-mapped onto the @BEAT-aware
        /// measure layout.
        /// </summary>
        private static void AppendBarTick(StringBuilder sb, EntryChuni entry)
        {
            int abs = entry.MetricMeasure * StandardTicksPerMeasure + ToMeasureTick(entry);
            int bar, tick;
            ConvertToUmiguriBarTick(abs, out bar, out tick);
            sb.Append("#" + bar.ToString() + "'" + tick.ToString());
        }

        /// <summary>
        /// Appends a child note line "#Offset>..."
        /// </summary>
        private static void AppendChild(StringBuilder sb, int offset, string note)
        {
            sb.AppendLine();
            sb.Append("#" + offset.ToString() + note);
        }

        /// <summary>
        /// Returns the note width from the encoded entry value.
        /// The value is encoded as type*100 + width (e.g. 104 = type 1, width 4).
        /// </summary>
        private static int GetWidth(EntryChuni entry)
        {
            return Math.Max(0, (int)(entry.Value.Numerator % 100));
        }

        /// <summary>
        /// Converts an integer to base-36 numeral (0-9, A-Z).
        /// </summary>
        private static string ToBase36(int value)
        {
            const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            if (value < 0) value = 0;
            if (value == 0) return "0";

            StringBuilder sb = new StringBuilder();
            while (value > 0)
            {
                int digit = value % 36;
                sb.Insert(0, digits[digit]);
                value /= 36;
            }
            return sb.ToString();
        }

        private sealed class MusicMetadata
        {
            public string Id;
            public string Title;
            public string Artist;
            public string Genre;
            public string ReleaseDate;
            public Dictionary<string, MusicChartMeta> Charts;
        }

        private sealed class MusicChartMeta
        {
            public string TypeId;
            public string TypeName;
            public string Level;
            public string WeAttr;
        }
    }
}