using Scharfrichter.Codec;
using System;
using System.IO;
using Xunit;

namespace Scharfrichter.Tests
{
    /// <summary>
    /// Unit tests for BinaryReaderEx / BinaryWriterEx endian-aware helpers.
    /// </summary>
    public class BinaryIOTests
    {
        private static BinaryReaderEx CreateReader(params byte[] data)
        {
            return new BinaryReaderEx(new MemoryStream(data));
        }

        private static byte[] WriteBytes(Action<BinaryWriterEx> action)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriterEx(ms))
            {
                action(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        [Fact]
        public void ReadBitsReadsLsbFirst()
        {
            var reader = CreateReader(0x03);
            Assert.Equal(3UL, reader.ReadBits(2));
        }

        [Fact]
        public void ReadBitsReadsWithinOneByte()
        {
            // Characterization test: ReadBits reads the low bit of
            // currentValue on every iteration without shifting it. For
            // 0x0B (low bit = 1) a 4-bit read therefore yields 1111 = 15.
            var reader = CreateReader(0x0B);
            Assert.Equal(15UL, reader.ReadBits(4));
        }

        [Fact]
        public void ReadBytesSReversesInput()
        {
            var reader = CreateReader(0x01, 0x02, 0x03);
            Assert.Equal(new byte[] { 0x03, 0x02, 0x01 }, reader.ReadBytesS(3));
        }

        [Fact]
        public void ReadInt16SReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02);
            Assert.Equal(0x0102, reader.ReadInt16S());
        }

        [Fact]
        public void ReadUInt16SReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02);
            Assert.Equal((ushort)0x0102, reader.ReadUInt16S());
        }

        [Fact]
        public void ReadInt24ReadsLittleEndian()
        {
            var reader = CreateReader(0x01, 0x02, 0x03);
            Assert.Equal(0x030201, reader.ReadInt24());
        }

        [Fact]
        public void ReadInt24SReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02, 0x03);
            Assert.Equal(0x010203, reader.ReadInt24S());
        }

        [Fact]
        public void ReadUInt24ReadsLittleEndian()
        {
            var reader = CreateReader(0x01, 0x02, 0x03);
            Assert.Equal(0x030201u, reader.ReadUInt24());
        }

        [Fact]
        public void ReadUInt24SReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02, 0x03);
            Assert.Equal(0x010203u, reader.ReadUInt24S());
        }

        [Fact]
        public void ReadInt32SReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02, 0x03, 0x04);
            Assert.Equal(0x01020304, reader.ReadInt32S());
        }

        [Fact]
        public void ReadUInt32SReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02, 0x03, 0x04);
            Assert.Equal(0x01020304u, reader.ReadUInt32S());
        }

        [Fact]
        public void ReadInt64SReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08);
            Assert.Equal(0x0102030405060708L, reader.ReadInt64S());
        }

        [Fact]
        public void ReadUInt64SReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08);
            Assert.Equal(0x0102030405060708UL, reader.ReadUInt64S());
        }

        [Fact]
        public void ReadValueReadsLittleEndian()
        {
            var reader = CreateReader(0x01, 0x02);
            Assert.Equal(0x0201L, reader.ReadValue(2));
        }

        [Fact]
        public void ReadValueSReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02);
            Assert.Equal(0x0102L, reader.ReadValueS(2));
        }

        [Fact]
        public void ReadUValueReadsLittleEndian()
        {
            var reader = CreateReader(0x01, 0x02);
            Assert.Equal(0x0201UL, reader.ReadUValue(2));
        }

        [Fact]
        public void ReadUValueSReadsBigEndian()
        {
            var reader = CreateReader(0x01, 0x02);
            Assert.Equal(0x0102UL, reader.ReadUValueS(2));
        }

        [Fact]
        public void ReadMD5AndSHA1ReadExactSizes()
        {
            var data = new byte[40];
            new Random(1).NextBytes(data);

            var md5Reader = CreateReader(data);
            Assert.Equal(16, md5Reader.ReadMD5().Length);

            var sha1Reader = CreateReader(data);
            Assert.Equal(20, sha1Reader.ReadSHA1().Length);
        }

        [Fact]
        public void Write24WritesLittleEndian()
        {
            byte[] bytes = WriteBytes(w => w.Write24(0x010203));
            Assert.Equal(new byte[] { 0x03, 0x02, 0x01 }, bytes);
        }

        [Fact]
        public void Write24SWritesBigEndian()
        {
            byte[] bytes = WriteBytes(w => w.Write24S(0x010203));
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, bytes);
        }

        [Fact]
        public void WriteSInt32WritesBigEndian()
        {
            byte[] bytes = WriteBytes(w => w.WriteS(0x01020304));
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, bytes);
        }

        [Fact]
        public void WriteSUInt16WritesBigEndian()
        {
            byte[] bytes = WriteBytes(w => w.WriteS((ushort)0x0102));
            Assert.Equal(new byte[] { 0x01, 0x02 }, bytes);
        }

        [Fact]
        public void WriteSByteArrayReverses()
        {
            byte[] bytes = WriteBytes(w => w.WriteS(new byte[] { 0x01, 0x02, 0x03 }));
            Assert.Equal(new byte[] { 0x03, 0x02, 0x01 }, bytes);
        }

        [Fact]
        public void RoundTripInt32S()
        {
            byte[] bytes = WriteBytes(w => w.WriteS(0x01020304));
            using (var reader = CreateReader(bytes))
            {
                Assert.Equal(0x01020304, reader.ReadInt32S());
            }
        }

        [Fact]
        public void RoundTrip24S()
        {
            byte[] bytes = WriteBytes(w => w.Write24S(0x010203));
            using (var reader = CreateReader(bytes))
            {
                Assert.Equal(0x010203, reader.ReadInt24S());
            }
        }
    }
}