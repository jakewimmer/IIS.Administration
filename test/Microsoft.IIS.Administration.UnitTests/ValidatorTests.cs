// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core;
    using Microsoft.IIS.Administration.Core.Utils;
    using Xunit;

    public class ValidatorTests
    {
        [Theory]
        [InlineData(0, 10, 5)]
        [InlineData(0, 10, 0)]   // lower boundary is inclusive
        [InlineData(0, 10, 10)]  // upper boundary is inclusive
        [InlineData(-5, 5, -5)]
        public void WithinRange_ValueInRange_ReturnsValue(long min, long max, long value)
        {
            Assert.Equal(value, Validator.WithinRange(min, max, value, "field"));
        }

        [Theory]
        [InlineData(0, 10, -1)]
        [InlineData(0, 10, 11)]
        [InlineData(0, 10, long.MaxValue)]
        [InlineData(0, 10, long.MinValue)]
        public void WithinRange_ValueOutOfRange_Throws(long min, long max, long value)
        {
            Assert.Throws<ApiArgumentOutOfRangeException>(() => Validator.WithinRange(min, max, value, "field"));
        }

        [Fact]
        public void WithinRange_OutOfRange_ExceptionCarriesParameterName()
        {
            var ex = Assert.Throws<ApiArgumentOutOfRangeException>(() => Validator.WithinRange(0, 10, 99, "myField"));

            Assert.Equal("myField", ex.ParamName);
        }
    }
}
