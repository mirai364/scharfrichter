namespace PopnToBMS
{
    class Program
    {
        static void Main(string[] args)
        {
            ConvertHelper.PopnToBMS.Convert(
                args,
                ConvertHelper.ConverterTiming.StandardNumerator,
                ConvertHelper.ConverterTiming.StandardDenominator);
        }
    }
}