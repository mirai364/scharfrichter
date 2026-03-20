namespace PopnToBMS
{
    class Program
    {
        static long unitNumerator = 1;
        static long unitDenominator = 1000;

        static void Main(string[] args)
        {
            ConvertHelper.PopnToBMS.Convert(args, unitNumerator, unitDenominator, 1);
        }
    }
}