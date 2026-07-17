using NAudio.Wave;
using System.IO;

namespace Scharfrichter.Codec.Sounds
{
    static public class BemaniS3PSound
    {
        static public Sound Read(byte[] source)
        {
            Sound result = new Sound();
            using (MemoryStream sourceStream = new MemoryStream(source, false))
            using (WaveStream fileReader = new StreamMediaFoundationReader(sourceStream, new MediaFoundationReader.MediaFoundationReaderSettings()))
            using (WaveStream wavStream = WaveFormatConversionStream.CreatePcmStream(fileReader))
            {
                int bytesToRead = (int)wavStream.Length;
                byte[] rawWaveData = new byte[bytesToRead];
                wavStream.ReadExactly(rawWaveData, 0, bytesToRead);
                result.SetSound(rawWaveData, wavStream.WaveFormat);
            }

            return result;
        }
    }
}