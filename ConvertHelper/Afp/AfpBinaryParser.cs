using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ConvertHelper.Afp
{
    internal static class AfpBinaryParser
    {
        private const int ShapeTag = 0x84;
        private const int SpriteTag = 0x79;
        private const int ActionTag = 0x7A;
        private const int PlaceTag = 0x7F;
        private const int RemoveTag = 0x80;

        public static AfpMovie ParseMovie(string fileName, byte[] source, byte[] bsi)
        {
            byte[] data = Descramble(source, bsi);
            if (data.Length < 60)
                throw new InvalidDataException("AFP header is truncated: " + fileName);

            uint length = U32(data, 4);
            if (length != data.Length)
                throw new InvalidDataException("AFP length does not match its header: " + fileName);

            int flags = I32(data, 12);
            ushort left = U16(data, 16);
            ushort right = U16(data, 18);
            ushort top = U16(data, 20);
            ushort bottom = U16(data, 22);
            double fps = (flags & 0x2) != 0 ? I32(data, 24) / 1024.0 : F32(data, 24);

            uint stringOffset = U32(data, 48);
            uint stringSize = U32(data, 52);
            Dictionary<int, string> strings = DescrambleStrings(data, checked((int)stringOffset), checked((int)stringSize));
            string GetString(int offset) => offset == 0 ? "" : strings.TryGetValue(offset, out string value)
                ? value
                : throw new InvalidDataException("AFP string offset is invalid: " + offset);

            AfpMovie movie = new AfpMovie
            {
                FileName = fileName,
                ExportedName = GetString(U16(data, 10)),
                Fps = fps,
                Width = right - left,
                Height = bottom - top,
            };

            int exportCount = U16(data, 32);
            int exportOffset = checked((int)U32(data, 40));
            for (int i = 0; i < exportCount; i++)
            {
                int tagId = U16(data, exportOffset);
                string name = GetString(U16(data, exportOffset + 2));
                movie.ExportedTags[name] = tagId;
                exportOffset += 4;
            }

            int tagsOffset = checked((int)U32(data, 36));
            movie.Root = ParseTimeline(data, tagsOffset, null, movie.ExportedName, GetString);

            int importCount = I16(data, 34);
            int importOffset = checked((int)U32(data, 44));
            int importDataOffset = importOffset + 4 * importCount;
            for (int i = 0; i < importCount; i++)
            {
                string swfName = GetString(U16(data, importOffset));
                int count = U16(data, importOffset + 2);
                for (int j = 0; j < count; j++)
                {
                    int tagId = U16(data, importDataOffset);
                    movie.ImportedTags[tagId] = new AfpImport
                    {
                        SwfName = swfName,
                        TagName = GetString(U16(data, importDataOffset + 2)),
                    };
                    importDataOffset += 4;
                }
                importOffset += 4;
            }

            return movie;
        }

        public static AfpShape ParseShape(string reference, byte[] data)
        {
            if (data.Length < 52)
                throw new InvalidDataException("GEO header is truncated: " + reference);
            bool littleEndian;
            string magic = Encoding.ASCII.GetString(data, 0, 4);
            if (magic == "D2EG") littleEndian = true;
            else if (magic == "GE2D") littleEndian = false;
            else throw new InvalidDataException("Invalid GEO magic: " + reference);

            uint fileSize = ReadU32(data, 12, littleEndian);
            if (fileSize != data.Length)
                throw new InvalidDataException("GEO length does not match its header: " + reference);

            int vertexCount = ReadU16(data, 20, littleEndian);
            int labelCount = ReadU16(data, 26, littleEndian);
            int paramsCount = ReadU16(data, 28, littleEndian);
            int vertexOffset = checked((int)ReadU32(data, 32, littleEndian));
            int labelOffset = checked((int)ReadU32(data, 44, littleEndian));
            int paramsOffset = checked((int)ReadU32(data, 48, littleEndian));
            List<string> labels = new List<string>();
            for (int i = 0; i < labelCount; i++)
            {
                int pointer = checked((int)ReadU32(data, labelOffset + i * 4, littleEndian));
                int end = pointer;
                while (end < data.Length && data[end] != 0) end++;
                byte[] value = data.AsSpan(pointer, end - pointer).ToArray();
                if (value.Length > 0 && value[0] - 0x20 > 0x7F)
                {
                    for (int x = 0; x < value.Length; x++) value[x] = unchecked((byte)(value[x] + 0x80));
                }
                labels.Add(Encoding.ASCII.GetString(value));
            }

            double minX = Double.MaxValue, minY = Double.MaxValue, maxX = Double.MinValue, maxY = Double.MinValue;
            for (int i = 0; i < vertexCount; i++)
            {
                double x = ReadF32(data, vertexOffset + i * 8, littleEndian);
                double y = ReadF32(data, vertexOffset + i * 8 + 4, littleEndian);
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            AfpShape shape = new AfpShape
            {
                Reference = reference,
                Width = vertexCount == 0 ? 0 : Math.Max(1, (int)Math.Round(maxX - minX)),
                Height = vertexCount == 0 ? 0 : Math.Max(1, (int)Math.Round(maxY - minY)),
            };
            for (int i = 0; i < paramsCount; i++)
            {
                int offset = paramsOffset + i * 16;
                int mode = data[offset];
                int flags = data[offset + 1];
                int textureIndex = data[offset + 2];
                uint rgba = ReadU32(data, offset + 8, littleEndian);
                if (mode != 4)
                    throw new InvalidDataException("Unsupported GEO draw mode in " + reference);

                AfpDrawParams draw = new AfpDrawParams { Flags = flags };
                if ((flags & 0x2) != 0)
                {
                    if (textureIndex == 0xFF || textureIndex >= labels.Count)
                        throw new InvalidDataException("Invalid GEO texture reference in " + reference);
                    draw.TextureName = labels[textureIndex];
                }
                if ((flags & 0x8) != 0)
                {
                    draw.BlendColor = new AfpColor(
                        ((rgba >> 24) & 0xFF) / 255.0,
                        ((rgba >> 16) & 0xFF) / 255.0,
                        ((rgba >> 8) & 0xFF) / 255.0,
                        (rgba & 0xFF) / 255.0);
                }
                shape.DrawParams.Add(draw);
            }
            return shape;
        }

        private static AfpTimeline ParseTimeline(byte[] data, int baseOffset, int? tagId, string movieName, Func<int, string> getString)
        {
            int nameCount = U16(data, baseOffset + 2);
            int frameCount = checked((int)U32(data, baseOffset + 4));
            int tagCount = checked((int)U32(data, baseOffset + 8));
            int namesOffset = baseOffset + checked((int)U32(data, baseOffset + 12));
            int framesOffset = baseOffset + checked((int)U32(data, baseOffset + 16));
            int tagsOffset = baseOffset + checked((int)U32(data, baseOffset + 20));

            AfpTimeline timeline = new AfpTimeline { TagId = tagId, MovieName = movieName };
            for (int i = 0; i < frameCount; i++)
            {
                uint frame = U32(data, framesOffset + i * 4);
                timeline.Frames.Add(new AfpFrame
                {
                    StartTag = (int)(frame & 0xFFFFF),
                    TagCount = (int)((frame >> 20) & 0xFFF),
                });
            }

            int offset = tagsOffset;
            for (int i = 0; i < tagCount; i++)
            {
                uint header = U32(data, offset);
                int currentTagId = (int)((header >> 22) & 0x3FF);
                int size = (int)(header & 0x3FFFFF);
                int payload = offset + 4;
                AfpTag tag;
                switch (currentTagId)
                {
                    case ShapeTag:
                        if (size != 4) throw new InvalidDataException("Invalid AP2 shape tag size.");
                        int shapeId = U16(data, payload + 2);
                        tag = new AfpShapeTag { Id = shapeId, Reference = movieName + "_shape" + shapeId };
                        break;
                    case SpriteTag:
                        int spriteFlags = U16(data, payload);
                        int spriteId = U16(data, payload + 2);
                        int subOffset = (spriteFlags & 1) == 0
                            ? payload + 4
                            : payload + checked((int)U32(data, payload + 4));
                        tag = new AfpSpriteTag
                        {
                            Id = spriteId,
                            Timeline = ParseTimeline(data, subOffset, spriteId, movieName, getString),
                        };
                        break;
                    case PlaceTag:
                        tag = ParsePlace(data.AsSpan(payload, size));
                        break;
                    case RemoveTag:
                        if (size != 4) throw new InvalidDataException("Invalid AP2 remove tag size.");
                        tag = new AfpRemoveTag { ObjectId = U16(data, payload), Depth = U16(data, payload + 2) };
                        break;
                    case ActionTag:
                        tag = new AfpActionTag { ByteCode = data.AsSpan(payload, size).ToArray() };
                        break;
                    default:
                        tag = new AfpActionTag { ByteCode = Array.Empty<byte>() };
                        break;
                }
                timeline.Tags.Add(tag);
                offset += 4 + ((size + 3) & ~3);
            }

            for (int i = 0; i < nameCount; i++)
            {
                int frame = U16(data, namesOffset + i * 4);
                timeline.Labels[getString(U16(data, namesOffset + i * 4 + 2))] = frame;
            }
            return timeline;
        }

        private static AfpPlaceTag ParsePlace(ReadOnlySpan<byte> data)
        {
            ulong flags = U32(data, 0);
            int objectId = U16(data, 6);
            int depth = U16(data, 4);
            int pointer = 8;
            if ((flags & 0x80000000UL) != 0)
            {
                flags |= (ulong)U32(data, pointer) << 32;
                pointer += 4;
            }

            int? sourceId = null;
            if ((flags & 0x2) != 0) { sourceId = U16(data, pointer); pointer += 2; }
            if ((flags & 0x10) != 0) pointer += 2;
            if ((flags & 0x20) != 0) pointer += 2;
            if ((flags & 0x40) != 0) pointer += 2;

            int? blend = null;
            if ((flags & 0x20000) != 0) { blend = data[pointer]; pointer++; }
            pointer = Align4(pointer);

            AfpMatrix transform = AfpMatrix.Identity();
            if ((flags & 0x100) != 0)
            {
                transform.A11 = I32(data, pointer) / 1024.0;
                transform.A22 = I32(data, pointer + 4) / 1024.0;
                transform.ScaleSet = true;
                pointer += 8;
            }
            if ((flags & 0x200) != 0)
            {
                transform.A12 = I32(data, pointer) / 1024.0;
                transform.A21 = I32(data, pointer + 4) / 1024.0;
                transform.RotateSet = true;
                pointer += 8;
            }
            if ((flags & 0x400) != 0)
            {
                transform.A41 = I32(data, pointer) / 20.0;
                transform.A42 = I32(data, pointer + 4) / 20.0;
                transform.TranslateXySet = true;
                pointer += 8;
            }

            AfpColor multiply = AfpColor.White;
            AfpColor add = AfpColor.Transparent;
            if ((flags & 0x800) != 0)
            {
                multiply = new AfpColor(I16(data, pointer) / 255.0, I16(data, pointer + 2) / 255.0,
                    I16(data, pointer + 4) / 255.0, I16(data, pointer + 6) / 255.0);
                pointer += 8;
            }
            if ((flags & 0x1000) != 0)
            {
                add = new AfpColor(I16(data, pointer) / 255.0, I16(data, pointer + 2) / 255.0,
                    I16(data, pointer + 4) / 255.0, I16(data, pointer + 6) / 255.0);
                pointer += 8;
            }
            if ((flags & 0x2000) != 0) { multiply = PackedColor(U32(data, pointer)); pointer += 4; }
            if ((flags & 0x4000) != 0) { add = PackedColor(U32(data, pointer)); pointer += 4; }

            if ((flags & 0x80) != 0)
            {
                int eventSize = checked((int)U32(data, pointer + 4));
                pointer += eventSize;
            }
            if ((flags & 0x10000) != 0)
            {
                int filterSize = U16(data, pointer + 2);
                pointer += filterSize;
            }

            AfpPoint? origin = null;
            if ((flags & 0x1000000) != 0)
            {
                origin = new AfpPoint(I32(data, pointer) / 20.0, I32(data, pointer + 4) / 20.0);
                pointer += 8;
            }
            if ((flags & 0x200000000UL) != 0)
            {
                double z = I32(data, pointer) / 20.0;
                AfpPoint old = origin ?? AfpPoint.Zero;
                origin = new AfpPoint(old.X, old.Y, z);
                pointer += 4;
            }
            if ((flags & 0x2000000) != 0) origin = AfpPoint.Zero;

            if ((flags & 0x40000) != 0 && pointer < data.Length)
            {
                transform.A11 = I16(data, pointer) / 32768.0;
                transform.A22 = I16(data, pointer + 2) / 32768.0;
                transform.ScaleSet = true;
                pointer += 4;
            }
            if ((flags & 0x80000) != 0)
            {
                transform.A12 = I16(data, pointer) / 32768.0;
                transform.A21 = I16(data, pointer + 2) / 32768.0;
                transform.RotateSet = true;
                pointer += 4;
            }
            if ((flags & 0x100000) != 0) pointer += 2;
            pointer = Align4(pointer);

            if ((flags & 0x8000000) != 0)
            {
                transform.A43 = I32(data, pointer) / 20.0;
                transform.TranslateZSet = true;
                pointer += 4;
            }
            if ((flags & 0x10000000) != 0)
            {
                double[] values = new double[9];
                for (int i = 0; i < 9; i++) values[i] = I32(data, pointer + i * 4) / 1024.0;
                pointer += 36;
                if (!transform.ScaleSet) { transform.A11 = values[0]; transform.A22 = values[4]; }
                if (!transform.RotateSet) { transform.A12 = values[1]; transform.A21 = values[3]; }
                transform.A13 = values[2]; transform.A23 = values[5];
                transform.A31 = values[6]; transform.A32 = values[7]; transform.A33 = values[8];
                transform.Grid3DSet = true;
            }

            AfpHsl? hsl = null;
            if ((flags & 0x20000000) != 0)
            {
                hsl = new AfpHsl(I16(data, pointer) / 360.0, unchecked((sbyte)data[pointer + 2]) / 100.0,
                    unchecked((sbyte)data[pointer + 3]) / 100.0);
                pointer += 4;
            }
            if ((flags & 0x800000000UL) != 0)
            {
                uint bitmask = U32(data, pointer);
                pointer += 4;
                for (int bit = 0; bit < 32; bit++)
                {
                    if ((bitmask & (1U << bit)) == 0) continue;
                    int unknownFlags = U16(data, pointer);
                    int count = U16(data, pointer + 2);
                    pointer += 4;
                    pointer += (((unknownFlags & 0x10) | 0x8) >> 2) * ((unknownFlags & 1) + 1) * count * 2;
                }
            }
            if ((flags & 0x1000000000UL) != 0) pointer += 8;
            if ((flags & 0x2000000000UL) != 0) pointer += 6;
            pointer = Align4(pointer);

            bool useTransform = (flags & 0x18000004UL) != 0;
            int projection = useTransform ? AfpPlaceTag.ProjectionAffine : AfpPlaceTag.ProjectionNone;
            if ((flags & 0x4000000) != 0) projection = AfpPlaceTag.ProjectionPerspective;
            else transform = transform.ToAffine();

            return new AfpPlaceTag
            {
                ObjectId = objectId,
                Depth = depth,
                SourceTagId = sourceId,
                Blend = blend,
                Update = (flags & 1) != 0,
                Transform = useTransform ? transform : null,
                RotationOrigin = origin,
                Projection = projection,
                MultiplyColor = (flags & 0x8) != 0 ? multiply : null,
                AddColor = (flags & 0x8) != 0 ? add : null,
                HslShift = hsl,
            };
        }

        private static AfpColor PackedColor(uint rgba) => new AfpColor(
            ((rgba >> 24) & 0xFF) / 255.0,
            ((rgba >> 16) & 0xFF) / 255.0,
            ((rgba >> 8) & 0xFF) / 255.0,
            (rgba & 0xFF) / 255.0);

        private static byte[] Descramble(byte[] source, byte[] bsi)
        {
            byte[] data = (byte[])source.Clone();
            int dataOffset = 0;
            for (int i = 0; i + 1 < bsi.Length; i += 2)
            {
                ushort word = U16(bsi, i);
                if (word == 0) break;
                int offset = (word & 0x7F) * 2;
                int type = (word >> 13) & 7;
                int loops = (word >> 7) & 0x3F;
                dataOffset += offset;
                if (type == 0) { dataOffset += 256 * loops; continue; }
                int length = type == 1 ? 2 : type == 2 ? 4 : type == 3 ? 8 : 0;
                if (length == 0) throw new InvalidDataException("Unsupported AFP byte-swap operation.");
                for (int loop = 0; loop <= loops; loop++)
                {
                    Array.Reverse(data, dataOffset, length);
                    dataOffset += length;
                }
            }
            return data;
        }

        private static Dictionary<int, string> DescrambleStrings(byte[] data, int offset, int size)
        {
            Dictionary<int, string> strings = new Dictionary<int, string>();
            List<byte> current = new List<byte>();
            int currentOffset = offset;
            int addition = 128;
            for (int i = 0; i < size; i++)
            {
                byte value = unchecked((byte)(data[offset + i] - addition));
                data[offset + i] = value;
                addition++;
                if (value == 0)
                {
                    if (current.Count > 0)
                    {
                        strings[currentOffset - offset] = Encoding.UTF8.GetString(current.ToArray());
                        current.Clear();
                    }
                    currentOffset = offset + i + 1;
                }
                else current.Add(value);
            }
            return strings;
        }

        private static int Align4(int value) => (value + 3) & ~3;
        private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        private static short I16(byte[] data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2));
        private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        private static int I32(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        private static float F32(byte[] data, int offset) => BitConverter.Int32BitsToSingle(I32(data, offset));
        private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        private static short I16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
        private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        private static int I32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        private static ushort ReadU16(byte[] data, int offset, bool little) => little
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        private static uint ReadU32(byte[] data, int offset, bool little) => little
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
        private static float ReadF32(byte[] data, int offset, bool little)
        {
            int bits = little
                ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4))
                : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
            return BitConverter.Int32BitsToSingle(bits);
        }
    }
}
