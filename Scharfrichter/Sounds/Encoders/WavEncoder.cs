using NAudio.Utils;
using NAudio.Wave;
using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    public class WavEncoder : ISoundEncoder
    {
        public void Encode(Sound sound, Stream target, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

            // True ADPCM passthrough: emit the raw fmt + data chunks as-is,
            // preserving the original compressed bitstream without ACM decode.
            if (sound.FormatData != null && sound.RawData != null && sound.RawData.Length > 0)
            {
                int dataLength = sound.RawData.Length;
                using (BinaryWriter bw = new BinaryWriter(target, System.Text.Encoding.ASCII, true))
                {
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                    bw.Write(20 + sound.FormatData.Length + dataLength);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                    bw.Write(sound.FormatData.Length);
                    bw.Write(sound.FormatData);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                    bw.Write(dataLength);
                    bw.Write(sound.RawData);
                }
                return;
            }

            using (MemoryStream mem = new MemoryStream())
            {
                using (WaveFileWriter writer = new WaveFileWriter(new IgnoreDisposeStream(mem), sound.Format))
                {
                    byte[] finalData = sound.Render(masterVolume);
                    writer.Write(finalData, 0, finalData.Length);
                }
                target.Write(mem.ToArray(), 0, (int)mem.Length);
            }
        }

        public void EncodeToFile(Sound sound, string targetFile, float masterVolume)
        {
            using (MemoryStream target = new MemoryStream())
            {
                Encode(sound, target, masterVolume);
                target.Flush();
                if (target.Length > 0)
                {
                    File.WriteAllBytes(targetFile, target.ToArray());
                }
            }
        }
    }
}