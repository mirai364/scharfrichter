using System.IO;

namespace Scharfrichter.Codec.Sounds.HCA
{
    public interface IWaveWriter
    {
        SamplingMode SamplingMode { get; }

        uint BytesPerSample { get; }

        uint DecodeToBuffer(float f, byte[] buffer, uint offset);

        uint DecodeToStream(float f, Stream stream);
    }
}