using Scharfrichter.Common;
using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace IIDXDBGenerator
{
    class Program
    {
        private static class Constants
        {
            public const int MagicIIDX = 0x58444949;
            public const int RecordSizeV32 = 2040;
            public const int RecordSizeBelow32 = 1324;
            public const int RecordSizeBelow27 = 800;
            public const int RecordSizeBelow20 = 828;

            public const int OverlayCount = 9;
            public const int OverlayNameLength = 32;
            public const int MovieNameLength = 32;

            public static readonly Encoding Enc932 = Encoding.GetEncoding(932);
            public static readonly Encoding EncUtf16 = Encoding.Unicode;
        }

        private sealed class MusicRecord
        {
            public int SongID;
            public int Volume;
            public int VideoDelay;
            public int OverlayFlags;
            public Encoding TextEncoding;
            public byte[] RawTitle;
            public byte[] RawEnglishTitle;
            public byte[] RawGenre;
            public byte[] RawArtist;
            public byte[] RawMovie;
            public byte[][] Overlays;
            public int TextureTitle;
            public int TextureArtist;
            public int TextureGenre;
            public int TextureLoad;
            public int TextureList;
            public int EntryFont;
            public int Folder;
            public int OtherFolder;
            public int BemaniFolder;
            public int SplittableDiff;
        }

        private sealed class MusicDataHeader
        {
            public int Version;
            public int MetaCount;
            public int EntryCount;
        }

        private static string GetString(byte[] source, Encoding encoding)
        {
            if (source == null || source.Length == 0 || source[0] == 0)
                return string.Empty;

            if (encoding == Constants.EncUtf16)
                return encoding.GetString(source).Replace("\0", "");

            int nullIndex = Array.IndexOf(source, (byte)0);
            int length = nullIndex >= 0 ? nullIndex : source.Length;
            return encoding.GetString(source, 0, length).Replace("\0", "");
        }

        private static void SkipBytes(BinaryReader reader, int count)
        {
            reader.BaseStream.Position += count;
        }

        private static byte[][] ReadOverlayNames(BinaryReader reader)
        {
            byte[][] overlays = new byte[Constants.OverlayCount][];
            for (int i = 0; i < overlays.Length; i++)
                overlays[i] = reader.ReadBytes(Constants.OverlayNameLength);
            return overlays;
        }

        private static void MoveToNextRecord(BinaryReader reader, long recordStart, int recordSize)
        {
            reader.BaseStream.Position = recordStart + recordSize;
        }

        private static void ReadTextureFlags(BinaryReader reader, MusicRecord record)
        {
            record.TextureTitle = reader.ReadInt32();
            record.TextureArtist = reader.ReadInt32();
            record.TextureGenre = reader.ReadInt32();
            record.TextureLoad = reader.ReadInt32();
            record.TextureList = reader.ReadInt32();
        }

        private static void SetStringIfPresent(InfoCollection section, string key, byte[] rawValue, Encoding encoding)
        {
            if (rawValue != null && rawValue.Length > 0 && rawValue[0] != 0)
                section[key] = GetString(rawValue, encoding);
        }

        private static void SetOverlayData(InfoCollection section, int overlayFlags, byte[][] overlays)
        {
            if (overlayFlags == 0)
                return;

            for (int i = 0; i < overlays.Length; i++)
            {
                if (overlays[i].Length > 0 && overlays[i][0] != 0)
                    section[$"OVERLAY{i}"] = GetString(overlays[i], Constants.Enc932);
            }
        }

        private static void WriteCommonFields(Configuration config, MusicRecord record)
        {
            InfoCollection section = config[record.SongID.ToString()];

            SetStringIfPresent(section, "TITLE", record.RawTitle, record.TextEncoding);
            SetStringIfPresent(section, "ENGLISHTITLE", record.RawEnglishTitle, Constants.Enc932);
            SetStringIfPresent(section, "ARTIST", record.RawArtist, record.TextEncoding);
            SetStringIfPresent(section, "GENRE", record.RawGenre, record.TextEncoding);
            SetStringIfPresent(section, "VIDEO", record.RawMovie, Constants.Enc932);

            SetOptionalValue(section, "TEXTURETITLE", record.TextureTitle);
            SetOptionalValue(section, "TEXTUREARTIST", record.TextureArtist);
            SetOptionalValue(section, "TEXTUREGENRE", record.TextureGenre);
            SetOptionalValue(section, "TEXTURELOAD", record.TextureLoad);
            SetOptionalValue(section, "TEXTURELIST", record.TextureList);
            SetOptionalValue(section, "ENTRYFONT", record.EntryFont);
            SetOptionalValue(section, "FOLDER", record.Folder);
            SetOptionalValue(section, "OTHERFOLDER", record.OtherFolder);
            SetOptionalValue(section, "BEMANIFOLDER", record.BemaniFolder);
            SetOptionalValue(section, "SPLITTABLEDIFF", record.SplittableDiff);

            section["VIDEODELAY"] = record.VideoDelay.ToString();
            if (record.Volume > 0)
                section["VOLUME"] = record.Volume.ToString();

            SetOverlayData(section, record.OverlayFlags, record.Overlays);
        }

        private static void WriteCurrentDifficultyFields(Configuration config, int songID, byte[] values)
        {
            InfoCollection section = config[songID.ToString()];

            SetValue(section, "DIFFICULTYSP0", values[0]);
            SetValue(section, "DIFFICULTYSP1", values[0]);
            SetValue(section, "DIFFICULTYSP2", values[1]);
            SetValue(section, "DIFFICULTYSP3", values[2]);
            SetValue(section, "DIFFICULTYSP4", values[3]);
            SetValue(section, "DIFFICULTYSP5", values[4]);

            SetValue(section, "DIFFICULTYDP0", values[5]);
            SetValue(section, "DIFFICULTYDP1", values[5]);
            SetValue(section, "DIFFICULTYDP2", values[6]);
            SetValue(section, "DIFFICULTYDP3", values[7]);
            SetValue(section, "DIFFICULTYDP4", values[8]);
            SetValue(section, "DIFFICULTYDP5", values[9]);
        }

        private static void WriteCurrentKeysetFields(Configuration config, int songID, byte[] values)
        {
            InfoCollection section = config[songID.ToString()];

            SetChar(section, "KEYSETSP0", values[0]);
            SetChar(section, "KEYSETSP1", values[0]);
            SetChar(section, "KEYSETSP2", values[1]);
            SetChar(section, "KEYSETSP3", values[2]);
            SetChar(section, "KEYSETSP4", values[3]);
            SetChar(section, "KEYSETSP5", values[4]);

            SetChar(section, "KEYSETDP0", values[5]);
            SetChar(section, "KEYSETDP1", values[5]);
            SetChar(section, "KEYSETDP2", values[6]);
            SetChar(section, "KEYSETDP3", values[7]);
            SetChar(section, "KEYSETDP4", values[8]);
            SetChar(section, "KEYSETDP5", values[9]);
        }

        private static void WriteLegacyDifficultyFields(Configuration config, int songID, byte[] values)
        {
            InfoCollection section = config[songID.ToString()];

            SetValue(section, "DIFFICULTYSP0", values[6]);
            SetValue(section, "DIFFICULTYSP1", values[6]);
            if (values[0] > 0)
            {
                if (values[6] <= 0)
                    section["DIFFICULTYSP1"] = values[0].ToString();
                section["DIFFICULTYSP2"] = values[0].ToString();
            }
            SetValue(section, "DIFFICULTYSP3", values[1]);
            SetValue(section, "DIFFICULTYSP4", values[2]);

            SetValue(section, "DIFFICULTYDP0", values[7]);
            SetValue(section, "DIFFICULTYDP1", values[7]);
            if (values[3] > 0)
            {
                if (values[7] <= 0)
                    section["DIFFICULTYDP1"] = values[3].ToString();
                section["DIFFICULTYDP2"] = values[3].ToString();
            }
            SetValue(section, "DIFFICULTYDP3", values[4]);
            SetValue(section, "DIFFICULTYDP4", values[5]);
        }

        private static void WriteLegacyKeysetFields(Configuration config, int songID, byte[] values)
        {
            InfoCollection section = config[songID.ToString()];

            SetChar(section, "KEYSETSP0", values[6]);
            SetChar(section, "KEYSETSP1", values[6]);
            if (values[0] > 0)
            {
                if (values[6] <= 0)
                    section["KEYSETSP1"] = ((char)values[0]).ToString();
                section["KEYSETSP2"] = ((char)values[0]).ToString();
            }
            SetChar(section, "KEYSETSP3", values[1]);
            SetChar(section, "KEYSETSP4", values[2]);

            SetChar(section, "KEYSETDP0", values[7]);
            SetChar(section, "KEYSETDP1", values[7]);
            if (values[3] > 0)
            {
                if (values[7] <= 0)
                    section["KEYSETDP1"] = ((char)values[3]).ToString();
                section["KEYSETDP2"] = ((char)values[3]).ToString();
            }
            SetChar(section, "KEYSETDP3", values[4]);
            SetChar(section, "KEYSETDP4", values[5]);
        }

        private static void SetOptionalValue(InfoCollection section, string key, int value)
        {
            if (value > 0)
                section[key] = value.ToString();
        }

        private static void SetValue(InfoCollection section, string key, byte value)
        {
            if (value > 0)
                section[key] = value.ToString();
        }

        private static void SetChar(InfoCollection section, string key, byte value)
        {
            if (value > 0)
                section[key] = ((char)value).ToString();
        }

        private static void SetPlayVideoFlags(Configuration config, string musicDataFile)
        {
            string videoListFile = Path.Combine(Path.GetDirectoryName(musicDataFile), "video_music_list.xml");
            if (!File.Exists(videoListFile))
                return;

            XDocument document = XDocument.Load(videoListFile);
            foreach (XElement music in document.Descendants("music"))
            {
                string id = (string)music.Attribute("id");
                XElement flags = music.Element("info")?.Element("play_video_flags");
                if (!String.IsNullOrWhiteSpace(id) && flags != null)
                    config[id]["PLAYVIDEOFLAGS"] = flags.Value.Trim();
            }
        }

        private static Configuration ConvertV32(BinaryReader reader, Configuration result, int metaCount)
        {
            for (int i = 0; i < metaCount; i++)
            {
                long startPos = reader.BaseStream.Position;

                MusicRecord record = new MusicRecord();
                record.TextEncoding = Constants.EncUtf16;
                record.RawTitle = reader.ReadBytes(256);
                record.RawEnglishTitle = reader.ReadBytes(64);
                record.RawGenre = reader.ReadBytes(128);
                record.RawArtist = reader.ReadBytes(256);
                SkipBytes(reader, 256); // rights/licensing memo, currently unused
                ReadTextureFlags(reader, record);
                SkipBytes(reader, 4); // v32-only rights/licensing type, currently unused
                record.EntryFont = reader.ReadInt32();
                record.Folder = reader.ReadByte();
                SkipBytes(reader, 1); // padding
                record.OtherFolder = reader.ReadUInt16();
                record.BemaniFolder = reader.ReadUInt16();
                SkipBytes(reader, 6); // v32-only reserved/unknown metadata flags
                record.SplittableDiff = reader.ReadUInt16();
                SkipBytes(reader, 2); // padding

                byte[] difficulties = reader.ReadBytes(10); // SP BEGINNER..LEGGENDARIA, DP BEGINNER..LEGGENDARIA
                SkipBytes(reader, 646); // zero-filled reserved chart metadata block before IDs

                record.SongID = reader.ReadUInt16();
                SkipBytes(reader, 2); // version, afp_flag
                record.Volume = reader.ReadInt32();

                byte[] keysets = reader.ReadBytes(10);
                record.VideoDelay = reader.ReadInt16();
                record.RawMovie = reader.ReadBytes(Constants.MovieNameLength);
                record.OverlayFlags = reader.ReadInt32();
                record.Overlays = ReadOverlayNames(reader);

                MoveToNextRecord(reader, startPos, Constants.RecordSizeV32);

                WriteCommonFields(result, record);
                WriteCurrentDifficultyFields(result, record.SongID, difficulties);
                WriteCurrentKeysetFields(result, record.SongID, keysets);
            }

            return result;
        }

        private static Configuration ConvertBelow32(BinaryReader reader, Configuration result, int metaCount)
        {
            for (int i = 0; i < metaCount; i++)
            {
                long startPos = reader.BaseStream.Position;

                MusicRecord record = new MusicRecord();
                record.TextEncoding = Constants.Enc932;
                record.RawTitle = reader.ReadBytes(64);
                record.RawEnglishTitle = reader.ReadBytes(64);
                record.RawGenre = reader.ReadBytes(64);
                record.RawArtist = reader.ReadBytes(64);
                ReadTextureFlags(reader, record);
                record.EntryFont = reader.ReadInt32();
                record.Folder = reader.ReadByte();
                SkipBytes(reader, 1); // padding
                record.OtherFolder = reader.ReadUInt16();
                record.BemaniFolder = reader.ReadUInt16();
                record.SplittableDiff = reader.ReadUInt16();

                byte[] difficulties = reader.ReadBytes(10);
                SkipBytes(reader, 646); // reserved chart metadata block with fixed legacy markers

                record.SongID = reader.ReadInt16();
                SkipBytes(reader, 2); // version, afp_flag
                record.Volume = reader.ReadInt32();

                byte[] keysets = reader.ReadBytes(10);
                record.VideoDelay = reader.ReadInt16();
                record.RawMovie = reader.ReadBytes(Constants.MovieNameLength);
                record.OverlayFlags = reader.ReadInt32();
                record.Overlays = ReadOverlayNames(reader);

                MoveToNextRecord(reader, startPos, Constants.RecordSizeBelow32);

                WriteCommonFields(result, record);
                WriteCurrentDifficultyFields(result, record.SongID, difficulties);
                WriteCurrentKeysetFields(result, record.SongID, keysets);
            }

            return result;
        }

        private static Configuration ConvertBelow27(BinaryReader reader, Configuration result, int metaCount, int musicDataVersion)
        {
            int recordStride = GetBelow27RecordStride(musicDataVersion);

            for (int i = 0; i < metaCount; i++)
            {
                long startPos = reader.BaseStream.Position;

                MusicRecord record = new MusicRecord();
                record.TextEncoding = Constants.Enc932;
                record.RawTitle = reader.ReadBytes(64);
                record.RawEnglishTitle = reader.ReadBytes(64);
                record.RawGenre = reader.ReadBytes(64);
                record.RawArtist = reader.ReadBytes(64);
                ReadTextureFlags(reader, record);
                record.EntryFont = reader.ReadInt32();
                record.Folder = reader.ReadByte();
                SkipBytes(reader, 1); // padding
                record.OtherFolder = reader.ReadUInt16();
                record.BemaniFolder = reader.ReadUInt16();
                record.SplittableDiff = reader.ReadUInt16();

                byte[] difficulties = reader.ReadBytes(8);
                SkipBytes(reader, 160); // zero-filled reserved chart metadata block before IDs

                record.SongID = reader.ReadInt16();
                SkipBytes(reader, 2); // version, afp_flag
                record.Volume = reader.ReadInt32();

                byte[] keysets = reader.ReadBytes(8);
                record.VideoDelay = reader.ReadInt16();
                SkipBytes(reader, 2); // reserved/unknown movie field
                record.RawMovie = reader.ReadBytes(Constants.MovieNameLength);
                record.OverlayFlags = reader.ReadInt32();
                record.Overlays = ReadOverlayNames(reader);

                MoveToNextRecord(reader, startPos, recordStride);

                WriteCommonFields(result, record);
                WriteLegacyDifficultyFields(result, record.SongID, difficulties);
                WriteLegacyKeysetFields(result, record.SongID, keysets);
            }

            return result;
        }

        private static Configuration ConvertBelow20(BinaryReader reader, Configuration result, int metaCount)
        {
            for (int i = 0; i < metaCount; i++)
            {
                long startPos = reader.BaseStream.Position;

                MusicRecord record = new MusicRecord();
                record.TextEncoding = Constants.Enc932;
                record.RawTitle = reader.ReadBytes(64);
                record.RawEnglishTitle = reader.ReadBytes(64);
                SkipBytes(reader, 32); // reserved/unknown metadata
                record.RawGenre = reader.ReadBytes(32);
                record.RawArtist = reader.ReadBytes(32);
                SkipBytes(reader, 8); // reserved/unknown metadata

                byte[] difficulties = reader.ReadBytes(8);
                SkipBytes(reader, 180); // reserved/unknown metadata block before IDs

                record.SongID = reader.ReadInt16();
                SkipBytes(reader, 2); // version, afp_flag
                record.Volume = reader.ReadInt32();

                byte[] keysets = reader.ReadBytes(8);
                record.VideoDelay = reader.ReadInt16();
                SkipBytes(reader, 2); // reserved/unknown movie field
                record.RawMovie = reader.ReadBytes(Constants.MovieNameLength);
                SkipBytes(reader, 64); // reserved/unknown movie metadata
                record.OverlayFlags = reader.ReadInt32();
                record.Overlays = ReadOverlayNames(reader);

                MoveToNextRecord(reader, startPos, Constants.RecordSizeBelow20);

                record.SongID = NormalizePreV20SongID(record.SongID);

                WriteCommonFields(result, record);
                WriteLegacyDifficultyFields(result, record.SongID, difficulties);
                WriteLegacyKeysetFields(result, record.SongID, keysets);
            }

            return result;
        }

        private static int GetBelow27RecordStride(int musicDataVersion)
        {
            int recordStride = Constants.RecordSizeBelow27;
            if (musicDataVersion < 80)
            {
                if (musicDataVersion > 25)
                    recordStride += 36;
                else if (musicDataVersion > 21)
                    recordStride += 32;
            }
            return recordStride;
        }

        private static int NormalizePreV20SongID(int songID)
        {
            int version = songID / 100;
            int sub = songID - version * 100;
            return version * 1000 + sub;
        }

        private static MusicDataHeader ReadHeader(BinaryReader reader)
        {
            if (reader.ReadInt32() != Constants.MagicIIDX)
                return null;

            MusicDataHeader header = new MusicDataHeader();
            header.Version = reader.ReadInt32();

            if (header.Version >= 32)
            {
                header.MetaCount = reader.ReadInt32();
                header.EntryCount = reader.ReadInt32();
            }
            else
            {
                header.MetaCount = reader.ReadInt16();
                header.EntryCount = reader.ReadInt16();
                SkipBytes(reader, 4); // header padding/reserved
            }

            return header;
        }

        private static void SkipEntryTable(BinaryReader reader, MusicDataHeader header)
        {
            int entrySize = header.Version >= 32 && header.Version < 80 ? 4 : 2;
            SkipBytes(reader, header.EntryCount * entrySize);
        }

        private static Configuration ConvertMusicData(BinaryReader reader, Configuration result)
        {
            MusicDataHeader header = ReadHeader(reader);
            if (header == null)
            {
                Console.WriteLine("Invalid file signature.");
                return null;
            }

            SkipEntryTable(reader, header);

            if (header.Version == 80)
                return ConvertBelow27(reader, result, header.MetaCount, header.Version);

            if (header.Version >= 32)
                return ConvertV32(reader, result, header.MetaCount);

            if (header.Version >= 27)
                return ConvertBelow32(reader, result, header.MetaCount);

            if (header.Version >= 20)
                return ConvertBelow27(reader, result, header.MetaCount, header.Version);

            return ConvertBelow20(reader, result, header.MetaCount);
        }

        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine();
                Console.WriteLine("Usage: IIDXDBGenerator <input file>");
                Console.WriteLine();
                Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
                Console.WriteLine();
                Console.WriteLine("Supported file:");
                Console.WriteLine("music_data.bin");
                return;
            }

            string sourceFileName = args[0];
            Console.WriteLine("inputFile : " + sourceFileName);

            byte[] data = File.ReadAllBytes(sourceFileName);
            Configuration result = Configuration.ReadFile("BeatmaniaDB");

            using (MemoryStream mem = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(mem))
                result = ConvertMusicData(reader, result);

            if (result == null)
                return;

            SetPlayVideoFlags(result, sourceFileName);
            result.WriteFile("BeatmaniaDB");
        }
    }
}
