using NAudio.Wave;

using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds
{
    static public class Bemani2DXSound
    {
        // sample volume table
        // TODO: determine correctness.
        static private float[] volTab;
        static public float[] VolumeTable
        {
            get
            {
                if (volTab == null)
                {
                    volTab = new float[256];
                    for (int i = 0; i < 256; i++)
                        volTab[i] = (float)Math.Pow(10.0f, (-36.0f * i / 64f) / 20.0f);
                }
                return volTab;
            }
        }

        static public Sound Read(Stream source)
        {
            Sound result = new Sound();
            BinaryReader reader = new BinaryReader(source);
            if (new string(reader.ReadChars(4)) == "2DX9")
            {
                int infoLength = reader.ReadInt32();
                int dataLength = reader.ReadInt32();
                reader.ReadInt16();
                int channel = reader.ReadInt16();
                int panning = reader.ReadInt16();
                int volume = reader.ReadInt16();
                int options = reader.ReadInt32();

                reader.ReadBytes(infoLength - 24);

                byte[] wavData = reader.ReadBytes(dataLength);

                // True ADPCM passthrough: extract the raw fmt/data chunks BEFORE
                // NAudio's WaveFileReader silently decodes ADPCM to PCM.
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
                            int bytesToRead = (int)wavStream.Length;
                            byte[] rawWaveData = new byte[bytesToRead];
                            wavStream.ReadExactly(rawWaveData, 0, bytesToRead);
                            result.SetSound(rawWaveData, wavStream.WaveFormat);
                        }
                    }
                }

                // calculate output panning
                if (panning > 0x7F || panning < 0x01)
                    panning = 0x40;
                result.Panning = ((float)panning - 1.0f) / 126.0f;

                // calculate output volume
                if (volume < 0x01)
                    volume = 0x01;
                else if (volume > 0xFF)
                    volume = 0xFF;
                result.Volume = VolumeTable[volume];

                result.Channel = channel;
            }

            return result;
        }
    }
}