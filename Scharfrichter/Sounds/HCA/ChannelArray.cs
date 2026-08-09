using System;
using System.Runtime.InteropServices;

namespace Scharfrichter.Codec.Sounds.HCA
{
    internal sealed unsafe class ChannelArray : IDisposable
    {
        public ChannelArray(int channelCount)
        {
            ChannelCount = channelCount;
            int totalSize = channelCount * ChannelSize;
            _basePtr = Marshal.AllocHGlobal(totalSize);
            ZeroMemory(_basePtr.ToPointer(), totalSize);
        }

        public int ChannelCount { get; }

        public void Decode1(int channelIndex, DataBits data, uint a, int b, byte[] ath)
        {
            int v = data.GetBit(3);
            uint* pCount = GetPtrOfCount(channelIndex);
            sbyte* pValue = GetPtrOfValue(channelIndex);

            if (v >= 6)
            {
                for (int i = 0; i < *pCount; ++i)
                {
                    pValue[i] = (sbyte)data.GetBit(6);
                }
            }
            else if (v != 0)
            {
                int v1 = data.GetBit(6);
                int v2 = (1 << v) - 1;
                int v3 = v2 >> 1;

                pValue[0] = (sbyte)v1;

                for (int i = 1; i < *pCount; ++i)
                {
                    int v4 = data.GetBit(v);

                    if (v4 != v2)
                    {
                        v1 += v4 - v3;
                    }
                    else
                    {
                        v1 = data.GetBit(6);
                    }

                    pValue[i] = (sbyte)v1;
                }
            }
            else
            {
                ZeroMemory(pValue, 0x80);
            }

            int* pType = GetPtrOfType(channelIndex);
            sbyte* pValue2 = GetPtrOfValue2(channelIndex);
            sbyte** ppValue3 = GetPtrOfValue3(channelIndex);

            if (*pType == 2)
            {
                v = data.CheckBit(4);
                pValue2[0] = (sbyte)v;

                if (v < 15)
                {
                    for (int i = 0; i < 8; ++i)
                    {
                        pValue2[i] = (sbyte)data.GetBit(4);
                    }
                }
            }
            else
            {
                for (int i = 0; i < a; ++i)
                {
                    (*ppValue3)[i] = (sbyte)data.GetBit(6);
                }
            }

            sbyte* pScale = GetPtrOfScale(channelIndex);

            for (int i = 0; i < *pCount; ++i)
            {
                v = pValue[i];

                if (v != 0)
                {
                    v = ath[i] + ((b + i) >> 8) - ((v * 5) >> 1) + 1;

                    if (v < 0)
                    {
                        v = 15;
                    }
                    else if (v >= 0x39)
                    {
                        v = 1;
                    }
                    else
                    {
                        v = ChannelTables.Decode1ScaleList[v];
                    }
                }

                pScale[i] = (sbyte)v;
            }

            ZeroMemory(&pScale[*pCount], (int)(0x80 - *pCount));

            float* pBase = GetPtrOfBase(channelIndex);

            for (int i = 0; i < *pCount; ++i)
            {
                pBase[i] = ChannelTables.Decode1ValueSingle[pValue[i]] * ChannelTables.Decode1ScaleSingle[pScale[i]];
            }
        }

        public void Decode2(int channelIndex, DataBits data)
        {
            uint* pCount = GetPtrOfCount(channelIndex);
            sbyte* pScale = GetPtrOfScale(channelIndex);
            float* pBlock = GetPtrOfBlock(channelIndex);
            float* pBase = GetPtrOfBase(channelIndex);

            for (int i = 0; i < *pCount; ++i)
            {
                int s = pScale[i];
                int bitSize = ChannelTables.Decode2List1[s];
                int v = data.GetBit(bitSize);
                float f;

                if (s < 8)
                {
                    v += s << 4;
                    data.AddBit(ChannelTables.Decode2List2[v] - bitSize);
                    f = ChannelTables.Decode2List3[v];
                }
                else
                {
                    v = (1 - ((v & 1) << 1)) * (v >> 1);

                    if (v == 0)
                    {
                        data.AddBit(-1);
                    }

                    f = v;
                }

                pBlock[i] = pBase[i] * f;
            }

            ZeroMemory(&pBlock[*pCount], sizeof(float) * (int)(0x80 - *pCount));
        }

