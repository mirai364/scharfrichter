using Scharfrichter.Codec;
using Scharfrichter.Codec.Archives;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace IFSExtract
{
    class Program
    {
        private class ExtractOptions
        {
            public bool StripRootFolder;
        }

        /// <summary>
        /// Parses the command line, shows usage when needed, and extracts all supported archives.
        /// </summary>
        /// <param name="args">File or folder paths and options supplied by the user.</param>
        static void Main(string[] args)
        {
            Console.WriteLine("IFSExtract");

            ExtractOptions options = ParseOptions(ref args);
            args = Subfolder.Parse(args);
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            ProcessInputs(args, options);
        }

        /// <summary>
        /// Parses supported command-line options and leaves only input paths in the argument list.
        /// </summary>
        /// <param name="args">The original command-line arguments, replaced with path-only arguments.</param>
        /// <returns>The parsed extraction options.</returns>
        static private ExtractOptions ParseOptions(ref string[] args)
        {
            ExtractOptions options = new ExtractOptions();
            List<string> filenames = new List<string>();

            foreach (string arg in args)
            {
                if (string.Equals(arg, "--strip-root", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/strip-root", StringComparison.OrdinalIgnoreCase))
                {
                    options.StripRootFolder = true;
                    continue;
                }

                filenames.Add(arg);
            }

            args = filenames.ToArray();
            return options;
        }

        /// <summary>
        /// Prints the command-line usage, options, and supported archive formats.
        /// </summary>
        static private void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: IFSExtract [options] <input file>");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("--strip-root    Omit the archive root folder when it matches the output folder name.");
            Console.WriteLine();
            Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
            Console.WriteLine();
            Console.WriteLine("Supported formats:");
            Console.WriteLine("IFS");
        }

        /// <summary>
        /// Processes every expanded input path and extracts supported IFS files.
        /// </summary>
        /// <param name="filenames">The input file paths to inspect.</param>
        /// <param name="options">The extraction options to apply.</param>
        static private void ProcessInputs(string[] filenames, ExtractOptions options)
        {
            foreach (string filename in filenames)
            {
                if (!IsSupportedArchive(filename))
                    continue;

                ExtractArchive(filename, options);
            }
        }

        /// <summary>
        /// Determines whether a path points to an existing IFS archive.
        /// </summary>
        /// <param name="filename">The path to test.</param>
        /// <returns>True when the path is an existing .ifs file; otherwise false.</returns>
        static private bool IsSupportedArchive(string filename)
        {
            return File.Exists(filename) && string.Equals(Path.GetExtension(filename), ".ifs", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads one IFS archive and writes each manifest entry to the output folder.
        /// </summary>
        /// <param name="filename">The IFS archive path to extract.</param>
        /// <param name="options">The extraction options to apply.</param>
        static private void ExtractArchive(string filename, ExtractOptions options)
        {
            Console.WriteLine();
            Console.WriteLine("Processing file " + filename);

            using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                string outputPath = GetOutputPath(filename);
                string outputRootName = Path.GetFileName(outputPath);
                Directory.CreateDirectory(outputPath);

                BemaniIFS archive = BemaniIFS.Read(fs);
                Console.WriteLine("Exporting " + archive.Entries.Length.ToString() + " files.");

                foreach (BemaniIFS.Entry entry in archive.Entries)
                    WriteEntry(outputPath, outputRootName, entry, options);

                PostProcessOutput(outputPath);
            }
        }

        /// <summary>
        /// Builds the extraction folder path for an input archive.
        /// </summary>
        /// <param name="filename">The IFS archive path.</param>
        /// <returns>The output folder next to the archive.</returns>
        static private string GetOutputPath(string filename)
        {
            return Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename));
        }

        /// <summary>
        /// Writes one archive entry to disk and restores its timestamp when available.
        /// </summary>
        /// <param name="outputPath">The root extraction folder.</param>
        /// <param name="outputRootName">The folder name used as the extraction root.</param>
        /// <param name="entry">The archive entry to write.</param>
        /// <param name="options">The extraction options to apply.</param>
        static private void WriteEntry(string outputPath, string outputRootName, BemaniIFS.Entry entry, ExtractOptions options)
        {
            string entryPath = GetEntryOutputPath(entry.FullPath, outputRootName, options);
            string outputFile = Path.Combine(outputPath, entryPath);
            string directory = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            byte[] data = entry.Data;
            if (string.Equals(Path.GetExtension(entryPath), ".xml", StringComparison.OrdinalIgnoreCase))
                BemaniIFS.TryConvertBinaryXml(data, out data);
            if (data == null)
                data = entry.Data;

            File.WriteAllBytes(outputFile, data);
            RestoreTimestamp(outputFile, entry.TimeStamp);

            Console.WriteLine(entryPath);
        }

        /// <summary>
        /// Applies path-related extraction options to an archive entry path.
        /// </summary>
        /// <param name="entryPath">The original archive-relative entry path.</param>
        /// <param name="outputRootName">The folder name used as the extraction root.</param>
        /// <param name="options">The extraction options to apply.</param>
        /// <returns>The entry path to use below the output folder.</returns>
        static private string GetEntryOutputPath(string entryPath, string outputRootName, ExtractOptions options)
        {
            if (!options.StripRootFolder)
                return entryPath;

            string rootPrefix = outputRootName + Path.DirectorySeparatorChar;
            if (entryPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return entryPath.Substring(rootPrefix.Length);

            return entryPath;
        }

        /// <summary>
        /// Runs format-specific post processing after all archive files have been written.
        /// </summary>
        /// <param name="outputPath">The root extraction folder.</param>
        static private void PostProcessOutput(string outputPath)
        {
            ProcessTextureFolder(Path.Combine(outputPath, "tex"));
            ProcessAfpFolder(outputPath);
        }

        /// <summary>
        /// Converts hashed texture payloads into named PNG files using texturelist.xml.
        /// </summary>
        /// <param name="texPath">The extracted tex folder path.</param>
        static private void ProcessTextureFolder(string texPath)
        {
            string textureListPath = Path.Combine(texPath, "texturelist.xml");
            if (!File.Exists(textureListPath))
                return;

            XDocument textureList = XDocument.Load(textureListPath);
            string compression = (string)textureList.Root.Attribute("compress");
            string cachePath = Path.Combine(texPath, "_cache");
            Directory.CreateDirectory(cachePath);

            foreach (XElement texture in textureList.Root.Elements("texture"))
            {
                string format = (string)texture.Attribute("format");
                foreach (XElement image in texture.Elements("image"))
                    ConvertTextureImage(texPath, cachePath, image, format, compression);
            }
        }

        /// <summary>
        /// Converts one hashed texture payload to a named PNG file.
        /// </summary>
        /// <param name="texPath">The extracted tex folder path.</param>
        /// <param name="cachePath">The folder that stores original hashed texture payloads.</param>
        /// <param name="image">The image element from texturelist.xml.</param>
        /// <param name="format">The pixel format declared by the parent texture.</param>
        /// <param name="compression">The compression declared by texturelist.xml.</param>
        static private void ConvertTextureImage(string texPath, string cachePath, XElement image, string format, string compression)
        {
            string imageName = (string)image.Attribute("name");
            if (string.IsNullOrEmpty(imageName))
                return;

            string sourcePath = Path.Combine(texPath, GetMd5Hex(imageName));
            string cacheFile = Path.Combine(cachePath, Path.GetFileName(sourcePath));
            if (!File.Exists(sourcePath) && File.Exists(cacheFile))
                sourcePath = cacheFile;
            if (!File.Exists(sourcePath))
                return;

            if (!File.Exists(cacheFile))
                File.Copy(sourcePath, cacheFile, true);

            int[] imgrect = ReadIntList(image.Element("imgrect").Value);
            int width = (imgrect[1] - imgrect[0]) / 2;
            int height = (imgrect[3] - imgrect[2]) / 2;
            byte[] pixels = LoadTexturePixels(File.ReadAllBytes(sourcePath), compression);
            string outputFile = Path.Combine(texPath, imageName + ".png");

            bool converted = false;
            if (string.Equals(format, "argb8888rev", StringComparison.OrdinalIgnoreCase))
            {
                SaveBgraPng(outputFile, pixels, width, height);
                converted = true;
            }
            else if (string.Equals(format, "argb4444", StringComparison.OrdinalIgnoreCase))
            {
                SaveArgb4444Png(outputFile, pixels, width, height);
                converted = true;
            }

            if (converted && !string.Equals(sourcePath, cacheFile, StringComparison.OrdinalIgnoreCase))
                File.Delete(sourcePath);
        }

        /// <summary>
        /// Applies MD5 name mappings for AFP, BSI, and GEO files using afplist.xml.
        /// </summary>
        /// <param name="outputPath">The root extraction folder.</param>
        static private void ProcessAfpFolder(string outputPath)
        {
            string afpPath = Path.Combine(outputPath, "afp");
            string afpListPath = Path.Combine(afpPath, "afplist.xml");
            if (!File.Exists(afpListPath))
                return;

            XDocument afpList = XDocument.Load(afpListPath);
            List<string> afpNames = new List<string>();
            List<string> geoNames = new List<string>();

            foreach (XElement afp in afpList.Descendants("afp"))
            {
                string name = (string)afp.Attribute("name");
                if (string.IsNullOrEmpty(name))
                    continue;

                afpNames.Add(name);
                foreach (XElement geo in afp.Elements("geo"))
                {
                    foreach (int shape in ReadIntList(geo.Value))
                        geoNames.Add(name + "_shape" + shape.ToString());
                }
            }

            ApplyMd5Folder(afpPath, afpNames, null);
            ApplyMd5Folder(Path.Combine(afpPath, "bsi"), afpNames, null);
            ApplyMd5Folder(Path.Combine(outputPath, "geo"), geoNames, null);
        }

        /// <summary>
        /// Renames MD5-hashed files in a folder to the supplied plain names.
        /// </summary>
        /// <param name="folderPath">The folder containing hashed files.</param>
        /// <param name="names">The plain names used to calculate MD5 file names.</param>
        /// <param name="extension">An optional extension appended to the plain output name.</param>
        static private void ApplyMd5Folder(string folderPath, IEnumerable<string> names, string extension)
        {
            if (!Directory.Exists(folderPath))
                return;

            foreach (string name in names)
            {
                string sourcePath = Path.Combine(folderPath, GetMd5Hex(name));
                if (!File.Exists(sourcePath))
                    continue;

                string targetPath = Path.Combine(folderPath, name + (extension ?? ""));
                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                File.Move(sourcePath, targetPath);
            }
        }

        /// <summary>
        /// Decompresses an extracted texture payload when it uses the avslz wrapper.
        /// </summary>
        /// <param name="data">The raw texture payload.</param>
        /// <param name="compression">The compression name from texturelist.xml.</param>
        /// <returns>The raw pixel payload.</returns>
        static private byte[] LoadTexturePixels(byte[] data, string compression)
        {
            if (!string.Equals(compression, "avslz", StringComparison.OrdinalIgnoreCase) || data.Length < 8)
                return data;

            int uncompressedSize = ReadInt32S(data, 0);
            int compressedSize = ReadInt32S(data, 4);
            if (data.Length == compressedSize + 8)
            {
                byte[] compressed = new byte[compressedSize];
                Array.Copy(data, 8, compressed, 0, compressed.Length);
                byte[] result = DecompressLz77(compressed);
                if (result.Length == uncompressedSize)
                    return result;
                return result;
            }

            byte[] movedHeader = new byte[data.Length];
            Array.Copy(data, 8, movedHeader, 0, data.Length - 8);
            Array.Copy(data, 0, movedHeader, data.Length - 8, 8);
            return movedHeader;
        }

        /// <summary>
        /// Decompresses the LZ77 stream used by avslz texture payloads.
        /// </summary>
        /// <param name="input">The compressed payload without the avslz size header.</param>
        /// <returns>The decompressed payload.</returns>
        static private byte[] DecompressLz77(byte[] input)
        {
            List<byte> output = new List<byte>();
            int offset = 0;
            while (offset < input.Length)
            {
                byte flag = input[offset++];
                for (int i = 0; i < 8 && offset < input.Length; i++)
                {
                    if (((flag >> i) & 1) != 0)
                    {
                        output.Add(input[offset++]);
                    }
                    else
                    {
                        if (offset + 1 >= input.Length)
                            return output.ToArray();

                        int window = (input[offset] << 8) | input[offset + 1];
                        offset += 2;
                        int position = window >> 4;
                        int length = (window & 0x0F) + 3;
                        if (position == 0)
                            return output.ToArray();

                        while (position > output.Count && length > 0)
                        {
                            output.Add(0);
                            length--;
                        }
                        for (int copy = 0; copy < length; copy++)
                            output.Add(output[output.Count - position]);
                    }
                }
            }
            return output.ToArray();
        }

        /// <summary>
        /// Saves BGRA pixel data as a PNG image.
        /// </summary>
        /// <param name="filename">The PNG output path.</param>
        /// <param name="pixels">The BGRA pixel data.</param>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        static private void SaveBgraPng(string filename, byte[] pixels, int width, int height)
        {
            int required = width * height * 4;
            if (pixels.Length < required)
                Array.Resize(ref pixels, required);

            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(pixels, y * width * 4, bitmapData.Scan0 + y * bitmapData.Stride, width * 4);
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }
                bitmap.Save(filename, ImageFormat.Png);
            }
        }

        /// <summary>
        /// Saves ARGB4444 pixel data as a PNG image.
        /// </summary>
        /// <param name="filename">The PNG output path.</param>
        /// <param name="pixels">The ARGB4444 pixel data.</param>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        static private void SaveArgb4444Png(string filename, byte[] pixels, int width, int height)
        {
            byte[] bgra = new byte[width * height * 4];
            for (int i = 0; i < width * height && i * 2 + 1 < pixels.Length; i++)
            {
                int value = (pixels[i * 2] << 8) | pixels[i * 2 + 1];
                byte r = Expand4((value >> 12) & 0x0F);
                byte g = Expand4((value >> 8) & 0x0F);
                byte b = Expand4((value >> 4) & 0x0F);
                byte a = Expand4(value & 0x0F);
                bgra[i * 4 + 0] = b;
                bgra[i * 4 + 1] = g;
                bgra[i * 4 + 2] = r;
                bgra[i * 4 + 3] = a;
            }
            SaveBgraPng(filename, bgra, width, height);
        }

        /// <summary>
        /// Expands a four-bit color channel to eight bits.
        /// </summary>
        /// <param name="value">The four-bit channel value.</param>
        /// <returns>The expanded eight-bit channel value.</returns>
        static private byte Expand4(int value)
        {
            return (byte)((value << 4) | value);
        }

        /// <summary>
        /// Parses a space-separated integer list.
        /// </summary>
        /// <param name="text">The list text.</param>
        /// <returns>The parsed integer values.</returns>
        static private int[] ReadIntList(string text)
        {
            string[] parts = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int[] result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = int.Parse(parts[i]);
            return result;
        }

        /// <summary>
        /// Calculates the lower-case MD5 hex string used by IFS hashed file names.
        /// </summary>
        /// <param name="text">The plain name to hash.</param>
        /// <returns>The lower-case MD5 hex string.</returns>
        static private string GetMd5Hex(string text)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(text));
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        /// <summary>
        /// Reads a big-endian 32-bit integer from a byte array.
        /// </summary>
        /// <param name="data">The byte array to read from.</param>
        /// <param name="offset">The offset to read at.</param>
        /// <returns>The decoded integer.</returns>
        static private int ReadInt32S(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        /// <summary>
        /// Restores a Unix timestamp on an extracted file when the archive provided one.
        /// </summary>
        /// <param name="filename">The extracted file path.</param>
        /// <param name="timeStamp">The Unix timestamp from the archive, or a non-positive value when absent.</param>
        static private void RestoreTimestamp(string filename, int timeStamp)
        {
            if (timeStamp <= 0)
                return;

            DateTime writeTime = DateTimeOffset.FromUnixTimeSeconds(timeStamp).LocalDateTime;
            File.SetLastWriteTime(filename, writeTime);
        }
    }
}