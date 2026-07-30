using NAudio.Utils;
using NAudio.Wave;
using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    /// <summary>
    /// Writes a linear PCM 16-bit WAV file regardless of the source format.
    /// Sound.Data always contains decoded PCM (raw ADPCM lives in RawData).
    /// </summary>
    public class WavPcmEncoder : ISoundEncoder
    {
        public void EncodeToFile(Sound sound, string targetFile, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

            using (FileStream target = new FileStream(targetFile, FileMode.Create, FileAccess.Write))
            {
                Encode(sound, target, masterVolume);
                target.Flush();
            }

            if (!File.Exists(targetFile) || new FileInfo(targetFile).Length == 0)
                throw new InvalidOperationException("WavPcmEncoder produced an empty output file.");
        }

        public void Encode(Sound sound, Stream target, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

            byte[] finalData = sound.Render(masterVolume);

            // Always force 16-bit PCM, preserving the source channel count and sample rate
            WaveFormat pcmFormat = new WaveFormat(sound.Format.SampleRate, 16, sound.Format.Channels);

            using (MemoryStream mem = new MemoryStream())
            {
                using (WaveFileWriter writer = new WaveFileWriter(new IgnoreDisposeStream(mem), pcmFormat))
                {
                    writer.Write(finalData, 0, finalData.Length);
                }
                target.Write(mem.ToArray(), 0, (int)mem.Length);
            }
        }
    }
}