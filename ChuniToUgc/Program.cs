namespace ChuniToUgc
{
    class Program
    {
        static void Main(string[] args)
        {
            // ProcessInput dispatches based on file extension:
            //   .c2s -> UGC chart conversion
            //   .acb / .awb -> ACB/AWB audio extraction
            ConvertHelper.ChuniToUgc.Convert(
                args,
                ConvertHelper.ConverterTiming.StandardNumerator,
                ConvertHelper.ConverterTiming.StandardDenominator,
                false);
        }
    }
}