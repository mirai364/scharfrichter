using Scharfrichter.Codec.ACB;
using Scharfrichter.Codec.Sounds.Encoders;
using Scharfrichter.Codec.Sounds.HCA;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AcbToWav
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string inputFile = null;
            string outputDir = null;
            string format = "wav";
            uint key1 = 0xf27e3b22; // DereTore CgssCipher.Key1
            uint key2 = 0x00003657; // DereTore CgssCipher.Key2
            bool keepRaw = true;
            bool includeCueIds = true;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "-o":
                    case "--output":
                        if (i + 1 < args.Length) outputDir = args[++i];
                        break;
                    case "-f":
                    case "--format":
                        if (i + 1 < args.Length) format = args[++i];
                        break;
                    case "-k1":
                    case "--key1":
                        if (i + 1 < args.Length)
                        {
                            string keyStr = args[++i].Replace("0x", "").Replace("0X", "");
                            uint.TryParse(keyStr, System.Globalization.NumberStyles.HexNumber, null, out key1);
                        }
                        break;
                    case "-k2":
                    case "--key2":
                        if (i + 1 < args.Length)
                        {
                            string keyStr = args[++i].Replace("0x", "").Replace("0X", "");
                            uint.TryParse(keyStr, System.Globalization.NumberStyles.HexNumber, null, out key2);
                        }
                        break;
                    case "--no-raw":
                        keepRaw = false;
                        break;
                    case "--no-cueid":
                        includeCueIds = false;
                        break;
                    case "-h":
                    case "--help":
                        PrintUsage();
                        return 0;
                    default:
                        if (inputFile == null && !arg.StartsWith("-"))
                        {
                            inputFile = arg;
                        }
                        break;
                }
            }

            if (inputFile == null)
            {
                PrintUsage();
                return -1;
            }

            if (!File.Exists(inputFile))
            {
                Console.Error.WriteLine($"File not found: {inputFile}");
                return -1;
            }

            string normalizedFormat = SoundEncoderFactory.NormalizeFormat(format);
            string ext = SoundEncoderFactory.GetFileExtension(normalizedFormat);

            if (outputDir == null)
            {
                FileInfo fi = new FileInfo(inputFile);
                outputDir = Path.Combine(fi.DirectoryName ?? ".", $"_acb_{fi.Name}");
            }

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            try
            {
                using (AcbFile acb = AcbFile.FromFile(inputFile))
                {
                    uint formatVersion = acb.FormatVersion;
                    Console.WriteLine($"ACB format version: 0x{formatVersion:X8}");
                    Console.WriteLine($"Cue count: {acb.Cues?.Length ?? 0}");

                    if (acb.InternalAwb != null)
                    {
                        string internalDir = Path.Combine(outputDir, "internal");
                        ProcessAllBinaries(formatVersion, internalDir, acb.InternalAwb, acb.Stream, true, key1, key2, normalizedFormat, ext, keepRaw, includeCueIds);
                    }

                    if (acb.ExternalAwb != null)
                    {
                        string externalDir = Path.Combine(outputDir, "external");
                        using (FileStream fs = File.Open(acb.ExternalAwb.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            ProcessAllBinaries(formatVersion, externalDir, acb.ExternalAwb, fs, false, key1, key2, normalizedFormat, ext, keepRaw, includeCueIds);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                return -1;
            }

            Console.WriteLine("Done.");
            return 0;
        }

        private static void ProcessAllBinaries(uint acbFormatVersion, string extractDir, Afs2Archive archive, Stream dataStream, bool isInternal,
            uint key1, uint key2, string format, string ext, bool keepRaw, bool includeCueIds)
        {
            if (!Directory.Exists(extractDir))
            {
                Directory.CreateDirectory(extractDir);
            }

            string afsSource = isInternal ? "internal" : "external";
            DecodeParams decodeParams = DecodeParams.CreateDefault(key1, key2);

            if (acbFormatVersion >= NewEncryptionVersion)
            {
                decodeParams.KeyModifier = archive.HcaKeyModifier;
            }
            else
            {
                decodeParams.KeyModifier = 0;
            }

            foreach (KeyValuePair<int, Afs2FileRecord> entry in archive.Files)
            {
                Afs2FileRecord record = entry.Value;
                string fileName = AcbFile.GetSymbolicFileNameFromCueId(record.CueId);

                if (includeCueIds)
                {
                    fileName = $"{record.CueId:D5}_" + fileName;
                }

                fileName = ReplaceExtension(fileName, ".bin", "." + ext);

                string extractFilePath = Path.Combine(extractDir, fileName);

                using (MemoryStream fileData = ExtractToNewStream(dataStream, record.FileOffsetAligned, (int)record.FileLength))
                {
                    bool isHcaStream = HcaReader.IsHcaStream(fileData);

                    Console.Write($"Processing {afsSource} AFS: #{record.CueId} (offset={record.FileOffsetAligned} size={record.FileLength})...   ");

                    if (isHcaStream)
                    {
                        try
                        {
                            DecodeAndEncodeHca(fileData, extractFilePath, decodeParams, format, ext);
                            Console.WriteLine($"decoded ({format})");
                        }
                        catch (Exception ex)
                        {
                            if (File.Exists(extractFilePath))
                            {
                                File.Delete(extractFilePath);
                            }

                            Console.WriteLine(ex.Message);

                            if (keepRaw)
                            {
                                fileData.Position = 0;
                                string rawPath = ReplaceExtension(extractFilePath, "." + ext, ".hca");
                                File.WriteAllBytes(rawPath, fileData.ToArray());
                                Console.WriteLine($"  saved raw HCA: {Path.GetFileName(rawPath)}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("skipped (not HCA)");
                        if (keepRaw)
                        {
                            fileData.Position = 0;
                            string rawPath = ReplaceExtension(extractFilePath, "." + ext, ".bin");
                            File.WriteAllBytes(rawPath, fileData.ToArray());
                            Console.WriteLine($"  saved raw: {Path.GetFileName(rawPath)}");
                        }
                    }
                }
            }
        }

        private static void DecodeAndEncodeHca(Stream hcaDataStream, string outputFilePath, DecodeParams decodeParams, string format, string ext)
        {
            string tempWavPath = null;

            try
            {
                if (format == "wav" || format == "lpcm")
                {
                    // Direct WAV output from HCA decoder (includes RIFF header)
                    using (FileStream fs = File.Open(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Write))
                    using (var hcaStream = new OneWayHcaAudioStream(hcaDataStream, decodeParams, true))
                    {
                        byte[] buffer = new byte[10240];
                        int read = 1;

                        while (read > 0)
                        {
                            read = hcaStream.Read(buffer, 0, buffer.Length);

                            if (read > 0)
                            {
                                fs.Write(buffer, 0, read);
                            }
                        }
                    }
                    return;
                }

                // For ogg/flac/mp3: decode HCA to WAV bytes first, then encode via SoundEncoderFactory
                byte[] wavBytes;
                using (MemoryStream wavMem = new MemoryStream())
                {
                    using (var hcaStream = new OneWayHcaAudioStream(hcaDataStream, decodeParams, true))
                    {
                        byte[] buffer = new byte[10240];
                        int read = 1;

                        while (read > 0)
                        {
                            read = hcaStream.Read(buffer, 0, buffer.Length);

                            if (read > 0)
                            {
                                wavMem.Write(buffer, 0, read);
                            }
                        }
                    }
                    wavBytes = wavMem.ToArray();
                }

                // Use the existing encoder infrastructure
                Scharfrichter.Codec.Sounds.Sound sound = Scharfrichter.Codec.Sounds.Sound.Read(new MemoryStream(wavBytes));
                ISoundEncoder encoder = SoundEncoderFactory.Create(format);
                encoder.EncodeToFile(sound, outputFilePath, 1.0f);
            }
            catch
            {
                if (tempWavPath != null && File.Exists(tempWavPath))
                {
                    File.Delete(tempWavPath);
                }
                throw;
            }
        }

        private static string ReplaceExtension(string str, string oldExt, string newExt)
        {
            if (str == null || oldExt == null || newExt == null)
            {
                throw new ArgumentNullException();
            }

            if (str.Length < oldExt.Length)
            {
                return str;
            }

            if (str.Substring(str.Length - oldExt.Length).ToLowerInvariant() != oldExt.ToLowerInvariant())
            {
                return str;
            }

            return str.Substring(0, str.Length - oldExt.Length) + newExt;
        }

        private static MemoryStream ExtractToNewStream(Stream stream, long offset, int length)
        {
            long originalPosition = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            byte[] buffer = new byte[length];
            byte[] memory = new byte[length];
            long currentIndex = 0;
            int bytesLeft = length;
            do
            {
                int read = stream.Read(buffer, 0, bytesLeft);
                Array.Copy(buffer, 0, memory, currentIndex, read);
                currentIndex += read;
                bytesLeft -= read;
            } while (bytesLeft > 0 && currentIndex < length);
            stream.Position = originalPosition;
            MemoryStream memoryStream = new MemoryStream(memory, false)
            {
                Capacity = length
            };
            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("AcbToWav - Extract audio from CRI ACB/AWB files");
            Console.WriteLine();
            Console.WriteLine("Usage: AcbToWav <input ACB> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -o, --output <dir>   Output directory (default: _acb_<filename> next to input)");
            Console.WriteLine("  -f, --format <fmt>   Output format: wav, lpcm, ogg, flac, mp3 (default: wav)");
            Console.WriteLine("  -k1, --key1 <hex>    HCA key 1 (default: f27e3b22)");
            Console.WriteLine("  -k2, --key2 <hex>    HCA key 2 (default: 00003657)");
            Console.WriteLine("      --no-raw         Do not save raw .hca / .bin fallback files for non-HCA/un-decodable entries");
            Console.WriteLine("      --no-cueid       Do not prefix file names with cue IDs");
            Console.WriteLine("  -h, --help           Show this help");
        }

        private const uint NewEncryptionVersion = 0x01300000;
    }
}