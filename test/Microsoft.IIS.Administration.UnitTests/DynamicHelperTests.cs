// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core;
    using Microsoft.IIS.Administration.Core.Utils;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Xunit;

    public class DynamicHelperTests
    {
        private enum Sample { First, Second }

        [Fact]
        public void To_Null_ReturnsNull()
        {
            Assert.Null(DynamicHelper.To<int>(null));
        }

        [Fact]
        public void To_LongValue_ConvertsToInt()
        {
            Assert.Equal(5, DynamicHelper.To<int>(5L));
        }

        [Fact]
        public void To_StringValue_ConvertsToInt()
        {
            Assert.Equal(7, DynamicHelper.To<int>("7"));
        }

        [Fact]
        public void To_BoolValue_ReturnsBool()
        {
            Assert.Equal(true, DynamicHelper.To<bool>(true));
            Assert.Equal(true, DynamicHelper.To<bool>("true"));
        }

        [Fact]
        public void To_Enum_IsParsedCaseInsensitively()
        {
            Assert.Equal(Sample.Second, DynamicHelper.To<Sample>("second"));
        }

        [Fact]
        public void To_TimeSpan_IsParsed()
        {
            Assert.Equal(TimeSpan.FromMinutes(5), DynamicHelper.To<TimeSpan>("00:05:00"));
        }

        [Fact]
        public void To_JValue_IsUnwrappedAndConverted()
        {
            Assert.Equal(9, DynamicHelper.To<int>(new JValue(9L)));
        }

        [Fact]
        public void To_JValueWithBadFormat_ThrowsApiArgumentException()
        {
            Assert.Throws<ApiArgumentException>(() => DynamicHelper.To<int>(new JValue("notanumber")));
        }

        [Fact]
        public void ToWithRange_InRange_ReturnsValue()
        {
            Assert.Equal(5L, DynamicHelper.To(5L, 0, 10));
        }

        [Fact]
        public void ToWithRange_OutOfRange_Throws()
        {
            Assert.Throws<ApiArgumentOutOfRangeException>(() => DynamicHelper.To(50L, 0, 10));
        }

        [Fact]
        public void ToLong_ParsesHexString()
        {
            Assert.Equal(255L, DynamicHelper.ToLong("ff", 16));
        }

        [Fact]
        public void ToLong_Null_ReturnsNull()
        {
            Assert.Null(DynamicHelper.ToLong(null, 16));
        }

        [Fact]
        public void ToLong_OutOfRange_Throws()
        {
            Assert.Throws<ApiArgumentOutOfRangeException>(() => DynamicHelper.ToLong("ff", 16, 0, 100));
        }

        [Fact]
        public void Value_Null_ReturnsNull()
        {
            Assert.Null(DynamicHelper.Value(null));
        }

        [Fact]
        public void Value_String_ReturnsString()
        {
            Assert.Equal("x", DynamicHelper.Value("x"));
        }

        [Fact]
        public void Value_JValue_ReturnsUnderlyingString()
        {
            Assert.Equal("y", DynamicHelper.Value(new JValue("y")));
        }

        [Fact]
        public void ToListOfStruct_ConvertsEachElement()
        {
            List<int> result = DynamicHelper.ToList<int>(new List<dynamic> { 1L, 2L, 3L });

            Assert.Equal(new[] { 1, 2, 3 }, result);
        }

        [Fact]
        public void ToListOfStruct_NullElement_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DynamicHelper.ToList<int>(new List<dynamic> { 1L, null }));
        }

        [Fact]
        public void ToListOfString_ExtractsEachValue()
        {
            List<string> result = DynamicHelper.ToList(new List<dynamic> { "a", "b" });

            Assert.Equal(new[] { "a", "b" }, result);
        }

        [Fact]
        public void ToJObject_NonJsonElement_ReturnsSameValue()
        {
            Assert.Equal("hello", (string)DynamicHelper.ToJObject("hello"));
        }

        [Fact]
        public void ToJObject_JsonElement_IsConvertedToJObject()
        {
            JsonElement element = JsonDocument.Parse("{\"a\":5}").RootElement;

            dynamic result = DynamicHelper.ToJObject(element);

            Assert.IsType<JObject>(result);
            Assert.Equal(5, (int)result.a);
        }
    }
}
