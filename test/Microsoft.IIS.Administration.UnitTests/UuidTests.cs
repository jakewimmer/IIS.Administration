// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core;
    using Microsoft.IIS.Administration.Core.Utils;
    using System;
    using Xunit;

    public class UuidTests
    {
        private const string Purpose = "Test.Purpose";

        // Uuid uses a process-wide AES key. Set a deterministic 256-bit key so the tests are
        // self-contained and order-independent (no other unit-test class exercises Uuid).
        private static void EnsureKey()
        {
            var key = new byte[32];
            for (int i = 0; i < key.Length; ++i) {
                key[i] = (byte)(i + 1);
            }
            Uuid.Key = key;
        }

        [Fact]
        public void EncodeDecode_RoundTrips()
        {
            EnsureKey();

            string encoded = Uuid.Encode("12345", Purpose);

            Assert.Equal("12345", Uuid.Decode(encoded, Purpose));
        }

        [Fact]
        public void Encode_IsStable_ForSameValueAndPurpose()
        {
            EnsureKey();

            Assert.Equal(Uuid.Encode("12345", Purpose), Uuid.Encode("12345", Purpose));
        }

        [Fact]
        public void Encode_DiffersByPurpose()
        {
            EnsureKey();

            Assert.NotEqual(Uuid.Encode("12345", "purpose.a"), Uuid.Encode("12345", "purpose.b"));
        }

        [Fact]
        public void Encode_DiffersByValue()
        {
            EnsureKey();

            Assert.NotEqual(Uuid.Encode("1", Purpose), Uuid.Encode("2", Purpose));
        }

        [Fact]
        public void Encode_ProducesUrlSafeOutput()
        {
            EnsureKey();

            string encoded = Uuid.Encode("a-longer-value-to-encode", Purpose);

            Assert.DoesNotContain("+", encoded);
            Assert.DoesNotContain("/", encoded);
            Assert.DoesNotContain("=", encoded);
        }

        [Fact]
        public void Decode_InvalidUuid_ThrowsNotFound()
        {
            EnsureKey();

            Assert.Throws<NotFoundException>(() => Uuid.Decode("###", Purpose));
        }

        [Fact]
        public void Encode_WithNullKey_Throws()
        {
            byte[] original = Uuid.Key;
            try {
                Uuid.Key = null;
                Assert.Throws<ArgumentNullException>(() => Uuid.Encode("x", Purpose));
            }
            finally {
                Uuid.Key = original;
            }
        }
    }
}
