using Scharfrichter.Codec;
using Xunit;

namespace Scharfrichter.Tests
{
    /// <summary>
    /// Unit tests for the core Fraction type: arithmetic operators,
    /// normalization, rationalization and the helper static methods.
    /// </summary>
    public class FractionTests
    {
        [Fact]
        public void ConstructorNormalizesZeroDenominatorToOne()
        {
            var f = new Fraction(1, 0);
            Assert.Equal(1, f.Numerator);
            Assert.Equal(1, f.Denominator);
        }

        [Fact]
        public void ReduceReturnsLowestTerms()
        {
            var f = Fraction.Reduce(new Fraction(6, 8));
            Assert.Equal(3, f.Numerator);
            Assert.Equal(4, f.Denominator);
        }

        [Fact]
        public void ReduceZeroNumeratorSetsUnitDenominator()
        {
            var f = Fraction.Reduce(new Fraction(0, 42));
            Assert.Equal(0, f.Numerator);
            Assert.Equal(1, f.Denominator);
        }

        [Fact]
        public void AdditionFindsCommonDenominator()
        {
            var result = new Fraction(1, 2) + new Fraction(1, 3);
            Assert.Equal(5, result.Numerator);
            Assert.Equal(6, result.Denominator);
        }

        [Fact]
        public void SubtractionFindsCommonDenominator()
        {
            var result = new Fraction(1, 2) - new Fraction(1, 3);
            Assert.Equal(1, result.Numerator);
            Assert.Equal(6, result.Denominator);
        }

        [Fact]
        public void MultiplicationReducesResult()
        {
            var result = new Fraction(2, 3) * new Fraction(3, 4);
            Assert.Equal(1, result.Numerator);
            Assert.Equal(2, result.Denominator);
        }

        [Fact]
        public void DivisionInvertsSecondOperand()
        {
            var result = new Fraction(2, 3) / new Fraction(3, 4);
            Assert.Equal(8, result.Numerator);
            Assert.Equal(9, result.Denominator);
        }

        [Fact]
        public void EqualityIgnoresRepresentation()
        {
            Assert.True(new Fraction(1, 2) == new Fraction(2, 4));
            Assert.False(new Fraction(1, 2) != new Fraction(2, 4));
        }

        [Fact]
        public void EqualsOverrideMatchesOperator()
        {
            Assert.True(new Fraction(1, 2).Equals(new Fraction(2, 4)));
            Assert.False(new Fraction(1, 2).Equals(new Fraction(3, 4)));
        }

        [Fact]
        public void RationalizeConvertsSimpleDecimals()
        {
            Assert.Equal(new Fraction(1, 2), Fraction.Rationalize(0.5));
            Assert.Equal(new Fraction(1, 4), Fraction.Rationalize(0.25));
            Assert.Equal(new Fraction(2, 1), Fraction.Rationalize(2.0));
        }

        [Fact]
        public void RationalizeHandlesNegativeDecimals()
        {
            var f = Fraction.Rationalize(-0.5);
            Assert.Equal(-1, f.Numerator);
            Assert.Equal(2, f.Denominator);
        }

        [Fact]
        public void CompoundMultipliesBothTerms()
        {
            var f = Fraction.Compound(new Fraction(1, 2), 5);
            Assert.Equal(5, f.Numerator);
            Assert.Equal(10, f.Denominator);
        }

        [Fact]
        public void QuantizeBuildsIntegerNumeratorOverVal()
        {
            var f = Fraction.Quantize(new Fraction(3, 4), 16);
            Assert.Equal(12, f.Numerator);
            Assert.Equal(16, f.Denominator);
        }

        [Fact]
        public void CommonDenominatorUsesProductOfUniqueDenominators()
        {
            long result = Fraction.CommonDenominator(new[]
            {
                new Fraction(1, 2),
                new Fraction(1, 3),
                new Fraction(1, 4),
            });
            Assert.Equal(24, result);
        }

        [Fact]
        public void CommonizeProducesEqualDenominators()
        {
            Fraction a;
            Fraction b;
            Fraction.Commonize(new Fraction(1, 2), new Fraction(1, 3), out a, out b);

            Assert.Equal(a.Denominator, b.Denominator);
            Assert.Equal(6, a.Denominator);
            Assert.Equal(3, a.Numerator);
            Assert.Equal(2, b.Numerator);
        }

        [Fact]
        public void ReciprocateSwapsTerms()
        {
            var f = new Fraction(2, 3).Reciprocate();
            Assert.Equal(3, f.Numerator);
            Assert.Equal(2, f.Denominator);
        }

        [Fact]
        public void ExplicitDoubleConversion()
        {
            Assert.Equal(0.25, (double)new Fraction(1, 4), 4);
        }

        [Fact]
        public void DefaultFractionWithZeroDenominatorCastsToZero()
        {
            Assert.Equal(0.0, (double)new Fraction(), 4);
        }

        [Fact]
        public void ExplicitFractionFromDouble()
        {
            var f = (Fraction)0.5;
            Assert.Equal(1, f.Numerator);
            Assert.Equal(2, f.Denominator);
        }
    }
}