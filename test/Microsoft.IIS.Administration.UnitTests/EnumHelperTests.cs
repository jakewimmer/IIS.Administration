// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core.Utils;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Xunit;

    public class EnumHelperTests
    {
        [Flags]
        private enum Colors
        {
            None = 0,
            Red = 1,
            Green = 2,
            Blue = 4
        }

        [Fact]
        public void GetFlags_ReturnsEachSetFlag()
        {
            List<Enum> flags = (Colors.Red | Colors.Blue).GetFlags().ToList();

            Assert.Equal(2, flags.Count);
            Assert.Contains(Colors.Red, flags);
            Assert.Contains(Colors.Blue, flags);
            Assert.DoesNotContain(Colors.Green, flags);
        }

        [Fact]
        public void GetFlags_ExcludesZeroValue()
        {
            List<Enum> flags = Colors.None.GetFlags().ToList();

            Assert.Empty(flags);
        }

        [Fact]
        public void GetFlags_AllFlagsSet_ReturnsAllNonZero()
        {
            List<Enum> flags = (Colors.Red | Colors.Green | Colors.Blue).GetFlags().ToList();

            Assert.Equal(3, flags.Count);
            Assert.DoesNotContain(Colors.None, flags);
        }
    }
}
