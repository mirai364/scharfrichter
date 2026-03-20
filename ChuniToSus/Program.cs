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
