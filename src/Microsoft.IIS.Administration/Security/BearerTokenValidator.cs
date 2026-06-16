// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.Security {
    using System;
    using System.Security.Claims;
    using System.Threading.Tasks;
    using Core.Http;
    using Core.Security;
    using AspNetCore.Authentication.JwtBearer;
    using IdentityModel.Tokens;



    public class BearerTokenValidator : TokenHandler {
        private IApiKeyProvider _keyProvider;


        public BearerTokenValidator(IApiKeyProvider keyProvider) {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        }

        public override async Task<TokenValidationResult> ValidateTokenAsync(string token,
                                                                             TokenValidationParameters validationParameters) {
            ApiKey key = null;

            // Look up api-key
            try {
                key = await _keyProvider.FindKeyAsync(token);
            }
            catch (Exception e) {
                //
                // Failure to obtain the key is treated as an invalid/missing key, but trace the
                // underlying fault so a transient storage error (locked/corrupted api-keys file,
                // permission problem) is diagnosable instead of a silent blanket 401.
                System.Diagnostics.Trace.TraceWarning(
                    $"BearerTokenValidator: failed to resolve api-key ({e.GetType().Name}): {e.Message}");
            }

            //
            // The api-key is not found, so the validation's failed.
            if (key == null) {
                return new TokenValidationResult() {
                    IsValid = false,
                    Exception = new SecurityTokenException("Invalid access token")
                };
            }

            //
            // Success!
            return new TokenValidationResult() {
                IsValid = true,
                SecurityToken = new SecurityToken(key),
                ClaimsIdentity = new ClaimsIdentity(
                    new Claim[] { new Claim(Core.Security.ClaimTypes.AccessToken, token) },
                    JwtBearerDefaults.AuthenticationScheme)
            };
        }


        public void OnReceivingToken(MessageReceivedContext ctx) {
            string token = null;
            //
            // Try get from request header
            string accessToken = ctx.Request.Headers[HeaderNames.Access_Token];

            //
            // Ensure the token is provided
            if (string.IsNullOrEmpty(accessToken)) {
                return;
            }

            //
            // Parse the access token
            // Access-Token: Bearer <token>
            //
            if (accessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
                token = accessToken.Substring("Bearer ".Length).Trim();
            }

            if (!string.IsNullOrEmpty(token)) {
                // ValidateTokenAsync will determine later if the provided token can be used
                ctx.Token = token;
            }
        }

        public void OnValidatedToken(TokenValidatedContext ctx) {
        }
    }
}
