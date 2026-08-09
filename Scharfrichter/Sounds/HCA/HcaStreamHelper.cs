using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Scharfrichter.Codec.Sounds.HCA
{
    /// <summary>
    /// Stream primitives used by the HCA decoder (peek/read Big/Little Endian helpers).
    /// </summary>
    internal static class HcaStreamHelper
    {
        public static byte PeekByte(this Stream stream)
        {
            return PeekByte(stream, stream.Position);
        }

        public static byte PeekByte(this Stream stream, long offset)
        {
            long position = stream.Position;
            stream.Seek(offset, SeekOrigin.Begin);
            int value = stream.ReadByte();
            stream.Position = position;
            return (byte)value;
        }

        public static uint PeekUInt32LE(this Stream stream)
        {
            return PeekUInt32LE(stream, stream.Position);
        }

        public static uint PeekUInt32LE(this Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 4);
            if (!BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToUInt32(data, 0);
        }

        public static ushort PeekUInt16LE(this Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 2);
            if (!BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToUInt16(data, 0);
        }

        public static ushort PeekUInt16BE(this Stream stream, long offset)
        {
            byte[] data = PeekBytes(stream, offset, 2);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return BitConverter.ToUInt16(data, 0);
        }

        public static byte[] PeekBytes(this Stream stream, long offset, int length)
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

        public static int Read<T>(this Stream stream, out T value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] bytes = new byte[size];
            int bytesRead = stream.Read(bytes, 0, size);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, 0, ptr, size);
                value = (T)Marshal.PtrToStructure(ptr, typeof(T));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return bytesRead;
        }

        public static T Read<T>(this Stream stream) where T : struct
        {
            T value;
            Read(stream, out value);
            return value;
        }

        public static void Skip(this Stream stream, int length)
        {
            stream.Seek(length, SeekOrigin.Current);
        }

        public static string PeekZeroEndedStringAsAscii(this Stream stream, long offset)
        {
            long streamLength = stream.Length;
            long originalPosition = stream.Position;
            int stringLength = 0;
            stream.Seek(offset, SeekOrigin.Begin);
            for (long i = offset; i < streamLength; i++)
            {
                int dummy = stream.ReadByte();
                if (dummy > 0) stringLength++;
                else break;
            }
            byte[] stringBytes = PeekBytes(stream, offset, stringLength);
            string result = Encoding.ASCII.GetString(stringBytes);
            stream.Position = originalPosition;
            return result;
        }

        public static int Write(this Stream stream, sbyte value)
        {
            unchecked
            {
                stream.WriteByte((byte)value);
            }
            return 1;
        }

        public static int Write(this Stream stream, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, 4);
            return 4;
        }

        public static int Write(this Stream stream, short value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, 2);
            return 2;
        }

        public static int Write(this Stream stream, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, 4);
            return 4;
        }

        public static int Write<T>(this Stream stream, T value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] bytes = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, ptr, true);
                Marshal.Copy(ptr, bytes, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            stream.Write(bytes, 0, size);
            return size;
        }

        public static ushort SwapEndian(ushort v)
        {
            unchecked
            {
                return (ushort)((v >> 8) | (v << 8));
            }
        }

        public static short SwapEndian(short v)
        {
            unchecked { return (short)SwapEndian((ushort)v); }
        }

        public static uint SwapEndian(uint v)
        {
            return ((v & 0x000000ff) << 24) | ((v & 0x0000ff00) << 8) | ((v & 0x00ff0000) >> 8) | ((v & 0xff000000) >> 24);
        }

        public static int SwapEndian(int v)
        {
            unchecked { return (int)SwapEndian((uint)v); }
        }

        public static ulong SwapEndian(ulong v)
        {
            return ((v & 0x00000000000000ffUL) << 56)
                 | ((v & 0x000000000000ff00UL) << 40)
                 | ((v & 0x0000000000ff0000UL) << 24)
                 | ((v & 0x00000000ff000000UL) << 8)
                 | ((v & 0x000000ff00000000UL) >> 8)
                 | ((v & 0x0000ff0000000000UL) >> 24)
                 | ((v & 0x00ff000000000000UL) >> 40)
                 | ((v & 0xff00000000000000UL) >> 56);
        }

        public static long SwapEndian(long v)
        {
            unchecked { return (long)SwapEndian((ulong)v); }
        }

        public static float SwapEndian(float v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        public static double SwapEndian(double v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes, 0);
        }
    }
}