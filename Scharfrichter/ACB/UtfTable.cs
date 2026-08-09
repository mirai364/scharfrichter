using System;
using System.Collections.Generic;
using System.IO;

namespace Scharfrichter.Codec.ACB
{
    public sealed class UtfHeader
    {
        internal UtfHeader()
        {
        }

        public uint TableSize { get; set; }
        public ushort Unknown1 { get; set; }
        public uint PerRowDataOffset { get; set; }
        public uint StringTableOffset { get; set; }
        public uint ExtraDataOffset { get; set; }
        public uint TableNameOffset { get; set; }
        public string TableName { get; set; }
        public ushort FieldCount { get; set; }
        public ushort RowSize { get; set; }
        public uint RowCount { get; set; }
    }

    public class UtfTable : IDisposable
    {
        private static readonly byte[] UtfSignature = { 0x40, 0x55, 0x54, 0x46 }; // '@UTF'

        internal UtfTable(Stream stream, long offset, long size, string acbFileName, bool disposeStream)
        {
            _acbFileName = acbFileName;
            _stream = stream;
            _offset = offset;
            _size = size;
            _disposeStream = disposeStream;
        }

        public Stream Stream => _stream;
        public string AcbFileName => _acbFileName;
        public long Offset => _offset;
        public long Size => _size;
        public bool IsEncrypted => _isEncrypted;
        public UtfHeader Header => _utfHeader;
        public Dictionary<string, UtfField>[] Rows => _rows;

