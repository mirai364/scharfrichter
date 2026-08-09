using System;
using System.IO;
using System.Runtime.InteropServices;
using Scharfrichter.Codec.Sounds.HCA.Native;

namespace Scharfrichter.Codec.Sounds.HCA
{
    public partial class HcaDecoder : HcaReader, IDisposable
    {
        public HcaDecoder(Stream sourceStream)
            : this(sourceStream, DecodeParams.Default)
        {
        }

        public HcaDecoder(Stream sourceStream, DecodeParams decodeParams)
            : base(sourceStream)
        {
            _decodeParams = decodeParams;
            HcaHelper.TranslateTables();
            _ath = new Ath();
            _cipher = new Cipher();
            Initialize();
        }

        public int GetMinWaveHeaderBufferSize()
        {
            if (_minWaveHeaderBufferSize != null)
            {
                return _minWaveHeaderBufferSize.Value;
            }
            int wavNoteSize = 0;
            HcaInfo hcaInfo = HcaInfo;
            if (hcaInfo.Comment != null)
            {
                wavNoteSize = 4 + (int)hcaInfo.CommentLength + 1;
                if ((wavNoteSize & 3) != 0)
                {
                    wavNoteSize += 4 - (wavNoteSize & 3);
                }
            }
            int sizeNeeded = Marshal.SizeOf(typeof(WaveRiffSection));
            if (hcaInfo.LoopFlag)
            {
                sizeNeeded += Marshal.SizeOf(typeof(WaveSampleSection));
            }
            if (hcaInfo.Comment != null && hcaInfo.Comment.Length > 0)
            {
                sizeNeeded += 8 + wavNoteSize;
            }
            sizeNeeded += Marshal.SizeOf(typeof(WaveDataSection));
            _minWaveHeaderBufferSize = sizeNeeded;
            return _minWaveHeaderBufferSize.Value;
        }

        public int GetMinWaveDataBufferSize()
        {
            if (_minWaveDataBufferSize != null)
            {
                return _minWaveDataBufferSize.Value;
            }
            _minWaveDataBufferSize = 0x80 * GetSampleBitsFromParams() * (int)HcaInfo.ChannelCount;
            return _minWaveDataBufferSize.Value;
        }

        public int WriteWaveHeader(byte[] stream)
        {
            return WriteWaveHeader(stream, AudioParams.Default);
        }

        public int WriteWaveHeader(byte[] stream, AudioParams audioParams)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            HcaInfo hcaInfo = HcaInfo;
            if (hcaInfo.LoopFlag && audioParams.InfiniteLoop)
            {
                throw new HcaException(ErrorMessages.GetInvalidParameter(nameof(audioParams.InfiniteLoop)), ActionResult.InvalidParameter);
            }
            int minimumHeaderBufferSize = GetMinWaveHeaderBufferSize();
            if (stream.Length < minimumHeaderBufferSize)
            {
                throw new HcaException(ErrorMessages.GetBufferTooSmall(minimumHeaderBufferSize, stream.Length), ActionResult.BufferTooSmall);
            }
            int sampleBits = GetSampleBitsFromParams();
            WaveRiffSection wavRiff = WaveRiffSection.CreateDefault();
            WaveNoteSection wavNote = WaveNoteSection.CreateDefault();
            WaveDataSection wavData = WaveDataSection.CreateDefault();
            wavRiff.FmtType = (ushort)(_decodeParams.Mode != SamplingMode.R32 ? 1 : 3);
            wavRiff.FmtChannels = (ushort)hcaInfo.ChannelCount;
            wavRiff.FmtBitCount = (ushort)(sampleBits > 0 ? sampleBits : sizeof(float));
            wavRiff.FmtSamplingRate = hcaInfo.SamplingRate;
            wavRiff.FmtSamplingSize = (ushort)(wavRiff.FmtBitCount / 8 * wavRiff.FmtChannels);
            wavRiff.FmtSamplesPerSec = wavRiff.FmtSamplingRate * wavRiff.FmtSamplingSize;
            if (hcaInfo.Comment != null)
            {
                wavNote.NoteSize = 4 + hcaInfo.CommentLength + 1;
                if ((wavNote.NoteSize & 3) != 0)
                {
                    wavNote.NoteSize += 4 - (wavNote.NoteSize & 3);
                }
            }

