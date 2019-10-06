using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scharfrichter.Codec.Archives
{
    public class BemaniIFS : Archive
    {
        static public readonly string PropBinaryNameChars = "0123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz";
        static public readonly DateTime UNIX_EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        public struct Stat
        {
            public string FileName;
            public int TimeStamp;
            public int Offset;
            public int Length;

            static public Stat Read(Stream source)
            {
                BinaryReaderEx reader = new BinaryReaderEx(source);
                Stat result = new Stat();
                result.TimeStamp = reader.ReadInt32S();
                result.Offset = reader.ReadInt32S();
                result.Length = reader.ReadInt32S();
                // Console.WriteLine("TimeStamp: " + getTimeStamp(result.TimeStamp) + " Offset: " + result.Offset + " Length: " + result.Length);
                return result;
            }
        }

        public struct formats
        {
            public string type;
            public int size;
            public int count;
            public string name;
            public string fromStr;
            public string toStr;
        }

        public static readonly int NODE_START = 1;
        public static readonly int NODE_END = 190;
        public static readonly int END_SECTION = 191;

        private static formats getXmlFormats(int value)
        {
            formats result = new formats();
            switch (value)
            {
                case 1:
                    result.name = "void";
                    break;
                case 2:
                    result.type = "b";
                    result.size = 1;
                    result.count = 1;
                    result.name = "s8";
                    break;
                case 3:
                    result.type = "B";
                    result.size = 1;
                    result.count = 1;
                    result.name = "u8";
                    break;
                case 4:
                    result.type = "h";
                    result.size = 2;
                    result.count = 1;
                    result.name = "s16";
                    break;
                case 5:
                    result.type = "H";
                    result.size = 2;
                    result.count = 1;
                    result.name = "u16";
                    break;
                case 6:
                    result.type = "i";
                    result.size = 4;
                    result.count = 1;
                    result.name = "s32";
                    break;
                case 7:
                    result.type = "I";
                    result.size = 4;
                    result.count = 1;
                    result.name = "u32";
                    break;
                case 8:
                    result.type = "q";
                    result.size = 8;
                    result.count = 1;
                    result.name = "s64";
                    break;
                case 9:
                    result.type = "Q";
                    result.size = 8;
                    result.count = 1;
                    result.name = "u64";
                    break;
                case 10:
                    result.type = "B";
                    result.size = 1;
                    result.count = -1;
                    result.name = "bin";
                    result.fromStr = "None";
                    break;
                case 11:
                    result.type = "B";
                    result.size = 1;
                    result.count = -1;
                    result.name = "str";
                    result.fromStr = "None";
                    break;
                case 12:
                    result.type = "I";
                    result.size = 4;
                    result.count = 1;
                    result.name = "ip4";
                    result.fromStr = "parseIP";
                    result.toStr = "writeIP";
                    break;
                case 13:
                    result.type = "I";
                    result.size = 4;
                    result.count = 1;
                    result.name = "time";
                    break;
                case 14:
                    result.type = "f";
                    result.size = 4;
                    result.count = 1;
                    result.name = "float";
                    result.fromStr = "float";
                    result.toStr = "writefloat";
                    break;
                case 15:
                    result.type = "d";
                    result.size = 8;
                    result.count = 1;
                    result.name = "double";
                    result.fromStr = "float";
                    result.toStr = "writefloat";
                    break;
                case 16:
                    result.type = "b";
                    result.size = 1;
                    result.count = 2;
                    result.name = "2s8";
                    break;
                case 17:
                    result.type = "B";
                    result.size = 1;
                    result.count = 2;
                    result.name = "2u8";
                    break;
                case 18:
                    result.type = "h";
                    result.size = 2;
                    result.count = 2;
                    result.name = "2s16";
                    break;
                case 19:
                    result.type = "H";
                    result.size = 2;
                    result.count = 2;
                    result.name = "2u16";
                    break;
                case 20:
                    result.type = "i";
                    result.size = 4;
                    result.count = 2;
                    result.name = "2s32";
                    break;
                case 21:
                    result.type = "I";
                    result.size = 4;
                    result.count = 2;
                    result.name = "2u32";
                    break;
                case 22:
                    result.type = "q";
                    result.size = 8;
                    result.count = 2;
                    result.name = "2s64";
                    break;
                case 23:
                    result.type = "Q";
                    result.size = 8;
                    result.count = 2;
                    result.name = "2u64";
                    break;
                case 24:
                    result.type = "f";
                    result.size = 4;
                    result.count = 2;
                    result.name = "2f";
                    result.fromStr = "float";
                    result.toStr = "writefloat";
                    break;
                case 25:
                    result.type = "d";
                    result.size = 8;
                    result.count = 2;
                    result.name = "2d";
                    result.fromStr = "float";
                    result.toStr = "writefloat";
                    break;
                case 26:
                    result.type = "b";
                    result.size = 1;
                    result.count = 3;
                    result.name = "3s8";
                    break;
                case 27:
                    result.type = "B";
                    result.size = 1;
                    result.count = 3;
                    result.name = "3u8";
                    break;
                case 28:
                    result.type = "h";
                    result.size = 2;
                    result.count = 3;
                    result.name = "3s16";
                    break;
                case 29:
                    result.type = "H";
                    result.size = 2;
                    result.count = 3;
                    result.name = "3u16";
                    break;
                case 30:
                    result.type = "i";
                    result.size = 4;
                    result.count = 3;
                    result.name = "3s32";
                    break;
                case 31:
                    result.type = "I";
                    result.size = 4;
                    result.count = 3;
                    result.name = "3u32";
                    break;
                case 32:
                    result.type = "q";
                    result.size = 8;
                    result.count = 3;
                    result.name = "3s64";
                    break;
                case 33:
                    result.type = "Q";
                    result.size = 8;
                    result.count = 3;
                    result.name = "3u64";
                    break;
                case 34:
                    result.type = "f";
                    result.size = 4;
                    result.count = 3;
                    result.name = "3f";
                    result.fromStr = "float";
                    result.toStr = "writefloat";
                    break;
                case 35:
                    result.type = "d";
                    result.size = 8;
                    result.count = 3;
                    result.name = "3d";
                    result.fromStr = "float";
                    result.toStr = "writefloat";
                    break;
                case 36:
                    result.type = "b";
                    result.size = 1;
                    result.count = 4;
                    result.name = "4s8";
                    break;
                case 37:
                    result.type = "B";
                    result.size = 1;
                    result.count = 4;
                    result.name = "4u8";
                    break;
                case 38:
                    result.type = "h";
                    result.size = 2;
                    result.count = 4;
                    result.name = "4s16";
                    break;
                case 39:
                    result.type = "H";
                    result.size = 2;
                    result.count = 4;
                    result.name = "4u16";
                    break;
                case 40:
                    result.type = "i";
                    result.size = 4;
                    result.count = 4;
                    result.name = "4s32";
                    break;
                case 41:
                    result.type = "I";
                    result.size = 4;
                    result.count = 4;
                    result.name = "4u32";
                    break;
                case 42:
                    result.type = "q";
                    result.size = 8;
                    result.count = 4;
                    result.name = "4s64";
                    break;
                case 43:
                    result.type = "Q";
                    result.size = 8;
                    result.count = 4;
                    result.name = "4u64";
                    break;
                case 44:
                    result.type = "f";
                    result.size = 4;
                    result.count = 4;
                    result.name = "4f";
                    result.fromStr = "float";
                    result.toStr = "writefloat";
                    break;
                case 45:
                    result.type = "d";
                    result.size = 8;
                    result.count = 4;
                    result.name = "4d";
                    result.fromStr = "float";
                    result.toStr = "writefloat";
                    break;
                case 46:
                    result.name = "attr";
                    break;
                case 48:
                    result.type = "b";
                    result.size = 1;
                    result.count = 16;
                    result.name = "vs8";
                    break;
                case 49:
                    result.type = "B";
                    result.size = 1;
                    result.count = 16;
                    result.name = "vu8";
                    break;
                case 50:
                    result.type = "h";
                    result.size = 2;
                    result.count = 8;
                    result.name = "vs16";
                    break;
                case 51:
                    result.type = "H";
                    result.size = 2;
                    result.count = 8;
                    result.name = "vu16";
                    break;
                case 52:
                    result.type = "b";
                    result.size = 1;
                    result.count = 1;
                    result.name = "bool";
                    break;
                case 53:
                    result.type = "b";
                    result.size = 1;
                    result.count = 2;
                    result.name = "2b";
                    break;
                case 54:
                    result.type = "b";
                    result.size = 1;
                    result.count = 3;
                    result.name = "3b";
                    break;
                case 55:
                    result.type = "b";
                    result.size = 1;
                    result.count = 4;
                    result.name = "4b";
                    break;
                case 56:
                    result.type = "b";
                    result.size = 1;
                    result.count = 16;
                    result.name = "vb";
                    break;
            }
            return result;
        }

        private List<byte[]> files = new List<byte[]>();
        private List<string> properties = new List<string>();
        private List<DateTime> timeStamp = new List<DateTime>();

        public DateTime[] TimeStamps
        {
            get
            {
                return timeStamp.ToArray();
            }
            set
            {
                timeStamp.Clear();
                timeStamp.AddRange(value);
            }
        }

        public string[] Properties
        {
            get
            {
                return properties.ToArray();
            }
            set
            {
                properties.Clear();
                properties.AddRange(value);
            }
        }

        public override byte[][] RawData
        {
            get
            {
                return files.ToArray();
            }
            set
            {
                files.Clear();
                files.AddRange(value);
            }
        }

        public override int RawDataCount
        {
            get
            {
                return files.Count;
            }
        }

        static private Encoding enc = Encoding.GetEncoding(932);

        static private string GetString(byte[] source)
        {
            List<byte> buffer = new List<byte>();
            int length = source.Length;
            for (int i = 0; i < length; i++)
            {
                if (source[i] != 0)
                    buffer.Add(source[i]);
                else
                    break;
            }

            return enc.GetString(buffer.ToArray());
        }

        static private string getEncodeTypeByKey(byte encoding_key)
        {
            var encodeType = "";
            switch (encoding_key)
            {
                case 0:
                    encodeType = "cp932"; break;
                case 32:
                    encodeType = "ASCII"; break;
                case 64:
                    encodeType = "ISO-8859-1"; break;
                case 96:
                    encodeType = "EUC_JP"; break;
                case 128:
                    encodeType = "cp932"; break;
                case 160:
                    encodeType = "UTF-8"; break;
            }
            return encodeType;
        }

        public static string getKonamiString(Int64 bits, int padding, int length)
        {
            char[] charmap = PropBinaryNameChars.ToCharArray();
            bits >>= padding;
            List<char> charList = new List<char>();
            for (int k = 0; k < length; k++)
            {
                var r = bits & 0b111111;
                bits >>= 6;
                charList.Add(charmap[r]);
            }
            charList.Reverse();
            string result = new String(charList.ToArray());
            result = result.Replace("_E", ".");
            result = result.Replace("__", "_");
            //result = result.Remove(0, 1);
            return result;
        }

        public static DateTime getTimeStamp(int time)
        {
            return UNIX_EPOCH.AddSeconds(time).ToLocalTime();
        }

        static public BemaniIFS Read(Stream source)
        {
            BemaniIFS result = new BemaniIFS();

            // read header
            //Header header = Header.Read(source);
            BinaryReaderEx reader = new BinaryReaderEx(source);

            var signature = reader.ReadInt32S();
            if (0x6CAD8F89 != signature)
            {
                throw new ArgumentException("Given file was not an IFS file!");
            }

            var file_version = reader.ReadInt16S();
            Console.WriteLine("File version: " + file_version);
            if ((reader.ReadInt16S() ^ file_version) != -1)
            {
                throw new ArgumentException("Given file was not an IFS file!");
            }
            var time = reader.ReadInt32S();
            Console.WriteLine("TimeStamp: " + getTimeStamp(time));

            var ifs_tree_size = reader.ReadInt32S();
            var fIndex = reader.ReadInt32S();
            if (file_version > 1)
            {
                reader.ReadBytes(16);
            }

            // read manifest
            signature = reader.ReadByte();
            if (0xA0 != signature)
            {
                throw new ArgumentException("Given file was not an IFS file!");
            }
            var sig_comp = reader.ReadByte();
            var compressed = (sig_comp == 0x42);
            var encoding_key = reader.ReadByte();
            var encodeType = getEncodeTypeByKey(encoding_key);
            if (reader.ReadByte() != (0xFF ^ encoding_key))
            {
                throw new ArgumentException("Given file was not an IFS file!");
            }

            var fHeader = reader.ReadInt32S();
            if (fHeader % 4 != 0)
            {
                throw new ArgumentException("fHeader%4 != 0");
            }

            Console.WriteLine();
            var tmpdata = reader.ReadBytes(fHeader + 28);
            MemoryStream strm = new MemoryStream(tmpdata);
            BinaryReaderEx sr = new BinaryReaderEx(strm);
            List<string> nameList = new List<string>();
            string folderName = "";
            byte beforeNodeType = 0;
            while (sr.BaseStream.Position < sr.BaseStream.Length)
            {
                byte nodeType = sr.ReadByte();
                if (nodeType == 0) { continue; }
                bool isArray = ((nodeType & 64) >> 6 == 1) ? true : false;
                nodeType &= 191;
                formats nodeFormat = getXmlFormats(nodeType);

                var name = "";
                if (nodeType != NODE_END && nodeType != END_SECTION)
                {
                    if (compressed)
                    {
                        var length = sr.ReadByte();
                        var length_bits = length * 6;
                        int length_bytes = (length_bits + 7) / 8;
                        var padding = 8 - (length_bits % 8);
                        if (padding == 8) { padding = 0; }
                        name = getKonamiString(sr.ReadValueS(length_bytes), padding, length);
                    }
                    else
                    {
                        var length_bytes = (sr.ReadByte() & 127) + 1;
                        name = GetString(sr.ReadBytes(length_bytes));
                    }
                    if (nodeType == 30)
                    {
                        nameList.Add(folderName + name);
                    }
                    else if (nodeType == 6)
                    {
                        folderName += name + "\\";
                    }
                }
                if (beforeNodeType == NODE_END && nodeType == NODE_END)
                {
                    var tmpList = new List<string>();
                    tmpList.AddRange(folderName.Split('\\'));
                    if(tmpList.Count() > 1)
                    {
                        if (tmpList.Count() > 2)
                        {
                            tmpList.RemoveAt(tmpList.Count() - 2);
                            folderName = string.Join("\\", tmpList);
                        }
                        else
                        {
                            folderName = "";
                        }
                    }
                }
                // Console.WriteLine("nodeType(" + nodeType + ") : " + folderName + name);
                bool isBreak = false;
                switch (nodeType)
                {
                    case 1:
                        break;
                    case 46:
                        break;
                    case 190:
                        break;
                    case 191:
                        isBreak = true;
                        break;
                }
                beforeNodeType = nodeType;
                if (isBreak)
                    break;
                if (nodeType == NODE_START)
                    continue;
            }

            var num = (fIndex - (fHeader + 72)) / 12;
            List<Stat> statList = new List<Stat>();
            for (var i = 0; i < num; i++)
            {
                var pos = reader.BaseStream.Position;
                MemoryStream temp = new MemoryStream(reader.ReadBytes(4 * 3));
                // Console.Write(pos + " ");
                Stat stat = Stat.Read(temp);
                // TODO Change to use nodeType
                if (stat.Length > reader.BaseStream.Length || stat.Offset > reader.BaseStream.Length)
                {
                    reader.BaseStream.Position = pos + 4;
                    i -= 1;
                    continue;
                }
                if (stat.Length == 0)
                {
                    continue;
                }
                // error check
                if(i >= nameList.Count() || i < 0)
                {
                    continue;
                }

                stat.FileName = nameList[i];
                statList.Add(stat);
            }

            foreach (Stat stat in statList)
            {
                reader.BaseStream.Position = stat.Offset + fIndex;
                result.files.Add(reader.ReadBytes(stat.Length));
                result.properties.Add(stat.FileName);
                result.timeStamp.Add(getTimeStamp(stat.TimeStamp));
            }


            return result;
        }
    }
}
