using Scharfrichter.Codec.Archives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ConvertHelper
{
    public static class AfpGraphicArchiveExtractor
    {
        public static string Extract(string archivePath, string outputRoot)
        {
            archivePath = Path.GetFullPath(archivePath);
            outputRoot = Path.GetFullPath(outputRoot);
            Directory.CreateDirectory(outputRoot);

            using FileStream stream = new FileStream(
                archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            BemaniIFS archive = BemaniIFS.Read(stream);
            foreach (BemaniIFS.Entry entry in archive.Entries)
            {
                string outputFile = ResolveEntryPath(outputRoot, entry.FullPath);
                string directory = Path.GetDirectoryName(outputFile);
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                byte[] data = entry.Data;
                if (String.Equals(Path.GetExtension(outputFile), ".xml", StringComparison.OrdinalIgnoreCase))
                    BemaniIFS.TryConvertBinaryXml(data, out data);
                File.WriteAllBytes(outputFile, data ?? entry.Data);
            }

            string graphicFolder = FindGraphicFolder(
                outputRoot, Path.GetFileNameWithoutExtension(archivePath));
            ProcessTextureFolder(Path.Combine(graphicFolder, "tex"));
            ProcessAfpFolder(graphicFolder);
            return graphicFolder;
        }

        private static string ResolveEntryPath(string root, string entryPath)
        {
            string normalized = entryPath.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            string outputFile = Path.GetFullPath(Path.Combine(root, normalized));
            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!outputFile.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "IFS entry points outside the extraction folder: " + entryPath);
            return outputFile;
        }

        private static string FindGraphicFolder(string root, string preferredName)
        {
            List<string> candidates = new List<string>();
            if (IsGraphicFolder(root))
                candidates.Add(root);
            candidates.AddRange(Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Where(IsGraphicFolder)
                .Select(Path.GetFullPath));

            if (candidates.Count == 0)
                throw new InvalidDataException("IFS archive does not contain an AFP graphic folder.");

            string preferred = candidates.FirstOrDefault(candidate => String.Equals(
                new DirectoryInfo(candidate).Name, preferredName, StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
                return preferred;
            if (candidates.Count == 1)
                return candidates[0];
            throw new InvalidDataException(
                "IFS archive contains multiple AFP graphic folders: " + String.Join(", ", candidates));
        }

        private static bool IsGraphicFolder(string folder)
        {
            return Directory.Exists(Path.Combine(folder, "afp")) &&
                Directory.Exists(Path.Combine(folder, "afp", "bsi")) &&
                Directory.Exists(Path.Combine(folder, "geo")) &&
                Directory.Exists(Path.Combine(folder, "tex"));
        }

        private static void ProcessAfpFolder(string outputRoot)
        {
            string afpPath = Path.Combine(outputRoot, "afp");
            string afpListPath = Path.Combine(afpPath, "afplist.xml");
            if (!File.Exists(afpListPath))
                return;

            XDocument afpList = XDocument.Load(afpListPath);
            List<string> afpNames = new List<string>();
            List<string> geoNames = new List<string>();
            foreach (XElement afp in afpList.Descendants("afp"))
            {
                string name = (string)afp.Attribute("name");
                if (String.IsNullOrEmpty(name))
                    continue;

                afpNames.Add(name);
                foreach (XElement geo in afp.Elements("geo"))
                {
                    foreach (int shape in ReadIntList(geo.Value))
                        geoNames.Add(name + "_shape" + shape.ToString());
                }
            }

            ApplyMd5Folder(afpPath, afpNames);
            ApplyMd5Folder(Path.Combine(afpPath, "bsi"), afpNames);
            ApplyMd5Folder(Path.Combine(outputRoot, "geo"), geoNames);
        }

        private static void ApplyMd5Folder(string folder, IEnumerable<string> names)
        {
            if (!Directory.Exists(folder))
                return;

            foreach (string name in names)
            {
                string source = Path.Combine(folder, GetMd5Hex(name));
                if (!File.Exists(source))
                    continue;

                string destination = Path.Combine(folder, name);
                File.Move(source, destination, true);
            }
        }

        private static void ProcessTextureFolder(string textureFolder)
        {
            string listPath = Path.Combine(textureFolder, "texturelist.xml");
            if (!File.Exists(listPath))
                return;

            XDocument textureList = XDocument.Load(listPath);
            string compression = (string)textureList.Root.Attribute("compress");
            string cacheFolder = Path.Combine(textureFolder, "_cache");
            Directory.CreateDirectory(cacheFolder);

            foreach (XElement texture in textureList.Root.Elements("texture"))
            {
                string format = (string)texture.Attribute("format");
                foreach (XElement image in texture.Elements("image"))
                    ConvertTextureImage(textureFolder, cacheFolder, image, format, compression);
            }
        }

        private static void ConvertTextureImage(
            string textureFolder,
            string cacheFolder,
            XElement image,
            string format,
            string compression)
        {
            string imageName = (string)image.Attribute("name");
            if (String.IsNullOrEmpty(imageName))
                return;

            string source = Path.Combine(textureFolder, GetMd5Hex(imageName));
            string cacheFile = Path.Combine(cacheFolder, Path.GetFileName(source));
            if (!File.Exists(source) && File.Exists(cacheFile))
                source = cacheFile;
            if (!File.Exists(source))
                return;
            if (!File.Exists(cacheFile))
                File.Copy(source, cacheFile, true);

            int[] rectangle = ReadIntList(image.Element("imgrect").Value);
            int width = (rectangle[1] - rectangle[0]) / 2;
            int height = (rectangle[3] - rectangle[2]) / 2;
            byte[] pixels = LoadTexturePixels(File.ReadAllBytes(source), compression);
            string outputFile = Path.Combine(textureFolder, imageName + ".png");

            bool converted = false;
            if (String.Equals(format, "argb8888rev", StringComparison.OrdinalIgnoreCase))
            {
                SaveBgraPng(outputFile, pixels, width, height);
                converted = true;
            }
            else if (String.Equals(format, "argb4444", StringComparison.OrdinalIgnoreCase))
            {
                SaveArgb4444Png(outputFile, pixels, width, height);
                converted = true;
            }

            if (converted && !String.Equals(source, cacheFile, StringComparison.OrdinalIgnoreCase))
                File.Delete(source);
        }

        private static void SaveBgraPng(string outputFile, byte[] source, int width, int height)
        {
            int pixelCount = checked(width * height);
            Rgba32[] pixels = new Rgba32[pixelCount];
            int available = Math.Min(pixelCount, source.Length / 4);
            for (int i = 0; i < available; i++)
            {
                int offset = i * 4;
                pixels[i] = new Rgba32(
                    source[offset + 2], source[offset + 1], source[offset], source[offset + 3]);
            }

            using Image<Rgba32> image = Image.LoadPixelData(pixels, width, height);
            image.SaveAsPng(outputFile);
        }

        private static void SaveArgb4444Png(string outputFile, byte[] source, int width, int height)
        {
            int pixelCount = checked(width * height);
            Rgba32[] pixels = new Rgba32[pixelCount];
            int available = Math.Min(pixelCount, source.Length / 2);
            for (int i = 0; i < available; i++)
            {
                int value = (source[i * 2] << 8) | source[i * 2 + 1];
                pixels[i] = new Rgba32(
                    Expand4((value >> 12) & 0x0F),
                    Expand4((value >> 8) & 0x0F),
                    Expand4((value >> 4) & 0x0F),
                    Expand4(value & 0x0F));
            }

            using Image<Rgba32> image = Image.LoadPixelData(pixels, width, height);
            image.SaveAsPng(outputFile);
        }

        private static byte[] LoadTexturePixels(byte[] data, string compression)
        {
            if (!String.Equals(compression, "avslz", StringComparison.OrdinalIgnoreCase) || data.Length < 8)
                return data;

            int uncompressedSize = ReadInt32S(data, 0);
            int compressedSize = ReadInt32S(data, 4);
            if (data.Length == compressedSize + 8)
            {
                byte[] compressed = new byte[compressedSize];
                Array.Copy(data, 8, compressed, 0, compressed.Length);
                byte[] result = DecompressLz77(compressed);
                if (result.Length != uncompressedSize)
                    throw new InvalidDataException("IFS texture decompressed to an unexpected size.");
                return result;
            }

            byte[] movedHeader = new byte[data.Length];
            Array.Copy(data, 8, movedHeader, 0, data.Length - 8);
            Array.Copy(data, 0, movedHeader, data.Length - 8, 8);
            return movedHeader;
        }

        private static byte[] DecompressLz77(byte[] input)
        {
            List<byte> output = new List<byte>();
            int offset = 0;
            while (offset < input.Length)
            {
                byte flags = input[offset++];
                for (int bit = 0; bit < 8 && offset < input.Length; bit++)
                {
                    if (((flags >> bit) & 1) != 0)
                    {
                        output.Add(input[offset++]);
                        continue;
                    }

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
            return output.ToArray();
        }

        private static int[] ReadIntList(string value)
        {
            return value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Int32.Parse)
                .ToArray();
        }

        private static string GetMd5Hex(string value)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
            StringBuilder result = new StringBuilder(hash.Length * 2);
            foreach (byte item in hash)
                result.Append(item.ToString("x2"));
            return result.ToString();
        }

        private static int ReadInt32S(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) |
                (data[offset + 2] << 8) | data[offset + 3];
        }

        private static byte Expand4(int value)
        {
            return (byte)((value << 4) | value);
        }
    }
}