            uint totalBlockCount = hcaInfo.BlockCount;
            if (hcaInfo.LoopFlag)
            {
                totalBlockCount += (hcaInfo.LoopEnd - hcaInfo.LoopStart) * audioParams.SimulatedLoopCount;
            }
            wavData.DataSize = totalBlockCount * 0x80 * 8 * wavRiff.FmtSamplingSize;
            wavRiff.RiffSize = (uint)(0x1c + (hcaInfo.Comment != null ? wavNote.NoteSize : 0) + Marshal.SizeOf(wavData) + wavData.DataSize);

            int bytesWritten = WriteStream(stream, wavRiff, 0);
            if (hcaInfo.Comment != null)
            {
                int address = bytesWritten;
                bytesWritten += WriteStream(stream, wavNote, bytesWritten);
                WriteBytes(stream, hcaInfo.Comment, bytesWritten);
                bytesWritten = address + 8 + (int)wavNote.NoteSize;
                bytesWritten += 8 + (int)wavNote.NoteSize;
            }
            bytesWritten += WriteStream(stream, wavData, bytesWritten);
            return bytesWritten;
        }

        public void Dispose()
        {
            _channels?.Dispose();
        }

        internal uint DecodeBlock(byte[] waveDataBuffer, uint blockIndex)
        {
            if (waveDataBuffer == null)
            {
                throw new ArgumentNullException(nameof(waveDataBuffer));
            }
            int waveBlockSize = GetMinWaveDataBufferSize();
            if (waveDataBuffer.Length < waveBlockSize)
            {
                throw new HcaException(ErrorMessages.GetBufferTooSmall(waveBlockSize, waveDataBuffer.Length), ActionResult.BufferTooSmall);
            }
            TransformWaveDataBlocks(SourceStream, waveDataBuffer, blockIndex, 1, GetProperWaveWriter());
            return 1;
        }

        internal uint DecodeBlocks(byte[] waveDataBuffer, uint startBlockIndex)
        {
            if (waveDataBuffer == null)
            {
                throw new ArgumentNullException(nameof(waveDataBuffer));
            }
            int waveBlockSize = GetMinWaveDataBufferSize();
            if (waveDataBuffer.Length < waveBlockSize)
            {
                throw new HcaException(ErrorMessages.GetBufferTooSmall(waveBlockSize, waveDataBuffer.Length), ActionResult.BufferTooSmall);
            }
            HcaInfo hcaInfo = HcaInfo;
            uint numBlocksToDecode = (uint)(waveDataBuffer.Length / waveBlockSize);
            if (startBlockIndex + numBlocksToDecode >= hcaInfo.BlockCount)
            {
                numBlocksToDecode = hcaInfo.BlockCount - startBlockIndex;
            }
            if (numBlocksToDecode == 0)
            {
                return 0;
            }
            TransformWaveDataBlocks(SourceStream, waveDataBuffer, startBlockIndex, numBlocksToDecode, GetProperWaveWriter());
            return numBlocksToDecode;
        }

        private void Initialize()
        {
            ParseHeaders();
            InitializeDecodeComponents();
        }

