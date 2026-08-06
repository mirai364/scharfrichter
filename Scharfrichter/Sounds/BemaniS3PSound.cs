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
            try
            {
                // CreatePcmStream only opens the stream; the actual acmStreamConvert
                // work happens inside Read(), so the entire decode must be under the lock.
                lock (Sound.AcmLock)
                {
                    using (MemoryStream sourceStream = new MemoryStream(source, false))
                    using (WaveStream fileReader = new StreamMediaFoundationReader(sourceStream, new MediaFoundationReader.MediaFoundationReaderSettings()))
                    using (WaveStream wavStream = WaveFormatConversionStream.CreatePcmStream(fileReader))
                    {
                        int bytesToRead = (int)wavStream.Length;
                        byte[] rawWaveData = new byte[bytesToRead];
                        wavStream.ReadExactly(rawWaveData, 0, bytesToRead);
                        result.SetSound(rawWaveData, wavStream.WaveFormat);
                    }
                }
            }
            catch (EndOfStreamException)
            {
                // truncated WMA: attempt chunked fallback read
                try
                {
                    lock (Sound.AcmLock)
                    {
                        using (MemoryStream sourceStream = new MemoryStream(source, false))
                        using (WaveStream fileReader = new StreamMediaFoundationReader(sourceStream, new MediaFoundationReader.MediaFoundationReaderSettings()))
                        using (WaveStream wavStream = WaveFormatConversionStream.CreatePcmStream(fileReader))
                        using (MemoryStream ms = new MemoryStream())
                        {
                            byte[] block = new byte[4096];
                            int read;
                            while ((read = wavStream.Read(block, 0, block.Length)) > 0)
                                ms.Write(block, 0, read);
                            if (ms.Length > 0)
                                result.SetSound(ms.ToArray(), wavStream.WaveFormat);
                        }
                    }
                }
                catch
                {
                    // both attempts failed; return empty sound
                }
            }

            return result;
        }
    }
}