using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;
using Scharfrichter.Codec.Heuristics;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace IFSExtract
{
    class Program
    {
        static void Main(string[] args)
        {
            args = Subfolder.Parse(args);
            if (args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Usage: IFSExtract <input file>");
                Console.WriteLine();
                Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
                Console.WriteLine();
                Console.WriteLine("Supported formats:");
                Console.WriteLine("IFS");
            }

            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string filename = args[i];

                    if (File.Exists(filename) && Path.GetExtension(filename).ToUpper() == @".IFS")
                    {
                        Console.WriteLine();
                        Console.WriteLine("Processing file " + args[i]);

                        using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                        {
                            string outputPath = Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename));
                            string outputFileBase = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(Path.GetFileName(filename)));

                            Directory.CreateDirectory(outputPath);

                            BemaniIFS archive = BemaniIFS.Read(fs);
                            int count = archive.RawDataCount;

                            Console.WriteLine("Exporting files.");
                            for (int j = count - 1; j >= 0; j--)
                            {
                                byte[] data = archive.RawData[j];
                                string name = archive.Properties[j];
                                DateTime timeStamp = archive.TimeStamps[j];

                                string outputFile = Path.Combine(outputPath, name);
                                Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
                                File.WriteAllBytes(outputFile, data);
                                File.SetCreationTime(outputFile, timeStamp);
                                File.SetLastAccessTimeUtc(outputFile, timeStamp);
                                File.SetLastWriteTimeUtc(outputFile, timeStamp);
                            }
                        }
                    }
                }
            }
        }
    }
}
