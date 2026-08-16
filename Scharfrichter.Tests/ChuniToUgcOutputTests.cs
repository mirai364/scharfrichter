using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Scharfrichter.Tests
{
    /// <summary>
    /// End-to-end output tests for ChuniToUgc.
    /// Parses a C2S chart through ChuniC2S and verifies the produced UGC text,
    /// including the companion-note ordering rules that UMIGURI relies on
    /// (Air-family notes must be emitted immediately after their ground note).
    /// </summary>
    public class ChuniToUgcOutputTests
    {
        /// <summary>
        /// Invokes ChuniToUgc.WriteUgc via reflection so we can inspect the
        /// produced UGC text without writing files.
        /// </summary>
        private static string WriteUgc(ChartChuni chart, string filename)
        {
            var type = typeof(ConvertHelper.ChuniToUgc);
            var method = type.GetMethod("WriteUgc",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (string)method.Invoke(null, new object[] { chart, null, filename });
        }

        private static ChartChuni ParseAndConvert(string c2sText, out string ugc)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(c2sText);
            using (StreamReader reader = new StreamReader(new MemoryStream(bytes)))
            {
                var archive = ChuniC2S.Read(reader, 480, 4); // same as ChuniToUgc.Program
                Assert.NotNull(archive.chart);
                ugc = WriteUgc(archive.chart, "test.c2s");
                return archive.chart;
            }
        }

        private static ChartChuni ParseChart(string c2sText)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(c2sText);
            using (StreamReader reader = new StreamReader(new MemoryStream(bytes)))
            {
                var archive = ChuniC2S.Read(reader, 480, 4);
                Assert.NotNull(archive.chart);
                return archive.chart;
            }
        }

        /// <summary>
        /// Writes a Music.xml into a temporary folder and returns that folder,
        /// so the (private) LoadMusicMetadata / WriteUgc flow can be tested
        /// end to end without the real game data.
        /// </summary>
        private static string CreateMusicXmlFolder(string musicXml)
        {
            string dir = Path.Combine(Path.GetTempPath(), "chuni-ugc-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Music.xml"), musicXml, Encoding.UTF8);
            return dir;
        }

        /// <summary>
        /// Invokes the private LoadMusicMetadata(string dir) method.
        /// </summary>
        private static object LoadMusicMetadata(string dir)
        {
            var type = typeof(ConvertHelper.ChuniToUgc);
            var method = type.GetMethod("LoadMusicMetadata",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return method.Invoke(null, new object[] { dir });
        }

        /// <summary>
        /// Invokes the private WriteUgc(chart, meta, filename) overload.
        /// </summary>
        private static string WriteUgcWithMeta(ChartChuni chart, object meta, string filename)
        {
            var type = typeof(ConvertHelper.ChuniToUgc);
            var method = type.GetMethod("WriteUgc",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (string)method.Invoke(null, new object[] { chart, meta, filename });
        }

        /// <summary>
        /// Returns the first non-empty line that immediately follows the given
        /// marker string in the UGC output.
        /// </summary>
        private static string FirstLineAfter(string text, string marker)
        {
            int idx = text.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx >= 0, "marker not found: " + marker);
            string rest = text.Substring(idx + marker.Length);
            // Skip any leading line breaks (\r and \n).
            int start = 0;
            while (start < rest.Length && (rest[start] == '\r' || rest[start] == '\n'))
                start++;
            int end = rest.IndexOf('\n', start);
            if (end < 0)
                end = rest.Length;
            return rest.Substring(start, end - start).TrimEnd('\r');
        }

        [Fact]
        public void RendersBasicNotes()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "TAP\t0\t0\t0\t4\n" +
                "CHR\t0\t96\t0\t3\tUP\n" +
                "FLK\t0\t192\t0\t1\tL\n" +
                "MNE\t1\t0\t0\t4\n", out ugc);

            Assert.Contains("#0'0:t04", ugc);
            Assert.Contains("#0'480:x03U", ugc);   // CHR UP
            Assert.Contains("#0'960:f01L", ugc);   // FLK L
            Assert.Contains("#1'0:d04", ugc);      // MNE
        }

        [Fact]
        public void RendersHold()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\nHLD\t1\t96\t0\t3\t384\n", out ugc);
            // parent = #1'480:h03, child = #1920>s
            Assert.Contains("#1'480:h03", ugc);
            Assert.Contains("#1920>s", ugc);
        }

        [Fact]
        public void RendersSlideChain()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLD\t2\t0\t0\t3\t384\t4\t2\n" +
                "SLC\t3\t0\t4\t2\t384\t3\t1\n", out ugc);
            Assert.Contains("#2'0:s03", ugc);
            Assert.Contains(">s42", ugc);
            Assert.Contains(">c31", ugc);
        }

        [Fact]
        public void RendersAirHoldAfterChrGround()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "CHR\t7\t0\t0\t4\tUP\n" +
                "AHD\t7\t0\t0\t4\tCHR\t96\n", out ugc);

            // The CHR must be immediately followed by the AIR-HOLD so UMIGURI
            // can resolve Previous.
            string nextLine = FirstLineAfter(ugc, "#7'0:x04U");
            Assert.StartsWith("#7'0:H04N", nextLine);
        }

        [Fact]
        public void AirHoldAttachesToSlideMiddleSegment()
        {
            // L346: AHD 27 336 13 3 SLD 240
            // The AHD sits on column 13, which is an intermediate column of a
            // slide chain whose first column is different. It must still be
            // emitted right after the SLIDE chain so UMIGURI can resolve
            // Previous against a chain segment on column 13.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLD\t27\t240\t0\t3\t384\t4\t2\n" +
                "SLC\t28\t0\t4\t2\t384\t13\t3\n" +
                "AHD\t27\t336\t13\t3\tSLD\t240\n", out ugc);

            // The slide chain exists (measure 27, position 240 -> #27'1200).
            Assert.Contains("#27'1200:s03", ugc);
            // The AHD must be in the output somewhere after the chain
            // (measure 27, position 336 -> #27'1680).
            Assert.Contains("#27'1680:H", ugc);
        }

        [Fact]
        public void AirHoldAttachesToSlideMiddleSegmentTime()
        {
            // L449: AHD 40 96 8 2 SLD 96
            // The AHD sits at (40,96) column 8, which is the END of the FIRST
            // slide segment. UMIGURI resolves Previous from the immediately
            // preceding line, so the AHD must be emitted right after the
            // segment child line (#`480>s88`), not after the whole chain.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLD\t40\t0\t8\t2\t96\t8\t2\n" +
                "SLC\t40\t96\t8\t2\t96\t8\t2\n" +
                "AHD\t40\t96\t8\t2\tSLD\t96\n", out ugc);

            // Slide parent at (40,0): cell 8, width 2 -> "s82".
            Assert.Contains("#40'0:s82", ugc);
            // First segment child line ends at (40,96) -> offset 480 ->
            // "#480>s82" (SLC -> ">c" only for end/control, SLD segment -> ">s").
            string afterSeg1 = FirstLineAfter(ugc, "#480>s82");
            // The AHD (Previous = the first segment) must immediately follow
            // this segment line.
            Assert.StartsWith("#40'480:H82N", afterSeg1);
        }

        [Fact]
        public void SlideChain40_192OutputsAndAirHoldsAttach()
        {
            // Actual 0933_03.c2s L448-L453:
            //   SLD 40 0   7 4 96 8  2   (chain1: 40,0 -> 40,96)
            //   AHD 40 96  8 2 SLD 96       -> attaches to chain1 end
            //   SLD 40 96  12 4 96 13 2   (chain2: 40,96 -> 40,192)
            //   SLD 40 192 7 4 96 8  2   (chain3: 40,192 -> 40,288)
            //   AHD 40 192 13 2 SLD 96     -> attaches to chain2 end
            //   AHD 40 288 8 2 SLD 96      -> attaches to chain3 end
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLD\t40\t0\t7\t4\t96\t8\t2\n" +
                "AHD\t40\t96\t8\t2\tSLD\t96\n" +
                "SLD\t40\t96\t12\t4\t96\t13\t2\n" +
                "SLD\t40\t192\t7\t4\t96\t8\t2\n" +
                "AHD\t40\t192\t13\t2\tSLD\t96\n" +
                "AHD\t40\t288\t8\t2\tSLD\t96\n", out ugc);

            // chain1: #40'0:s74 -> #480>s82
            Assert.Contains("#40'0:s74", ugc);
            Assert.Contains("#480>s82", ugc);
            // AHD L449 (#40'480:H82N) attaches to chain1 end.
            string afterC1 = FirstLineAfter(ugc, "#480>s82");
            Assert.StartsWith("#40'480:H82N", afterC1);

            // chain2: #40'480:sC4 -> #480>sD2
            Assert.Contains("#40'480:sC4", ugc);
            Assert.Contains("#480>sD2", ugc);
            // AHD L452 (#40'960:HD2N) attaches to chain2 end.
            string afterC2 = FirstLineAfter(ugc, "#480>sD2");
            Assert.StartsWith("#40'960:HD2N", afterC2);

            // chain3: #40'960:s74 -> #480>s82
            Assert.Contains("#40'960:s74", ugc);
            // AHD L453 attaches to chain3 end. CHUNITHM (40,288) becomes
            // #40'1440 under the MET-based @BEAT layout (measure 40 is
            // 1440 ticks because of the 3/4 MET at measure 38).
            Assert.Contains("#40'1440:H82N", ugc);
        }

        [Fact]
        public void AirHoldNotStolenByEarlierChainSegmentWithSameColumn()
        {
            // Regression test for 0933_03.c2s L439-L453.
            // The long chain (identifier 0, starting at L439 SLC) passes
            // through column 8 at an earlier time. The AHD at (40,96) col 8
            // belongs to the SEPARATE chain SLD 40 0 7 4 96 8 2 (L448), whose
            // segment ends at (40,96) col 8. The long chain's segment ending
            // at that same time ends on column 12 (L447), so the AHD must NOT
            // be consumed by the long chain.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLC\t39\t192\t5\t4\t9\t6\t4\n" +
                "SLC\t39\t201\t6\t4\t11\t7\t4\n" +
                "SLC\t39\t212\t7\t4\t13\t8\t4\n" +
                "SLC\t39\t225\t8\t4\t16\t9\t4\n" +
                "SLC\t39\t241\t9\t4\t22\t10\t4\n" +
                "SLC\t39\t263\t10\t4\t25\t11\t4\n" +
                "SLC\t39\t288\t11\t4\t48\t12\t4\n" +
                "SLD\t39\t336\t12\t4\t144\t12\t4\n" +
                "SLD\t40\t0\t7\t4\t96\t8\t2\n" +
                "AHD\t40\t96\t8\t2\tSLD\t96\n" +
                "SLD\t40\t96\t12\t4\t96\t13\t2\n" +
                "SLD\t40\t192\t7\t4\t96\t8\t2\n" +
                "AHD\t40\t192\t13\t2\tSLD\t96\n" +
                "AHD\t40\t288\t8\t2\tSLD\t96\n", out ugc);

            // The L448 chain: #40'0:s74 -> #480>s82. The AHD at (40,96) col 8
            // (#40'480:H82N) must immediately follow this segment line.
            Assert.Contains("#40'0:s74", ugc);
            string afterL448 = FirstLineAfter(ugc, "#480>s82");
            Assert.StartsWith("#40'480:H82N", afterL448);
        }

        [Fact]
        public void ComplexSlideChainWithAirHolds()
        {
            // Actual 0933_03.c2s L464-470:
            //   SLD 41 144 5 4  144 5 4
            //   SLD 41 192 0 4  96  1 2
            //   AHD 41 288 1 2 SLD 96
            //   SLD 41 288 5 4  96  6 2
            //   SLD 42 0  0 4  96  1 2
            //   AHD 42 0  6 2 SLD 96
            //   AHD 42 96 1 2 SLD 96
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLD\t41\t144\t5\t4\t144\t5\t4\n" +
                "SLD\t41\t192\t0\t4\t96\t1\t2\n" +
                "AHD\t41\t288\t1\t2\tSLD\t96\n" +
                "SLD\t41\t288\t5\t4\t96\t6\t2\n" +
                "SLD\t42\t0\t0\t4\t96\t1\t2\n" +
                "AHD\t42\t0\t6\t2\tSLD\t96\n" +
                "AHD\t42\t96\t1\t2\tSLD\t96\n", out ugc);

            // L467's SLD (41,288 col5->6) ends at (42,0 col6):
            // the AHD (42,0 col6) must attach right after that segment.
            Assert.Contains("#42'0:H62N", ugc);
            Assert.Contains("#42'0", ugc);
        }

        [Fact]
        public void SameTimeAirHoldAttachesToSlideRegardlessOfColumn()
        {
            // L468-L470:
            //   SLD 42 0 0 4 96 1 2
            //   AHD 42 0 6 2 SLD 96
            //   AHD 42 96 1 2 SLD 96
            // The second AHD sits at the SLIDE end column (1) and time, so it
            // attaches as a companion after the segment.
            // The first AHD is on column 6, which never matches any SLIDE
            // segment column, so UMIGURI cannot resolve a Previous and the
            // AHD is emitted as an independent (Previous-less) note.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLD\t42\t0\t0\t4\t96\t1\t2\n" +
                "AHD\t42\t0\t6\t2\tSLD\t96\n" +
                "AHD\t42\t96\t1\t2\tSLD\t96\n", out ugc);

            // SLIDE parent written at (42,0), then the segment line.
            Assert.Contains("#42'0:s04", ugc);
            Assert.Contains("#480>s12", ugc);

            // AIR-HOLD 2 (#42'480:H12N) is emitted right after the segment.
            string afterSeg = FirstLineAfter(ugc, "#480>s12");
            Assert.StartsWith("#42'480:H12N", afterSeg);

            // AIR-HOLD 1 (#42'0:H62N) is emitted as an independent note.
            Assert.Contains("#42'0:H62N", ugc);
        }

        [Fact]
        public void AirAttachesToSlideChain()
        {
            // L416: ADW 36 288 0 16 SLD
            // The AIR sits on column 0 at the slide end. It must be emitted
            // right after the SLIDE chain.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLD\t36\t288\t0\t16\t384\t4\t2\n" +
                "ADW\t36\t288\t0\t16\tSLD\n", out ugc);

            Assert.Contains("#36'1440:s0G", ugc);
            Assert.Contains("#36'1440:a0GDCN", ugc);
        }

        [Fact]
        public void RendersAirHoldAfterHoldGround()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "HLD\t7\t0\t12\t3\t96\n" +
                "AHD\t7\t96\t12\t4\tHLD\t96\n", out ugc);

            // HLD parent (#7'0:hC3) -> child (#480>s) -> AHD (#7'480:HC4N)
            int hldIdx = ugc.IndexOf("#7'0:hC3", StringComparison.Ordinal);
            Assert.True(hldIdx >= 0, "HLD not found");

            // the line right after the HLD is the child line
            string afterHld = ugc.Substring(hldIdx + "#7'0:hC3".Length);
            string childLine = FirstLineAfter(ugc, "#7'0:hC3");
            Assert.StartsWith("#480>s", childLine);

            // the AHD appears immediately after the child line
            string afterChild = ugc.Substring(hldIdx + "#7'0:hC3".Length);
            string secondLine = FirstLineAfter(afterChild, "#480>s");
            Assert.StartsWith("#7'480:HC4N", secondLine);
        }

        [Fact]
        public void RendersAirAfterChrGround()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "CHR\t7\t96\t4\t4\tUP\n" +
                "ADR\t7\t96\t4\t4\tCHR\n", out ugc);

            string nextLine = FirstLineAfter(ugc, "#7'480:x44U");
            Assert.StartsWith("#7'480:a44DRN", nextLine);
        }

        [Fact]
        public void SameTimingDifferentColumnsRhAidsStayOnTheirOwnGround()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "CHR\t25\t96\t5\t3\tUP\n" +
                "AHD\t25\t96\t5\t3\tCHR\t192\n" +
                "CHR\t25\t96\t8\t3\tUP\n" +
                "AHD\t25\t96\t8\t3\tCHR\t192\n", out ugc);

            // AHD (col 5) attaches to CHR (col 5).
            string nextLine5 = FirstLineAfter(ugc, "#25'480:x53U");
            Assert.StartsWith("#25'480:H53N", nextLine5);

            // AHD (col 8) attaches to CHR (col 8).
            string nextLine8 = FirstLineAfter(ugc, "#25'480:x83U");
            Assert.StartsWith("#25'480:H83N", nextLine8);
        }

        [Fact]
        public void BpmReturningToMainIsEmitted()
        {
            // BPM 0 0 199 -> BPM 17 192 99.5 -> BPM 21 192 132.669 ->
            // BPM 25 192 199. The final 199 entry restores the main BPM and
            // must be emitted even though it equals @MAINBPM, otherwise
            // UMIGURI would keep the previous 132.669 BPM.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "BPM\t0\t0\t199.0\n" +
                "BPM\t17\t192\t99.5\n" +
                "BPM\t21\t192\t132.669\n" +
                "BPM\t25\t192\t199.0\n", out ugc);

            // No MET events => every bar is 4/4 (1920 ticks), so the BPM
            // positions stay on the 384-grid mapping: (17,192)=(17*384+192)*5
            // = 33600 -> bar 17 tick 960.
            Assert.Contains("@BPM\t17'960\t99.50000", ugc);
            Assert.Contains("@BPM\t21'960\t132.66900", ugc);
            Assert.Contains("@BPM\t25'960\t199.00000", ugc);
        }

        [Fact]
        public void RendersBeatHeader()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\nMET\t38\t0\t4\t3\n", out ugc);
            Assert.Contains("@BEAT\t0\t4\t4", ugc);
            Assert.Contains("@BEAT\t38\t3\t4", ugc);
        }

        [Fact]
        public void LongOpeningMeterUsesChartBeatAndPositionsNotes()
        {
            // A chart whose measure 0 is 20/4 must emit a single @BEAT 0 20 4
            // (not an extra 4/4 at bar 0), and notes on the fixed grid must be
            // re-mapped onto the accumulated bar layout. MET 0 0 4 20 makes
            // bar 0 span 5 grid measures (20 beats = 9600 ticks), so a HOLD at
            // grid measure 17 offset 192 lands on bar 14 tick 0.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "MET\t0\t0\t4\t20\n" +
                "MET\t5\t0\t4\t4\n" +
                "MET\t13\t0\t4\t3\n" +
                "MET\t15\t96\t4\t4\n" +
                "MET\t16\t96\t4\t5\n" +
                "MET\t17\t192\t4\t4\n" +
                "HLD\t17\t192\t0\t3\t96\n", out ugc);

            Assert.Contains("@BEAT\t0\t20\t4", ugc);
            Assert.False(ugc.Contains("@BEAT\t0\t4\t4"), "stray 4/4 at bar 0 shifts all boundaries");
            Assert.Contains("#14'0:h03", ugc);
        }

        [Fact]
        public void RendersMultipleMetsInSameMeasure()
        {
            // Arcahv measure 2 has a tuplet run of METs:
            //   MET 2 0 384 1, MET 2 2 192 1, MET 2 4 128 1,
            //   MET 2 7 96 1, MET 2 11 64 64
            // UMIGURI @BEAT is per-measure, so the LAST MET in the measure
            // (64/64 = 4/4) determines the measure length. A note at measure 3
            // must be placed at #3'0, not shifted into a huge bar number.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "MET\t2\t0\t384\t1\n" +
                "MET\t2\t2\t192\t1\n" +
                "MET\t2\t4\t128\t1\n" +
                "MET\t2\t7\t96\t1\n" +
                "MET\t2\t11\t64\t64\n" +
                "MET\t3\t0\t4\t4\n" +
                "SLC\t3\t0\t12\t4\t6\t10\t4\n", out ugc);

            // Only one @BEAT for measure 2 (the last MET), plus the 4/4 restore.
            Assert.Contains("@BEAT\t2\t64\t64", ugc);
            Assert.DoesNotContain("@BEAT\t2\t1\t384", ugc);
            // The SLC at (3,0) must land on #3'0, not a shifted bar.
            Assert.Contains("#3'0:sC4", ugc);
            Assert.DoesNotContain("#150'", ugc);
        }

        [Fact]
        public void StpNotEmittedAsBeatOrMeasureLength()
        {
            // STP (stop) events must not leak into @BEAT or change measure
            // lengths. ChuniPC stores the STP end as a Player-1 Event with
            // Value = 1/1, which previously looked like a MET.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "STP\t5\t0\t192\n" +
                "TAP\t6\t0\t0\t4\n", out ugc);

            // No @BEAT should be emitted for the STP (only the initial 4/4).
            Assert.DoesNotContain("@BEAT\t5\t", ugc);
            // The note after the stop lands on #6'0 (measure length unchanged).
            Assert.Contains("#6'0:t04", ugc);
        }

        [Fact]
        public void SlpExplicitTimelineIdEmitsOwnTil()
        {
            // SLP's last column is the soflan timeline id. The @TIL definition
            // must be emitted on that timeline (not timeline 0), and a note
            // covered by an SLA region on that lane must @USETIL into it.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLP\t1\t0\t96\t0.500000\t1\n" +
                "SLA\t1\t0\t0\t4\t96\t1\n" +
                "TAP\t1\t0\t0\t4\n", out ugc);

            Assert.Contains("@TIL\t1\t1'0\t0.50000", ugc);
            Assert.Contains("@TIL\t1\t1'480\t1.00000", ugc);
            Assert.Contains("@USETIL\t1", ugc);
            Assert.Contains("#1'0:t04", ugc);
        }

        [Fact]
        public void SlaRegionAssignsTimelineToCoveredNotes()
        {
            // SLA M O Cell Width Duration Timeline. A note at (2,0) on lane 15
            // (the rightmost lane) falls inside the SLA region and must be
            // switched onto timeline 1, while a note outside the lane range
            // stays on the default timeline 0.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLP\t1\t0\t96\t0.500000\t1\n" +
                "SLA\t2\t0\t15\t1\t96\t1\n" +
                "TAP\t2\t0\t15\t1\n" +
                "TAP\t3\t0\t0\t1\n", out ugc);

            Assert.Contains("@TIL\t1\t1'0\t0.50000", ugc);
            Assert.Contains("@USETIL\t1", ugc);
            // The lane-15 note is covered by the SLA and switched to timeline 1.
            string afterUsetil = FirstLineAfter(ugc, "@USETIL\t1");
            Assert.StartsWith("#2'0:tF1", afterUsetil);
            // The later lane-0 note is outside the SLA region; it switches
            // back to the default timeline 0.
            Assert.Contains("@USETIL\t0", ugc);
            Assert.Contains("#3'0:t01", ugc);
        }

        [Fact]
        public void RendersTimelineFromSfl()
        {
            // CHUNITHM SFL is a field scroll (SOF-LAN), so it is emitted as
            // @TIL timeline definitions on timeline 0 (the @MAINTIL base),
            // not as @SPDMOD note-speed definitions.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\nSFL\t0\t0\t0\t3\t0\t1.500000\t192\n", out ugc);
            Assert.Contains("@TIL\t0\t", ugc);
            Assert.DoesNotContain("@SPDMOD\t", ugc);
        }

        [Fact]
        public void RendersTimelineFromSlp()
        {
            // CHUNITHM SLP is a speed-change event with an interval:
            //   SLP 121 48 336 0.500000 0
            //   SLP [Measure] [Time] [Interval] [Speed] [0]
            // The speed is applied at the start position and restored to 1.0
            // after the interval, so it is emitted as two @TIL points on
            // timeline 0 (matching @MAINTIL 0):
            //   start  (121, 48) -> 121'240  0.50000
            //   end    (121, 384) -> 122'0   1.00000
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SLP\t121\t48\t336\t0.500000\t0\n", out ugc);

            Assert.Contains("@TIL\t0\t121'240\t0.50000", ugc);
            Assert.Contains("@TIL\t0\t122'0\t1.00000", ugc);
            Assert.DoesNotContain("@SPDMOD\t", ugc);
        }

        [Fact]
        public void RendersNegativeTimeline()
        {
            // SFL speeds can be negative (stop / reverse segments). When SFL
            // segments are contiguous, the 1.0 restore of the previous SFL
            // lands exactly on the next SFL's speed-change position:
            //   SFL 1 0 2 700      -> 1'0:700, restore 1'10:1.0
            //   SFL 1 2 2 -3       -> 1'10:-3, restore 1'20:1.0
            //   SFL 1 4 2 -2.75    -> 1'20:-2.75, restore 1'30:1.0
            // The speed change at 1'10 / 1'20 must win over the 1.0 restore
            // that lands on the same position.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "SFL\t1\t0\t2\t700.000000\n" +
                "SFL\t1\t2\t2\t-3.000000\n" +
                "SFL\t1\t4\t2\t-2.750000\n", out ugc);

            Assert.Contains("@TIL\t0\t1'0\t700.00000", ugc);
            Assert.Contains("@TIL\t0\t1'10\t-3.00000", ugc);
            Assert.Contains("@TIL\t0\t1'20\t-2.75000", ugc);
            // The final restore point is still emitted.
            Assert.Contains("@TIL\t0\t1'30\t1.00000", ugc);
            // The 1.0 restore must NOT overwrite the negative speed changes.
            Assert.False(ugc.Contains("@TIL\t0\t1'10\t1.00000"), "1.0 restore overwrote negative speed");
            Assert.False(ugc.Contains("@TIL\t0\t1'20\t1.00000"), "1.0 restore overwrote negative speed");
        }

        [Fact]
        public void RendersNoteSpeedFromDcm()
        {
            // CHUNITHM DCM is a note-speed (追い越し / overtake) event:
            //   DCM 17 192 3 0.500000
            //   DCM [Measure] [Offset] [Duration] [Speed]
            // It is emitted as UGC @SPDMOD (note speed), not @TIL (soflan).
            //   start  (17, 192) -> 17'960  0.50000
            //   end    (17, 195) -> 17'975  1.00000
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "DCM\t17\t192\t3\t0.500000\n", out ugc);

            Assert.Contains("@SPDMOD\t17'960\t0.50000", ugc);
            Assert.Contains("@SPDMOD\t17'975\t1.00000", ugc);
            Assert.DoesNotContain("@TIL\t0\t17'960", ugc);
        }

        [Fact]
        public void ContiguousDcmCollapsesRestore()
        {
            // When two DCM segments are contiguous, the 1.0 restore of the
            // first lands exactly on the second's speed-change position. The
            // speed change must win over the restore.
            //   DCM 17 192 96 0.5  -> 17'960 0.5, restore 17'1440 1.0
            //   DCM 17 288 96 0.25 -> 17'1440 0.25, restore 18'0 1.0
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "DCM\t17\t192\t96\t0.500000\n" +
                "DCM\t17\t288\t96\t0.250000\n", out ugc);

            Assert.Contains("@SPDMOD\t17'960\t0.50000", ugc);
            Assert.Contains("@SPDMOD\t17'1440\t0.25000", ugc);
            Assert.False(ugc.Contains("@SPDMOD\t17'1440\t1.00000"), "restore overwrote contiguous speed change");
            Assert.Contains("@SPDMOD\t18'0\t1.00000", ugc);
        }

        [Fact]
        public void RendersAirSlide()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\nASD\t8\t0\t0\t3\tTAP\t5\t384\t4\t2\t5\tGRN\n", out ugc);
            Assert.Contains("#8'0:S03", ugc);
            Assert.Contains(">s42", ugc);
        }

        [Fact]
        public void AirSlideAttachesToSameColumnChr()
        {
            // 2891_03.c2s measure 10: the ASC (col8, TargetNote=CHR) attaches
            // to the CHR on col8, and the AUR (col12, companion=CHR) attaches
            // to the CHR on col12. UMIGURI resolves Previous from the
            // immediately preceding line, so each Air-family note must be
            // flushed right after ITS OWN ground (same column, 1:1 pairing).
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "CHR\t10\t0\t8\t4\tUP\n" +
                "ASC\t10\t0\t8\t4\tCHR\t5.0\t24\t12\t4\t5.0\tDEF\n" +
                "CHR\t10\t0\t12\t4\tUP\n" +
                "AUR\t10\t0\t12\t4\tCHR\tDEF\n", out ugc);

            // ASC (col8, height 5.0 -> 75 -> "23", DEF -> N) immediately
            // follows its CHR (col8).
            string afterChr8 = FirstLineAfter(ugc, "#10'0:x84U");
            Assert.StartsWith("#10'0:S8423N", afterChr8);

            // AUR (col12) immediately follows its CHR (col12), and is NOT
            // consumed by the col8 chain.
            string afterChr12 = FirstLineAfter(ugc, "#10'0:xC4U");
            Assert.StartsWith("#10'0:aC4URN", afterChr12);
        }

        [Fact]
        public void AirSlideAttachesToHoldEnd()
        {
            // 2891_03.c2s measure 82: ASC 82 0 0 4 HLD attaches to the HLD
            // whose END lands on (82,0) col0 (HLD 81 372 0 4 12). The S is
            // placed at the hold's release point, right after the HLD child.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "HLD\t81\t372\t0\t4\t12\n" +
                "ASC\t82\t0\t0\t4\tHLD\t5.0\t1248\t0\t2\t7.0\tDEF\n", out ugc);

            // HLD parent at (81,372) col0 width4.
            Assert.Contains("#81'1860:h04", ugc);
            // The HLD child line (#60>s) must be immediately followed by the
            // ASC at (82,0) col0 (height 5.0 -> 75 -> "23", DEF -> N).
            string afterHold = FirstLineAfter(ugc, "#60>s");
            Assert.StartsWith("#82'0:S042XN", afterHold);
        }

        [Fact]
        public void AscControlPointUsesEndHeight()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "ASC\t128\t192\t1\t1\tSLD\t1.0\t24\t0\t1\t19.0\tDEF\n", out ugc);

            Assert.Contains("#128'960:S117XN", ugc);
            // ASC (Air Slide Control) segments end with a control point >c.
            Assert.Contains("#120>c017X", ugc);
        }

        [Fact]
        public void AirSlideRelayToAsdUsesActionChild()
        {
            // A standalone ASD whose TargetNote is ASD hands off to another
            // air slide at its end. The endpoint is a relay point (中継点).
            // The child marker follows the segment source type: ASD -> >s
            // (AIR-ACTION), ASC -> >c (control point).
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\nASD\t48\t96\t7\t2\tASD\t5.0\t192\t7\t2\t5.0\tDEF\n", out ugc);

            Assert.Contains("#48'480:S7223N", ugc);
            Assert.Contains("#960>s7223", ugc);
            Assert.DoesNotContain("#960>c7223", ugc);
        }

        [Fact]
        public void AirSlideRelayToAsdAtChainEndUsesActionChild()
        {
            // music0059_04.c2s L2307/L2309:
            //   ASD 48 0   7 2 SLD 5.0  96 7 2 5.0 DEF
            //   ASD 48 96  7 2 ASD 5.0 192 7 2 5.0 DEF
            // The second ASD relays to another air slide at its end (48,288).
            // Since all segments are ASD (Air Slide, not ASC control), every
            // child uses the >s action marker, including the merged waypoint
            // and the ASD->ASD relay end.
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "ASD\t48\t0\t7\t2\tSLD\t5.0\t96\t7\t2\t5.0\tDEF\n" +
                "ASD\t48\t96\t7\t2\tASD\t5.0\t192\t7\t2\t5.0\tDEF\n", out ugc);

            // Chain parent at (48,0) -> merged waypoint at (48,96) -> relay end (48,288).
            Assert.Contains("#48'0:S7223N", ugc);
            Assert.Contains("#480>s7223", ugc);    // merged waypoint
            Assert.Contains("#1440>s7223", ugc);   // relay to ASD -> >s
            Assert.DoesNotContain("#480>c7223", ugc);
            Assert.DoesNotContain("#1440>c7223", ugc);
        }

        [Fact]
        public void RendersAirCrush()
        {
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\nALD\t10\t0\t0\t4\t96\t5\t384\t0\t4\t5\tRED\n", out ugc);
            // parent: #10'0:C04 + height 23 (5.0*15 = 75 = base36 "23") + color 1 (RED) + interval 480 (96 * 1920/384)
            Assert.Contains("#10'0:C04231,480", ugc);
            // child: offset 384 -> #1920>c + end cell 0 + end width 4 + height 23
            Assert.Contains("#1920>c0423", ugc);
        }

        [Fact]
        public void RendersAirCrushColors()
        {
            // AIR-CRUSH color tag -> UGC color character correspondence table.
            // The trailing color column (parts[11]) is mapped by C2UAirCrushColor:
            //   DEF -> "0" (通常), BLK -> "D" (黒), CYN -> "7" (空), GRY -> "C" (白)
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "ALD\t0\t0\t0\t1\t0\t1.0\t96\t0\t1\t1.0\tCYN\n" +
                "ALD\t1\t0\t0\t1\t0\t1.0\t96\t0\t1\t1.0\tBLK\n" +
                "ALD\t2\t0\t0\t1\t0\t1.0\t96\t0\t1\t1.0\tGRY\n" +
                "ALD\t3\t0\t0\t1\t0\t1.0\t96\t0\t1\t1.0\tDEF\n", out ugc);

            // height 1.0 -> "0F", width 1 -> "1", cell 0 -> "0", interval 0 -> "0"
            Assert.Contains("#0'0:C010F7,0", ugc);   // CYN -> 7
            Assert.Contains("#1'0:C010FD,0", ugc);   // BLK -> D
            Assert.Contains("#2'0:C010FC,0", ugc);   // GRY -> C
            Assert.Contains("#3'0:C010F0,0", ugc);   // DEF -> 0
        }

        [Fact]
        public void NoUnwantedCompanionTapForAhd()
        {
            // The AHD's companion CHR must not be emitted as a separate TAP
            // (it is already an independent row in the C2S data).
            string ugc;
            ParseAndConvert(
                "RESOLUTION\t384\n" +
                "CHR\t7\t96\t4\t4\tUP\n" +
                "AHD\t7\t96\t4\t4\tCHR\t96\n", out ugc);

            Assert.Contains("#7'480:x44U", ugc);   // independent CHR
            Assert.True(ugc.IndexOf("#7'480:t44", StringComparison.Ordinal) < 0, "unwanted companion TAP emitted");
        }

        [Fact]
        public void WorldEndChartFileLabelIsWeAttrPlusStars()
        {
            // Several WORLD'S END charts can exist for one song, so the file
            // name label for a WORLD'S END chart must be "WEATTR☆☆...☆"
            // (attribute + one star per level), e.g. "蔵☆☆☆☆☆", rather than
            // the generic "WORLD'S END" text.
            var type = typeof(ConvertHelper.ChuniToUgc);
            var labelMethod = type.GetMethod("BuildChartFileLabel",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(labelMethod);

            var chartMetaType = type.GetNestedType("MusicChartMeta", BindingFlags.NonPublic);
            Assert.NotNull(chartMetaType);

            object chartMeta = Activator.CreateInstance(chartMetaType, nonPublic: true);
            chartMetaType.GetField("IsWorldsEnd", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(chartMeta, true);
            chartMetaType.GetField("WeAttr", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(chartMeta, "蔵");
            chartMetaType.GetField("Level", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(chartMeta, "5");
            chartMetaType.GetField("TypeName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(chartMeta, "WORLD'S END");

            string label = (string)labelMethod.Invoke(null, new object[] { chartMeta });
            Assert.Equal("蔵☆☆☆☆☆", label);
        }

        [Fact]
        public void NonWorldEndChartFileLabelIsTypeName()
        {
            var type = typeof(ConvertHelper.ChuniToUgc);
            var labelMethod = type.GetMethod("BuildChartFileLabel",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(labelMethod);

            var chartMetaType = type.GetNestedType("MusicChartMeta", BindingFlags.NonPublic);
            Assert.NotNull(chartMetaType);

            object chartMeta = Activator.CreateInstance(chartMetaType, nonPublic: true);
            chartMetaType.GetField("IsWorldsEnd", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(chartMeta, false);
            chartMetaType.GetField("TypeName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(chartMeta, "MASTER");

            string label = (string)labelMethod.Invoke(null, new object[] { chartMeta });
            Assert.Equal("MASTER", label);
        }

        [Fact]
        public void WorldEndFumenSetsWeAttrFromMusicXml()
        {
            // SDBT 1.50 identifies the WORLD'S END fumen with type id 5
            // (<str>WorldsEnd</str> / <data>WORLD'S END</data>). The @DIFF must
            // become 4, @WEATTR must be filled from worldsEndTagName, and the
            // play level is the star count from starDifType.
            string dir = CreateMusicXmlFolder(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<MusicData>" +
                "  <name><id>8314</id><str>sølips</str></name>" +
                "  <artistName><str>rintaro soma</str></artistName>" +
                "  <genreNames><list><StringID><str>ゲキマイ</str></StringID></list></genreNames>" +
                "  <releaseDate>20250716</releaseDate>" +
                "  <worldsEndTagName><id>27</id><str>蔵</str></worldsEndTagName>" +
                "  <starDifType>9</starDifType>" +
                "  <fumens>" +
                "    <MusicFumenData>" +
                "      <type><id>5</id><str>WorldsEnd</str><data>WORLD'S END</data></type>" +
                "      <enable>true</enable>" +
                "      <file><path>8314_05.c2s</path></file>" +
                "      <level>0</level>" +
                "      <levelDecimal>0</levelDecimal>" +
                "    </MusicFumenData>" +
                "  </fumens>" +
                "</MusicData>");

            try
            {
                object meta = LoadMusicMetadata(dir);
                Assert.NotNull(meta);

                ChartChuni chart = ParseChart("RESOLUTION\t384\nTAP\t0\t0\t0\t4\n");
                string ugc = WriteUgcWithMeta(chart, meta, "8314_05.c2s");

                Assert.Contains("@DIFF\t4", ugc);
                Assert.Contains("@WEATTR\t蔵", ugc);
                Assert.Contains("@LEVEL\t5", ugc); // starDifType 9 -> 5 stars
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void UltimaFumenSetsDiff5WithoutWeAttr()
        {
            // CHUNITHM data versions can keep ULTIMA at type id 4; it must be
            // resolved by the <data>ULTIMA</data> name and must NOT emit @WEATTR.
            string dir = CreateMusicXmlFolder(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<MusicData>" +
                "  <name><id>8314</id><str>sølips</str></name>" +
                "  <artistName><str>rintaro soma</str></artistName>" +
                "  <genreNames><list><StringID><str>ゲキマイ</str></StringID></list></genreNames>" +
                "  <fumens>" +
                "    <MusicFumenData>" +
                "      <type><id>4</id><str>Ultima</str><data>ULTIMA</data></type>" +
                "      <enable>true</enable>" +
                "      <file><path>8314_04.c2s</path></file>" +
                "      <level>14</level>" +
                "      <levelDecimal>0</levelDecimal>" +
                "    </MusicFumenData>" +
                "  </fumens>" +
                "</MusicData>");

            try
            {
                object meta = LoadMusicMetadata(dir);
                Assert.NotNull(meta);

                ChartChuni chart = ParseChart("RESOLUTION\t384\nTAP\t0\t0\t0\t4\n");
                string ugc = WriteUgcWithMeta(chart, meta, "8314_04.c2s");

                Assert.Contains("@DIFF\t5", ugc);
                Assert.DoesNotContain("@WEATTR\t", ugc);
                Assert.Contains("@LEVEL\t14", ugc);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}