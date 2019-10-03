using Scharfrichter.Common;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IIDXDBGenerator
{
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

        static private Configuration convert (BinaryReader reader, Configuration result, int metaCount, int musicDataVersion)
        {

            for (int i = 0; i < metaCount; i++)
            {
                byte[] metaRaw = reader.ReadBytes(800);

                if (musicDataVersion > 21)
                    reader.ReadBytes(32);
                if (musicDataVersion > 25)
                    reader.ReadBytes(4);

                using (MemoryStream metaMem = new MemoryStream(metaRaw))
                {
                    BinaryReader metaReader = new BinaryReader(metaMem);
                    byte[] rawTitle = metaReader.ReadBytes(64);
                    byte[] rawTitleTranslit = metaReader.ReadBytes(64);
                    byte[] rawGenre = metaReader.ReadBytes(64);
                    byte[] rawArtist = metaReader.ReadBytes(64);
                    int flags00 = metaReader.ReadInt16();
                    int flags02 = metaReader.ReadInt16();
                    int flags04 = metaReader.ReadInt16();
                    int flags06 = metaReader.ReadInt16();
                    int flags08 = metaReader.ReadInt16();
                    int flags0A = metaReader.ReadInt16();
                    int flags0C = metaReader.ReadInt16();
                    int flags0E = metaReader.ReadInt16();
                    int flags10 = metaReader.ReadInt16();
                    int flags12 = metaReader.ReadInt16();
                    int flags14 = metaReader.ReadInt16();
                    int flags16 = metaReader.ReadInt16();
                    int folder = metaReader.ReadInt16();
                    int flags1A = metaReader.ReadInt16();
                    int flags1C = metaReader.ReadInt16();
                    int flags1E = metaReader.ReadInt16();
                    int difficulty0 = metaReader.ReadByte();
                    int difficulty1 = metaReader.ReadByte();
                    int difficulty2 = metaReader.ReadByte();
                    int difficulty3 = metaReader.ReadByte();
                    int difficulty4 = metaReader.ReadByte();
                    int difficulty5 = metaReader.ReadByte();
                    int difficulty6 = metaReader.ReadByte();
                    int difficulty7 = metaReader.ReadByte();
                    metaReader.ReadBytes(160);
                    int songID = metaReader.ReadInt16();
                    int version = metaReader.ReadByte();
                    int afp_flag = metaReader.ReadByte();
                    int volume = metaReader.ReadInt32();
                    char rawKeyset0 = (char)metaReader.ReadByte();
                    char rawKeyset1 = (char)metaReader.ReadByte();
                    char rawKeyset2 = (char)metaReader.ReadByte();
                    char rawKeyset3 = (char)metaReader.ReadByte();
                    char rawKeyset4 = (char)metaReader.ReadByte();
                    char rawKeyset5 = (char)metaReader.ReadByte();
                    char rawKeyset6 = (char)metaReader.ReadByte();
                    char rawKeyset7 = (char)metaReader.ReadByte();
                    int bgaDelay = metaReader.ReadInt16();
                    metaReader.ReadInt16();
                    byte[] rawMovie = metaReader.ReadBytes(32);
                    int overlayFlags = metaReader.ReadInt32();
                    byte[] rawOverlay0 = metaReader.ReadBytes(32);
                    byte[] rawOverlay1 = metaReader.ReadBytes(32);
                    byte[] rawOverlay2 = metaReader.ReadBytes(32);
                    byte[] rawOverlay3 = metaReader.ReadBytes(32);
                    byte[] rawOverlay4 = metaReader.ReadBytes(32);
                    byte[] rawOverlay5 = metaReader.ReadBytes(32);
                    byte[] rawOverlay6 = metaReader.ReadBytes(32);
                    byte[] rawOverlay7 = metaReader.ReadBytes(32);
                    byte[] rawOverlay8 = metaReader.ReadBytes(32);

                    string databasePrimaryKey = songID.ToString();
                    if (rawTitle[0] != 0)
                        result[databasePrimaryKey]["TITLE"] = GetString(rawTitle);
                    if (rawArtist[0] != 0)
                        result[databasePrimaryKey]["ARTIST"] = GetString(rawArtist);
                    if (rawGenre[0] != 0)
                        result[databasePrimaryKey]["GENRE"] = GetString(rawGenre);
                    if (rawMovie[0] != 0)
                        result[databasePrimaryKey]["VIDEO"] = GetString(rawMovie);
                    result[databasePrimaryKey]["VIDEODELAY"] = bgaDelay.ToString();
                    if (volume > 0)
                        result[databasePrimaryKey]["VOLUME"] = volume.ToString();
                    if (overlayFlags != 0)
                    {
                        if (rawOverlay0[0] != 0)
                            result[databasePrimaryKey]["OVERLAY0"] = GetString(rawOverlay0);
                        if (rawOverlay1[0] != 0)
                            result[databasePrimaryKey]["OVERLAY1"] = GetString(rawOverlay1);
                        if (rawOverlay2[0] != 0)
                            result[databasePrimaryKey]["OVERLAY2"] = GetString(rawOverlay2);
                        if (rawOverlay3[0] != 0)
                            result[databasePrimaryKey]["OVERLAY3"] = GetString(rawOverlay3);
                        if (rawOverlay4[0] != 0)
                            result[databasePrimaryKey]["OVERLAY4"] = GetString(rawOverlay4);
                        if (rawOverlay5[0] != 0)
                            result[databasePrimaryKey]["OVERLAY5"] = GetString(rawOverlay5);
                        if (rawOverlay6[0] != 0)
                            result[databasePrimaryKey]["OVERLAY6"] = GetString(rawOverlay6);
                        if (rawOverlay7[0] != 0)
                            result[databasePrimaryKey]["OVERLAY7"] = GetString(rawOverlay7);
                        if (rawOverlay8[0] != 0)
                            result[databasePrimaryKey]["OVERLAY8"] = GetString(rawOverlay8);
                    }
                    if (difficulty6 > 0)
                    {
                        result[databasePrimaryKey]["DIFFICULTYSP0"] = difficulty6.ToString();
                        result[databasePrimaryKey]["DIFFICULTYSP1"] = difficulty6.ToString();
                    }
                    if (difficulty0 > 0)
                    {
                        if (difficulty6 <= 0)
                        {
                            result[databasePrimaryKey]["DIFFICULTYSP1"] = difficulty0.ToString();
                        }
                        result[databasePrimaryKey]["DIFFICULTYSP2"] = difficulty0.ToString();
                    }
                    if (difficulty1 > 0)
                        result[databasePrimaryKey]["DIFFICULTYSP3"] = difficulty1.ToString();
                    if (difficulty2 > 0)
                        result[databasePrimaryKey]["DIFFICULTYSP4"] = difficulty2.ToString();

                    if (difficulty7 > 0)
                    {
                        result[databasePrimaryKey]["DIFFICULTYDP0"] = difficulty7.ToString();
                        result[databasePrimaryKey]["DIFFICULTYDP1"] = difficulty7.ToString();
                    }
                    if (difficulty3 > 0)
                    {
                        if (difficulty7 <= 0)
                        {
                            result[databasePrimaryKey]["DIFFICULTYDP1"] = difficulty3.ToString();
                        }
                        result[databasePrimaryKey]["DIFFICULTYDP2"] = difficulty3.ToString();
                    }
                    if (difficulty4 > 0)
                        result[databasePrimaryKey]["DIFFICULTYDP3"] = difficulty4.ToString();
                    if (difficulty5 > 0)
                        result[databasePrimaryKey]["DIFFICULTYDP4"] = difficulty5.ToString();




                    if (rawKeyset6 > 0)
                    {
                        result[databasePrimaryKey]["KEYSETSP0"] = rawKeyset6.ToString();
                        result[databasePrimaryKey]["KEYSETSP1"] = rawKeyset6.ToString();
                    }
                    if (rawKeyset0 > 0)
                    {
                        if (rawKeyset6 <= 0)
                        {
                            result[databasePrimaryKey]["KEYSETSP1"] = rawKeyset0.ToString();
                        }
                        result[databasePrimaryKey]["KEYSETSP2"] = rawKeyset0.ToString();
                    }
                    if (rawKeyset1 > 0)
                        result[databasePrimaryKey]["KEYSETSP3"] = rawKeyset1.ToString();
                    if (rawKeyset2 > 0)
                        result[databasePrimaryKey]["KEYSETSP4"] = rawKeyset2.ToString();

                    if (rawKeyset7 > 0)
                    {
                        result[databasePrimaryKey]["KEYSETDP0"] = rawKeyset7.ToString();
                        result[databasePrimaryKey]["KEYSETDP1"] = rawKeyset7.ToString();
                    }
                    if (rawKeyset3 > 0)
                    {
                        if (rawKeyset7 <= 0)
                        {
                            result[databasePrimaryKey]["KEYSETDP1"] = rawKeyset3.ToString();
                        }
                        result[databasePrimaryKey]["KEYSETDP2"] = rawKeyset3.ToString();
                    }
                    if (rawKeyset4 > 0)
                        result[databasePrimaryKey]["KEYSETDP3"] = rawKeyset4.ToString();
                    if (rawKeyset5 > 0)
                        result[databasePrimaryKey]["KEYSETDP4"] = rawKeyset5.ToString();
                }
            }
            return result;
        }


        static private Configuration convertBelow20 (BinaryReader reader, Configuration result, int metaCount, int musicDataVersion)
        {

            for (int i = 0; i < metaCount; i++)
            {
                byte[] metaRaw = reader.ReadBytes(828);

                using (MemoryStream metaMem = new MemoryStream(metaRaw))
                {
                    BinaryReader metaReader = new BinaryReader(metaMem);
                    byte[] rawTitle = metaReader.ReadBytes(64);
                    byte[] lTitle = metaReader.ReadBytes(32);
                    byte[] tTitle = metaReader.ReadBytes(64);
                    byte[] rawGenre = metaReader.ReadBytes(32);
                    byte[] rawArtist = metaReader.ReadBytes(32);
                    int flags00 = metaReader.ReadInt16();
                    int flags02 = metaReader.ReadInt16();
                    int flags04 = metaReader.ReadInt16();
                    int flags06 = metaReader.ReadInt16();
                    int difficulty0 = metaReader.ReadByte();
                    int difficulty1 = metaReader.ReadByte();
                    int difficulty2 = metaReader.ReadByte();
                    int difficulty3 = metaReader.ReadByte();
                    int difficulty4 = metaReader.ReadByte();
                    int difficulty5 = metaReader.ReadByte();
                    int difficulty6 = metaReader.ReadByte();
                    int difficulty7 = metaReader.ReadByte();
                    metaReader.ReadBytes(180);
                    int songID = metaReader.ReadInt16();
                    metaReader.ReadByte();
                    int afp_flag = metaReader.ReadByte();
                    int volume = metaReader.ReadInt32();
                    char rawKeyset0 = (char)metaReader.ReadByte();
                    char rawKeyset1 = (char)metaReader.ReadByte();
                    char rawKeyset2 = (char)metaReader.ReadByte();
                    char rawKeyset3 = (char)metaReader.ReadByte();
                    char rawKeyset4 = (char)metaReader.ReadByte();
                    char rawKeyset5 = (char)metaReader.ReadByte();
                    char rawKeyset6 = (char)metaReader.ReadByte();
                    char rawKeyset7 = (char)metaReader.ReadByte();
                    int bgaDelay = metaReader.ReadInt16();
                    metaReader.ReadInt16();
                    byte[] rawMovie = metaReader.ReadBytes(32);
                    byte[] iTitle = metaReader.ReadBytes(32);
                    byte[] item = metaReader.ReadBytes(32);
                    int overlayFlags = metaReader.ReadInt32();
                    byte[] rawOverlay0 = metaReader.ReadBytes(32);
                    byte[] rawOverlay1 = metaReader.ReadBytes(32);
                    byte[] rawOverlay2 = metaReader.ReadBytes(32);
                    byte[] rawOverlay3 = metaReader.ReadBytes(32);
                    byte[] rawOverlay4 = metaReader.ReadBytes(32);
                    byte[] rawOverlay5 = metaReader.ReadBytes(32);
                    byte[] rawOverlay6 = metaReader.ReadBytes(32);
                    byte[] rawOverlay7 = metaReader.ReadBytes(32);
                    byte[] rawOverlay8 = metaReader.ReadBytes(32);

                    int version = songID / 100;
                    int sub = songID - version * 100;
                    songID = version * 1000 + sub;

                    string databasePrimaryKey = songID.ToString();
                    if (rawTitle[0] != 0)
                        result[databasePrimaryKey]["TITLE"] = GetString(rawTitle);
                    if (rawArtist[0] != 0)
                        result[databasePrimaryKey]["ARTIST"] = GetString(rawArtist);
                    if (rawGenre[0] != 0)
                        result[databasePrimaryKey]["GENRE"] = GetString(rawGenre);
                    if (rawMovie[0] != 0)
                        result[databasePrimaryKey]["VIDEO"] = GetString(rawMovie);
                    result[databasePrimaryKey]["VIDEODELAY"] = bgaDelay.ToString();
                    if (volume > 0)
                        result[databasePrimaryKey]["VOLUME"] = volume.ToString();
                    if (overlayFlags != 0)
                    {
                        if (rawOverlay0[0] != 0)
                            result[databasePrimaryKey]["OVERLAY0"] = GetString(rawOverlay0);
                        if (rawOverlay1[0] != 0)
                            result[databasePrimaryKey]["OVERLAY1"] = GetString(rawOverlay1);
                        if (rawOverlay2[0] != 0)
                            result[databasePrimaryKey]["OVERLAY2"] = GetString(rawOverlay2);
                        if (rawOverlay3[0] != 0)
                            result[databasePrimaryKey]["OVERLAY3"] = GetString(rawOverlay3);
                        if (rawOverlay4[0] != 0)
                            result[databasePrimaryKey]["OVERLAY4"] = GetString(rawOverlay4);
                        if (rawOverlay5[0] != 0)
                            result[databasePrimaryKey]["OVERLAY5"] = GetString(rawOverlay5);
                        if (rawOverlay6[0] != 0)
                            result[databasePrimaryKey]["OVERLAY6"] = GetString(rawOverlay6);
                        if (rawOverlay7[0] != 0)
                            result[databasePrimaryKey]["OVERLAY7"] = GetString(rawOverlay7);
                        if (rawOverlay8[0] != 0)
                            result[databasePrimaryKey]["OVERLAY8"] = GetString(rawOverlay8);
                    }
                    if (difficulty6 > 0)
                    {
                        result[databasePrimaryKey]["DIFFICULTYSP0"] = difficulty6.ToString();
                        result[databasePrimaryKey]["DIFFICULTYSP1"] = difficulty6.ToString();
                    }
                    if (difficulty0 > 0)
                    {
                        if (difficulty6 <= 0)
                        {
                            result[databasePrimaryKey]["DIFFICULTYSP1"] = difficulty0.ToString();
                        }
                        result[databasePrimaryKey]["DIFFICULTYSP2"] = difficulty0.ToString();
                    }
                    if (difficulty1 > 0)
                        result[databasePrimaryKey]["DIFFICULTYSP3"] = difficulty1.ToString();
                    if (difficulty2 > 0)
                        result[databasePrimaryKey]["DIFFICULTYSP4"] = difficulty2.ToString();

                    if (difficulty7 > 0)
                    {
                        result[databasePrimaryKey]["DIFFICULTYDP0"] = difficulty7.ToString();
                        result[databasePrimaryKey]["DIFFICULTYDP1"] = difficulty7.ToString();
                    }
                    if (difficulty3 > 0)
                    {
                        if (difficulty7 <= 0)
                        {
                            result[databasePrimaryKey]["DIFFICULTYDP1"] = difficulty3.ToString();
                        }
                        result[databasePrimaryKey]["DIFFICULTYDP2"] = difficulty3.ToString();
                    }
                    if (difficulty4 > 0)
                        result[databasePrimaryKey]["DIFFICULTYDP3"] = difficulty4.ToString();
                    if (difficulty5 > 0)
                        result[databasePrimaryKey]["DIFFICULTYDP4"] = difficulty5.ToString();




                    if (rawKeyset6 > 0)
                    {
                        result[databasePrimaryKey]["KEYSETSP0"] = rawKeyset6.ToString();
                        result[databasePrimaryKey]["KEYSETSP1"] = rawKeyset6.ToString();
                    }
                    if (rawKeyset0 > 0)
                    {
                        if (rawKeyset6 <= 0)
                        {
                            result[databasePrimaryKey]["KEYSETSP1"] = rawKeyset0.ToString();
                        }
                        result[databasePrimaryKey]["KEYSETSP2"] = rawKeyset0.ToString();
                    }
                    if (rawKeyset1 > 0)
                        result[databasePrimaryKey]["KEYSETSP3"] = rawKeyset1.ToString();
                    if (rawKeyset2 > 0)
                        result[databasePrimaryKey]["KEYSETSP4"] = rawKeyset2.ToString();

                    if (rawKeyset7 > 0)
                    {
                        result[databasePrimaryKey]["KEYSETDP0"] = rawKeyset7.ToString();
                        result[databasePrimaryKey]["KEYSETDP1"] = rawKeyset7.ToString();
                    }
                    if (rawKeyset3 > 0)
                    {
                        if (rawKeyset7 <= 0)
                        {
                            result[databasePrimaryKey]["KEYSETDP1"] = rawKeyset3.ToString();
                        }
                        result[databasePrimaryKey]["KEYSETDP2"] = rawKeyset3.ToString();
                    }
                    if (rawKeyset4 > 0)
                        result[databasePrimaryKey]["KEYSETDP3"] = rawKeyset4.ToString();
                    if (rawKeyset5 > 0)
                        result[databasePrimaryKey]["KEYSETDP4"] = rawKeyset5.ToString();
                }
            }
            return result;
        }

        static void Main(string[] args)
        {
            // show usage if no args provided
            if (args.Length == 0 || args.Length > 1)
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
            {
                BinaryReader reader = new BinaryReader(mem);
                if (reader.ReadInt32() != 0x58444949)
                {
                    Console.WriteLine("1");
                    return;
                }
                int musicDataVersion = reader.ReadInt32();

                int metaCount = reader.ReadInt16();
                int entryCount = reader.ReadInt16();
                reader.ReadInt32();
                
                List<int> entries = new List<int>();
                for (int i = 0; i < entryCount; i++)
                    entries.Add(reader.ReadInt16());


                if (musicDataVersion >= 20)
                {
                    result = convert(reader, result, metaCount, musicDataVersion);
                }
                else
                {
                    result = convertBelow20(reader, result, metaCount, musicDataVersion);
                }

            }
            result.WriteFile("BeatmaniaDB");
        }
    }
}
