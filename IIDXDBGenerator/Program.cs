using Scharfrichter.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IIDXDBGenerator
{
    class Program
    {
        // Define constants to eliminate magic numbers
        private static class Constants
        {
            public const int MagicIIDX = 0x58444949;
            public const int RecordSizeV32 = 2040;
            public const int RecordSizeBelow32 = 1324;
            public const int RecordSizeBelow27 = 800;
            public const int RecordSizeBelow20 = 828;

            public static readonly Encoding Enc932;
            public static readonly Encoding EncUtf16;

            static Constants()
            {
                Enc932 = Encoding.GetEncoding(932);
                EncUtf16 = Encoding.Unicode;
            }
        }

        // Extract and decode strings appropriately based on encoding, 
        // with strict null-character removal for safe INI file writing.
        static private string GetString(byte[] source, Encoding encoding)
        {
            if (source == null || source.Length == 0 || source[0] == 0) return string.Empty;

            if (encoding == Constants.EncUtf16)
            {
                // Faithfully replicate the original robust behavior for UTF-16:
                // Decode the entire buffer and strip all nulls to handle malformed padding
                return encoding.GetString(source).Replace("\0", "");
            }
            else
            {
                // For Shift-JIS, decode up to the first null byte and strip any remaining nulls
                int nullIndex = Array.IndexOf(source, (byte)0);
                int length = nullIndex >= 0 ? nullIndex : source.Length;
                return encoding.GetString(source, 0, length).Replace("\0", "");
            }
        }

        // Extracted method for processing overlay data
        static private void SetOverlayData(Configuration config, string key, int overlayFlags, byte[][] overlays)
        {
            if (overlayFlags == 0) return;
            for (int i = 0; i < overlays.Length; i++)
            {
                if (overlays[i][0] != 0)
                {
                    config[key][$"OVERLAY{i}"] = GetString(overlays[i], Constants.Enc932);
                }
            }
        }

        static private Configuration ConvertV32(BinaryReader reader, Configuration result, int metaCount)
        {
            for (int i = 0; i < metaCount; i++)
            {
                long startPos = reader.BaseStream.Position;

                byte[] rawTitle = reader.ReadBytes(256);
                reader.BaseStream.Position += 64; // Skip rawTitleTranslit
                byte[] rawGenre = reader.ReadBytes(128);
                byte[] rawArtist = reader.ReadBytes(256);
                reader.BaseStream.Position += 300; // Skip unknown

                byte[] diffs = reader.ReadBytes(10); // Difficulty SP 0-4 + DP 5-9
                reader.BaseStream.Position += 646; // Skip unknown

                int songID = reader.ReadUInt16();
                reader.BaseStream.Position += 2; // Skip version, afp_flag
                int volume = reader.ReadInt32();

                byte[] keysets = reader.ReadBytes(10); // Keyset SP 0-4 + DP 5-9

                int bgaDelay = reader.ReadInt16();
                byte[] rawMovie = reader.ReadBytes(32);
                int overlayFlags = reader.ReadInt32();

                byte[][] overlays = new byte[9][];
                for (int o = 0; o < 9; o++) overlays[o] = reader.ReadBytes(32);

                reader.BaseStream.Position = startPos + Constants.RecordSizeV32;

                string key = songID.ToString();

                // Assign directly to ensure the Configuration library properly tracks new sections
                if (rawTitle[0] != 0) result[key]["TITLE"] = GetString(rawTitle, Constants.EncUtf16);
                if (rawArtist[0] != 0) result[key]["ARTIST"] = GetString(rawArtist, Constants.EncUtf16);
                if (rawGenre[0] != 0) result[key]["GENRE"] = GetString(rawGenre, Constants.EncUtf16);
                if (rawMovie[0] != 0) result[key]["VIDEO"] = GetString(rawMovie, Constants.Enc932);

                result[key]["VIDEODELAY"] = bgaDelay.ToString();
                if (volume > 0) result[key]["VOLUME"] = volume.ToString();

                SetOverlayData(result, key, overlayFlags, overlays);

                if (diffs[0] > 0) { result[key]["DIFFICULTYSP0"] = diffs[0].ToString(); result[key]["DIFFICULTYSP1"] = diffs[0].ToString(); }
                if (diffs[1] > 0) result[key]["DIFFICULTYSP2"] = diffs[1].ToString();
                if (diffs[2] > 0) result[key]["DIFFICULTYSP3"] = diffs[2].ToString();
                if (diffs[3] > 0) result[key]["DIFFICULTYSP4"] = diffs[3].ToString();
                if (diffs[4] > 0) result[key]["DIFFICULTYSP5"] = diffs[4].ToString();

                if (diffs[5] > 0) { result[key]["DIFFICULTYDP0"] = diffs[5].ToString(); result[key]["DIFFICULTYDP1"] = diffs[5].ToString(); }
                if (diffs[6] > 0) result[key]["DIFFICULTYDP2"] = diffs[6].ToString();
                if (diffs[7] > 0) result[key]["DIFFICULTYDP3"] = diffs[7].ToString();
                if (diffs[8] > 0) result[key]["DIFFICULTYDP4"] = diffs[8].ToString();
                if (diffs[9] > 0) result[key]["DIFFICULTYDP5"] = diffs[9].ToString();

                if (keysets[0] > 0) { result[key]["KEYSETSP0"] = ((char)keysets[0]).ToString(); result[key]["KEYSETSP1"] = ((char)keysets[0]).ToString(); }
                if (keysets[1] > 0) result[key]["KEYSETSP2"] = ((char)keysets[1]).ToString();
                if (keysets[2] > 0) result[key]["KEYSETSP3"] = ((char)keysets[2]).ToString();
                if (keysets[3] > 0) result[key]["KEYSETSP4"] = ((char)keysets[3]).ToString();
                if (keysets[4] > 0) result[key]["KEYSETSP5"] = ((char)keysets[4]).ToString();

                if (keysets[5] > 0) { result[key]["KEYSETDP0"] = ((char)keysets[5]).ToString(); result[key]["KEYSETDP1"] = ((char)keysets[5]).ToString(); }
                if (keysets[6] > 0) result[key]["KEYSETDP2"] = ((char)keysets[6]).ToString();
                if (keysets[7] > 0) result[key]["KEYSETDP3"] = ((char)keysets[7]).ToString();
                if (keysets[8] > 0) result[key]["KEYSETDP4"] = ((char)keysets[8]).ToString();
                if (keysets[9] > 0) result[key]["KEYSETDP5"] = ((char)keysets[9]).ToString();
            }
            return result;
        }

        static private Configuration ConvertBelow32(BinaryReader reader, Configuration result, int metaCount)
        {
            for (int i = 0; i < metaCount; i++)
            {
                long startPos = reader.BaseStream.Position;

                byte[] rawTitle = reader.ReadBytes(64);
                reader.BaseStream.Position += 64;
                byte[] rawGenre = reader.ReadBytes(64);
                byte[] rawArtist = reader.ReadBytes(64);
                reader.BaseStream.Position += 32;

                byte[] diffs = reader.ReadBytes(10);
                reader.BaseStream.Position += 646;

                int songID = reader.ReadInt16();
                reader.BaseStream.Position += 2;
                int volume = reader.ReadInt32();

                byte[] keysets = reader.ReadBytes(10);

                int bgaDelay = reader.ReadInt16();
                byte[] rawMovie = reader.ReadBytes(32);
                int overlayFlags = reader.ReadInt32();

                byte[][] overlays = new byte[9][];
                for (int o = 0; o < 9; o++) overlays[o] = reader.ReadBytes(32);

                reader.BaseStream.Position = startPos + Constants.RecordSizeBelow32;

                string key = songID.ToString();

                if (rawTitle[0] != 0) result[key]["TITLE"] = GetString(rawTitle, Constants.Enc932);
                if (rawArtist[0] != 0) result[key]["ARTIST"] = GetString(rawArtist, Constants.Enc932);
                if (rawGenre[0] != 0) result[key]["GENRE"] = GetString(rawGenre, Constants.Enc932);
                if (rawMovie[0] != 0) result[key]["VIDEO"] = GetString(rawMovie, Constants.Enc932);

                result[key]["VIDEODELAY"] = bgaDelay.ToString();
                if (volume > 0) result[key]["VOLUME"] = volume.ToString();

                SetOverlayData(result, key, overlayFlags, overlays);

                if (diffs[0] > 0) { result[key]["DIFFICULTYSP0"] = diffs[0].ToString(); result[key]["DIFFICULTYSP1"] = diffs[0].ToString(); }
                if (diffs[1] > 0) result[key]["DIFFICULTYSP2"] = diffs[1].ToString();
                if (diffs[2] > 0) result[key]["DIFFICULTYSP3"] = diffs[2].ToString();
                if (diffs[3] > 0) result[key]["DIFFICULTYSP4"] = diffs[3].ToString();
                if (diffs[4] > 0) result[key]["DIFFICULTYSP5"] = diffs[4].ToString();

                if (diffs[5] > 0) { result[key]["DIFFICULTYDP0"] = diffs[5].ToString(); result[key]["DIFFICULTYDP1"] = diffs[5].ToString(); }
                if (diffs[6] > 0) result[key]["DIFFICULTYDP2"] = diffs[6].ToString();
                if (diffs[7] > 0) result[key]["DIFFICULTYDP3"] = diffs[7].ToString();
                if (diffs[8] > 0) result[key]["DIFFICULTYDP4"] = diffs[8].ToString();
                if (diffs[9] > 0) result[key]["DIFFICULTYDP5"] = diffs[9].ToString();

                if (keysets[0] > 0) { result[key]["KEYSETSP0"] = ((char)keysets[0]).ToString(); result[key]["KEYSETSP1"] = ((char)keysets[0]).ToString(); }
                if (keysets[1] > 0) result[key]["KEYSETSP2"] = ((char)keysets[1]).ToString();
                if (keysets[2] > 0) result[key]["KEYSETSP3"] = ((char)keysets[2]).ToString();
                if (keysets[3] > 0) result[key]["KEYSETSP4"] = ((char)keysets[3]).ToString();
                if (keysets[4] > 0) result[key]["KEYSETSP5"] = ((char)keysets[4]).ToString();

                if (keysets[5] > 0) { result[key]["KEYSETDP0"] = ((char)keysets[5]).ToString(); result[key]["KEYSETDP1"] = ((char)keysets[5]).ToString(); }
                if (keysets[6] > 0) result[key]["KEYSETDP2"] = ((char)keysets[6]).ToString();
                if (keysets[7] > 0) result[key]["KEYSETDP3"] = ((char)keysets[7]).ToString();
                if (keysets[8] > 0) result[key]["KEYSETDP4"] = ((char)keysets[8]).ToString();
                if (keysets[9] > 0) result[key]["KEYSETDP5"] = ((char)keysets[9]).ToString();
            }
            return result;
        }

        static private Configuration ConvertBelow27(BinaryReader reader, Configuration result, int metaCount, int musicDataVersion)
        {
            int recordStride = Constants.RecordSizeBelow27;
            if (musicDataVersion < 80)
            {
                if (musicDataVersion > 25) recordStride += 36;
                else if (musicDataVersion > 21) recordStride += 32;
            }

            for (int i = 0; i < metaCount; i++)
            {
                long startPos = reader.BaseStream.Position;

                byte[] rawTitle = reader.ReadBytes(64);
                reader.BaseStream.Position += 64;
                byte[] rawGenre = reader.ReadBytes(64);
                byte[] rawArtist = reader.ReadBytes(64);
                reader.BaseStream.Position += 32;

                byte[] diffs = reader.ReadBytes(8);
                reader.BaseStream.Position += 160;

                int songID = reader.ReadInt16();
                reader.BaseStream.Position += 2;
                int volume = reader.ReadInt32();

                byte[] keysets = reader.ReadBytes(8);

                int bgaDelay = reader.ReadInt16();
                reader.BaseStream.Position += 2;
                byte[] rawMovie = reader.ReadBytes(32);
                int overlayFlags = reader.ReadInt32();

                byte[][] overlays = new byte[9][];
                for (int o = 0; o < 9; o++) overlays[o] = reader.ReadBytes(32);

                reader.BaseStream.Position = startPos + recordStride;

                string key = songID.ToString();

                if (rawTitle[0] != 0) result[key]["TITLE"] = GetString(rawTitle, Constants.Enc932);
                if (rawArtist[0] != 0) result[key]["ARTIST"] = GetString(rawArtist, Constants.Enc932);
                if (rawGenre[0] != 0) result[key]["GENRE"] = GetString(rawGenre, Constants.Enc932);
                if (rawMovie[0] != 0) result[key]["VIDEO"] = GetString(rawMovie, Constants.Enc932);

                result[key]["VIDEODELAY"] = bgaDelay.ToString();
                if (volume > 0) result[key]["VOLUME"] = volume.ToString();

                SetOverlayData(result, key, overlayFlags, overlays);

                if (diffs[6] > 0) { result[key]["DIFFICULTYSP0"] = diffs[6].ToString(); result[key]["DIFFICULTYSP1"] = diffs[6].ToString(); }
                if (diffs[0] > 0) { if (diffs[6] <= 0) result[key]["DIFFICULTYSP1"] = diffs[0].ToString(); result[key]["DIFFICULTYSP2"] = diffs[0].ToString(); }
                if (diffs[1] > 0) result[key]["DIFFICULTYSP3"] = diffs[1].ToString();
                if (diffs[2] > 0) result[key]["DIFFICULTYSP4"] = diffs[2].ToString();

                if (diffs[7] > 0) { result[key]["DIFFICULTYDP0"] = diffs[7].ToString(); result[key]["DIFFICULTYDP1"] = diffs[7].ToString(); }
                if (diffs[3] > 0) { if (diffs[7] <= 0) result[key]["DIFFICULTYDP1"] = diffs[3].ToString(); result[key]["DIFFICULTYDP2"] = diffs[3].ToString(); }
                if (diffs[4] > 0) result[key]["DIFFICULTYDP3"] = diffs[4].ToString();
                if (diffs[5] > 0) result[key]["DIFFICULTYDP4"] = diffs[5].ToString();

                if (keysets[6] > 0) { result[key]["KEYSETSP0"] = ((char)keysets[6]).ToString(); result[key]["KEYSETSP1"] = ((char)keysets[6]).ToString(); }
                if (keysets[0] > 0) { if (keysets[6] <= 0) result[key]["KEYSETSP1"] = ((char)keysets[0]).ToString(); result[key]["KEYSETSP2"] = ((char)keysets[0]).ToString(); }
                if (keysets[1] > 0) result[key]["KEYSETSP3"] = ((char)keysets[1]).ToString();
                if (keysets[2] > 0) result[key]["KEYSETSP4"] = ((char)keysets[2]).ToString();

                if (keysets[7] > 0) { result[key]["KEYSETDP0"] = ((char)keysets[7]).ToString(); result[key]["KEYSETDP1"] = ((char)keysets[7]).ToString(); }
                if (keysets[3] > 0) { if (keysets[7] <= 0) result[key]["KEYSETDP1"] = ((char)keysets[3]).ToString(); result[key]["KEYSETDP2"] = ((char)keysets[3]).ToString(); }
                if (keysets[4] > 0) result[key]["KEYSETDP3"] = ((char)keysets[4]).ToString();
                if (keysets[5] > 0) result[key]["KEYSETDP4"] = ((char)keysets[5]).ToString();
            }
            return result;
        }

        static private Configuration ConvertBelow20(BinaryReader reader, Configuration result, int metaCount)
        {
            for (int i = 0; i < metaCount; i++)
            {
                long startPos = reader.BaseStream.Position;

                byte[] rawTitle = reader.ReadBytes(64);
                reader.BaseStream.Position += 96;
                byte[] rawGenre = reader.ReadBytes(32);
                byte[] rawArtist = reader.ReadBytes(32);
                reader.BaseStream.Position += 8;

                byte[] diffs = reader.ReadBytes(8);
                reader.BaseStream.Position += 180;

                int songID = reader.ReadInt16();
                reader.BaseStream.Position += 2;
                int volume = reader.ReadInt32();

                byte[] keysets = reader.ReadBytes(8);

                int bgaDelay = reader.ReadInt16();
                reader.BaseStream.Position += 2;
                byte[] rawMovie = reader.ReadBytes(32);
                reader.BaseStream.Position += 64;

                int overlayFlags = reader.ReadInt32();

                byte[][] overlays = new byte[9][];
                for (int o = 0; o < 9; o++) overlays[o] = reader.ReadBytes(32);

                reader.BaseStream.Position = startPos + Constants.RecordSizeBelow20;

                int version = songID / 100;
                int sub = songID - version * 100;
                songID = version * 1000 + sub;

                string key = songID.ToString();

                if (rawTitle[0] != 0) result[key]["TITLE"] = GetString(rawTitle, Constants.Enc932);
                if (rawArtist[0] != 0) result[key]["ARTIST"] = GetString(rawArtist, Constants.Enc932);
                if (rawGenre[0] != 0) result[key]["GENRE"] = GetString(rawGenre, Constants.Enc932);
                if (rawMovie[0] != 0) result[key]["VIDEO"] = GetString(rawMovie, Constants.Enc932);

                result[key]["VIDEODELAY"] = bgaDelay.ToString();
                if (volume > 0) result[key]["VOLUME"] = volume.ToString();

                SetOverlayData(result, key, overlayFlags, overlays);

                if (diffs[6] > 0) { result[key]["DIFFICULTYSP0"] = diffs[6].ToString(); result[key]["DIFFICULTYSP1"] = diffs[6].ToString(); }
                if (diffs[0] > 0) { if (diffs[6] <= 0) result[key]["DIFFICULTYSP1"] = diffs[0].ToString(); result[key]["DIFFICULTYSP2"] = diffs[0].ToString(); }
                if (diffs[1] > 0) result[key]["DIFFICULTYSP3"] = diffs[1].ToString();
                if (diffs[2] > 0) result[key]["DIFFICULTYSP4"] = diffs[2].ToString();

                if (diffs[7] > 0) { result[key]["DIFFICULTYDP0"] = diffs[7].ToString(); result[key]["DIFFICULTYDP1"] = diffs[7].ToString(); }
                if (diffs[3] > 0) { if (diffs[7] <= 0) result[key]["DIFFICULTYDP1"] = diffs[3].ToString(); result[key]["DIFFICULTYDP2"] = diffs[3].ToString(); }
                if (diffs[4] > 0) result[key]["DIFFICULTYDP3"] = diffs[4].ToString();
                if (diffs[5] > 0) result[key]["DIFFICULTYDP4"] = diffs[5].ToString();

                if (keysets[6] > 0) { result[key]["KEYSETSP0"] = ((char)keysets[6]).ToString(); result[key]["KEYSETSP1"] = ((char)keysets[6]).ToString(); }
                if (keysets[0] > 0) { if (keysets[6] <= 0) result[key]["KEYSETSP1"] = ((char)keysets[0]).ToString(); result[key]["KEYSETSP2"] = ((char)keysets[0]).ToString(); }
                if (keysets[1] > 0) result[key]["KEYSETSP3"] = ((char)keysets[1]).ToString();
                if (keysets[2] > 0) result[key]["KEYSETSP4"] = ((char)keysets[2]).ToString();

                if (keysets[7] > 0) { result[key]["KEYSETDP0"] = ((char)keysets[7]).ToString(); result[key]["KEYSETDP1"] = ((char)keysets[7]).ToString(); }
                if (keysets[3] > 0) { if (keysets[7] <= 0) result[key]["KEYSETDP1"] = ((char)keysets[3]).ToString(); result[key]["KEYSETDP2"] = ((char)keysets[3]).ToString(); }
                if (keysets[4] > 0) result[key]["KEYSETDP3"] = ((char)keysets[4]).ToString();
                if (keysets[5] > 0) result[key]["KEYSETDP4"] = ((char)keysets[5]).ToString();
            }
            return result;
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
            {
                if (reader.ReadInt32() != Constants.MagicIIDX)
                {
                    Console.WriteLine("Invalid file signature.");
                    return;
                }

                int musicDataVersion = reader.ReadInt32();
                int metaCount, entryCount;

                if (musicDataVersion >= 32)
                {
                    metaCount = reader.ReadInt32();
                    entryCount = reader.ReadInt32();
                }
                else
                {
                    metaCount = reader.ReadInt16();
                    entryCount = reader.ReadInt16();
                    reader.ReadInt32(); // Skip padding/unknown
                }

                List<int> entries = new List<int>();
                for (int i = 0; i < entryCount; i++)
                {
                    entries.Add((musicDataVersion >= 32 && musicDataVersion < 80) ? reader.ReadInt32() : reader.ReadInt16());
                }

                if (musicDataVersion == 80)
                {
                    result = ConvertBelow27(reader, result, metaCount, musicDataVersion);
                }
                else if (musicDataVersion >= 32)
                {
                    result = ConvertV32(reader, result, metaCount);
                }
                else if (musicDataVersion >= 27)
                {
                    result = ConvertBelow32(reader, result, metaCount);
                }
                else if (musicDataVersion >= 20)
                {
                    result = ConvertBelow27(reader, result, metaCount, musicDataVersion);
                }
                else
                {
                    result = ConvertBelow20(reader, result, metaCount);
                }
            }
            result.WriteFile("BeatmaniaDB");
        }
    }
}