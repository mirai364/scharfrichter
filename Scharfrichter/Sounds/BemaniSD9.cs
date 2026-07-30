using NAudio.Wave;
using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds
{
    static public class BemaniSD9
    {
        static public Sound Read(Stream source)
        {
            BinaryReaderEx reader = new BinaryReaderEx(source);
            Sound result = new Sound();

            int id = reader.ReadInt32();
            if (id == 0x00394453)
            {
                int headerLength = reader.ReadInt32();
                int sampleLength = reader.ReadInt32();

                headerLength -= 12;
                if (headerLength > 0)
                    reader.ReadBytes(headerLength);

                byte[] wavData = reader.ReadBytes(sampleLength);

                // Extract raw fmt/data chunks BEFORE NAudio's WaveFileReader
                // silently decodes ADPCM to PCM.
                byte[] rawFmt = null;
                byte[] rawData = null;
                bool isAdpcm = Util.TryReadWavRawChunks(wavData, out rawFmt, out rawData) &&
                               rawFmt != null && rawFmt.Length >= 2 &&
                               BitConverter.ToInt16(rawFmt, 0) == (short)WaveFormatEncoding.Adpcm;

                if (isAdpcm)
                {
                    // Keep the raw ADPCM bitstream for passthrough WAV output,
                    // but ALSO decode to PCM so ogg/flac/mp3/lpcm encoders
                    // receive valid decoded audio (not a compressed blob).
                    byte[] pcm;
                    WaveFormat pcmFormat;
                    int ch;
                    int rate;
                    if (Sound.TryDecodeAdpcmToPcm(rawFmt, rawData, out pcm, out pcmFormat, out ch, out rate))
                    {
                        // Sound.Render() assumes stereo. Convert mono PCM to
                        // stereo (duplicate samples) so playback timing and
                        // length stay correct for all encoders.
                        if (ch == 1)
                        {
                            int sampleCount = pcm.Length / 2;
                            byte[] stereo = new byte[sampleCount * 4];
                            for (int i = 0; i < sampleCount; i++)
                            {
                                stereo[i * 4] = pcm[i * 2];
                                stereo[i * 4 + 1] = pcm[i * 2 + 1];
                                stereo[i * 4 + 2] = pcm[i * 2];
                                stereo[i * 4 + 3] = pcm[i * 2 + 1];
                            }
                            pcm = stereo;
                        }
                        result.Data = pcm;
                        result.Format = new WaveFormat(rate, 16, 2);
                    }
                    else
                    {
                        result.Data = rawData;
                        result.Format = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Adpcm, rate, ch, 0, 0, 4);
                    }
                    result.RawData = rawData;
                    result.FormatData = rawFmt;
                }
                else
                {
                    using (MemoryStream wavDataMem = new MemoryStream(wavData))
                    {
                        using (WaveStream wavStream = new WaveFileReader(wavDataMem))
                        {
                            byte[] rawWaveData = new byte[wavStream.Length];
                            wavStream.ReadExactly(rawWaveData, 0, (int)wavStream.Length);
                            result.SetSound(rawWaveData, wavStream.WaveFormat);
                        }
                    }
                }
            }

            return result;
        }
    }
}