        private void InitializeDecodeComponents()
        {
            HcaInfo hcaInfo = HcaInfo;
            if (!_ath.Initialize(hcaInfo.AthType, hcaInfo.SamplingRate))
            {
                throw new HcaException(ErrorMessages.GetAthInitializationFailed(), ActionResult.AthInitFailed);
            }
            DecodeParams decodeParams = _decodeParams;
            CipherType cipherType = decodeParams.CipherTypeOverrideEnabled ? decodeParams.OverriddenCipherType : hcaInfo.CipherType;
            if (!_cipher.Initialize(cipherType, decodeParams.Key1, decodeParams.Key2, decodeParams.KeyModifier))
            {
                throw new HcaException(ErrorMessages.GetCiphInitializationFailed(), ActionResult.CiphInitFailed);
            }

            ChannelArray channels = _channels = new ChannelArray(0x10);
            byte[] r = new byte[10];
            uint b = hcaInfo.ChannelCount / hcaInfo.CompR03;

            if (hcaInfo.CompR07 != 0 && b > 1)
            {
                uint rIndex = 0;
                for (uint i = 0; i < hcaInfo.CompR03; ++i, rIndex += b)
                {
                    switch (b)
                    {
                        case 2:
                        case 3:
                            r[rIndex] = 1;
                            r[rIndex + 1] = 2;
                            break;
                        case 4:
                            r[rIndex] = 1;
                            r[rIndex + 1] = 2;
                            if (hcaInfo.CompR04 == 0)
                            {
                                r[rIndex + 2] = 1;
                                r[rIndex + 3] = 2;
                            }
                            break;
                        case 5:
                            r[rIndex] = 1;
                            r[rIndex + 1] = 2;
                            if (hcaInfo.CompR04 <= 2)
                            {
                                r[rIndex + 3] = 1;
                                r[rIndex + 4] = 2;
                            }
                            break;
                        case 6:
                        case 7:
                            r[rIndex] = 1;
                            r[rIndex + 1] = 2;
                            r[rIndex + 4] = 1;
                            r[rIndex + 5] = 2;
                            break;
                        case 8:
                            r[rIndex] = 1;
                            r[rIndex + 1] = 2;
                            r[rIndex + 4] = 1;
                            r[rIndex + 5] = 2;
                            r[rIndex + 6] = 1;
                            r[rIndex + 7] = 2;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException("b");
                    }
                }
            }

            unsafe
            {
                for (int i = 0; i < hcaInfo.ChannelCount; ++i)
                {
                    int* pType = channels.GetPtrOfType(i);
                    sbyte* pValue = channels.GetPtrOfValue(i);
                    sbyte** ppValue3 = channels.GetPtrOfValue3(i);
                    uint* pCount = channels.GetPtrOfCount(i);

                    *pType = r[i];
                    *ppValue3 = &pValue[hcaInfo.CompR06 + hcaInfo.CompR07];
                    *pCount = (uint)(hcaInfo.CompR06 + (r[i] != 2 ? hcaInfo.CompR07 : 0));
                }
            }
        }

        private int DecodeToWaveR32(byte[] blockData, int blockIndex)
        {
            HcaInfo hcaInfo = HcaInfo;
            if (blockData == null)
            {
                throw new ArgumentNullException(nameof(blockData));
            }
            if (blockData.Length < hcaInfo.BlockSize)
            {
                throw new HcaException(ErrorMessages.GetInvalidParameter(nameof(blockData) + "." + nameof(blockData.Length)), ActionResult.InvalidParameter);
            }
            ushort checksum = HcaHelper.Checksum(blockData, 0);
            if (checksum != 0)
            {
                throw new HcaException(ErrorMessages.GetChecksumNotMatch(0, checksum), ActionResult.ChecksumNotMatch);
            }
            _cipher.Decrypt(blockData);
            DataBits d = new DataBits(blockData, hcaInfo.BlockSize);
            int magic = d.GetBit(16);
            if (magic != 0xffff)
            {
                throw new HcaException(ErrorMessages.GetMagicNotMatch(0xffff, magic), ActionResult.MagicNotMatch);
            }
            int a = (d.GetBit(9) << 8) - d.GetBit(7);
            ChannelArray channels = _channels;
            Ath ath = _ath;
            string site = null;

            try
            {
                int i;

                for (i = 0; i < hcaInfo.ChannelCount; ++i)
                {
                    site = $"Decode1({i.ToString()})";
                    channels.Decode1(i, d, hcaInfo.CompR09, a, ath.Table);
                }

                for (i = 0; i < 8; ++i)
                {
                    for (int j = 0; j < hcaInfo.ChannelCount; ++j)
                    {
                        site = $"Decode2({i.ToString()}/{j.ToString()})";
                        channels.Decode2(j, d);
                    }

                    for (int j = 0; j < hcaInfo.ChannelCount; ++j)
                    {
                        site = $"Decode3({i.ToString()}/{j.ToString()})";
                        channels.Decode3(j, hcaInfo.CompR09, hcaInfo.CompR08, (uint)(hcaInfo.CompR07 + hcaInfo.CompR06), hcaInfo.CompR05);
                    }

                    for (int j = 0; j < hcaInfo.ChannelCount - 1; ++j)
                    {
                        site = $"Decode4({i.ToString()}/{j.ToString()})";
                        channels.Decode4(j, j + 1, i, (uint)(hcaInfo.CompR05 - hcaInfo.CompR06), hcaInfo.CompR06, hcaInfo.CompR07);
                    }

                    for (int j = 0; j < hcaInfo.ChannelCount; ++j)
                    {
                        site = $"Decode5({i.ToString()}/{j.ToString()})";
                        channels.Decode5(j, i);
                    }
                }

                return blockData.Length;
            }
            catch (IndexOutOfRangeException ex)
            {
                const string message = "Index access exception detected. It is probably because you are using an incorrect HCA key pair while decoding a type 56 HCA file.";
                string siteInfo = $"Site: {site} @ block {blockIndex.ToString()}";
                string err = message + Environment.NewLine + siteInfo;
                throw new HcaException(err, ActionResult.DecodeFailed, ex);
            }
        }

