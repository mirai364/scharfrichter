using Scharfrichter.Codec;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace Scharfrichter.Tests
{
    /// <summary>
    /// Unit tests for the core Util helpers: byte swapping, string encoding,
    /// PCM sample mixing, stream helpers and RIFF WAV chunk parsing.
    /// </summary>
    public class UtilTests
    {
        [Fact]
        public void ByteSwapReversesEachBlock()
        {
            byte[] result = Util.ByteSwap(new byte[] { 1, 2, 3, 4 }, 2);
            Assert.Equal(new byte[] { 2, 1, 4, 3 }, result);
        }

        [Fact]
        public void ByteSwapInPlace16SwapsAdjacentPairs()
        {
            byte[] target = new byte[] { 1, 2, 3, 4 };
            Util.ByteSwapInPlace16(target);
            Assert.Equal(new byte[] { 2, 1, 4, 3 }, target);
        }

        [Fact]
        public void ByteSwapInPlace16IgnoresFinalOddByte()
        {
            byte[] target = new byte[] { 1, 2, 3 };
            Util.ByteSwapInPlace16(target);
            Assert.Equal(new byte[] { 2, 1, 3 }, target);
        }

        [Fact]
        public void CalculateMeasureRateIsReduced()
        {
            Fraction rate = Util.CalculateMeasureRate(new Fraction(60, 1));
            Assert.Equal(4, rate.Numerator);
            Assert.Equal(1, rate.Denominator);
        }

        [Fact]
        public void ConvertToBMEStringUsesBase36()
        {
            Assert.Equal("0A", Util.ConvertToBMEString(10, 2));
            Assert.Equal("00", Util.ConvertToBMEString(0, 2));
        }

        [Fact]
        public void ConvertToBMSObjectStringUsesBase36()
        {
            Assert.Equal("0A", Util.ConvertToBMSObjectString(10, 2, 36));
            Assert.Equal("ZZ", Util.ConvertToBMSObjectString(1295, 2, 36));
        }

        [Fact]
        public void ConvertToBMSObjectStringUsesBase62()
        {
            // Values inside the legacy base-36 range (0..1295) are encoded
            // with base-36, even when base 62 is requested.
            Assert.Equal("10", Util.ConvertToBMSObjectString(36, 2, 62));

            // The first base-62-only identifier begins right after the
            // legacy base-36 range: 1296 -> "0a".
            Assert.Equal("0a", Util.ConvertToBMSObjectString(1296, 2, 62));
        }

        [Fact]
        public void GetBMSObjectAlphabetSelectsByBase()
        {
            Assert.Equal(Util.alphabetBMS62, Util.GetBMSObjectAlphabet(62));
            Assert.Equal(Util.alphabetBME, Util.GetBMSObjectAlphabet(36));
        }

        [Fact]
        public void ConvertToDecimalStringPadsWithZeros()
        {
            Assert.Equal("01", Util.ConvertToDecimalString(1, 2));
            Assert.Equal("12", Util.ConvertToDecimalString(12, 2));
        }

        [Fact]
        public void ConvertToHexStringUsesUppercase()
        {
            Assert.Equal("0A", Util.ConvertToHexString(10, 2));
            Assert.Equal("FF", Util.ConvertToHexString(255, 2));
        }

        [Fact]
        public void DiscardBytesAdvancesStream()
        {
            var stream = new MemoryStream(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            Util.DiscardBytes(stream, 4);
            Assert.Equal(4, stream.Position);
            Assert.Equal(4, stream.ReadByte());
        }

        [Fact]
        public void GetLineReductionDivisorHandlesEmptyArray()
        {
            Assert.Equal(1, Util.GetLineReductionDivisor(new int[0]));
        }

        [Fact]
        public void GetLineReductionDivisorFindsCommonFactor()
        {
            Assert.Equal(2, Util.GetLineReductionDivisor(new[] { 2, 4, 6 }));
            Assert.Equal(6, Util.GetLineReductionDivisor(new[] { 6, 12, 18 }));
        }

        [Fact]
        public void GetLineReductionDivisorIgnoresZeros()
        {
            Assert.Equal(3, Util.GetLineReductionDivisor(new[] { 0, 3, 6, 0 }));
        }

        [Fact]
        public void Sum16MixesLittleEndianSamples()
        {
            // 0x0100 (256) + 0x0200 (512) = 0x0300 (768)
            byte[] result = Util.Sum16(
                new byte[] { 0x00, 0x01 },
                new byte[] { 0x00, 0x02 });
            Assert.Equal(new byte[] { 0x00, 0x03 }, result);
        }

        [Fact]
        public void Sum16ClampsToInt16()
        {
            // 0x7FFF + 0x7FFF would overflow; clamp to 0x7FFF.
            byte[] result = Util.Sum16(
                new byte[] { 0xFF, 0x7F },
                new byte[] { 0xFF, 0x7F });
            Assert.Equal(new byte[] { 0xFF, 0x7F }, result);
        }

        [Fact]
        public void TrimNullsCutsAtFirstNull()
        {
            Assert.Equal("abc", Util.TrimNulls("abc\0def"));
            Assert.Equal("abc", Util.TrimNulls("abc"));
        }

        [Fact]
        public void TryReadWavRawChunksExtractsFmtAndData()
        {
            byte[] fmt = BuildPcmFmt();
            byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            byte[] wav = BuildWav(fmt, data);

            byte[] outFmt;
            byte[] outData;
            Assert.True(Util.TryReadWavRawChunks(wav, out outFmt, out outData));
            Assert.Equal(fmt, outFmt);
            Assert.Equal(data, outData);
        }

        [Fact]
        public void TryReadWavRawChunksRejectsInvalidInput()
        {
            byte[] fmt;
            byte[] data;
            Assert.False(Util.TryReadWavRawChunks(null, out fmt, out data));
            Assert.False(Util.TryReadWavRawChunks(new byte[4], out fmt, out data));
            Assert.False(Util.TryReadWavRawChunks(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, out fmt, out data));
        }

        [Fact]
        public void TryParseWavFormatInfoReadsSampleRateAndChannels()
        {
            byte[] fmt = BuildPcmFmt();
            int sampleRate;
            int channels;
            Assert.True(Util.TryParseWavFormatInfo(fmt, out sampleRate, out channels));
            Assert.Equal(44100, sampleRate);
            Assert.Equal(2, channels);
        }

        [Fact]
        public void TryParseWavFormatInfoRejectsInvalidInput()
        {
            int sampleRate;
            int channels;
            Assert.False(Util.TryParseWavFormatInfo(null, out sampleRate, out channels));
            Assert.False(Util.TryParseWavFormatInfo(new byte[4], out sampleRate, out channels));
        }

        private static byte[] BuildPcmFmt()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((short)1);       // PCM
                w.Write((short)2);       // channels
                w.Write(44100);          // sample rate
                w.Write(176400);         // byte rate
                w.Write((short)4);       // block align
                w.Write((short)16);      // bits per sample
                return ms.ToArray();
            }
        }

        private static byte[] BuildWav(byte[] fmt, byte[] data)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int fmtPad = fmt.Length & 1;
                int dataPad = data.Length & 1;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(4 + (8 + fmt.Length + fmtPad) + (8 + data.Length + dataPad));
                w.Write(Encoding.ASCII.GetBytes("WAVE"));

                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(fmt.Length);
                w.Write(fmt);
                if (fmtPad == 1)
                    w.Write((byte)0);

                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(data.Length);
                w.Write(data);
                if (dataPad == 1)
                    w.Write((byte)0);

                return ms.ToArray();
            }
        }
    }
}