using ConvertHelper;

namespace BemaniToBMSTroopers
{
    class Program
    {
        static void Main(string[] args)
        {
            BemaniToBMS.Convert(
                args,
                ConverterTiming.StandardNumerator,
                ConverterTiming.StandardDenominator,
                false);
        }
    }
}
