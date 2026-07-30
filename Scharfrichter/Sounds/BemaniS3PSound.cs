using NAudio.Wave;
using System;
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
                long reportedLength = wavStream.Length;
                int bufferSize = (reportedLength > 0 && reportedLength <= int.MaxValue) ? (int)reportedLength : 0;
                if (bufferSize == 0)
                {
                    // fallback: read in chunks
                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] block = new byte[4096];
                        int read;
                        while ((read = wavStream.Read(block, 0, block.Length)) > 0)
                            ms.Write(block, 0, read);
                        result.SetSound(ms.ToArray(), wavStream.WaveFormat);
                    }
                }
                else
                {
                    byte[] rawWaveData = new byte[bufferSize];
                    int totalRead = 0;
                    while (totalRead < bufferSize)
                    {
                        int bytesRead = wavStream.Read(rawWaveData, totalRead, bufferSize - totalRead);
                        if (bytesRead == 0)
                            break;
                        totalRead += bytesRead;
                    }
                    if (totalRead < bufferSize)
                        Array.Resize(ref rawWaveData, totalRead);
                    result.SetSound(rawWaveData, wavStream.WaveFormat);
                }
            }

            return result;
        }
    }
}