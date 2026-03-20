using System.IO;

namespace Scharfrichter.Codec.Sounds.Encoders
{
    public interface ISoundEncoder
    {
        void Encode(Sound sound, Stream target, float masterVolume);
        void EncodeToFile(Sound sound, string targetFile, float masterVolume);
    }
}