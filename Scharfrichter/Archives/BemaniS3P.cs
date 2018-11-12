using Scharfrichter.Codec.Encryption;
using Scharfrichter.Codec.Sounds;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scharfrichter.Codec.Archives
{
    public struct s3vData
    {
        public int memStart { get; set; }
        public int memLength { get; set; }
    }

    public class BemaniS3P : Archive
    {
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

            for (int i = 0; i < count; i++)
            {
                int memStart = result.s3vDataList[i].memStart;
                reader.BaseStream.Position = memStart;
                if (new string(reader.ReadChars(4)) == "S3V0")
                {
                    int start = reader.ReadInt32();
                    reader.BaseStream.Position = memStart + start;
                    byte[] wmaData = reader.ReadBytes(result.s3vDataList[i].memLength - start);
                    result.sounds.Add(BemaniS3PSound.Read(wmaData));
                }
            }

            return result;
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
