using ConvertHelper;

namespace BemaniToBMSTroopers
{
    class Program
    {
        static long unitNumerator = 1;
        static long unitDenominator = 1000;

        static void Main(string[] args)
        {
            BemaniToBMS.Convert(args, unitNumerator, unitDenominator, false);
        }
    }
}
