namespace ConvertHelper
{
    /// <summary>
    /// Provides named timing ratios used by the command-line converters.
    /// </summary>
    public static class ConverterTiming
    {
        public const long StandardNumerator = 1;
        public const long StandardDenominator = 1000;

        public const long BemaniToBmsNumerator = 100;
        public const long BemaniToBmsDenominator = 5994;

        public const long BemaniToBmsGoldNumerator = 100;
        public const long BemaniToBmsGoldDenominator = 6004;
    }
}