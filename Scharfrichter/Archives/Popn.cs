using Scharfrichter.Codec.Charts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scharfrichter.Codec.Archives
{
    public class Popn: Archive
    {
        private Chart[] charts = new Chart[1];

        public override Chart[] Charts
        {
            get
            {
                return charts;
            }
            set
            {
                if (value.Length == 1)
                    charts = value;
            }
        }

        public override int ChartCount
        {
            get
            {
                return 1;
            }
        }

        public static Popn Read(Stream source, long unitNumerator, long unitDenominator, int maxIndex, int version)
        {
            Popn result = new Popn();

            Chart chart;
            chart = PopnPC.Read(source, maxIndex, version);
            chart.TickRate = new Fraction(unitNumerator, unitDenominator);

            // fill in the metric offsets
            chart.CalculateMetricOffsets();

            if (chart.Entries.Count > 0)
                result.charts[0] = chart;
            else
                result.charts[0] = null;

            return result;
        }

        public void Write(Stream target, long unitNumerator, long unitDenominator)
        {
            // Unsupported
        }
    }
}
