using NAudio.Utils;
using NAudio.Wave;
using System.IO;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    public class WavEncoder : ISoundEncoder
    {
        public void Encode(Sound sound, Stream target, float masterVolume)
        {
            if (sound.Data == null || sound.Data.Length == 0) return;

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