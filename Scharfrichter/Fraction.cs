using System;

namespace Scharfrichter.Codec
{
    public struct Fraction
    {
        static private long minVal = int.MinValue / 2;
        static private long maxVal = int.MaxValue / 2;

        public static Fraction operator +(Fraction a, Fraction b)
        {
            Fraction result = new Fraction();
            Fraction commonA;
            Fraction commonB;

            Commonize(a, b, out commonA, out commonB);
            result.Numerator = (commonA.Numerator + commonB.Numerator);
            result.Denominator = commonA.Denominator;

            return Reduce(result);
        }

        public static Fraction operator -(Fraction a, Fraction b)
        {
            Fraction result = new Fraction();
            Fraction commonA;
            Fraction commonB;

            Commonize(a, b, out commonA, out commonB);
            result.Numerator = (commonA.Numerator - commonB.Numerator);
            result.Denominator = commonA.Denominator;

            return Reduce(result);
        }

        public static Fraction operator *(Fraction a, Fraction b)
        {
            a = Shrink(a);
            b = Shrink(b);
            if (!IsMultiplySafe(a.Numerator, b.Numerator) || !IsMultiplySafe(a.Denominator, b.Denominator))
                return Rationalize((double)a * (double)b);
            Fraction result = new Fraction();
            result.Numerator = (a.Numerator * b.Numerator);
            result.Denominator = (a.Denominator * b.Denominator);
            return Reduce(result);
        }

        public static Fraction operator /(Fraction a, Fraction b)
        {
            a = Shrink(a);
            b = Shrink(b);
            if (!IsMultiplySafe(a.Numerator, b.Denominator) || !IsMultiplySafe(a.Denominator, b.Numerator))
                return Rationalize((double)a / (double)b);
            Fraction result = new Fraction();
            result.Numerator = (a.Numerator * b.Denominator);
            result.Denominator = (a.Denominator * b.Numerator);
            return Reduce(result);
        }

        public static bool operator ==(Fraction a, Fraction b)
        {
            Commonize(a, b, out a, out b);
            return (a.Numerator == b.Numerator);
        }

        public static bool operator !=(Fraction a, Fraction b)
        {
            Commonize(a, b, out a, out b);
            return (a.Numerator != b.Numerator);
        }

        public static explicit operator Fraction(double d)
        {
            return Rationalize(d);
        }

        public static explicit operator double(Fraction f)
        {
            if (f.Denominator == 0)
                return 0;
            return ((double)f.Numerator / (double)f.Denominator);
        }

        public override bool Equals(object obj)
        {
            Fraction other = (Fraction)obj;
            Fraction a;
            Fraction b;
            Commonize(this, other, out a, out b);
            return (a.Numerator == b.Numerator);
        }

        public override int GetHashCode()
        {
            // this is a poor way to do it, I know that, but VS wants it
            long num = numerator;
            long den = denominator;
            num <<= 32;
            num >>= 32;
            den <<= 32;
            den >>= 32;
            return (int)num ^ (int)den;
        }

        public override string ToString()
        {
            return Numerator.ToString() + "/" + Denominator.ToString() + ":" + (Denominator == 0 ? "undef" : ((double)Numerator / (double)Denominator).ToString());
        }

        public static long CommonDenominator(Fraction[] fractions)
        {
            int count = fractions.Length;
            long result = 1;

            for (int i = 0; i < count; i++)
            {
                Fraction frac = Fraction.Reduce(fractions[i]);
                if (frac.denominator != 0)
                {
                    if (result % frac.denominator != 0)
                    {
                        result *= frac.denominator;
                    }
                }
            }

            return result;
        }