        public void Decode3(int channelIndex, uint a, uint b, uint c, uint d)
        {
            int* pType = GetPtrOfType(channelIndex);

            if (*pType != 2 && b != 0)
            {
                fixed (float* listFloatBase = ChannelTables.Decode3ListSingle)
                {
                    float* pBlock = GetPtrOfBlock(channelIndex);
                    sbyte** ppValue3 = GetPtrOfValue3(channelIndex);
                    sbyte* pValue = GetPtrOfValue(channelIndex);
                    float* listFloat = listFloatBase + 0x40;

                    uint k = c;
                    uint l = c - 1;

                    for (int i = 0; i < a; ++i)
                    {
                        for (int j = 0; j < b && k < d; ++j, --l)
                        {
                            pBlock[k++] = listFloat[(*ppValue3)[i] - pValue[l]] * pBlock[l];
                        }
                    }

                    pBlock[0x80 - 1] = 0;
                }
            }
        }

        public void Decode4(int channelIndex1, int channelIndex2, int index, uint a, uint b, uint c)
        {
            int* pTypeA = GetPtrOfType(channelIndex1);

            if (*pTypeA == 1 && c != 0)
            {
                sbyte* pValue2B = GetPtrOfValue2(channelIndex2);
                float* pBlockA = GetPtrOfBlock(channelIndex1);
                float* pBlockB = GetPtrOfBlock(channelIndex2);

                float f1 = ChannelTables.Decode4ListSingle[pValue2B[index]];
                float f2 = f1 - 2.0f;

                float* s = &pBlockA[b];
                float* d = &pBlockB[b];

                for (int i = 0; i < a; ++i)
                {
                    *(d++) = *s * f2;
                    *(s++) = *s * f1;
                }
            }
        }

        public void Decode5(int channelIndex, int index)
        {
            float* s, d;

            s = GetPtrOfBlock(channelIndex);
            d = GetPtrOfWav1(channelIndex);

            int count1 = 1;
            int count2 = 0x40;

            for (int i = 0; i < 7; ++i, count1 <<= 1, count2 >>= 1)
            {
                float* d1 = d;
                float* d2 = &d[count2];

                for (int j = 0; j < count1; ++j)
                {
                    for (int k = 0; k < count2; ++k)
                    {
                        float a = *(s++);
                        float b = *(s++);

                        *(d1++) = b + a;
                        *(d2++) = a - b;
                    }

                    d1 += count2;
                    d2 += count2;
                }

                float* w = &s[-0x80];
                s = d;
                d = w;
            }

            s = GetPtrOfWav1(channelIndex);
            d = GetPtrOfBlock(channelIndex);

            fixed (float* list1FloatBase = ChannelTables.Decode5List1Single)
            fixed (float* list2FloatBase = ChannelTables.Decode5List2Single)
            {
                count1 = 0x40;
                count2 = 1;

                for (int i = 0; i < 7; ++i, count1 >>= 1, count2 <<= 1)
                {
                    float* list1Float = &list1FloatBase[i * 0x40];
                    float* list2Float = &list2FloatBase[i * 0x40];

                    float* s1 = s;
                    float* s2 = &s1[count2];
                    float* d1 = d;
                    float* d2 = &d1[count2 * 2 - 1];

                    for (int j = 0; j < count1; ++j)
                    {
                        for (int k = 0; k < count2; ++k)
                        {
                            float fa = *(s1++);
                            float fb = *(s2++);
                            float fc = *(list1Float++);
                            float fd = *(list2Float++);

                            *(d1++) = fa * fc - fb * fd;
                            *(d2--) = fa * fd + fb * fc;
                        }

                        s1 += count2;
                        s2 += count2;
                        d1 += count2;
                        d2 += count2 * 3;
                    }

                    float* w = s;
                    s = d;
                    d = w;
                }
            }

            d = GetPtrOfWav2(channelIndex);

            for (int i = 0; i < 0x80; ++i)
            {
                *(d++) = *(s++);
            }

            fixed (float* list3FloatBase = ChannelTables.Decode5List3Single)
            {
                s = list3FloatBase;
                d = GetPtrOfWave(channelIndex) + index * 0x80;

                float* s1 = &GetPtrOfWav2(channelIndex)[0x40];
                float* s2 = GetPtrOfWav3(channelIndex);

                for (int i = 0; i < 0x40; ++i)
                {
                    *(d++) = *(s1++) * *(s++) + *(s2++);
                }

                for (int i = 0; i < 0x40; ++i)
                {
                    *(d++) = *(s++) * *(--s1) - *(s2++);
                }

                s1 = &GetPtrOfWav2(channelIndex)[0x40 - 1];
                s2 = GetPtrOfWav3(channelIndex);

                for (int i = 0; i < 0x40; ++i)
                {
                    *(s2++) = *(s1--) * *(--s);
                }

                for (int i = 0; i < 0x40; ++i)
                {
                    *(s2++) = *(--s) * *(++s1);
                }
            }
        }