        internal virtual void Initialize()
        {
            Stream stream = _stream;
            long offset = _offset;

            byte[] magic = PeekBytes(stream, offset, 4);
            magic = CheckEncryption(magic);
            if (!AreDataIdentical(magic, UtfSignature))
            {
                throw new FormatException($"'@UTF' signature (or its encrypted equivalent) is not found in '{_acbFileName}' at offset 0x{offset:x8}.");
            }
            using (Stream tableDataStream = GetTableDataStream())
            {
                UtfHeader header = GetUtfHeader(tableDataStream);
                _utfHeader = header;
                _rows = new Dictionary<string, UtfField>[header.RowCount];
                if (header.TableSize > 0)
                {
                    InitializeUtfSchema(stream, tableDataStream, 0x20);
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && _disposeStream)
            {
                try
                {
                    _stream.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private static bool GetKeysForEncryptedUtfTable(byte[] encryptedUtfSignature, out byte seed, out byte increment)
        {
            for (int s = 0; s <= byte.MaxValue; s++)
            {
                if ((encryptedUtfSignature[0] ^ s) != UtfSignature[0])
                {
                    continue;
                }
                for (int i = 0; i <= byte.MaxValue; i++)
                {
                    byte m = (byte)(s * i);
                    if ((encryptedUtfSignature[1] ^ m) != UtfSignature[1])
                    {
                        continue;
                    }
                    byte t = (byte)i;
                    for (int j = 2; j < UtfSignature.Length; j++)
                    {
                        m = (byte)(m * t);
                        if ((encryptedUtfSignature[j] ^ m) != UtfSignature[j])
                        {
                            break;
                        }
                        if (j < UtfSignature.Length - 1)
                        {
                            continue;
                        }
                        seed = (byte)s;
                        increment = (byte)i;
                        return true;
                    }
                }
            }
            seed = 0;
            increment = 0;
            return false;
        }

        private byte[] CheckEncryption(byte[] magicBytes)
        {
            if (AreDataIdentical(magicBytes, UtfSignature))
            {
                _isEncrypted = false;
                _utfReader = new UtfReader();
                return magicBytes;
            }
            else
            {
                _isEncrypted = true;
                byte seed, increment;
                bool keysFound = GetKeysForEncryptedUtfTable(magicBytes, out seed, out increment);
                if (!keysFound)
                {
                    throw new FormatException($"Unable to decrypt UTF table at offset 0x{_offset:x8}");
                }
                else
                {
                    _utfReader = new UtfReader(seed, increment);
                }
                return UtfSignature;
            }
        }

        private Stream GetTableDataStream()
        {
            Stream stream = _stream;
            long offset = _offset;
            int tableSize = (int)_utfReader.PeekUInt32(stream, offset, 4) + 8;
            if (!IsEncrypted)
            {
                return ExtractToNewStream(stream, offset, tableSize);
            }
            long originalPosition = stream.Position;
            byte[] memory = new byte[tableSize];
            int totalBytesRead = 0;
            int currentIndex = 0;
            long currentOffset = offset;
            do
            {
                int shouldRead = tableSize - totalBytesRead;
                byte[] buffer = _utfReader.PeekBytes(stream, currentOffset, shouldRead, totalBytesRead);
                Array.Copy(buffer, 0, memory, currentIndex, buffer.Length);
                currentOffset += buffer.Length;
                currentIndex += buffer.Length;
                totalBytesRead += buffer.Length;
            } while (totalBytesRead < tableSize);
            stream.Position = originalPosition;
            return new MemoryStream(memory, false)
            {
                Capacity = tableSize
            };
        }

        private static UtfHeader GetUtfHeader(Stream stream)
        {
            return GetUtfHeader(stream, stream.Position);
        }

        private static UtfHeader GetUtfHeader(Stream stream, long offset)
        {
            if (offset != stream.Position)
            {
                stream.Seek(offset, SeekOrigin.Begin);
            }
            UtfHeader header = new UtfHeader
            {
                TableSize = PeekUInt32BE(stream, offset + 4),
                Unknown1 = PeekUInt16BE(stream, offset + 8),
                PerRowDataOffset = (uint)(PeekUInt16BE(stream, offset + 10) + 8),
                StringTableOffset = PeekUInt32BE(stream, offset + 12) + 8,
                ExtraDataOffset = PeekUInt32BE(stream, offset + 16) + 8,
                TableNameOffset = PeekUInt32BE(stream, offset + 20),
                FieldCount = PeekUInt16BE(stream, offset + 24),
                RowSize = PeekUInt16BE(stream, offset + 26),
                RowCount = PeekUInt32BE(stream, offset + 28)
            };
            header.TableName = ReadZeroEndedAsciiAt(stream, header.StringTableOffset + header.TableNameOffset);
            return header;
        }

        private void InitializeUtfSchema(Stream sourceStream, Stream tableDataStream, long schemaOffset)
        {
            UtfHeader header = _utfHeader;
            Dictionary<string, UtfField>[] rows = _rows;
            long baseOffset = _offset;
            for (uint i = 0; i < header.RowCount; i++)
            {
                long currentOffset = schemaOffset;
                Dictionary<string, UtfField> row = new Dictionary<string, UtfField>();
                rows[i] = row;
                long currentRowOffset = 0;
                long currentRowBase = header.PerRowDataOffset + header.RowSize * i;

                for (int j = 0; j < header.FieldCount; j++)
                {
                    UtfField field = new UtfField
                    {
                        Type = PeekByteAt(tableDataStream, currentOffset)
                    };

                    long nameOffset = PeekInt32BE(tableDataStream, currentOffset + 1);
                    field.Name = ReadZeroEndedAsciiAt(tableDataStream, header.StringTableOffset + nameOffset);

                    NumericUnion union = new NumericUnion();
                    ColumnStorage constrainedStorage = (ColumnStorage)(field.Type & (byte)ColumnStorage.Mask);
                    ColumnType constrainedType = (ColumnType)(field.Type & (byte)ColumnType.Mask);
                    switch (constrainedStorage)
                    {
                        case ColumnStorage.Constant:
                        case ColumnStorage.Constant2:
                            {
                                long constantOffset = currentOffset + 5;
                                long dataOffset;
                                switch (constrainedType)
                                {
                                    case ColumnType.String:
                                        dataOffset = PeekInt32BE(tableDataStream, constantOffset);
                                        field.StringValue = ReadZeroEndedAsciiAt(tableDataStream, header.StringTableOffset + dataOffset);
                                        currentOffset += 4;
                                        break;
                                    case ColumnType.Int64:
                                        union.S64 = PeekInt64BE(tableDataStream, constantOffset);
                                        currentOffset += 8;
                                        break;
                                    case ColumnType.UInt64:
                                        union.U64 = PeekUInt64BE(tableDataStream, constantOffset);
                                        currentOffset += 8;
                                        break;
                                    case ColumnType.Data:
                                        dataOffset = PeekUInt32BE(tableDataStream, constantOffset);
                                        long dataSize = PeekUInt32BE(tableDataStream, constantOffset + 4);
                                        field.Offset = baseOffset + header.ExtraDataOffset + dataOffset;
                                        field.Size = dataSize;
                                        field.DataValue = PeekBytes(sourceStream, field.Offset, (int)dataSize);
                                        currentOffset += 8;
                                        break;
                                    case ColumnType.Double:
                                        union.R64 = PeekDoubleBE(tableDataStream, constantOffset);
                                        currentOffset += 8;
                                        break;
                                    case ColumnType.Single:
                                        union.R32 = PeekSingleBE(tableDataStream, constantOffset);
                                        currentOffset += 4;
                                        break;
                                    case ColumnType.Int32:
                                        union.S32 = PeekInt32BE(tableDataStream, constantOffset);
                                        currentOffset += 4;
                                        break;
                                    case ColumnType.UInt32:
                                        union.U32 = PeekUInt32BE(tableDataStream, constantOffset);
                                        currentOffset += 4;
                                        break;
                                    case ColumnType.Int16:
                                        union.S16 = PeekInt16BE(tableDataStream, constantOffset);
                                        currentOffset += 2;
                                        break;
                                    case ColumnType.UInt16:
                                        union.U16 = PeekUInt16BE(tableDataStream, constantOffset);
                                        currentOffset += 2;
                                        break;
                                    case ColumnType.SByte:
                                        unchecked { union.S8 = (sbyte)PeekByteAt(tableDataStream, constantOffset); }
                                        currentOffset += 1;
                                        break;
                                    case ColumnType.Byte:
                                        union.U8 = PeekByteAt(tableDataStream, constantOffset);
                                        currentOffset += 1;
                                        break;
                                    default:
                                        throw new FormatException($"Unknown column type at offset: 0x{currentOffset:x8}");
                                }
                                break;
                            }
                        case ColumnStorage.PerRow:
                            {
                                long rowDataOffset;
                                switch (constrainedType)
                                {
                                    case ColumnType.String:
                                        rowDataOffset = PeekUInt32BE(tableDataStream, currentRowBase + currentRowOffset);
                                        field.StringValue = ReadZeroEndedAsciiAt(tableDataStream, header.StringTableOffset + rowDataOffset);
                                        currentRowOffset += 4;
                                        break;
                                    case ColumnType.Int64:
                                        union.S64 = PeekInt64BE(tableDataStream, currentRowBase + currentRowOffset);
                                        currentRowOffset += 8;
                                        break;
                                    case ColumnType.UInt64:
                                        union.U64 = PeekUInt64BE(tableDataStream, currentRowBase + currentRowOffset);
                                        currentRowOffset += 8;
                                        break;
                                    case ColumnType.Data:
                                        rowDataOffset = PeekUInt32BE(tableDataStream, currentRowBase + currentRowOffset);
                                        long rowDataSize = PeekUInt32BE(tableDataStream, currentRowBase + currentRowOffset + 4);
                                        field.Offset = baseOffset + header.ExtraDataOffset + rowDataOffset;
                                        field.Size = rowDataSize;
                                        field.DataValue = PeekBytes(sourceStream, field.Offset, (int)rowDataSize);
                                        currentRowOffset += 8;
                                        break;
                                    case ColumnType.Double:
                                        union.R64 = PeekDoubleBE(tableDataStream, currentRowBase + currentRowOffset);
                                        currentRowOffset += 8;
                                        break;
                                    case ColumnType.Single:
                                        union.R32 = PeekSingleBE(tableDataStream, currentRowBase + currentRowOffset);
                                        currentRowOffset += 4;
                                        break;
                                    case ColumnType.Int32:
                                        union.S32 = PeekInt32BE(tableDataStream, currentRowBase + currentRowOffset);
                                        currentRowOffset += 4;
                                        break;
                                    case ColumnType.UInt32:
                                        union.U32 = PeekUInt32BE(tableDataStream, currentRowBase + currentRowOffset);
                                        currentRowOffset += 4;
                                        break;
                                    case ColumnType.Int16:
                                        union.S16 = PeekInt16BE(tableDataStream, currentRowBase + currentRowOffset);
                                        currentRowOffset += 2;
                                        break;
                                    case ColumnType.UInt16:
                                        union.U16 = PeekUInt16BE(tableDataStream, currentRowBase + currentRowOffset);
                                        currentRowOffset += 2;
                                        break;
                                    case ColumnType.SByte:
                                        unchecked { union.S8 = (sbyte)PeekByteAt(tableDataStream, currentRowBase + currentRowOffset); }
                                        currentRowOffset += 1;
                                        break;
                                    case ColumnType.Byte:
                                        union.U8 = PeekByteAt(tableDataStream, currentRowBase + currentRowOffset);
                                        currentRowOffset += 1;
                                        break;
                                    default:
                                        throw new FormatException($"Unknown column type at offset: 0x{currentOffset:x8}");
                                }
                                break;
                            }
                        default:
                            throw new FormatException($"Unknown column storage at offset: 0x{currentOffset:x8}");
                    }
                    field.ConstrainedType = constrainedType;
                    switch (constrainedType)
                    {
                        case ColumnType.String:
                        case ColumnType.Data:
                            break;
                        default:
                            field.NumericValue = union;
                            break;
                    }
                    row.Add(field.Name, field);
                    currentOffset += 5;
                }
            }
        }

        private object GetFieldValue(int rowIndex, string fieldName)
        {
            Dictionary<string, UtfField>[] rows = Rows;
            if (rowIndex >= rows.Length)
            {
                return null;
            }
            Dictionary<string, UtfField> row = rows[rowIndex];
            return row.ContainsKey(fieldName) ? row[fieldName].GetValue() : null;
        }

        internal T? GetFieldValueAsNumber<T>(int rowIndex, string fieldName) where T : struct
        {
            return (T?)GetFieldValue(rowIndex, fieldName);
        }

        internal string GetFieldValueAsString(int rowIndex, string fieldName)
        {
            return (string)GetFieldValue(rowIndex, fieldName);
        }

        internal byte[] GetFieldValueAsData(int rowIndex, string fieldName)
        {
            return (byte[])GetFieldValue(rowIndex, fieldName);
        }

        internal long? GetFieldOffset(int rowIndex, string fieldName)
        {
            Dictionary<string, UtfField>[] rows = Rows;
            if (rowIndex >= rows.Length)
            {
                return null;
            }
            Dictionary<string, UtfField> row = rows[rowIndex];
            if (row.ContainsKey(fieldName))
            {
                return row[fieldName].Offset;
            }
            return null;
        }

        internal long? GetFieldSize(int rowIndex, string fieldName)
        {
            Dictionary<string, UtfField>[] rows = Rows;
            if (rowIndex >= rows.Length)
            {
                return null;
            }
            Dictionary<string, UtfField> row = rows[rowIndex];
            if (row.ContainsKey(fieldName))
            {
                return row[fieldName].Size;
            }
            return null;
        }

        private static bool AreDataIdentical(byte[] array1, byte[] array2)
        {
            if (array1.Length != array2.Length)
            {
                return false;
            }
            for (int i = 0; i < array1.Length; ++i)
            {
                if (array1[i] != array2[i])
                {
                    return false;
                }
            }
            return true;
        }

        internal static Stream ExtractToNewStream(Stream stream, long offset, int length)
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
            MemoryStream memoryStream = new MemoryStream(memory, false)
            {
                Capacity = length
            };
            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        }

        private static byte[] PeekBytes(Stream stream, long offset, int length)
        {
            long originalPosition = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            byte[] buffer = new byte[length];
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = stream.Read(buffer, totalRead, length - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }
            stream.Position = originalPosition;
            if (totalRead < length)
            {
                Array.Resize(ref buffer, totalRead);
            }
            return buffer;
        }

        private static byte PeekByteAt(Stream stream, long offset)
        {
            long originalPosition = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            int value = stream.ReadByte();
            stream.Position = originalPosition;
            return (byte)value;
        }

        private static ushort PeekUInt16BE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 2);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToUInt16(data, 0);
        }

        private static short PeekInt16BE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 2);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToInt16(data, 0);
        }

        private static uint PeekUInt32BE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToUInt32(data, 0);
        }

        private static int PeekInt32BE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToInt32(data, 0);
        }

        private static ulong PeekUInt64BE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 8);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToUInt64(data, 0);
        }

        private static long PeekInt64BE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 8);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToInt64(data, 0);
        }

        private static float PeekSingleBE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToSingle(data, 0);
        }

        private static double PeekDoubleBE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 8);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToDouble(data, 0);
        }

        private static string ReadZeroEndedAsciiAt(Stream stream, long offset)
        {
            long originalPosition = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            while (stream.Position < stream.Length)
            {
                int b = stream.ReadByte();
                if (b <= 0) break;
                sb.Append((char)b);
            }
            stream.Position = originalPosition;
            return sb.ToString();
        }

        private readonly string _acbFileName;
        private readonly Stream _stream;
        private readonly long _offset;
        private readonly long _size;
        private readonly bool _disposeStream;

        private UtfReader _utfReader;
        private bool _isEncrypted;
        private UtfHeader _utfHeader;
        private Dictionary<string, UtfField>[] _rows;
    }
}