        public static void Commonize(Fraction a, Fraction b, out Fraction outputA, out Fraction outputB)
        {
            long[] Primes = Util.Primes;
            int PrimeCount = Util.PrimeCount;

            a = Shrink(a);
            b = Shrink(b);

            if (!IsMultiplySafe(a.Numerator, b.Denominator) ||
                !IsMultiplySafe(a.Denominator, b.Denominator) ||
                !IsMultiplySafe(b.Numerator, a.Denominator))
            {
                double da = Math.Abs((double)a);
                double db = Math.Abs((double)b);
                outputA = (da > db) ? a : Shrink(a);
                outputB = (db > da) ? b : Shrink(b);
                return;
            }

            long newNumeratorA = a.Numerator * b.Denominator;
            long newDenominator = a.Denominator * b.Denominator;
            long newNumeratorB = b.Numerator * a.Denominator;
            bool finished = false;

            if (a.Denominator != b.Denominator)
            {
                while (!finished)
                {
                    finished = true;
                    for (int i = 0; i < PrimeCount; i++)
                    {
                        long thisPrime = Primes[i];

                        if (thisPrime > newDenominator)
                            break;

                        if ((newDenominator % thisPrime == 0) && (newNumeratorA % thisPrime == 0) && (newNumeratorB % thisPrime == 0))
                        {
                            newDenominator /= thisPrime;
                            newNumeratorA /= thisPrime;
                            newNumeratorB /= thisPrime;
                            i--;
                        }
                    }
                }
                outputA = new Fraction(newNumeratorA, newDenominator);
                outputB = new Fraction(newNumeratorB, newDenominator);
            }
            else
            {
                outputA = a;
                outputB = b;
            }
        }

        private static bool IsMultiplySafe(long a, long b)
        {
            if (a == 0 || b == 0)
                return true;
            long absA = Math.Abs(a);
            long absB = Math.Abs(b);
            return absA <= long.MaxValue / absB;
        }

        public static Fraction Compound(Fraction f, long val)
        {
            return new Fraction(f.Numerator * val, f.Denominator * val);
        }

        public static Fraction Quantize(Fraction f, long val)
        {
            return new Fraction((f.Numerator * val) / f.Denominator, val);
        }

        public static Fraction Rationalize(double input)
        {
            double original = input;
            Fraction result = new Fraction();
            result.Denominator = 1;
            int maxIterations = 15;
            while (input != Math.Round(input) && maxIterations > 0)
            {
                input *= 10;
                result.Denominator *= 10;
                maxIterations--;
            }
            if (result.Denominator < 0 || result.Denominator > long.MaxValue / 100)
            {
                result.Denominator = 1;
                result.Numerator = (long)Math.Round(original);
                return result;
            }
            result.Numerator = (long)Math.Round(input);
            return Reduce(result);
        }

        public Fraction Reciprocate()
        {
            return new Fraction(Denominator, Numerator);
        }

        public static Fraction Reduce(Fraction input)
        {
            long[] Primes = Util.Primes;
            int PrimeCount = Util.PrimeCount;
            bool finished = false;

            if (input.Numerator == 0)
            {
                input.Denominator = 1;
                return input;
            }

            while (!finished)
            {
                finished = true;
                for (int i = 0; i < PrimeCount; i++)
                {
                    long thisPrime = Primes[i];

                    if (thisPrime > input.Denominator)
                        break;

                    if ((input.Denominator % thisPrime == 0) && (input.Numerator % thisPrime == 0))
                    {
                        input.Denominator /= thisPrime;
                        input.Numerator /= thisPrime;
                        i--;
                    }
                }
            }
            return input;
        }

        public static Fraction Shrink(Fraction f)
        {
            int limit = 128;
            while (((f.Numerator > maxVal) || (f.Numerator < minVal) || (f.Denominator > maxVal) || (f.Denominator < minVal)) && limit > 0)
            {
                f.Numerator /= 2;
                f.Denominator /= 2;
                limit--;
            }
            if (limit <= 0)
            {
                double d = Math.Abs((double)f);
                return Rationalize(d < 0.0001 && d != 0 ? 0 : d);
            }
            return f;
        }

        public Fraction(long newNum, long newDen)
        {
            denominator = (newDen == 0) ? 1 : newDen;
            numerator = newNum;
        }

        private long denominator;
        private long numerator;

        public long Denominator
        {
            get
            {
                return denominator;
            }
            set
            {
                denominator = (value == 0) ? 1 : value;
            }
        }

        public long Numerator
        {
            get
            {
                return numerator;
            }
            set
            {
                numerator = value;
            }
        }
    }
}