using System;
using System.IO;
using System.Text;

namespace Scharfrichter.Codec.ACB
{
    internal sealed class UtfReader
    {
        public UtfReader()
        {
            _isEncrypted = false;
        }

        public UtfReader(byte seed, byte increment)
        {
            _seed = seed;
            _increment = increment;
            _isEncrypted = true;
        }

        public bool IsEncrypted => _isEncrypted;

        public byte[] PeekBytes(Stream stream, long baseOffset, int size, long utfOffset)
        {
            byte[] data = ReadBytesAt(stream, baseOffset + utfOffset, size);
            if (!IsEncrypted)
            {
                return data;
            }
            if (utfOffset < _currentUtfOffset)
            {
                _currentUtfOffset = 0;
            }
            if (_currentUtfOffset == 0)
            {
                _currentXor = _seed;
            }
            for (long j = _currentUtfOffset; j < utfOffset; j++)
            {
                if (j > 0)
                {
                    _currentXor = (byte)(_currentXor * _increment);
                }
                _currentUtfOffset++;
            }
            for (long i = 0; i < size; i++)
            {
                if ((_currentUtfOffset != 0) || (i > 0))
                {
                    _currentXor = (byte)(_currentXor * _increment);
                }

                data[i] = (byte)(data[i] ^ _currentXor);
                _currentUtfOffset++;
            }
            return data;
        }

        public byte PeekByte(Stream stream, long baseOffset, long utfOffset)
        {
            byte data = ReadByteAt(stream, baseOffset + utfOffset);
            if (!IsEncrypted)
            {
                return data;
            }
            if (utfOffset < _currentUtfOffset)
            {
                _currentUtfOffset = 0;
            }
            if (_currentUtfOffset == 0)
            {
                _currentXor = _seed;
            }
            for (long j = _currentUtfOffset; j < utfOffset; j++)
            {
                if (j > 0)
                {
                    _currentXor = (byte)(_currentXor * _increment);
                }
                _currentUtfOffset++;
            }
            if (_currentUtfOffset != 0)
            {
                _currentXor = (byte)(_currentXor * _increment);
            }
            data = (byte)(data ^ _currentXor);
            _currentUtfOffset++;
            return data;
        }

        public sbyte PeekSByte(Stream stream, long baseOffset, long utfOffset)
        {
            unchecked
            {
                return (sbyte)PeekByte(stream, baseOffset, utfOffset);
            }
        }

        public ushort PeekUInt16(Stream stream, long baseOffset, long utfOffset)
        {
            byte[] temp = PeekBytes(stream, baseOffset, 2, utfOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            return BitConverter.ToUInt16(temp, 0);
        }

        public short PeekInt16(Stream stream, long baseOffset, long utfOffset)
        {
            byte[] temp = PeekBytes(stream, baseOffset, 2, utfOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            return BitConverter.ToInt16(temp, 0);
        }

        public uint PeekUInt32(Stream stream, long baseOffset, long utfOffset)
        {
            byte[] temp = PeekBytes(stream, baseOffset, 4, utfOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            return BitConverter.ToUInt32(temp, 0);
        }

        public int PeekInt32(Stream stream, long baseOffset, long utfOffset)
        {
            byte[] temp = PeekBytes(stream, baseOffset, 4, utfOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            return BitConverter.ToInt32(temp, 0);
        }

        public ulong PeekUInt64(Stream stream, long baseOffset, long utfOffset)
        {
            byte[] temp = PeekBytes(stream, baseOffset, 8, utfOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            return BitConverter.ToUInt64(temp, 0);
        }

        public long PeekInt64(Stream stream, long baseOffset, long utfOffset)
        {
            byte[] temp = PeekBytes(stream, baseOffset, 8, utfOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            return BitConverter.ToInt64(temp, 0);
        }

        public float PeekSingle(Stream stream, long baseOffset, long utfOffset)
        {
            byte[] temp = PeekBytes(stream, baseOffset, 4, utfOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            return BitConverter.ToSingle(temp, 0);
        }

        public double PeekDouble(Stream stream, long baseOffset, long utfOffset)
        {
            byte[] temp = PeekBytes(stream, baseOffset, 8, utfOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            return BitConverter.ToDouble(temp, 0);
        }

        public string ReadZeroEndedStringAsAscii(Stream stream, long baseOffset, long utfOffset)
        {
            if (!IsEncrypted)
            {
                return ReadZeroEndedAsciiAt(stream, baseOffset + utfOffset);
            }

            stream.Position = baseOffset + utfOffset;
            if (utfOffset < _currentUtfStringOffset)
            {
                _currentUtfStringOffset = 0;
            }

            if (_currentUtfStringOffset == 0)
            {
                _currentStringXor = _seed;
            }
            for (long j = _currentUtfStringOffset; j < utfOffset; j++)
            {
                if (j > 0)
                {
                    _currentStringXor = (byte)(_currentStringXor * _increment);
                }
                _currentUtfStringOffset++;
            }

            StringBuilder asciiVal = new StringBuilder();
            long remained = stream.Length - stream.Position;
            for (long i = 0; i < remained; i++)
            {
                _currentStringXor = (byte)(_currentStringXor * _increment);
                _currentUtfStringOffset++;
                int encryptedByte = stream.ReadByte();
                byte decryptedByte = (byte)(encryptedByte ^ _currentStringXor);
                if (decryptedByte == 0)
                {
                    break;
                }
                else
                {
                    asciiVal.Append((char)decryptedByte);
                }
            }
            return asciiVal.ToString();
        }

        private static byte[] ReadBytesAt(Stream stream, long offset, int size)
        {
            long originalPosition = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            byte[] buffer = new byte[size];
            int totalRead = 0;
            while (totalRead < size)
            {
                int read = stream.Read(buffer, totalRead, size - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }
            stream.Position = originalPosition;
            return buffer;
        }

        private static byte ReadByteAt(Stream stream, long offset)
        {
            long originalPosition = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            int value = stream.ReadByte();
            stream.Position = originalPosition;
            return (byte)value;
        }

        private static string ReadZeroEndedAsciiAt(Stream stream, long offset)
        {
            long originalPosition = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            StringBuilder sb = new StringBuilder();
            while (stream.Position < stream.Length)
            {
                int b = stream.ReadByte();
                if (b <= 0) break;
                sb.Append((char)b);
            }
            stream.Position = originalPosition;
            return sb.ToString();
        }

        private readonly bool _isEncrypted;
        private readonly byte _increment;
        private readonly byte _seed;

        private byte _currentXor;
        private long _currentUtfOffset;
        private byte _currentStringXor;
        private long _currentUtfStringOffset;
    }
}