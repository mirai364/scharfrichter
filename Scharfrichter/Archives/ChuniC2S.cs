using Scharfrichter.Codec.Charts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scharfrichter.Codec.Archives
{
    public class ChuniC2S : Archive
    {
        public ChartChuni chart;

        public static ChuniC2S Read(StreamReader file, long unitNumerator, long unitDenominator)
        {
            ChuniC2S result = new ChuniC2S();

            ChartChuni chart;
            chart = ChuniPC.Read(file);
            chart.TickRate = new Fraction(unitNumerator, unitDenominator);

            // fill in the metric offsets
            chart.CalculateMetricOffsets();

            if (chart.Entries.Count > 0)
                result.chart = chart;
            else
                result.chart = null;

            return result;
        }

        public void Write(Stream target, long unitNumerator, long unitDenominator)
        {
            // Unsupported
        }
    }
}
