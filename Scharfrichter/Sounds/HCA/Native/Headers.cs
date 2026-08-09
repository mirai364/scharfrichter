using System.Runtime.InteropServices;

namespace Scharfrichter.Codec.Sounds.HCA.Native
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HcaHeader
    {
        public uint HCA;
        public ushort Version;
        public ushort DataOffset;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FormatHeader
    {
        public uint FMT;
        public uint Channels_And_SamplingRate;
        public uint Blocks;
        public ushort R01;
        public ushort R02;

        public uint Channels
        {
            get { return Channels_And_SamplingRate & 0x000000ff; }
            set { Channels_And_SamplingRate = (Channels_And_SamplingRate & 0xffffff00) | (value & 0x000000ff); }
        }

        public uint SamplingRate
        {
            get { return (Channels_And_SamplingRate & 0xffffff00) >> 8; }
            set { Channels_And_SamplingRate = (Channels_And_SamplingRate & 0x000000ff) | ((value & 0x00ffffff) << 8); }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CompressHeader
    {
        public uint COMP;
        public ushort BlockSize;
        public byte R01;
        public byte R02;
        public byte R03;
        public byte R04;
        public byte R05;
        public byte R06;
        public byte R07;
        public byte R08;
        public byte Reserved1;
        public byte Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DecodeHeader
    {
        public uint DEC;
        public ushort BlockSize;
        public byte R01;
        public byte R02;
        public byte Count1;
        public byte Count2;
        public byte TmpField1;
        public byte EnableCount2Field;

        public byte R03
        {
            get { return (byte)(TmpField1 & 0x0f); }
            set { TmpField1 = (byte)((TmpField1 & 0xf0) | (value & 0x0f)); }
        }

        public byte R04
        {
            get { return (byte)((TmpField1 & 0xf0) >> 4); }
            set { TmpField1 = (byte)((TmpField1 & 0x0f) | ((value & 0x0f) << 4)); }
        }

        public bool EnableCount2
        {
            get { return EnableCount2Field != 0; }
            set { EnableCount2Field = (byte)(value ? 1 : 0); }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VbrHeader
    {
        public uint VBR;
        public ushort R01;
        public ushort R02;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AthHeader
    {
        public uint ATH;
        public ushort Type;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LoopHeader
    {
        public uint LOOP;
        public uint LoopStart;
        public uint LoopEnd;
        public ushort R01;
        public ushort R02;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CipherHeader
    {
        public uint CIPH;
        public ushort Type;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RvaHeader
    {
        public uint RVA;
        public float Volume;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct CommentHeader
    {
        public uint COMM;
        public byte Length;
    }
}