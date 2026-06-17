// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Utils;
    using System;
    using Xunit;

    public class TimeSpanExtensionsTests
    {
        [Theory]
        [InlineData(400, 0, 0, 0, "1 year")]
        [InlineData(800, 0, 0, 0, "2 years")]
        [InlineData(45, 0, 0, 0, "2 months")]
        [InlineData(5, 0, 0, 0, "5 days")]
        [InlineData(1, 0, 0, 0, "1 day")]
        [InlineData(0, 3, 0, 0, "3 hours")]
        [InlineData(0, 1, 0, 0, "1 hour")]
        [InlineData(0, 0, 5, 0, "5 minutes")]
        [InlineData(0, 0, 1, 0, "1 minute")]
        [InlineData(0, 0, 0, 30, "a few moments")]
        [InlineData(0, 0, 0, 0, "a few moments")]
        public void Humanize_FormatsTimeSpan(int days, int hours, int minutes, int seconds, string expected)
        {
            var ts = new TimeSpan(days, hours, minutes, seconds);

            Assert.Equal(expected, ts.Humanize());
        }

        [Fact]
        public void Humanize_NegativeTimeSpan_IsTreatedByMagnitude()
        {
            var ts = new TimeSpan(-5, 0, 0, 0);

            Assert.Equal("5 days", ts.Humanize());
        }
    }
}
