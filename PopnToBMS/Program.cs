using ConvertHelper;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace PopnToBMS
{
    class Program
    {
        static long unitNumerator = 1;
        static long unitDenominator = 1000;

        static void Main(string[] args)
        {
            ConvertHelper.PopnToBMS.Convert(args, unitNumerator, unitDenominator,1);
        }
    }
}