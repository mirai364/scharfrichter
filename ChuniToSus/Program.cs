namespace ChuniToSus
{
    class Program
    {
        static void Main(string[] args)
        {
            ConvertHelper.ChuniToSus.Convert(
                args,
                ConvertHelper.ConverterTiming.StandardNumerator,
                ConvertHelper.ConverterTiming.StandardDenominator,
                false);
        }
    }
}
