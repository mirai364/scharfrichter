using Scharfrichter.Common;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace S3PConverter
{
    public struct s3vData
    {
        public int memStart { get; set; }
        public int memLength { get; set; }

        public int getMemEnd ()
        {
            return memStart + memLength;
        }

        public void memEcho ()
        {
            Console.WriteLine("s3v mem " + memStart + " to " + getMemEnd());
        }

        public void getMemData (int index, BinaryReader reader)
        {
            reader.BaseStream.Position = memStart;
            byte[] extension = reader.ReadBytes(4);
            int start = reader.ReadInt32();
            reader.BaseStream.Position = memStart + start;
            byte[] readData  = reader.ReadBytes(memLength - start);
            File.WriteAllBytes("result\\" + ConvertToAlphabetString(index, 4, "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ") + @".wma", readData.ToArray());
        }

        private string ConvertToAlphabetString(int value, int places, string alphabet)
        {
            string result = "";
            int alphabetLength = alphabet.Length;
            while (places > 0)
            {
                result = alphabet.Substring(value % alphabetLength, 1) + result;
                value /= alphabetLength;
                places--;
            }

            return result;
        }
    }

    class Program
    {
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

        static void Main(string[] args)
        {
            List<s3vData> s3vList = new List<s3vData>();
            // show usage if no args provided
            if (args.Length == 0 || args.Length > 1)
            {
                Console.WriteLine();
                Console.WriteLine("Usage: S3PConverter <input file>");
                Console.WriteLine();
                Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
                Console.WriteLine();
                Console.WriteLine("Supported file:");
                Console.WriteLine("*.s3p");
                return;
            }

            string sourceFileName = args[0];
            Console.WriteLine("inputFile : " + sourceFileName);
            byte[] data = File.ReadAllBytes(sourceFileName);

            using (MemoryStream mem = new MemoryStream(data))
            {
                BinaryReader reader = new BinaryReader(mem);
                byte[] extension = reader.ReadBytes(4);
                if (GetString(extension) != "S3P0")
                {
                    Console.WriteLine("It is not a supported file.");
                    return;
                }
                int count = reader.ReadInt32();
                for (int i = 0; i< count; i++)
                {
                    s3vData s3vdata = new s3vData();
                    s3vdata.memStart = reader.ReadInt32();
                    s3vdata.memLength = reader.ReadInt32();
                    s3vList.Add(s3vdata);
                }

                SafeCreateDirectory("result");
                for (int i = 0; i < count; i++)
                {
                    s3vList[i].memEcho();
                    s3vList[i].getMemData(i+1, reader);
                }
            }
        }

        /// <summary>
        /// Create folder if folder does not exist
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static DirectoryInfo SafeCreateDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                return null;
            }
            return Directory.CreateDirectory(path);
        }
    }
}
