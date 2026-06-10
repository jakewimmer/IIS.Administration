// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.IIS.Administration.Core.Security;
    using Microsoft.IIS.Administration.Security;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Xunit;

    public class BearerTokenValidatorTests
    {
        private const string KnownToken = "known-token";

        [Fact]
        public async Task UnknownToken_IsInvalid()
        {
            var validator = new BearerTokenValidator(new FakeApiKeyProvider(null));

            TokenValidationResult result = await validator.ValidateTokenAsync("unknown", new TokenValidationParameters());

            Assert.False(result.IsValid);
            Assert.NotNull(result.Exception);
        }

        [Fact]
        public async Task ProviderFailure_IsTreatedAsInvalidToken()
        {
            var validator = new BearerTokenValidator(new FakeApiKeyProvider(null) { ThrowOnFind = true });

            TokenValidationResult result = await validator.ValidateTokenAsync(KnownToken, new TokenValidationParameters());

            Assert.False(result.IsValid);
        }

        [Fact]
        public async Task ValidToken_ProducesAuthenticatedIdentityWithAccessTokenClaim()
        {
            var validator = new BearerTokenValidator(new FakeApiKeyProvider(CreateKey(expiresOn: DateTime.UtcNow.AddHours(1))));

            TokenValidationResult result = await validator.ValidateTokenAsync(KnownToken, new TokenValidationParameters());

            Assert.True(result.IsValid);
            Assert.True(result.ClaimsIdentity.IsAuthenticated);
            Assert.Contains(result.ClaimsIdentity.Claims, c => c.Type == ClaimTypes.AccessToken && c.Value == KnownToken);
        }

        [Fact]
        public async Task NeverExpiringKey_ValidTo_IsUtcAndConvertibleToDateTimeOffset()
        {
            //
            // Regression test for https://github.com/microsoft/IIS.Administration/issues/329 and /331:
            // a "never expires" key reported DateTime.MaxValue with an Unspecified kind, which overflows
            // DateTimeOffset during JWT processing on machines with a non-zero UTC offset.
            var validator = new BearerTokenValidator(new FakeApiKeyProvider(CreateKey(expiresOn: null)));

            TokenValidationResult result = await validator.ValidateTokenAsync(KnownToken, new TokenValidationParameters());

            Assert.True(result.IsValid);
            Assert.Equal(DateTimeKind.Utc, result.SecurityToken.ValidTo.Kind);
            Assert.Equal(DateTimeKind.Utc, result.SecurityToken.ValidFrom.Kind);

            // Throws ArgumentOutOfRangeException before the fix when the local offset is negative
            var expires = new DateTimeOffset(result.SecurityToken.ValidTo);
            Assert.Equal(TimeSpan.Zero, expires.Offset);
        }

        [Fact]
        public async Task ExpiringKey_ValidTo_RoundTripsAsUtc()
        {
            DateTime expiresOn = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var validator = new BearerTokenValidator(new FakeApiKeyProvider(CreateKey(expiresOn)));

            TokenValidationResult result = await validator.ValidateTokenAsync(KnownToken, new TokenValidationParameters());

            Assert.Equal(expiresOn, result.SecurityToken.ValidTo);
            Assert.Equal(DateTimeKind.Utc, result.SecurityToken.ValidTo.Kind);
        }

        private static ApiKey CreateKey(DateTime? expiresOn)
        {
            return new ApiKey("hash", "SWT") {
                Id = "key-id",
                CreatedOn = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                ExpiresOn = expiresOn
            };
        }

        private sealed class FakeApiKeyProvider : IApiKeyProvider
        {
            private readonly ApiKey _key;

            public FakeApiKeyProvider(ApiKey key)
            {
                _key = key;
            }

            public bool ThrowOnFind { get; set; }

            public ApiKey FindKey(string token)
            {
                if (ThrowOnFind) {
                    throw new InvalidOperationException("storage failure");
                }

                return token == KnownToken ? _key : null;
            }

            public ApiToken GenerateKey(string purpose) => throw new NotImplementedException();

            public Task<string> RenewToken(ApiKey key) => throw new NotImplementedException();

            public Task<IEnumerable<ApiKey>> GetAllKeys() => throw new NotImplementedException();

            public ApiKey GetKey(string id) => throw new NotImplementedException();

            public Task SaveKey(ApiKey key) => throw new NotImplementedException();

            public Task DeleteKey(ApiKey key) => throw new NotImplementedException();
        }
    }
}
