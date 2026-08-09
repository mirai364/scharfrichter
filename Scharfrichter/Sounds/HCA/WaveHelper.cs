using System;
using System.IO;

namespace Scharfrichter.Codec.Sounds.HCA
{
    public static class WaveHelper
    {
        public static readonly IWaveWriter U8 = new WaveWriterU8();
        public static readonly IWaveWriter S16 = new WaveWriterS16();
        public static readonly IWaveWriter S32 = new WaveWriterS32();
        public static readonly IWaveWriter R32 = new WaveWriterR32();

        private sealed class WaveWriterU8 : IWaveWriter
        {
            public uint BytesPerSample => 1;
            public SamplingMode SamplingMode => SamplingMode.U8;

            public uint DecodeToBuffer(float f, byte[] buffer, uint offset)
            {
                if (offset >= buffer.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }
                sbyte s = (sbyte)((int)(f * 0xff) - 0x80);
                unchecked
                {
                    buffer[offset] = (byte)s;
                }
                return 1;
            }

            public uint DecodeToStream(float f, Stream stream)
            {
                unchecked
                {
                    stream.WriteByte((byte)((sbyte)((int)(f * 0xff) - 0x80)));
                }
                return 1;
            }
        }

        private sealed class WaveWriterS16 : IWaveWriter
        {
            public uint BytesPerSample => 2;
            public SamplingMode SamplingMode => SamplingMode.S16;

            public uint DecodeToBuffer(float f, byte[] buffer, uint offset)
            {
                if (offset >= buffer.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }
                short value = (short)(f * 0x7fff);
                byte[] bytes = BitConverter.GetBytes(value);
                uint bytesWritten = 0;
                for (int i = 0; i < 2; ++i)
                {
                    if (offset + i > buffer.Length)
                    {
                        break;
                    }
                    buffer[offset + i] = bytes[i];
                    ++bytesWritten;
                }
                return bytesWritten;
            }

            public uint DecodeToStream(float f, Stream stream)
            {
                byte[] bytes = BitConverter.GetBytes((short)(f * 0x7fff));
                stream.Write(bytes, 0, bytes.Length);
                return (uint)bytes.Length;
            }
        }

        private sealed class WaveWriterS32 : IWaveWriter
        {
            public uint BytesPerSample => 4;
            public SamplingMode SamplingMode => SamplingMode.S32;

            public uint DecodeToBuffer(float f, byte[] buffer, uint offset)
            {
                if (offset >= buffer.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }
                int value = (int)(f * 0x7fffffff);
                byte[] bytes = BitConverter.GetBytes(value);
                uint bytesWritten = 0;
                for (int i = 0; i < 4; ++i)
                {
                    if (offset + i >= buffer.Length)
                    {
                        break;
                    }
                    buffer[offset + i] = bytes[i];
                    ++bytesWritten;
                }
                return bytesWritten;
            }

            public uint DecodeToStream(float f, Stream stream)
            {
                byte[] bytes = BitConverter.GetBytes((int)(f * 0x7fffffff));
                stream.Write(bytes, 0, bytes.Length);
                return (uint)bytes.Length;
            }
        }

        private sealed class WaveWriterR32 : IWaveWriter
        {
            public uint BytesPerSample => 4;
            public SamplingMode SamplingMode => SamplingMode.R32;

            public uint DecodeToBuffer(float f, byte[] buffer, uint offset)
            {
                if (offset >= buffer.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }
                byte[] bytes = BitConverter.GetBytes(f);
                uint bytesWritten = 0;
                for (int i = 0; i < 4; ++i)
                {
                    if (offset + i >= buffer.Length)
                    {
                        break;
                    }
                    buffer[offset + i] = bytes[i];
                    ++bytesWritten;
                }
                return bytesWritten;
            }

            public uint DecodeToStream(float f, Stream stream)
            {
                byte[] bytes = BitConverter.GetBytes(f);
                stream.Write(bytes, 0, bytes.Length);
                return (uint)bytes.Length;
            }
        }
    }
}