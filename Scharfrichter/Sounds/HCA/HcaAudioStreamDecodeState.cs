namespace Scharfrichter.Codec.Sounds.HCA
{
    internal enum HcaAudioStreamDecodeState
    {
        Initialized,
        WaveHeaderTransmitting,
        WaveHeaderTransmitted,
        DataTransmitting,
        DataTransmitted
    }
}