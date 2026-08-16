using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Charts;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Scharfrichter.Tests
{
    public class AldDiagnosticTests
    {
        private readonly ITestOutputHelper _output;

        public AldDiagnosticTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void DumpUserSample()
        {
            string c2s =
                "RESOLUTION\t384\n" +
                "ALD\t0\t0\t0\t1\t0\t1.0\t1918\t0\t1\t1.0\tCYN\n" +
                "ALD\t4\t382\t15\t1\t0\t1.0\t2306\t15\t1\t1.0\tBLK\n" +
                "ALD\t5\t0\t6\t1\t0\t1.0\t2\t6\t1\t18.0\tGRY\n" +
                "ALD\t33\t48\t2\t2\t12\t5.0\t15\t1\t2\t5.0\tDEF\n";

            byte[] bytes = Encoding.UTF8.GetBytes(c2s);
            ChartChuni chart;
            using (StreamReader reader = new StreamReader(new MemoryStream(bytes)))
            {
                var archive = ChuniC2S.Read(reader, 480, 4);
                chart = archive.chart;
            }

            // Dump parsed ALD entries and their Tag/Height/CrushInterval.
            foreach (EntryChuni e in chart.Entries)
            {
                if (e.Type != EntryTypeChuni.Marker || e.Player != 7)
                    continue;
                _output.WriteLine(
                    $"P7 off={(int)((double)e.LinearOffset)} col={e.Column} " +
                    $"type={(int)(e.Value.Numerator / 100)} width={e.Value.Numerator % 100} " +
                    $"Tag='{e.Tag}' Height={e.Height} EndHeight={e.EndHeight} CrushInterval={e.CrushInterval}");
            }

            var type = typeof(ConvertHelper.ChuniToUgc);
            var method = type.GetMethod("WriteUgc", BindingFlags.NonPublic | BindingFlags.Static);
            string ugc = (string)method.Invoke(null, new object[] { chart, null, "test.c2s" });
            _output.WriteLine("=== UGC ===");
            _output.WriteLine(ugc);
        }
    }
}