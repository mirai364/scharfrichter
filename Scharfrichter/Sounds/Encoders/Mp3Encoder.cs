using NAudio.Lame;
using NAudio.Wave;
using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    public class Mp3Encoder : ISoundEncoder
    {
        // LAME encoder delay at 44.1kHz: 1105 (filter warm-up) + 1152 (first frame MDCT overlap)
        // This is the constant sample offset between input and decoded output.
        // See: https://lame.sourceforge.io/tech-FAQ.txt
        private const int LameEncoderDelay44k = 1105 + 1152; // = 2257 samples

        public void EncodeToFile(Sound sound, string targetFile, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

            using (FileStream target = new FileStream(targetFile, FileMode.Create, FileAccess.Write))
            {
                Encode(sound, target, masterVolume);
                target.Flush();
            }

            if (!File.Exists(targetFile) || new FileInfo(targetFile).Length == 0)
                throw new InvalidOperationException("MP3 encoder produced an empty output file.");
        }

        public void Encode(Sound sound, Stream target, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

            byte[] finalData = sound.Render(masterVolume);

            int sampleRate = sound.Format.SampleRate;
            int channels = sound.Format.Channels;
            int bytesPerFrame = channels * 2; // 16-bit

            // Append one MP3 frame of silence to prevent the last frame's
            // MDCT overlap from truncating the tail of the real audio.
            // LAME's encoder delay (2,257 samples at 44.1 kHz) is an inherent
            // property of the codec and is handled by the Xing/VBR gapless tag
            // written by setting WriteVBRTag = true.  Do NOT prepend silence to
            // compensate for it — that would double the delay to ~102 ms.
            int tailPadding = 1152;
            if (sampleRate > 48000)
                tailPadding = 2304;

            byte[] silenceTail = new byte[tailPadding * bytesPerFrame];
            byte[] paddedData = new byte[finalData.Length + silenceTail.Length];
            Buffer.BlockCopy(finalData, 0, paddedData, 0, finalData.Length);
            Buffer.BlockCopy(silenceTail, 0, paddedData, finalData.Length, silenceTail.Length);

            var config = new LameConfig
            {
                WriteVBRTag = true
            };

            using (LameMP3FileWriter writer = new LameMP3FileWriter(
                new NonClosingStream(target), sound.Format, config))
            {
                writer.Write(paddedData, 0, paddedData.Length);
                writer.Flush();
            }
        }
    }
}