        public void* GetBasePtr(int channelIndex)
        {
            return (void*)(_basePtr + ChannelSize * channelIndex);
        }

        public float* GetPtrOfBlock(int channelIndex)
        {
            return (float*)GetPtrOf(channelIndex, OffsetOfBlock);
        }

        public float* GetPtrOfBase(int channelIndex)
        {
            return (float*)GetPtrOf(channelIndex, OffsetOfBase);
        }

        public sbyte* GetPtrOfValue(int channelIndex)
        {
            return (sbyte*)GetPtrOf(channelIndex, OffsetOfValue);
        }

        public sbyte* GetPtrOfScale(int channelIndex)
        {
            return (sbyte*)GetPtrOf(channelIndex, OffsetOfScale);
        }

        public sbyte* GetPtrOfValue2(int channelIndex)
        {
            return (sbyte*)GetPtrOf(channelIndex, OffsetOfValue2);
        }

        public int* GetPtrOfType(int channelIndex)
        {
            return (int*)GetPtrOf(channelIndex, OffsetOfType);
        }

        public sbyte** GetPtrOfValue3(int channelIndex)
        {
            return (sbyte**)GetPtrOf(channelIndex, OffsetOfValue3);
        }

        public uint* GetPtrOfCount(int channelIndex)
        {
            return (uint*)GetPtrOf(channelIndex, OffsetOfCount);
        }

        public float* GetPtrOfWav1(int channelIndex)
        {
            return (float*)GetPtrOf(channelIndex, OffsetOfWav1);
        }

        public float* GetPtrOfWav2(int channelIndex)
        {
            return (float*)GetPtrOf(channelIndex, OffsetOfWav2);
        }

        public float* GetPtrOfWav3(int channelIndex)
        {
            return (float*)GetPtrOf(channelIndex, OffsetOfWav3);
        }

        public float* GetPtrOfWave(int channelIndex)
        {
            return (float*)GetPtrOf(channelIndex, OffsetOfWave);
        }

        public void Dispose()
        {
            if (_basePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_basePtr);
            }

            _basePtr = IntPtr.Zero;
        }

        private IntPtr GetPtrOf(int channelIndex, IntPtr fieldOffset)
        {
            return _basePtr + ChannelSize * channelIndex + fieldOffset.ToInt32();
        }

        private static void ZeroMemory(void* ptr, int byteCount)
        {
            if (ptr == null || byteCount <= 0)
            {
                return;
            }

            byte* p = (byte*)ptr;

            for (int i = 0; i < byteCount; ++i)
            {
                p[i] = 0;
            }
        }

        private static readonly int ChannelSize = Marshal.SizeOf(typeof(Channel));

        private static readonly IntPtr OffsetOfBlock = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Block));
        private static readonly IntPtr OffsetOfBase = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Base));
        private static readonly IntPtr OffsetOfValue = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Value));
        private static readonly IntPtr OffsetOfScale = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Scale));
        private static readonly IntPtr OffsetOfValue2 = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Value2));
        private static readonly IntPtr OffsetOfType = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Type));
        private static readonly IntPtr OffsetOfValue3 = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Value3));
        private static readonly IntPtr OffsetOfCount = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Count));
        private static readonly IntPtr OffsetOfWav1 = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Wav1));
        private static readonly IntPtr OffsetOfWav2 = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Wav2));
        private static readonly IntPtr OffsetOfWav3 = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Wav3));
        private static readonly IntPtr OffsetOfWave = Marshal.OffsetOf(typeof(Channel), nameof(Channel.Wave));

        private IntPtr _basePtr;
    }
}