using Scharfrichter.Codec.Encryption;
using Scharfrichter.Codec.Sounds;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Scharfrichter.Codec.Archives
{
    public enum Bemani2DXType
    {
        Unencrypted,
        IIDX9,
        IIDX10,
        IIDX11,
        IIDX12,
        IIDXHID,
        IIDX16
    }

    public class Bemani2DX : Archive
    {
        private static readonly ParallelOptions DecodeParallelOptions = CreateDecodeParallelOptions();
        private List<Sound> sounds = new List<Sound>();
        public Bemani2DXType Type;

        static public Bemani2DX Read(Stream source)
        {
            Bemani2DX result = new Bemani2DX();
            BinaryReader reader = new BinaryReader(source);
            byte[] key = new byte[] { };
            Bemani2DXEncryptionType encType = Bemani2DXEncryptionType.Standard;

            string headerID = new string(reader.ReadChars(4));

            switch (headerID)
            {
                case @"%eNc":
                    result.Type = Bemani2DXType.IIDX9;
                    key = Bemani2DXEncryptionKeys.IIDX9;
                    encType = Bemani2DXEncryptionType.Standard;
                    break;
                case @"%e10":
                    result.Type = Bemani2DXType.IIDX10;
                    key = Bemani2DXEncryptionKeys.IIDX10;
                    encType = Bemani2DXEncryptionType.Standard;
                    break;
                case @"%e11":
                    result.Type = Bemani2DXType.IIDX11;
                    key = Bemani2DXEncryptionKeys.IIDX11;
                    encType = Bemani2DXEncryptionType.Standard;
                    break;
                case @"%e12":
                    result.Type = Bemani2DXType.IIDX12;
                    key = Bemani2DXEncryptionKeys.IIDX11;
                    encType = Bemani2DXEncryptionType.Partial;
                    break;
                case @"%hid":
                    result.Type = Bemani2DXType.IIDXHID;
                    key = Bemani2DXEncryptionKeys.IIDX11;
                    encType = Bemani2DXEncryptionType.Partial;
                    break;
                case @"%iO0":
                    result.Type = Bemani2DXType.IIDX16;
                    key = Bemani2DXEncryptionKeys.IIDX16;
                    encType = Bemani2DXEncryptionType.Standard;
                    break;
                default:
                    result.Type = Bemani2DXType.Unencrypted;
                    source.Position -= 4;
                    break;
            }

            try
            {
                if (result.Type != Bemani2DXType.Unencrypted)
                {
                    MemoryStream decodedData = new MemoryStream();
                    int filelength = reader.ReadInt32();
                    int fileExtraBytes = (8 - (filelength % 8)) % 8;
                    byte[] data = reader.ReadBytes(filelength + fileExtraBytes);
                    using (MemoryStream encodedDataMem = new MemoryStream(data))
                    {
                        Bemani2DXEncryption.Decrypt(encodedDataMem, decodedData, key, encType);
                    }
                    decodedData.Position = 0;
                    reader = new BinaryReader(decodedData);
                }

                reader.BaseStream.Position = 0x10;

                int headerLength = reader.ReadInt32();
                int sampleCount = reader.ReadInt32();
                long[] sampleOffset = new long[sampleCount];

                reader.BaseStream.Position = 0x48;

                for (int i = 0; i < sampleCount; i++)
                {
                    sampleOffset[i] = reader.ReadInt32();
                }

                byte[][] sampleData = ReadSampleData(reader.BaseStream, sampleOffset);
                Sound[] decodedSounds = new Sound[sampleData.Length];
                Parallel.For(0, sampleData.Length, DecodeParallelOptions, i =>
                {
                    if (sampleData[i] == null || sampleData[i].Length == 0)
                        return;
                    try
                    {
                        using (MemoryStream sampleStream = new MemoryStream(sampleData[i]))
                        {
                            decodedSounds[i] = Bemani2DXSound.Read(sampleStream);
                        }
                    }
                    catch (Exception)
                    {
                        decodedSounds[i] = null;
                    }
                });
                foreach (Sound s in decodedSounds)
                    if (s != null)
                        result.sounds.Add(s);
            }
            catch (EndOfStreamException)
            {
                result.sounds = new List<Sound>();
            }

            return result;
        }

        private static byte[][] ReadSampleData(Stream source, long[] sampleOffset)
        {
            const int MaxSampleBytes = 16 * 1024 * 1024;
            byte[][] sampleData = new byte[sampleOffset.Length][];
            long streamLength = source.Length;
            for (int i = 0; i < sampleOffset.Length; i++)
            {
                long nextOffset = i + 1 < sampleOffset.Length ? sampleOffset[i + 1] : streamLength;
                long length = nextOffset - sampleOffset[i];
                if (length <= 0 || sampleOffset[i] < 0 || sampleOffset[i] >= streamLength || length > MaxSampleBytes)
                {
                    sampleData[i] = new byte[0];
                    continue;
                }
                if (sampleOffset[i] + length > streamLength)
                    length = streamLength - sampleOffset[i];
                if (length > MaxSampleBytes)
                    length = MaxSampleBytes;
                sampleData[i] = new byte[length];
                source.Position = sampleOffset[i];
                source.ReadExactly(sampleData[i], 0, (int)length);
            }

            return sampleData;
        }

        private static ParallelOptions CreateDecodeParallelOptions()
        {
            return new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
            };
        }

        public override Sound[] Sounds
        {
            get
            {
                return sounds.ToArray();
            }
            set
            {
                sounds.Clear();
                sounds.AddRange(value);
            }
        }

        public override int SoundCount
        {
            get
            {
                return sounds.Count;
            }
        }
    }
}