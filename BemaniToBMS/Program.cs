namespace BemaniToBMS
{
    class Program
    {
        static void Main(string[] args)
        {
            ConvertHelper.BemaniToBMS.Convert(
                args,
                ConvertHelper.ConverterTiming.BemaniToBmsNumerator,
                ConvertHelper.ConverterTiming.BemaniToBmsDenominator,
                false);
        }
    }
}
