using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.PixelFormats;

namespace ConvertHelper.Afp
{
    internal readonly struct AfpPoint
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public AfpPoint(double x, double y, double z = 0.0)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static AfpPoint Zero => new AfpPoint(0.0, 0.0, 0.0);
        public AfpPoint Subtract(AfpPoint other) => new AfpPoint(X - other.X, Y - other.Y, Z - other.Z);
    }

    internal readonly struct AfpColor
    {
        public readonly double R;
        public readonly double G;
        public readonly double B;
        public readonly double A;

        public AfpColor(double r, double g, double b, double a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static AfpColor White => new AfpColor(1.0, 1.0, 1.0, 1.0);
        public static AfpColor Transparent => new AfpColor(0.0, 0.0, 0.0, 0.0);

        public AfpColor Multiply(AfpColor other) =>
            new AfpColor(R * other.R, G * other.G, B * other.B, A * other.A);

        public AfpColor Add(AfpColor other) =>
            new AfpColor(R + other.R, G + other.G, B + other.B, A + other.A);
    }

    internal readonly struct AfpHsl
    {
        public readonly double H;
        public readonly double S;
        public readonly double L;

        public AfpHsl(double h, double s, double l)
        {
            H = h;
            S = s;
            L = l;
        }

        public static AfpHsl Zero => new AfpHsl(0.0, 0.0, 0.0);
        public bool IsIdentity => Math.Abs(H) < 0.000001 && Math.Abs(S) < 0.000001 && Math.Abs(L) < 0.000001;
        public AfpHsl Add(AfpHsl other) => new AfpHsl(H + other.H, S + other.S, L + other.L);
    }

    internal sealed class AfpMatrix
    {
        public double A11 = 1.0;
        public double A12;
        public double A13;
        public double A21;
        public double A22 = 1.0;
        public double A23;
        public double A31;
        public double A32;
        public double A33 = 1.0;
        public double A41;
        public double A42;
        public double A43;

        public bool ScaleSet;
        public bool RotateSet;
        public bool TranslateXySet;
        public bool TranslateZSet;
        public bool Grid3DSet;

        public static AfpMatrix Identity() => new AfpMatrix();

        public AfpMatrix Clone() => new AfpMatrix
        {
            A11 = A11, A12 = A12, A13 = A13,
            A21 = A21, A22 = A22, A23 = A23,
            A31 = A31, A32 = A32, A33 = A33,
            A41 = A41, A42 = A42, A43 = A43,
            ScaleSet = ScaleSet, RotateSet = RotateSet,
            TranslateXySet = TranslateXySet, TranslateZSet = TranslateZSet,
            Grid3DSet = Grid3DSet,
        };

        public AfpMatrix ToAffine()
        {
            return new AfpMatrix
            {
                A11 = A11, A12 = A12,
                A21 = A21, A22 = A22,
                A41 = A41, A42 = A42,
                ScaleSet = ScaleSet, RotateSet = RotateSet,
                TranslateXySet = TranslateXySet,
            };
        }

        public AfpPoint MultiplyPoint(AfpPoint point)
        {
            return new AfpPoint(
                A11 * point.X + A21 * point.Y + A31 * point.Z + A41,
                A12 * point.X + A22 * point.Y + A32 * point.Z + A42,
                A13 * point.X + A23 * point.Y + A33 * point.Z + A43);
        }

        public AfpMatrix Multiply(AfpMatrix other)
        {
            return new AfpMatrix
            {
                A11 = A11 * other.A11 + A12 * other.A21 + A13 * other.A31,
                A12 = A11 * other.A12 + A12 * other.A22 + A13 * other.A32,
                A13 = A11 * other.A13 + A12 * other.A23 + A13 * other.A33,
                A21 = A21 * other.A11 + A22 * other.A21 + A23 * other.A31,
                A22 = A21 * other.A12 + A22 * other.A22 + A23 * other.A32,
                A23 = A21 * other.A13 + A22 * other.A23 + A23 * other.A33,
                A31 = A31 * other.A11 + A32 * other.A21 + A33 * other.A31,
                A32 = A31 * other.A12 + A32 * other.A22 + A33 * other.A32,
                A33 = A31 * other.A13 + A32 * other.A23 + A33 * other.A33,
                A41 = A41 * other.A11 + A42 * other.A21 + A43 * other.A31 + other.A41,
                A42 = A41 * other.A12 + A42 * other.A22 + A43 * other.A32 + other.A42,
                A43 = A41 * other.A13 + A42 * other.A23 + A43 * other.A33 + other.A43,
            };
        }

        public AfpMatrix Translate(AfpPoint point)
        {
            AfpMatrix result = Clone();
            AfpPoint translated = MultiplyPoint(point);
            result.A41 = translated.X;
            result.A42 = translated.Y;
            result.A43 = translated.Z;
            return result;
        }

        public AfpMatrix Update(AfpMatrix other, bool perspective)
        {
            AfpMatrix result = Clone();
            if (!(other.ScaleSet || other.RotateSet || other.Grid3DSet || other.TranslateXySet || other.TranslateZSet))
            {
                if (perspective)
                {
                    result.A11 = other.A11; result.A12 = other.A12; result.A13 = other.A13;
                    result.A21 = other.A21; result.A22 = other.A22; result.A23 = other.A23;
                    result.A31 = other.A31; result.A32 = other.A32; result.A33 = other.A33;
                    result.A41 = other.A41; result.A42 = other.A42; result.A43 = other.A43;
                }
                else
                {
                    result.A11 = other.A11; result.A12 = other.A12;
                    result.A21 = other.A21; result.A22 = other.A22;
                    result.A41 = other.A41; result.A42 = other.A42;
                }
                return result;
            }

            if (other.Grid3DSet && perspective)
            {
                result.A11 = other.A11; result.A12 = other.A12; result.A13 = other.A13;
                result.A21 = other.A21; result.A22 = other.A22; result.A23 = other.A23;
                result.A31 = other.A31; result.A32 = other.A32; result.A33 = other.A33;
            }
            else
            {
                if (other.ScaleSet) { result.A11 = other.A11; result.A22 = other.A22; }
                if (other.RotateSet) { result.A12 = other.A12; result.A21 = other.A21; }
            }
            if (other.TranslateXySet) { result.A41 = other.A41; result.A42 = other.A42; }
            if (other.TranslateZSet && perspective) result.A43 = other.A43;
            return result;
        }

        public bool TryInverseAffine(out AfpMatrix inverse)
        {
            double determinant = A11 * A22 - A12 * A21;
            if (Math.Abs(determinant) < 0.000000001)
            {
                inverse = null;
                return false;
            }

            double inv = 1.0 / determinant;
            inverse = new AfpMatrix
            {
                A11 = A22 * inv,
                A12 = -A12 * inv,
                A21 = -A21 * inv,
                A22 = A11 * inv,
            };
            inverse.A41 = -(A41 * inverse.A11 + A42 * inverse.A21);
            inverse.A42 = -(A41 * inverse.A12 + A42 * inverse.A22);
            return true;
        }
    }

    internal sealed class AfpTexture
    {
        public string Name;
        public int Width;
        public int Height;
        public Rgba32[] Pixels;
    }

    internal sealed class AfpShape
    {
        public string Reference;
        public int Width;
        public int Height;
        public List<AfpDrawParams> DrawParams = new List<AfpDrawParams>();
    }

    internal sealed class AfpDrawParams
    {
        public int Flags;
        public string TextureName;
        public AfpColor? BlendColor;
    }

    internal abstract class AfpTag { }

    internal sealed class AfpShapeTag : AfpTag
    {
        public int Id;
        public string Reference;
    }

    internal sealed class AfpSpriteTag : AfpTag
    {
        public int Id;
        public AfpTimeline Timeline;
    }

    internal sealed class AfpPlaceTag : AfpTag
    {
        public const int ProjectionNone = 0;
        public const int ProjectionAffine = 1;
        public const int ProjectionPerspective = 2;

        public int ObjectId;
        public int Depth;
        public int? SourceTagId;
        public int? Blend;
        public bool Update;
        public AfpMatrix Transform;
        public AfpPoint? RotationOrigin;
        public int Projection;
        public AfpColor? MultiplyColor;
        public AfpColor? AddColor;
        public AfpHsl? HslShift;
    }

    internal sealed class AfpRemoveTag : AfpTag
    {
        public int ObjectId;
        public int Depth;
    }

    internal sealed class AfpActionTag : AfpTag
    {
        public byte[] ByteCode;
    }

    internal sealed class AfpFrame
    {
        public int StartTag;
        public int TagCount;
    }

    internal sealed class AfpTimeline
    {
        public string MovieName;
        public int? TagId;
        public List<AfpTag> Tags = new List<AfpTag>();
        public List<AfpFrame> Frames = new List<AfpFrame>();
        public Dictionary<string, int> Labels = new Dictionary<string, int>();
    }

    internal sealed class AfpImport
    {
        public string SwfName;
        public string TagName;
    }

    internal sealed class AfpMovie
    {
        public string FileName;
        public string ExportedName;
        public double Fps;
        public int Width;
        public int Height;
        public AfpTimeline Root;
        public Dictionary<string, int> ExportedTags = new Dictionary<string, int>();
        public Dictionary<int, AfpImport> ImportedTags = new Dictionary<int, AfpImport>();
    }
}
