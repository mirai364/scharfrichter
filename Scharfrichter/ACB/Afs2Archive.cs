using System;
using System.Collections.Generic;
using System.IO;

namespace Scharfrichter.Codec.ACB
{
    public sealed class Afs2FileRecord
    {
        internal Afs2FileRecord()
        {
        }

        public ushort CueId { get; set; }
        public long FileOffsetRaw { get; set; }
        public long FileOffsetAligned { get; set; }
        public long FileLength { get; set; }
        public string FileName { get; set; }
    }

    public sealed class Afs2Archive : IDisposable
    {
        public Afs2Archive(Stream stream, long offset, string fileName, bool disposeStream)
        {
            _fileName = fileName;
            _stream = stream;
            _streamOffset = offset;
            _disposeStream = disposeStream;
        }

        public void Initialize()
        {
            Stream stream = _stream;
            long offset = _streamOffset;
            string acbFileName = _fileName;
            if (!IsAfs2Archive(stream, offset))
            {
                throw new FormatException($"File '{acbFileName}' does not contain a valid AFS2 archive at offset {offset}.");
            }
            int fileCount = (int)PeekUInt32LE(stream, offset + 8);
            if (fileCount > ushort.MaxValue)
            {
                throw new IndexOutOfRangeException($"File count {fileCount} exceeds maximum possible value (65535).");
            }
            Dictionary<int, Afs2FileRecord> files = new Dictionary<int, Afs2FileRecord>(fileCount);
            _files = files;
            uint byteAlignment = PeekUInt32LE(stream, offset + 12);
            _byteAlignment = byteAlignment & 0xffff;
            _hcaKeyModifier = (ushort)(byteAlignment >> 16);
            uint version = PeekUInt32LE(stream, offset + 4);
            _version = version;
            int offsetFieldSize = (int)(version >> 8) & 0xff;
            uint offsetMask = 0;
            for (int j = 0; j < offsetFieldSize; j++)
            {
                offsetMask |= (uint)(0xff << (j * 8));
            }

            const int invalidCueId = -1;
            int previousCueId = invalidCueId;
            int fileOffsetFieldBase = 0x10 + fileCount * 2;
            for (ushort i = 0; i < fileCount; ++i)
            {
                int currentFileOffsetBase = fileOffsetFieldBase + offsetFieldSize * i;
                Afs2FileRecord record = new Afs2FileRecord
                {
                    CueId = PeekUInt16LE(stream, offset + (0x10 + 2 * i)),
                    FileOffsetRaw = PeekUInt32LE(stream, offset + currentFileOffsetBase)
                };
                record.FileOffsetRaw &= offsetMask;
                record.FileOffsetRaw += offset;
                record.FileOffsetAligned = RoundUpToAlignment(record.FileOffsetRaw, ByteAlignment);
                if (i == fileCount - 1)
                {
                    record.FileLength = PeekUInt32LE(stream, offset + currentFileOffsetBase + offsetFieldSize) + offset - record.FileOffsetAligned;
                }
                if (previousCueId != invalidCueId)
                {
                    files[previousCueId].FileLength = record.FileOffsetRaw - files[previousCueId].FileOffsetAligned;
                }
                files.Add(record.CueId, record);
                previousCueId = record.CueId;
            }
        }

        public static bool IsAfs2Archive(Stream stream, long offset)
        {
            byte[] fileSignature = PeekBytes(stream, offset, 4);
            return AreDataIdentical(fileSignature, Afs2Signature);
        }

        public string FileName => _fileName;

        public uint ByteAlignment => _byteAlignment;

        public ushort HcaKeyModifier => _hcaKeyModifier;

        public Dictionary<int, Afs2FileRecord> Files => _files;

        public uint Version => _version;

        public void Dispose()
        {
            if (_disposeStream)
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

        public static readonly byte[] Afs2Signature = { 0x41, 0x46, 0x53, 0x32 }; // 'AFS2'

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

        private static ushort PeekUInt16LE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 2);
            if (!BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToUInt16(data, 0);
        }

        private static uint PeekUInt32LE(Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 4);
            if (!BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToUInt32(data, 0);
        }

        private static long RoundUpToAlignment(long valueToRound, long byteAlignment)
        {
            return (valueToRound + byteAlignment - 1) / byteAlignment * byteAlignment;
        }

        private uint _byteAlignment;
        private ushort _hcaKeyModifier;
        private Dictionary<int, Afs2FileRecord> _files;
        private uint _version;
        private readonly string _fileName;
        private readonly Stream _stream;
        private readonly long _streamOffset;
        private readonly bool _disposeStream;
    }
}