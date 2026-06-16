// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core.Utils;
    using System;
    using Xunit;

    public class DateTimeHelperTests
    {
        [Fact]
        public void AsUtc_Unspecified_IsTreatedAsUtc_PreservingTicks()
        {
            var value = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

            DateTime result = DateTimeHelper.AsUtc(value);

            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(value.Ticks, result.Ticks);
        }

        [Fact]
        public void AsUtc_Local_IsConvertedToUtc()
        {
            var local = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Local);

            DateTime result = DateTimeHelper.AsUtc(local);

            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(local.ToUniversalTime(), result);
        }

        [Fact]
        public void AsUtc_Utc_IsUnchanged()
        {
            var utc = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            DateTime result = DateTimeHelper.AsUtc(utc);

            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(utc, result);
        }

        [Fact]
        public void AsUtc_MinValue_StaysMinValue()
        {
            // Regression guard for issues #329/#331: ticks are preserved so never-set timestamps
            // do not shift and overflow when later converted to DateTimeOffset.
            DateTime result = DateTimeHelper.AsUtc(DateTime.MinValue);

            Assert.Equal(DateTime.MinValue.Ticks, result.Ticks);
            Assert.Equal(DateTimeKind.Utc, result.Kind);
        }

        [Fact]
        public void AsUtc_MaxValue_StaysMaxValue()
        {
            DateTime result = DateTimeHelper.AsUtc(DateTime.MaxValue);

            Assert.Equal(DateTime.MaxValue.Ticks, result.Ticks);
            Assert.Equal(DateTimeKind.Utc, result.Kind);
        }

        [Fact]
        public void AsUtc_NullableNull_ReturnsNull()
        {
            Assert.Null(DateTimeHelper.AsUtc((DateTime?)null));
        }

        [Fact]
        public void AsUtc_NullableValue_IsConverted()
        {
            DateTime? value = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

            DateTime? result = DateTimeHelper.AsUtc(value);

            Assert.NotNull(result);
            Assert.Equal(DateTimeKind.Utc, result.Value.Kind);
            Assert.Equal(value.Value.Ticks, result.Value.Ticks);
        }
    }
}
