using Scharfrichter.Codec.Sounds;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Scharfrichter.Codec.Archives
{
    public struct s3vData
    {
        public int memStart { get; set; }
        public int memLength { get; set; }
    }

    public class BemaniS3P : Archive
    {
        private static readonly ParallelOptions DecodeParallelOptions = CreateDecodeParallelOptions();
        private List<Sound> sounds = new List<Sound>();
        private List<s3vData> s3vDataList = new List<s3vData>();

        static public BemaniS3P Read(Stream source)
        {
            BemaniS3P result = new BemaniS3P();
            BinaryReader reader = new BinaryReader(source);
            reader.ReadBytes(4);

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                s3vData tmp = new s3vData();
                tmp.memStart = reader.ReadInt32();
                tmp.memLength = reader.ReadInt32();
                result.s3vDataList.Add(tmp);
            }

            List<byte[]> wmaDataList = ReadWmaDataList(reader, result.s3vDataList);
            Sound[] decodedSounds = new Sound[wmaDataList.Count];
            Parallel.For(0, wmaDataList.Count, DecodeParallelOptions, i =>
            {
                decodedSounds[i] = BemaniS3PSound.Read(wmaDataList[i]);
            });
            result.sounds.AddRange(decodedSounds);

            return result;
        }

        private static List<byte[]> ReadWmaDataList(BinaryReader reader, List<s3vData> dataList)
        {
            List<byte[]> wmaDataList = new List<byte[]>();
            for (int i = 0; i < dataList.Count; i++)
            {
                int memStart = dataList[i].memStart;
                reader.BaseStream.Position = memStart;
                if (new string(reader.ReadChars(4)) != "S3V0")
                    continue;

                int start = reader.ReadInt32();
                reader.BaseStream.Position = memStart + start;
                wmaDataList.Add(reader.ReadBytes(dataList[i].memLength - start));
            }

            return wmaDataList;
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