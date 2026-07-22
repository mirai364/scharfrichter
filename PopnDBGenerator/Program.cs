using Scharfrichter.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PopnDBGenerator
{
    class Program
    {
        private const int MaxReadStringLength = 512;
        private static readonly Encoding Enc932 = Encoding.GetEncoding(932);

        private sealed class MusicLayout
        {
            public string Name;
            public int MusicTableOffset;
            public int MusicRecordSize;
            public int MusicCount;
            public int FileTableOffset;
            public int FileRecordSize;
            public int GenreOffset = 0;
            public int TitleOffset = 4;
            public int ArtistOffset = 8;
            public int CommentOffset = 12;
            public int EnglishTitleOffset = 16;
            public int EnglishArtistOffset = 20;
            public int ChartMaskOffset = 28;
            public int CategoryOffset = 32;
            public int DifficultyOffset = 44;
            public int FileIndexOffset = 52;
            public bool LegacyDifficultyOrder;
            public bool LegacyAlwaysEasy;
        }

        private sealed class Section
        {
            public uint VirtualAddress;
            public uint VirtualSize;
            public uint RawAddress;
            public uint RawSize;
        }

        private sealed class PeImage
        {
            public byte[] Data;
            public ulong ImageBase;
            public List<Section> Sections = new List<Section>();

            public int VirtualToRaw(uint address)
            {
                ulong virtualAddress = address;
                if (virtualAddress >= ImageBase)
                    virtualAddress -= ImageBase;

                foreach (Section section in Sections)
                {
                    uint size = Math.Max(section.VirtualSize, section.RawSize);
                    if (virtualAddress >= section.VirtualAddress && virtualAddress < section.VirtualAddress + size)
                        return checked((int)(section.RawAddress + (virtualAddress - section.VirtualAddress)));
                }

                throw new InvalidDataException(String.Format("Couldn't find raw offset for virtual offset 0x{0:X8}.", address));
            }

            public bool TryReadString(uint address, out string value)
            {
                value = "";
                int raw;
                try { raw = VirtualToRaw(address); }
                catch { return false; }

                if (raw < 0 || raw >= Data.Length) return false;
                int length = 0;
                while (raw + length < Data.Length && length < MaxReadStringLength && Data[raw + length] != 0) length++;
                if (length == 0 || length >= MaxReadStringLength) return false;
                value = Enc932.GetString(Data, raw, length).Replace("\r", " ").Replace("\n", " ").Trim();
                return IsUsableString(value);
            }
        }

        private sealed class SongRecord
        {
            public int Id;
            public string Title;
            public string Artist;
            public string Genre;
            public string Comment;
            public string EnglishTitle;
            public string EnglishArtist;
            public int Category;
            public int[] Difficulties = new int[4];
            public string[] Files = new string[4];
        }

        private static MusicLayout[] GetKnownLayouts()
        {
            return new[]
            {
                // ===== v19-20: legacy layouts with [Normal, Hyper, Easy, EX] byte order =====
                // struct: 6I 2H I I I B B B B B B x x x 6H + padding
                // Field offsets from struct: genre=0, title=4, artist=8, comment=12,
                //   charts=20, folder=24, normal=33, hyper=34, easy=35, ex=36,
                //   normalFile=42, hyperFile=44, easyFile=46, exFile=48
                new MusicLayout {
                    Name = "popn19", MusicTableOffset = 0x1F68E8, MusicRecordSize = 72, MusicCount = 1048,
                    FileTableOffset = 0x2D6888, FileRecordSize = 24,
                    GenreOffset = 0, TitleOffset = 4, ArtistOffset = 8, CommentOffset = 12,
                    EnglishTitleOffset = -1, EnglishArtistOffset = -1,
                    ChartMaskOffset = 20, CategoryOffset = 24,
                    DifficultyOffset = 33, FileIndexOffset = 42,
                    LegacyDifficultyOrder = true, LegacyAlwaysEasy = true
                },
                new MusicLayout {
                    Name = "popn20-1", MusicTableOffset = 0x1797D0, MusicRecordSize = 160, MusicCount = 1116,
                    FileTableOffset = 0x238AD0, FileRecordSize = 24,
                    EnglishTitleOffset = -1, EnglishArtistOffset = -1,
                    ChartMaskOffset = 20, CategoryOffset = 24,
                    DifficultyOffset = 33, FileIndexOffset = 42,
                    LegacyDifficultyOrder = true, LegacyAlwaysEasy = true
                },
                new MusicLayout {
                    Name = "popn20-2", MusicTableOffset = 0x1AE240, MusicRecordSize = 160, MusicCount = 1122,
                    FileTableOffset = 0x273768, FileRecordSize = 24,
                    EnglishTitleOffset = -1, EnglishArtistOffset = -1,
                    ChartMaskOffset = 20, CategoryOffset = 24,
                    DifficultyOffset = 33, FileIndexOffset = 42,
                    LegacyDifficultyOrder = true, LegacyAlwaysEasy = true
                },

                // ===== v21: Sunny Park (164-byte records, English fields present) =====
                // struct: 6I 2H I I I H 6B 6H + padding
                // genre=0, title=4, artist=8, comment=12, englishTitle=16, englishArtist=20,
                // extendedGenre=24, ??=28, ??=30, charts=32, folder=36, event1=40, event2=44,
                // easy=46, normal=47, hyper=48, ex=49, battleNormal=50, battleHyper=51,
                // easyFile=52, normalFile=54, hyperFile=56, exFile=58, battleNormalFile=60, battleHyperFile=62
                new MusicLayout {
                    Name = "popn21-1", MusicTableOffset = 0x16C880, MusicRecordSize = 164, MusicCount = 1184,
                    FileTableOffset = 0x2399B8, FileRecordSize = 28,
                    EnglishTitleOffset = 16, EnglishArtistOffset = 20,
                    ChartMaskOffset = 32, CategoryOffset = 36,
                    DifficultyOffset = 46, FileIndexOffset = 52,
                },
                new MusicLayout {
                    Name = "popn21-2", MusicTableOffset = 0x16C880, MusicRecordSize = 164, MusicCount = 1183,
                    FileTableOffset = 0x2399B8, FileRecordSize = 28,
                    EnglishTitleOffset = 16, EnglishArtistOffset = 20,
                    ChartMaskOffset = 32, CategoryOffset = 36,
                    DifficultyOffset = 46, FileIndexOffset = 52,
                },
                new MusicLayout {
                    Name = "popn21-3", MusicTableOffset = 0x170ED8, MusicRecordSize = 164, MusicCount = 1183,
                    FileTableOffset = 0x23EFC0, FileRecordSize = 28,
                    EnglishTitleOffset = 16, EnglishArtistOffset = 20,
                    ChartMaskOffset = 32, CategoryOffset = 36,
                    DifficultyOffset = 46, FileIndexOffset = 52,
                },
                new MusicLayout {
                    Name = "popn21-4", MusicTableOffset = 0x1FB640, MusicRecordSize = 164, MusicCount = 1280,
                    FileTableOffset = 0x2E0D20, FileRecordSize = 28,
                    EnglishTitleOffset = 16, EnglishArtistOffset = 20,
                    ChartMaskOffset = 32, CategoryOffset = 36,
                    DifficultyOffset = 46, FileIndexOffset = 52,
                },

                // ===== v22: Lapistoria (160-byte records) =====
                // struct: 6I 2H I I I H 6B 6H + padding
                // genre=0, title=4, artist=8, comment=12, englishTitle=16, englishArtist=20,
                // ??=24, ??=26, charts=28, folder=32, event1=36, event2=40,
                // easy=42, normal=43, hyper=44, ex=45,
                // easyFile=48, normalFile=50, hyperFile=52, exFile=54
                new MusicLayout {
                    Name = "popn22", MusicTableOffset = 0x3124B0, MusicRecordSize = 160, MusicCount = 1423,
                    FileTableOffset = 0x472130, FileRecordSize = 28,
                    EnglishTitleOffset = 16, EnglishArtistOffset = 20,
                    ChartMaskOffset = 28, CategoryOffset = 32,
                    DifficultyOffset = 42, FileIndexOffset = 48,
                },

                // ===== v23: Eclale (160-byte records, 32-byte file records) =====
                new MusicLayout {
                    Name = "popn23", MusicTableOffset = 0x2DE5C8, MusicRecordSize = 160, MusicCount = 1551,
                    FileTableOffset = 0x2D1948, FileRecordSize = 32,
                    EnglishTitleOffset = 16, EnglishArtistOffset = 20,
                    ChartMaskOffset = 28, CategoryOffset = 32,
                    DifficultyOffset = 42, FileIndexOffset = 48,
                },

                // ===== v24-28 (172-byte records, standard [Easy,Normal,Hyper,EX] order) =====
                new MusicLayout { Name = "popn24-1", MusicTableOffset = 0x299410, MusicRecordSize = 172, MusicCount = 1704, FileTableOffset = 0x28B108, FileRecordSize = 32 },
                new MusicLayout { Name = "popn24-2", MusicTableOffset = 0x299210, MusicRecordSize = 172, MusicCount = 1704, FileTableOffset = 0x28AF08, FileRecordSize = 32 },
                new MusicLayout { Name = "popn25-1", MusicTableOffset = 0x2B3840, MusicRecordSize = 172, MusicCount = 1780, FileTableOffset = 0x2A48F8, FileRecordSize = 32 },
                new MusicLayout { Name = "popn25-2", MusicTableOffset = 0x2B8C20, MusicRecordSize = 172, MusicCount = 1795, FileTableOffset = 0x2A9AF8, FileRecordSize = 32 },
                new MusicLayout { Name = "popn25-3", MusicTableOffset = 0x2C7C78, MusicRecordSize = 172, MusicCount = 1877, FileTableOffset = 0x2B8010, FileRecordSize = 32 },
                new MusicLayout { Name = "popn26-1", MusicTableOffset = 0x2D0628, MusicRecordSize = 172, MusicCount = 1945, FileTableOffset = 0x2C00C0, FileRecordSize = 32 },
                new MusicLayout { Name = "popn26-2", MusicTableOffset = 0x2DB398, MusicRecordSize = 172, MusicCount = 2012, FileTableOffset = 0x2CA510, FileRecordSize = 32 },
                new MusicLayout { Name = "popn26-3", MusicTableOffset = 0x2DEA68, MusicRecordSize = 172, MusicCount = 2019, FileTableOffset = 0x2CDB00, FileRecordSize = 32 },
                new MusicLayout { Name = "popn27-1", MusicTableOffset = 0x2A7CE8, MusicRecordSize = 172, MusicCount = 2043, FileTableOffset = 0x296B00, FileRecordSize = 32 },
                new MusicLayout { Name = "popn27-2", MusicTableOffset = 0x2ADB10, MusicRecordSize = 172, MusicCount = 2056, FileTableOffset = 0x29C788, FileRecordSize = 32 },
                new MusicLayout { Name = "popn27-3", MusicTableOffset = 0x2AE1F0, MusicRecordSize = 172, MusicCount = 2071, FileTableOffset = 0x29CD88, FileRecordSize = 32 },
                new MusicLayout { Name = "popn27-4", MusicTableOffset = 0x2AEC50, MusicRecordSize = 172, MusicCount = 2081, FileTableOffset = 0x29D588, FileRecordSize = 32 },
                new MusicLayout { Name = "popn27-5", MusicTableOffset = 0x2B0010, MusicRecordSize = 172, MusicCount = 2090, FileTableOffset = 0x29E828, FileRecordSize = 32 },
                new MusicLayout { Name = "popn27-6", MusicTableOffset = 0x2B3130, MusicRecordSize = 172, MusicCount = 2099, FileTableOffset = 0x2A1828, FileRecordSize = 32 },
                new MusicLayout { Name = "popn27-7", MusicTableOffset = 0x2C5510, MusicRecordSize = 172, MusicCount = 2113, FileTableOffset = 0x2B2F28, FileRecordSize = 32 },
                new MusicLayout { Name = "popn28", MusicTableOffset = 0x2DB668, MusicRecordSize = 172, MusicCount = 2202, FileTableOffset = 0x2C8580, FileRecordSize = 32 },
            };
        }

        private static PeImage ReadPeImage(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException("Invalid DOS signature.");
                stream.Position = 0x3C;
                uint peOffset = reader.ReadUInt32();
                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550) throw new InvalidDataException("Invalid PE signature.");

                reader.ReadUInt16();
                ushort sectionCount = reader.ReadUInt16();
                stream.Position += 12;
                ushort optionalHeaderSize = reader.ReadUInt16();
                stream.Position += 2;

                long optionalHeaderStart = stream.Position;
                ushort magic = reader.ReadUInt16();
                ulong imageBase;
                if (magic == 0x10B) { stream.Position = optionalHeaderStart + 28; imageBase = reader.ReadUInt32(); }
                else if (magic == 0x20B) { stream.Position = optionalHeaderStart + 24; imageBase = reader.ReadUInt64(); }
                else throw new InvalidDataException("Unsupported PE optional header.");

                stream.Position = optionalHeaderStart + optionalHeaderSize;

                PeImage image = new PeImage { Data = data, ImageBase = imageBase };
                for (int i = 0; i < sectionCount; i++)
                {
                    stream.Position += 8;
                    uint vs = reader.ReadUInt32();
                    uint va = reader.ReadUInt32();
                    uint rs = reader.ReadUInt32();
                    uint ra = reader.ReadUInt32();
                    stream.Position += 16;
                    image.Sections.Add(new Section { VirtualAddress = va, VirtualSize = vs, RawAddress = ra, RawSize = rs });
                }

                return image;
            }
        }

        private static ushort ReadUInt16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);
        private static uint ReadUInt32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);

        private static string ReadRequiredString(PeImage image, byte[] data, int recordOffset, int valueOffset)
        {
            if (valueOffset < 0) return "";
            uint pointer = ReadUInt32(data, recordOffset + valueOffset);
            if (!image.TryReadString(pointer, out string value)) return "";
            return FixAccents(value);
        }

        private static bool IsUsableString(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            int bad = 0;
            foreach (char c in value) { if (Char.IsControl(c) && c != '\t') bad++; }
            return bad == 0;
        }

        private static bool IsRemovedSong(SongRecord song) => IsDash(song.Title) && IsDash(song.Artist) && IsDash(song.Genre) && IsDash(song.Comment);
        private static bool IsDummySong(SongRecord song) => song.Title == "ＤＵＭＭＹ" && song.Artist == "ＤＵＭＭＹ" && song.Genre == "ＤＵＭＭＹ";
        private static bool IsDash(string value) => value == "-" || value == "‐";

        private static bool[] AvailableCharts(uint mask, bool legacyAlwaysEasy) => new[]
        {
            legacyAlwaysEasy || (mask & 0x0080000) != 0, true,
            (mask & 0x1000000) != 0, (mask & 0x2000000) != 0
        };

        private static string ReadFileHandle(PeImage image, MusicLayout layout, ushort fileIndex)
        {
            if (layout.FileTableOffset <= 0) return "";
            int offset = layout.FileTableOffset + (layout.FileRecordSize * fileIndex);
            if (offset < 0 || offset + 8 > image.Data.Length) return "";
            uint folderPointer = ReadUInt32(image.Data, offset);
            uint namePointer = ReadUInt32(image.Data, offset + 4);
            string folder = "", name = "";
            bool folderOk = image.TryReadString(folderPointer, out folder);
            bool nameOk = image.TryReadString(namePointer, out name);
            Console.Error.WriteLine($"DEBUG ReadFileHandle: fileIndex={fileIndex} offset=0x{offset:X} folderPtr=0x{folderPointer:X} namePtr=0x{namePointer:X} folderOk={folderOk} nameOk={nameOk} folder=[{folder}] name=[{name}]");
            if (!folderOk || !nameOk) return "";
            return folder + "/" + name;
        }

        private static SongRecord ReadSong(PeImage image, MusicLayout layout, int songId)
        {
            int recordOffset = layout.MusicTableOffset + (layout.MusicRecordSize * songId);
            int minSize = Math.Max(layout.ChartMaskOffset + 4, layout.CategoryOffset + 4);
            if (recordOffset < 0 || recordOffset + minSize > image.Data.Length) return null;

            uint chartMask = ReadUInt32(image.Data, recordOffset + layout.ChartMaskOffset);
            uint category = ReadUInt32(image.Data, recordOffset + layout.CategoryOffset);
            if (category > Int32.MaxValue) return null;

            SongRecord song = new SongRecord
            {
                Id = songId,
                Genre = ReadRequiredString(image, image.Data, recordOffset, layout.GenreOffset),
                Title = ReadRequiredString(image, image.Data, recordOffset, layout.TitleOffset),
                Artist = ReadRequiredString(image, image.Data, recordOffset, layout.ArtistOffset),
                Comment = ReadRequiredString(image, image.Data, recordOffset, layout.CommentOffset),
                EnglishTitle = ReadRequiredString(image, image.Data, recordOffset, layout.EnglishTitleOffset),
                EnglishArtist = ReadRequiredString(image, image.Data, recordOffset, layout.EnglishArtistOffset),
                Category = (int)category
            };

            if (String.IsNullOrEmpty(song.Title) || String.IsNullOrEmpty(song.Artist) || String.IsNullOrEmpty(song.Genre)) return null;

            bool[] validCharts = AvailableCharts(chartMask, layout.LegacyAlwaysEasy);

            if (layout.LegacyDifficultyOrder)
            {
                song.Difficulties[0] = validCharts[0] ? image.Data[recordOffset + layout.DifficultyOffset + 2] : 0;
                song.Difficulties[1] = validCharts[1] ? image.Data[recordOffset + layout.DifficultyOffset + 0] : 0;
                song.Difficulties[2] = validCharts[2] ? image.Data[recordOffset + layout.DifficultyOffset + 1] : 0;
                song.Difficulties[3] = validCharts[3] ? image.Data[recordOffset + layout.DifficultyOffset + 3] : 0;
            }
            else
            {
                for (int i = 0; i < 4; i++)
                    song.Difficulties[i] = validCharts[i] ? image.Data[recordOffset + layout.DifficultyOffset + i] : 0;
            }

            if (layout.LegacyDifficultyOrder)
            {
                ushort normalIdx = ReadUInt16(image.Data, recordOffset + layout.FileIndexOffset + 0);
                ushort hyperIdx = ReadUInt16(image.Data, recordOffset + layout.FileIndexOffset + 2);
                ushort easyIdx = ReadUInt16(image.Data, recordOffset + layout.FileIndexOffset + 4);
                ushort exIdx = ReadUInt16(image.Data, recordOffset + layout.FileIndexOffset + 6);
                song.Files[0] = validCharts[0] ? ReadFileHandle(image, layout, easyIdx) : "";
                song.Files[1] = validCharts[1] ? ReadFileHandle(image, layout, normalIdx) : "";
                song.Files[2] = validCharts[2] ? ReadFileHandle(image, layout, hyperIdx) : "";
                song.Files[3] = validCharts[3] ? ReadFileHandle(image, layout, exIdx) : "";
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    ushort fileIndex = ReadUInt16(image.Data, recordOffset + layout.FileIndexOffset + (i * 2));
                    song.Files[i] = validCharts[i] ? ReadFileHandle(image, layout, fileIndex) : "";
                }
            }

            if (IsRemovedSong(song) || IsDummySong(song)) return null;
            return song;
        }

        private static (int start, int end) GetDataScanRange(PeImage image)
        {
            int start = 0, end = 0;
            foreach (Section section in image.Sections)
            {
                if (section.RawSize > 0 && section.RawAddress > 0)
                {
                    int sectionEnd = checked((int)(section.RawAddress + section.RawSize));
                    if (start == 0 || section.RawAddress < start) start = (int)section.RawAddress;
                    if (sectionEnd > end) end = sectionEnd;
                }
            }
            if (end == 0) end = Math.Min(image.Data.Length, 4 * 1024 * 1024);
            return (start, end);
        }

        private static MusicLayout DetectKnownLayout(PeImage image)
        {
            byte[] data = image.Data;
            foreach (MusicLayout candidate in GetKnownLayouts())
            {
                int recSize = candidate.MusicRecordSize;
                int checkCount = Math.Min(10, candidate.MusicCount);
                if (candidate.MusicTableOffset + (recSize * checkCount) > data.Length) continue;

                // Quick structure validation (no string resolution needed)
                int validCount = 0;
                for (int i = 0; i < checkCount; i++)
                {
                    int recOff = candidate.MusicTableOffset + (recSize * i);
                    if (recOff + candidate.DifficultyOffset + 4 > data.Length) break;
                    uint cat = ReadUInt32(data, recOff + candidate.CategoryOffset);
                    if (cat > 99) continue;
                    int d0 = data[recOff + candidate.DifficultyOffset + 0];
                    int d1 = data[recOff + candidate.DifficultyOffset + 1];
                    int d2 = data[recOff + candidate.DifficultyOffset + 2];
                    int d3 = data[recOff + candidate.DifficultyOffset + 3];
                    if (d0 >= 0 && d0 <= 50 && d1 >= 0 && d1 <= 50 && d2 >= 0 && d2 <= 50 && d3 >= 0 && d3 <= 50)
                        validCount++;
                }
                if (validCount < 3) continue;

                // Verify file table pointers are within PE image bounds
                if (candidate.FileTableOffset > 0)
                {
                    if (candidate.FileTableOffset + (candidate.FileRecordSize * 2) > data.Length) continue;
                    bool fileOk = true;
                    for (int i = 0; i < 2 && fileOk; i++)
                    {
                        int fileOff = candidate.FileTableOffset + (candidate.FileRecordSize * i);
                        uint fp = ReadUInt32(data, fileOff);
                        uint np = ReadUInt32(data, fileOff + 4);
                        // Verify pointers can actually be resolved to valid strings
                        if (!image.TryReadString(fp, out string _)) fileOk = false;
                        if (!image.TryReadString(np, out string _)) fileOk = false;
                    }
                    if (!fileOk) continue;
                }

                return candidate;
            }
            return null;
        }

        private static bool ValidateDetectedOffset(PeImage image, MusicLayout layout)
        {
            int maxCheck = Math.Min(40, (image.Data.Length - layout.MusicTableOffset) / layout.MusicRecordSize);
            int firstValid = -1;
            int validCount = 0;
            for (int i = 0; i < maxCheck; i++)
            {
                SongRecord song = ReadSong(image, layout, i);
                if (song != null) { validCount++; if (firstValid < 0) firstValid = i; }
            }
            return firstValid >= 0 && firstValid < 10 && validCount >= 15;
        }

        private static MusicLayout DetectLayout(PeImage image)
        {
            // Evaluate all known layouts, pick the one with the most valid songs
            // and a matching file table
            MusicLayout bestLayout = null;
            int bestSongCount = 0;

            foreach (MusicLayout candidate in GetKnownLayouts())
            {
                int recSize = candidate.MusicRecordSize;
                int checkCount = Math.Min(10, candidate.MusicCount);
                if (candidate.MusicTableOffset + (recSize * checkCount) > image.Data.Length) continue;

                // Count valid songs in the table at this offset
                int songCount = 0;
                for (int i = 0; i < checkCount; i++)
                {
                    SongRecord song = ReadSong(image, candidate, i);
                    if (song != null) songCount++;
                }

                if (songCount < 5) continue;

                // Verify file table has resolvable string pointers forming path-like entries
                if (candidate.FileTableOffset > 0)
                {
                    if (candidate.FileTableOffset + (candidate.FileRecordSize * 4) > image.Data.Length) continue;
                    int fileHits = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int fileOff = candidate.FileTableOffset + (candidate.FileRecordSize * i);
                        uint fp = ReadUInt32(image.Data, fileOff);
                        uint np = ReadUInt32(image.Data, fileOff + 4);
                        if (image.TryReadString(fp, out string folder) &&
                            image.TryReadString(np, out string name) &&
                            folder.Length >= 4 && folder.Length <= 32 &&
                            name.Length >= 4 && name.Length <= 32)
                            fileHits++;
                    }
                    if (fileHits < 3) continue;
                }

                if (songCount > bestSongCount)
                {
                    // Passes all checks - accept this as the best layout so far
                    bestSongCount = songCount;
                    bestLayout = candidate;
                }
            }

            if (bestLayout != null)
            {
                Console.WriteLine("Layout: " + bestLayout.Name + " (offset=0x" + bestLayout.MusicTableOffset.ToString("X") + ", size=" + bestLayout.MusicRecordSize + ", count=" + bestLayout.MusicCount + ")");
                return bestLayout;
            }

            throw new InvalidDataException("No known music table layout matches this DLL. If you know the correct offset, add it to GetKnownLayouts().");
        }

        private static bool ValidateDetectedMusicTable(PeImage image, MusicLayout layout) => ValidateDetectedOffset(image, layout);

        private static int DetectRecordCount(PeImage image, MusicLayout layout)
        {
            int maxRecords = (image.Data.Length - layout.MusicTableOffset) / layout.MusicRecordSize;
            int lastSong = 0;
            for (int i = 0; i < maxRecords; i++)
            {
                SongRecord song = ReadSong(image, layout, i);
                if (song != null) lastSong = i;
            }
            return lastSong + 1;
        }

        private static int DetectFileTable(PeImage image, MusicLayout layout)
        {
            var (scanStart, scanEnd) = GetDataScanRange(image);
            int bestOffset = 0;
            int bestScore = 0;
            int frs = layout.FileRecordSize;
            int maxOffset = scanEnd - (frs * 32);
            for (int offset = scanStart; offset < maxOffset; offset += 4)
            {
                if (Math.Abs(offset - layout.MusicTableOffset) < 4096) continue;
                int score = 0;
                for (int i = 0; i < 32; i++)
                {
                    int record = offset + (frs * i);
                    uint fp = ReadUInt32(image.Data, record);
                    uint np = ReadUInt32(image.Data, record + 4);
                    if (image.TryReadString(fp, out string f) && image.TryReadString(np, out string n) && f.Length <= 64 && n.Length <= 64)
                        score++;
                }
                if (score > bestScore) { bestOffset = offset; bestScore = score; if (score >= 30) break; }
            }
            return bestScore >= 12 ? bestOffset : 0;
        }

        private static List<SongRecord> ConvertDll(string sourceFileName)
        {
            byte[] data = File.ReadAllBytes(sourceFileName);
            PeImage image = ReadPeImage(data);
            MusicLayout layout = DetectLayout(image);

            Console.WriteLine("music table: 0x" + layout.MusicTableOffset.ToString("X"));
            Console.WriteLine("music size : " + layout.MusicRecordSize);
            Console.WriteLine("music count: " + layout.MusicCount);
            Console.WriteLine("file table : 0x" + (layout.FileTableOffset > 0 ? layout.FileTableOffset.ToString("X") : "not detected"));
            Console.WriteLine("file size  : " + layout.FileRecordSize);

            List<SongRecord> songs = new List<SongRecord>();
            for (int i = 0; i < layout.MusicCount; i++)
            {
                SongRecord song = ReadSong(image, layout, i);
                if (song != null) songs.Add(song);
            }
            return songs;
        }

        private static string DisplayTitle(SongRecord song) => !String.IsNullOrEmpty(song.EnglishTitle) ? song.EnglishTitle : song.Title;
        private static string DisplayArtist(SongRecord song) => !String.IsNullOrEmpty(song.EnglishArtist) ? song.EnglishArtist : song.Artist;

        private static IEnumerable<string> DatabaseKeys(SongRecord song)
        {
            foreach (string file in song.Files)
            {
                if (String.IsNullOrEmpty(file)) continue;
                string stem = Path.GetFileNameWithoutExtension(file.Replace('/', Path.DirectorySeparatorChar));
                if (String.IsNullOrEmpty(stem)) continue;
                yield return stem;
                foreach (string suffix in new[] { "_ep", "_np", "_hp", "_op" })
                {
                    if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        yield return stem.Substring(0, stem.Length - suffix.Length);
                }
            }
        }

        private static void WriteSong(Configuration config, string key, SongRecord song)
        {
            InfoCollection section = config[key];
            section["TITLE"] = DisplayTitle(song);
            section["ARTIST"] = DisplayArtist(song);
            section["GENRE"] = song.Genre;
            section["CATEGORY"] = song.Category.ToString("00");
            section["DIFFICULTYDP1"] = song.Difficulties[0].ToString();
            section["DIFFICULTYDP2"] = song.Difficulties[1].ToString();
            section["DIFFICULTYDP3"] = song.Difficulties[2].ToString();
            section["DIFFICULTYDP4"] = song.Difficulties[3].ToString();
            SetOptional(section, "FILEEASY", song.Files[0]);
            SetOptional(section, "FILENORMAL", song.Files[1]);
            SetOptional(section, "FILEHYPER", song.Files[2]);
            SetOptional(section, "FILEEX", song.Files[3]);
        }

        private static void SetOptional(InfoCollection section, string key, string value)
        {
            if (!String.IsNullOrEmpty(value)) section[key] = value;
        }

        private static void WriteDatabase(List<SongRecord> songs)
        {
            Configuration result = Configuration.ReadFile("PopnDB");
            foreach (SongRecord song in songs)
            {
                foreach (string key in DatabaseKeys(song).Distinct(StringComparer.OrdinalIgnoreCase))
                    WriteSong(result, key, song);
            }
            result.WriteFile("PopnDB");
        }

        private static string FixAccents(string value)
        {
            var accents = new Dictionary<string, string>
            {
                { "鵝", "7" }, { "圄", "à" }, { "圉", "ä" }, { "鵤", "Ä" }, { "鵑", "👁" },
                { "鶤", "©" }, { "圈", "é" }, { "鵐", "ê" }, { "鵙", "Ə" }, { "鵲", "ë" },
                { "！", "!" }, { "囿", "♥" }, { "鶚", "㊙" }, { "鶉", "ó" }, { "鶇", "ö" },
                { "鶲", "Ⓟ" }, { "鶫", "²" }, { "圍", "@" }, { "圖", "ţ" }, { "鵺", "Ü" },
                { "囎", ":" }, { "囂", "♡" }, { "釁", "🐾" }, { "佰", "你" }, { "罕", "έ" },
                { "罔", "ς" }, { "彑", "Ø" }, { "冫", "ꓘ" }, { "炙", "焱" },
            };
            foreach (var a in accents) value = value.Replace(a.Key, a.Value);
            return value;
        }

        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (args.Length != 1)
            {
                Console.WriteLine();
                Console.WriteLine("Usage: PopnDBGenerator <input dll>");
                Console.WriteLine();
                Console.WriteLine("Drag and drop with files and folders is fully supported for this application.");
                Console.WriteLine();
                Console.WriteLine("Supported file:");
                Console.WriteLine("popn DLL/EXE containing the music database");
                return;
            }

            string sourceFileName = args[0];
            Console.WriteLine("inputFile : " + sourceFileName);

            try
            {
                List<SongRecord> songs = ConvertDll(sourceFileName);
                WriteDatabase(songs);
                Console.WriteLine("PopnDB entries generated: " + songs.Count);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}