using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ChuniToSus
{
    class Program
    {
        static long unitNumerator = 1;
        static long unitDenominator = 1000;

        static void Main(string[] args)
        {
            ConvertHelper.ChuniToSus.Convert(args, unitNumerator, unitDenominator, false);
        }
    }
}
