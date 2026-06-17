// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core.Utils;
    using System.Text;
    using Xunit;

    public class Base64Tests
    {
        [Theory]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("ab")]
        [InlineData("abc")]
        [InlineData("hello world")]
        [InlineData("The quick brown fox jumps over 12345 +/=")]
        public void Encode_Decode_RoundTrips(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);

            string encoded = Base64.Encode(bytes);
            byte[] decoded = Base64.Decode(encoded);

            Assert.Equal(bytes, decoded);
        }

        [Fact]
        public void Encode_ProducesUrlSafeOutput_WithoutPlusSlashOrPadding()
        {
            // A 0..255 byte sequence yields '+' and '/' in standard Base64, which must be escaped.
            byte[] bytes = new byte[256];
            for (int i = 0; i < bytes.Length; ++i) {
                bytes[i] = (byte)i;
            }

            string encoded = Base64.Encode(bytes);

            Assert.DoesNotContain("+", encoded);
            Assert.DoesNotContain("/", encoded);
            Assert.DoesNotContain("=", encoded);

            // Still round-trips after escaping/padding removal.
            Assert.Equal(bytes, Base64.Decode(encoded));
        }

        [Fact]
        public void Decode_RestoresEscapedCharacters()
        {
            // 0xFF, 0xEF, 0xFE -> standard Base64 "/+/+" -> url-safe "_-_-" (no padding needed)
            byte[] original = new byte[] { 0xFF, 0xEF, 0xFE };

            string encoded = Base64.Encode(original);

            Assert.Equal("_-_-", encoded);
            Assert.Equal(original, Base64.Decode(encoded));
        }
    }
}
