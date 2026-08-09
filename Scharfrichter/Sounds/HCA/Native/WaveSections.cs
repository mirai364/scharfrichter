using System.Runtime.InteropServices;

namespace Scharfrichter.Codec.Sounds.HCA.Native
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct WaveRiffSection
    {
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 4)]
        public byte[] RIFF;

        public uint RiffSize;

        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 4)]
        public byte[] WAVE;

        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 4)]
        public byte[] FMT;

        public uint FmtSize;
        public ushort FmtType;
        public ushort FmtChannels;
        public uint FmtSamplingRate;
        public uint FmtSamplesPerSec;
        public ushort FmtSamplingSize;
        public ushort FmtBitCount;

        public static WaveRiffSection CreateDefault()
        {
            WaveRiffSection v = default(WaveRiffSection);
            v.RiffSize = 0;
            v.FmtSize = 0x10;
            v.FmtType = 0;
            v.FmtChannels = 0;
            v.FmtSamplingRate = 0;
            v.FmtSamplesPerSec = 0;
            v.FmtSamplingSize = 0;
            v.FmtBitCount = 0;
            v.RIFF = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' };
            v.WAVE = new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' };
            v.FMT = new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' };
            return v;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct WaveNoteSection
    {
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 4)]
        public byte[] NOTE;

        public uint NoteSize;
        public uint Name;

        public static WaveNoteSection CreateDefault()
        {
            WaveNoteSection v = default(WaveNoteSection);
            v.Name = 0;
            v.NoteSize = 0;
            v.NOTE = new byte[] { (byte)'n', (byte)'o', (byte)'t', (byte)'e' };
            return v;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct WaveDataSection
    {
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 4)]
        public byte[] DATA;

        public uint DataSize;

        public static WaveDataSection CreateDefault()
        {
            WaveDataSection v = default(WaveDataSection);
            v.DataSize = 0;
            v.DATA = new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' };
            return v;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct WaveSampleSection
    {
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 4)]
        public byte[] SMPL;

        public uint SmplSize;
        public uint Manufacturer;
        public uint Product;
        public uint SamplePeriod;
        public uint MidiUnityNote;
        public uint MidiPitchFraction;
        public uint SmpteFormat;
        public uint SmpteOffset;
        public uint SampleLoops;
        public uint SamplerData;
        public uint LoopIdentifier;
        public uint LoopType;
        public uint LoopStart;
        public uint LoopEnd;
        public uint LoopFraction;
        public uint LoopPlayCount;

        public static WaveSampleSection CreateDefault()
        {
            WaveSampleSection v = default(WaveSampleSection);
            v.SmplSize = 0x3c;
            v.Manufacturer = 0;
            v.Product = 0;
            v.SamplePeriod = 0;
            v.MidiUnityNote = 0x3c;
            v.MidiPitchFraction = 0;
            v.SmpteFormat = 0;
            v.SmpteOffset = 0;
            v.SampleLoops = 1;
            v.SamplerData = 0x18;
            v.LoopIdentifier = 0;
            v.LoopType = 0;
            v.LoopStart = 0;
            v.LoopEnd = 0;
            v.LoopFraction = 0;
            v.LoopPlayCount = 0;
            v.SMPL = new byte[] { (byte)'s', (byte)'m', (byte)'p', (byte)'l' };
            return v;
        }
    }
}