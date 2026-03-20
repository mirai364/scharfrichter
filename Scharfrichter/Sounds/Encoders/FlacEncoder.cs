using NAudio.Wave;
using NReco.VideoConverter;
using System;
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
            if (sound.Data == null || sound.Data.Length == 0) return;

            // Generate a temporary file path for the intermediate WAV file
            string tempWavPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");

            try
            {
                // 1. Export the sound data as a pristine temporary WAV using NAudio
                byte[] finalData = sound.Render(masterVolume);
                using (MemoryStream dataStream = new MemoryStream(finalData))
                using (RawSourceWaveStream wavStream = new RawSourceWaveStream(dataStream, sound.Format))
                {
                    WaveFileWriter.CreateWaveFile(tempWavPath, wavStream);
                }

                // 2. Convert the intermediate WAV to FLAC using NReco.VideoConverter
                // This completely bypasses Windows Media Foundation and avoids the 0xC00D36B4 error.
                var ffmpeg = new FFMpegConverter();
                ffmpeg.ConvertMedia(tempWavPath, targetFile, "flac");
            }
            finally
            {
                // Clean up the temporary WAV file to free up disk space
                if (File.Exists(tempWavPath))
                {
                    File.Delete(tempWavPath);
                }
            }
        }

        public void Encode(Sound sound, Stream target, float masterVolume)
        {
            // Generate a temporary file path for the FLAC output
            string tempFlacPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".flac");

            try
            {
                EncodeToFile(sound, tempFlacPath, masterVolume);

                using (FileStream tempFileStream = new FileStream(tempFlacPath, FileMode.Open, FileAccess.Read))
                {
                    tempFileStream.CopyTo(target);
                }
            }
            finally
            {
                // Clean up the temporary FLAC file
                if (File.Exists(tempFlacPath))
                {
                    File.Delete(tempFlacPath);
                }
            }
        }
    }
}