using NAudio.Wave;
using NReco.VideoConverter;
using System.IO;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    public class FlacEncoder : ISoundEncoder
    {
        public FlacEncoder()
        {
            // Initialization for NReco.VideoConverter is not strictly required.
            // It automatically extracts and manages the embedded ffmpeg binaries.
        }

        public void EncodeToFile(Sound sound, string targetFile, float masterVolume)
        {
            using (FileStream target = new FileStream(targetFile, FileMode.Create, FileAccess.Write))
            {
                Encode(sound, target, masterVolume);
                target.Flush();
            }
        }

        public void Encode(Sound sound, Stream target, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

            byte[] wavData = CreateWaveData(sound, masterVolume);
            using (MemoryStream inputStream = new MemoryStream(wavData))
            {
                FFMpegConverter ffmpeg = new FFMpegConverter();
                ffmpeg.ConvertLiveMedia(inputStream, "wav", target, "flac", new ConvertSettings());
            }
        }

        private static byte[] CreateWaveData(Sound sound, float masterVolume)
        {
            byte[] finalData = sound.Render(masterVolume);
            using (MemoryStream dataStream = new MemoryStream(finalData))
            using (RawSourceWaveStream wavStream = new RawSourceWaveStream(dataStream, sound.Format))
            using (MemoryStream wavData = new MemoryStream())
            {
                WaveFileWriter.WriteWavFileToStream(wavData, wavStream);
                return wavData.ToArray();
            }
        }
    }
}