        private void TransformWaveDataBlocks(Stream source, byte[] destination, uint startBlockIndex, uint blockCount, IWaveWriter waveWriter)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (waveWriter == null)
            {
                throw new ArgumentNullException(nameof(waveWriter));
            }
            HcaInfo hcaInfo = HcaInfo;
            long startOffset = hcaInfo.DataOffset + startBlockIndex * hcaInfo.BlockSize;
            source.Seek(startOffset, SeekOrigin.Begin);
            ChannelArray channels = _channels;
            DecodeParams decodeParams = _decodeParams;
            byte[] hcaBlockBuffer = GetHcaBlockBuffer();

            uint channelCount = hcaInfo.ChannelCount;
            float rvaVolume = hcaInfo.RvaVolume;
            uint bytesPerSample = waveWriter.BytesPerSample;
            float volume = decodeParams.Volume;

            for (int l = 0; l < (int)blockCount; ++l)
            {
                int totalRead = 0;
                while (totalRead < hcaBlockBuffer.Length)
                {
                    int read = source.Read(hcaBlockBuffer, totalRead, hcaBlockBuffer.Length - totalRead);
                    if (read <= 0)
                    {
                        break;
                    }
                    totalRead += read;
                }

                DecodeToWaveR32(hcaBlockBuffer, l + (int)startBlockIndex);

                for (int i = 0; i < 8; ++i)
                {
                    for (int j = 0; j < 0x80; ++j)
                    {
                        for (int k = 0; k < channelCount; ++k)
                        {
                            float f;

                            unsafe
                            {
                                float* pWave = channels.GetPtrOfWave(k);

                                f = pWave[i * 0x80 + j];
                                f = f * volume * rvaVolume;
                            }

                            HcaHelper.Clamp(ref f, -1f, 1f);

                            uint offset = (uint)((((l * 8 + i) * 0x80 + j) * (int)channelCount + k) * bytesPerSample);

                            waveWriter.DecodeToBuffer(f, destination, offset);
                        }
                    }
                }
            }
        }

        private byte[] GetHcaBlockBuffer()
        {
            return _hcaBlockBuffer ?? (_hcaBlockBuffer = new byte[HcaInfo.BlockSize]);
        }

        private int GetSampleBitsFromParams()
        {
            SamplingMode mode = _decodeParams.Mode;
            switch (mode)
            {
                case SamplingMode.R32:
                    return 32;
                case SamplingMode.S16:
                    return 16;
                case SamplingMode.S24:
                    return 24;
                case SamplingMode.S32:
                    return 32;
                case SamplingMode.U8:
                    return 8;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private IWaveWriter GetProperWaveWriter()
        {
            SamplingMode mode = _decodeParams.Mode;
            switch (mode)
            {
                case SamplingMode.S16:
                    return WaveHelper.S16;
                case SamplingMode.R32:
                    return WaveHelper.R32;
                case SamplingMode.S32:
                    return WaveHelper.S32;
                case SamplingMode.U8:
                    return WaveHelper.U8;
                case SamplingMode.S24:
                    throw new NotImplementedException();
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private int WriteStream(byte[] stream, object value, int offset)
        {
            int size = Marshal.SizeOf(value.GetType());
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
            Array.Copy(bytes, 0, stream, offset, size);
            return size;
        }

        private void WriteBytes(byte[] stream, byte[] data, int offset)
        {
            Array.Copy(data, 0, stream, offset, data.Length);
        }

        private byte[] _hcaBlockBuffer;
        private readonly Ath _ath;
        private readonly Cipher _cipher;
        private ChannelArray _channels;
        private readonly DecodeParams _decodeParams;
        private int? _minWaveHeaderBufferSize;
        private int? _minWaveDataBufferSize;
    }
}