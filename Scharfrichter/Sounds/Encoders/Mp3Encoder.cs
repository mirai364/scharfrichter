using NAudio.Lame;
using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    public class Mp3Encoder : ISoundEncoder
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
                throw new InvalidOperationException("MP3 encoder produced an empty output file.");
        }

        public void Encode(Sound sound, Stream target, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

            byte[] finalData = sound.Render(masterVolume);
            using (LameMP3FileWriter writer = new LameMP3FileWriter(new NonClosingStream(target), sound.Format, new LameConfig()))
            {
                writer.Write(finalData, 0, finalData.Length);
                writer.Flush();
            }
        }
    }
}
