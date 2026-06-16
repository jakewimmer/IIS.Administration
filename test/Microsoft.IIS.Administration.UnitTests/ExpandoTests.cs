// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core.Utils;
    using System.Collections.Generic;
    using Xunit;

    public class ExpandoTests
    {
        [Fact]
        public void ToExpando_CopiesProperties()
        {
            var dict = (IDictionary<string, object>)new { Name = "x", Count = 5 }.ToExpando();

            Assert.Equal(2, dict.Count);
            Assert.Equal("x", dict["Name"]);
            Assert.Equal(5, dict["Count"]);
        }

        [Fact]
        public void ToExpando_EmptyObject_ProducesEmptyExpando()
        {
            var dict = (IDictionary<string, object>)new { }.ToExpando();

            Assert.Empty(dict);
        }
    }
}
