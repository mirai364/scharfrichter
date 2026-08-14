namespace ChuniToSus
{
    class Program
    {
        static void Main(string[] args)
        {
            // ChuniToSus.ProcessFile dispatches by extension:
            //   .c2s / .dds -> SUS chart/jacket conversion
            //   .acb / .awb -> ACB/AWB audio extraction (calls ChuniToUgc.ConvertAudio)
            ConvertHelper.ChuniToSus.Convert(
                args,
                ConvertHelper.ConverterTiming.StandardNumerator,
                ConvertHelper.ConverterTiming.StandardDenominator,
                false);
        }
    }
}