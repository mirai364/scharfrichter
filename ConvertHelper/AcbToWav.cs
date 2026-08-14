using Scharfrichter.Codec;
using Scharfrichter.Codec.ACB;
using Scharfrichter.Codec.Sounds.Encoders;
using Scharfrichter.Codec.Sounds.HCA;
using Scharfrichter.Common;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ConvertHelper
{
    /// <summary>
    /// Extracts CHUNITHM ACB/AWB audio into genre/title folders using Music.xml metadata.
    /// Output path: <OUTPUT>\<genre>\<title>\music.<codec>
    /// </summary>
    static public class AcbToWav
    {
        private const uint NewAudioEncryptionVersion = 0x01300000;

        /// <summary>
        /// HCA master keys used by CHUNITHM. The key changed with CHUNITHM NEW:
        /// older data (v1.xx, e.g. 1.50) used the SEGA key 0x00003657F27E3B22,
        /// while NEW / later versions use 0x0074FF1FCE264700 (vgmstream).
        /// </summary>
        private static readonly uint[] HcaMasterKey1 = { 0xf27e3b22, 0xce264700 };
        private static readonly uint[] HcaMasterKey2 = { 0x00003657, 0x0074ff1f };

        /// <summary>
        /// Holds song-level metadata read from Music.xml for audio extraction.
        /// </summary>
        private sealed class AudioMusicXmlInfo
        {
            public string Id;
            public string Title;
            public string Artist;
            public string Genre;
        }

        /// <summary>
        /// Extracts CHUNITHM ACB/AWB audio into genre/title folders using Music.xml metadata.
        /// Output path: <OUTPUT>\<genre>\<title>\music.<codec>
        /// </summary>
        static public void Convert(string[] inArgs)
        {
            Configuration config = Configuration.LoadIIDXConfig(Common.configFileName);
            ShowSplash();

            string[] args = inArgs != null ? inArgs : new string[0];
            string musicFolder = ResolveFolder(config["CHUNI"]["MusicFolder"]);
            string cueFileFolder = ResolveFolder(config["CHUNI"]["CueFileFolder"]);
            string outputFolder = ResolveFolder(GetOutputRoot(config));
            string soundFormat = SoundEncoderFactory.NormalizeFormat(config["CHUNI"].GetString("SoundOutputFormat", SoundEncoderFactory.DefaultFormat));

            if (string.IsNullOrEmpty(outputFolder))
            {
                outputFolder = "output";
            }

            // If arguments are provided, process only ACB/AWB files passed explicitly.
            // C2S/DDS-only invocations should not trigger folder-wide extraction.
            if (args != null && args.Length > 0)
            {
                bool anyProcessed = false;
                foreach (string file in args)
                {
                    if (!File.Exists(file))
                        continue;

                    string lower = file.ToLowerInvariant();
                    if (!lower.EndsWith(".acb") && !lower.EndsWith(".awb"))
                        continue;

                    anyProcessed = true;
                    ProcessSingleFile(file, musicFolder, outputFolder, soundFormat);
                }

                if (anyProcessed)
                {
                    Console.WriteLine("Done.");
                }
                return;
            }

            if (string.IsNullOrEmpty(musicFolder) || !Directory.Exists(musicFolder))
            {
                Console.Error.WriteLine("ERROR: MusicFolder is not set or does not exist in [CHUNI] config.");
                return;
            }

            if (string.IsNullOrEmpty(cueFileFolder) || !Directory.Exists(cueFileFolder))
            {
                Console.Error.WriteLine("ERROR: CueFileFolder is not set or does not exist in [CHUNI] config.");
                return;
            }

            Console.WriteLine("MusicFolder    : " + musicFolder);
            Console.WriteLine("CueFileFolder  : " + cueFileFolder);
            Console.WriteLine("Output         : " + outputFolder);
            Console.WriteLine("SoundFormat    : " + soundFormat);
            Console.WriteLine();

            ProcessMusicFolders(musicFolder, cueFileFolder, outputFolder, soundFormat);
        }

        /// <summary>
        /// Processes a single ACB/AWB file using the [CHUNI] configuration.
        /// </summary>
        private static void ProcessSingleFile(string acbFile, string musicFolder, string outputFolder, string soundFormat)
        {
            try
            {
                Console.WriteLine("Processing file: " + acbFile);

                string parentDir = Path.GetFileName(Path.GetDirectoryName(acbFile)) ?? "";
                string numberPart = ExtractNumericSuffix(parentDir);
                if (numberPart == null)
                {
                    numberPart = ExtractNumericSuffix(Path.GetFileNameWithoutExtension(acbFile));
                }

                if (numberPart == null)
                {
                    Console.WriteLine("  FAILED (cannot determine music number from path)");
                    return;
                }

                // If the configured music folder is empty (not set), fall back to
                // guessing the sibling "music" folder from the cueFile path layout:
                //   <data>\A000\cueFile\cueFile002891\music2891.acb  ->  <data>\A000\music
                if (string.IsNullOrEmpty(musicFolder) || !Directory.Exists(musicFolder))
                {
                    string guess = GuessMusicFolderFromCueFile(acbFile);
                    if (!string.IsNullOrEmpty(guess))
                        musicFolder = guess;
                }

                string musicDir = FindMusicFolder(musicFolder, numberPart);
                if (musicDir == null)
                {
                    Console.WriteLine("  FAILED (no matching music folder for number " + numberPart + ")");
                    return;
                }

                AudioMusicXmlInfo musicInfo = ReadMusicXml(Path.Combine(musicDir, "Music.xml"));
                string genreDir = Common.nameReplace(musicInfo.Genre);
                string titleDir = Common.nameReplace(musicInfo.Title);
                string targetDir = Path.Combine(outputFolder, genreDir, titleDir);
                Directory.CreateDirectory(targetDir);

                string ext = SoundEncoderFactory.GetFileExtension(soundFormat);
                string targetFile = Path.Combine(targetDir, "music." + ext);

                bool ok = ExtractAudioFile(acbFile, targetFile, soundFormat);

                if (ok)
                    Console.WriteLine("  -> " + targetFile);
                else
                    Console.WriteLine("  FAILED");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  FAILED: " + ex.Message);
            }
        }

        /// <summary>
        /// Guesses the music folder from a cueFile ACB path.
        /// </summary>
        private static string GuessMusicFolderFromCueFile(string acbFile)
        {
            try
            {
                string cueFileDir = Path.GetDirectoryName(acbFile);
                string cueFileRoot = Path.GetDirectoryName(cueFileDir);
                string baseDir = Path.GetDirectoryName(cueFileRoot);

                if (!string.IsNullOrEmpty(baseDir))
                {
                    string guess = Path.Combine(baseDir, "music");
                    if (Directory.Exists(guess))
                        return guess;
                }

                if (!string.IsNullOrEmpty(cueFileRoot))
                {
                    string siblingGuess = Path.Combine(Path.GetDirectoryName(cueFileRoot) ?? "", "music");
                    if (Directory.Exists(siblingGuess))
                        return siblingGuess;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string FindMusicFolder(string musicFolder, string numberPart)
        {
            if (string.IsNullOrEmpty(musicFolder) || !Directory.Exists(musicFolder))
                return null;

            string trimmed = numberPart.TrimStart('0');
            if (trimmed.Length == 0) trimmed = "0";
            if (!int.TryParse(trimmed, out int number))
                return null;

            string[] musicDirs = Directory.GetDirectories(musicFolder, "music*", SearchOption.TopDirectoryOnly);
            foreach (string dir in musicDirs)
            {
                string dirName = Path.GetFileName(dir);
                string dirNumber = ExtractNumericSuffix(dirName);
                if (dirNumber == null)
                    continue;

                string dirTrimmed = dirNumber.TrimStart('0');
                if (dirTrimmed.Length == 0) dirTrimmed = "0";
                if (int.TryParse(dirTrimmed, out int dirNumberInt) && dirNumberInt == number)
                    return dir;
            }

            return null;
        }

        private static void ProcessMusicFolders(string musicFolder, string cueFileFolder, string outputFolder, string soundFormat)
        {
            string[] musicDirs;
            try
            {
                musicDirs = Directory.GetDirectories(musicFolder, "music*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: Failed to enumerate music folders: " + ex.Message);
                return;
            }

            Array.Sort(musicDirs);
            int success = 0;
            int failed = 0;

            foreach (string musicDir in musicDirs)
            {
                string dirName = Path.GetFileName(musicDir);
                string musicXmlPath = Path.Combine(musicDir, "Music.xml");
                if (!File.Exists(musicXmlPath))
                {
                    Console.WriteLine("Skipped (no Music.xml): " + dirName);
                    continue;
                }

                try
                {
                    AudioMusicXmlInfo musicInfo = ReadMusicXml(musicXmlPath);
                    string cueFile = FindCueFile(cueFileFolder, dirName);

                    if (cueFile == null)
                    {
                        Console.WriteLine("Skipped (no cueFile found): " + dirName);
                        failed++;
                        continue;
                    }

                    string genreDir = Common.nameReplace(musicInfo.Genre);
                    string titleDir = Common.nameReplace(musicInfo.Title);
                    string targetDir = Path.Combine(outputFolder, genreDir, titleDir);
                    Directory.CreateDirectory(targetDir);

                    string ext = SoundEncoderFactory.GetFileExtension(soundFormat);
                    string targetFile = Path.Combine(targetDir, "music." + ext);

                    Console.WriteLine("Processing: " + dirName + " -> " + musicInfo.Title + " [" + musicInfo.Genre + "]");
                    bool ok = ExtractAudioFile(cueFile, targetFile, soundFormat);

                    if (ok)
                    {
                        Console.WriteLine("  -> " + targetFile);
                        success++;
                    }
                    else
                    {
                        Console.WriteLine("  FAILED");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERROR in " + dirName + ": " + ex.Message);
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine("Done. Extracted: " + success + ", Failed: " + failed);
        }

        private static AudioMusicXmlInfo ReadMusicXml(string musicXmlPath)
        {
            XElement musicXml = XElement.Load(musicXmlPath);
            return new AudioMusicXmlInfo
            {
                Id = musicXml.Element("name")?.Element("id")?.Value ?? "",
                Title = musicXml.Element("name")?.Element("str")?.Value ?? "",
                Artist = musicXml.Element("artistName")?.Element("str")?.Value ?? "",
                Genre = musicXml.Element("genreNames")?.Element("list")?.Element("StringID")?.Element("str")?.Value ?? ""
            };
        }

        private static string FindCueFile(string cueFileFolder, string musicDirName)
        {
            string numberPart = ExtractNumericSuffix(musicDirName);
            if (numberPart == null)
                return null;

            string numberValue = numberPart.TrimStart('0');
            if (numberValue.Length == 0)
                numberValue = "0";
            int number;
            if (!int.TryParse(numberValue, out number))
                return null;

            // 1) Search matching subfolder
            if (Directory.Exists(cueFileFolder))
            {
                string[] subDirs = Directory.GetDirectories(cueFileFolder, "*", SearchOption.TopDirectoryOnly);
                foreach (string subDir in subDirs)
                {
                    string dirName = Path.GetFileName(subDir);
                    string dirNumberPart = ExtractNumericSuffix(dirName);
                    if (dirNumberPart == null)
                        continue;

                    string dirTrimmed = dirNumberPart.TrimStart('0');
                    if (dirTrimmed.Length == 0) dirTrimmed = "0";
                    if (int.TryParse(dirTrimmed, out int dirNumberInt) && dirNumberInt == number)
                    {
                        string[] files = Directory.GetFiles(subDir, "*", SearchOption.TopDirectoryOnly);
                        foreach (string file in files)
                        {
                            string lower = file.ToLowerInvariant();
                            if (lower.EndsWith(".acb") || lower.EndsWith(".awb"))
                                return file;
                        }
                        if (files.Length > 0)
                            return files[0];
                    }
                }
            }

            // 2) Direct file candidates
            string[] candidates = {
                "cueFile" + number.ToString("D6"),
                "cuefile" + number.ToString("D6"),
                "cueFile" + number.ToString("D4"),
                "cuefile" + number.ToString("D4"),
                "cueFile" + numberPart,
                "cuefile" + numberPart,
                numberPart
            };

            foreach (string candidate in candidates)
            {
                string candidatePath = Path.Combine(cueFileFolder, candidate);
                if (File.Exists(candidatePath))
                    return candidatePath;

                string[] files = Directory.GetFiles(cueFileFolder, candidate + ".*", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                    return files[0];
            }

            // 3) Fallback: scan all files for numeric match
            string[] allFiles = Directory.GetFiles(cueFileFolder, "*", SearchOption.TopDirectoryOnly);
            foreach (string file in allFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string fileNumber = ExtractNumericSuffix(fileName);
                if (fileNumber != null)
                {
                    string fileTrimmed = fileNumber.TrimStart('0');
                    if (fileTrimmed.Length == 0) fileTrimmed = "0";
                    int fileNumberInt;
                    if (int.TryParse(fileTrimmed, out fileNumberInt) && fileNumberInt == number)
                        return file;
                }
            }

            return null;
        }

        private static string ExtractNumericSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            int i = name.Length - 1;
            while (i >= 0 && char.IsDigit(name[i]))
                i--;

            if (i == name.Length - 1)
                return null;

            return name.Substring(i + 1);
        }

        private static bool ExtractAudioFile(string cueFile, string targetFile, string soundFormat)
        {
            string lowerFile = cueFile.ToLowerInvariant();

            if (lowerFile.EndsWith(".awb"))
                return ExtractFromAwb(cueFile, targetFile, soundFormat);

            if (lowerFile.EndsWith(".acb"))
                return ExtractFromAcb(cueFile, targetFile, soundFormat);

            // Detect by magic
            using (FileStream fs = File.OpenRead(cueFile))
            {
                byte[] magic = new byte[4];
                int read = fs.Read(magic, 0, 4);
                if (read >= 4 && magic[0] == 'A' && magic[1] == 'F' && magic[2] == 'S' && magic[3] == '2')
                    return ExtractFromAwb(cueFile, targetFile, soundFormat);
                if (read >= 4 && magic[0] == '@' && magic[1] == 'U' && magic[2] == 'T' && magic[3] == 'F')
                    return ExtractFromAcb(cueFile, targetFile, soundFormat);
            }

            string dir = Path.GetDirectoryName(cueFile) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(cueFile);
            string[] acbFiles = Directory.GetFiles(dir, baseName + ".acb", SearchOption.TopDirectoryOnly);
            if (acbFiles.Length > 0)
                return ExtractFromAcb(acbFiles[0], targetFile, soundFormat);

            string[] awbFiles = Directory.GetFiles(dir, baseName + ".awb", SearchOption.TopDirectoryOnly);
            if (awbFiles.Length > 0)
                return ExtractFromAwb(awbFiles[0], targetFile, soundFormat);

            return false;
        }

        private static bool ExtractFromAcb(string acbFile, string targetFile, string soundFormat)
        {
            using (AcbFile acb = AcbFile.FromFile(acbFile))
            {
                if (acb.InternalAwb != null && acb.InternalAwb.Files.Count > 0)
                {
                    int firstCueId = acb.InternalAwb.Files.Keys.OrderBy(k => k).First();
                    using (MemoryStream dataStream = ExtractToNewStream(acb.Stream, acb.InternalAwb.Files[firstCueId].FileOffsetAligned, (int)acb.InternalAwb.Files[firstCueId].FileLength))
                    {
                        return DecodeToFile(dataStream, targetFile, soundFormat, acb.FormatVersion, acb.InternalAwb.HcaKeyModifier);
                    }
                }

                if (acb.ExternalAwb != null && acb.ExternalAwb.Files.Count > 0)
                {
                    int firstCueId = acb.ExternalAwb.Files.Keys.OrderBy(k => k).First();
                    using (FileStream fs = File.Open(acb.ExternalAwb.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (MemoryStream dataStream = ExtractToNewStream(fs, acb.ExternalAwb.Files[firstCueId].FileOffsetAligned, (int)acb.ExternalAwb.Files[firstCueId].FileLength))
                    {
                        return DecodeToFile(dataStream, targetFile, soundFormat, acb.FormatVersion, acb.ExternalAwb.HcaKeyModifier);
                    }
                }

                return false;
            }
        }

        private static bool ExtractFromAwb(string awbFile, string targetFile, string soundFormat)
        {
            using (FileStream fs = File.Open(awbFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Afs2Archive archive = new Afs2Archive(fs, 0, fs.Name, false);
                archive.Initialize();

                if (archive.Files.Count == 0)
                    return false;

                int firstCueId = archive.Files.Keys.OrderBy(k => k).First();
                using (MemoryStream dataStream = ExtractToNewStream(fs, archive.Files[firstCueId].FileOffsetAligned, (int)archive.Files[firstCueId].FileLength))
                {
                    return DecodeToFile(dataStream, targetFile, soundFormat, 0, archive.HcaKeyModifier);
                }
            }
        }

        private static bool DecodeToFile(Stream hcaDataStream, string targetFile, string soundFormat, uint acbFormatVersion, ushort hcaKeyModifier)
        {
            if (!HcaReader.IsHcaStream(hcaDataStream))
                return false;

            // CHUNITHM changed its HCA key with NEW: try both the legacy SEGA
            // key (v1.xx) and the NEW key. The key modifier from the AWB is
            // applied when the ACB uses the new audio encryption.
            ushort keyMod = acbFormatVersion >= NewAudioEncryptionVersion ? hcaKeyModifier : (ushort)0;

            for (int k = 0; k < HcaMasterKey1.Length; k++)
            {
                hcaDataStream.Seek(0, SeekOrigin.Begin);
                DecodeParams decodeParams = DecodeParams.CreateDefault(HcaMasterKey1[k], HcaMasterKey2[k], keyMod);

                try
                {
                    using (FileStream fs = File.Open(targetFile, FileMode.Create, FileAccess.Write, FileShare.Write))
                    using (var hcaStream = new OneWayHcaAudioStream(hcaDataStream, decodeParams, true))
                    {
                        byte[] buffer = new byte[10240];
                        int read = 1;

                        while (read > 0)
                        {
                            read = hcaStream.Read(buffer, 0, buffer.Length);
                            if (read > 0)
                                fs.Write(buffer, 0, read);
                        }
                    }

                    string normalized = SoundEncoderFactory.NormalizeFormat(soundFormat);
                    if (normalized != "wav" && normalized != "lpcm")
                    {
                        byte[] wavBytes = File.ReadAllBytes(targetFile);
                        Scharfrichter.Codec.Sounds.Sound sound = Scharfrichter.Codec.Sounds.Sound.Read(new MemoryStream(wavBytes));
                        ISoundEncoder encoder = SoundEncoderFactory.Create(normalized);
                        encoder.EncodeToFile(sound, targetFile, 1.0f);
                    }

                    return true;
                }
                catch
                {
                    // Try the next key.
                }
            }

            return false;
        }

        private static MemoryStream ExtractToNewStream(Stream stream, long offset, int length)
        {
            long originalPosition = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            byte[] memory = new byte[length];
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = stream.Read(memory, totalRead, length - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }
            stream.Position = originalPosition;
            Array.Resize(ref memory, totalRead);
            return new MemoryStream(memory, false);
        }

        private static string GetOutputRoot(Configuration config)
        {
            string chuniOutput = config["CHUNI"]["Output"];
            if (!string.IsNullOrEmpty(chuniOutput))
                return chuniOutput;
            return config["BMS"]["Output"];
        }

        private static string ResolveFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            path = Environment.ExpandEnvironmentVariables(path);
            path = path.Replace("\\\\", "\\");
            path = path.Replace("//", "/");
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(path);

            return path;
        }

        private static void ShowSplash()
        {
            Splash.Show("Chuni to UGC Audio Script");
        }
    }
}