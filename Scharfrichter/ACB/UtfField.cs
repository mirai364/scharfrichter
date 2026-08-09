using System;
using System.Runtime.InteropServices;

namespace Scharfrichter.Codec.ACB
{
    [StructLayout(LayoutKind.Explicit)]
    public struct NumericUnion
    {
        [FieldOffset(0)]
        public byte U8;
        [FieldOffset(0)]
        public sbyte S8;
        [FieldOffset(0)]
        public short S16;
        [FieldOffset(0)]
        public ushort U16;
        [FieldOffset(0)]
        public int S32;
        [FieldOffset(0)]
        public uint U32;
        [FieldOffset(0)]
        public long S64;
        [FieldOffset(0)]
        public ulong U64;
        [FieldOffset(0)]
        public float R32;
        [FieldOffset(0)]
        public double R64;
    }

    public sealed class UtfField
    {
        internal UtfField()
        {
        }

        public byte Type { get; set; }
        public string Name { get; set; }
        public ColumnType ConstrainedType { get; set; }
        public NumericUnion NumericValue { get; set; }
        public byte[] DataValue { get; set; }
        public string StringValue { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }

        public object GetValue()
        {
            ColumnType constrainedType = ConstrainedType;
            object ret;
            switch (constrainedType)
            {
                case ColumnType.Byte:
                    ret = NumericValue.U8;
                    break;
                case ColumnType.SByte:
                    ret = NumericValue.S8;
                    break;
                case ColumnType.UInt16:
                    ret = NumericValue.U16;
                    break;
                case ColumnType.Int16:
                    ret = NumericValue.S16;
                    break;
                case ColumnType.UInt32:
                    ret = NumericValue.U32;
                    break;
                case ColumnType.Int32:
                    ret = NumericValue.S32;
                    break;
                case ColumnType.UInt64:
                    ret = NumericValue.U64;
                    break;
                case ColumnType.Int64:
                    ret = NumericValue.S64;
                    break;
                case ColumnType.Single:
                    ret = NumericValue.R32;
                    break;
                case ColumnType.Double:
                    ret = NumericValue.R64;
                    break;
                case ColumnType.String:
                    ret = StringValue;
                    break;
                case ColumnType.Data:
                    ret = DataValue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(constrainedType));
            }
            return ret;
        }
    }
}