using CUETools.Codecs;
using CUETools.Codecs.FLAKE;
using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    public class FlacEncoder : ISoundEncoder
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
                throw new InvalidOperationException("FLAC encoder produced an empty output file.");
        }

        public void Encode(Sound sound, Stream target, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

            byte[] finalData = sound.Render(masterVolume);
            AudioPCMConfig pcm = CreatePcmConfig(sound);
            int sampleCount = finalData.Length / pcm.BlockAlign;
            AudioBuffer buffer = new AudioBuffer(pcm, finalData, sampleCount);

            using (FlakeWriter writer = new FlakeWriter(null, new NonClosingStream(target), pcm))
            {
                writer.CompressionLevel = 8;
                writer.FinalSampleCount = sampleCount;
                writer.Write(buffer);
                writer.Close();
            }
        }

        private static AudioPCMConfig CreatePcmConfig(Sound sound)
        {
            return new AudioPCMConfig(sound.Format.BitsPerSample, sound.Format.Channels, sound.Format.SampleRate);
        }
    }
}
