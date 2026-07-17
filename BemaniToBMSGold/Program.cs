using ConvertHelper;

namespace BemaniToBMSGold
{
    class Program
    {
        static void Main(string[] args)
        {
            BemaniToBMS.Convert(
                args,
                ConverterTiming.BemaniToBmsGoldNumerator,
                ConverterTiming.BemaniToBmsGoldDenominator,
                false);
        }
    }
}
