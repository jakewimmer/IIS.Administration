// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.Tests
{
    using Core.Http;
    using System.Net;
    using System.Net.Http;

    public class ApiHttpClient : HttpClient
    {
        private string _keyId;
        private string _serverUri;

        public static HttpClient Create()
        {
            return Create(Configuration.Instance().TEST_SERVER_URL);
        }

        public static HttpClient Create(string serverUri)
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => {
                return true;
            };

            // Use explicit Windows credentials for an API-owner account when configured; otherwise
            // fall back to the current user's default credentials. Default-credential SSO yields an
            // anonymous NTLM logon on GitHub-hosted runners, which the API rejects (no XSRF token).
            var config = Configuration.Instance();
            if (!string.IsNullOrEmpty(config.TEST_USERNAME)) {
                handler.Credentials = new NetworkCredential(config.TEST_USERNAME, config.TEST_PASSWORD);
            }
            else {
                handler.UseDefaultCredentials = true;
            }

            return new ApiHttpClient(serverUri, handler, true);
        }

        private ApiHttpClient(string serverUri, HttpMessageHandler handler, bool disposeHandler) : base(handler, disposeHandler)
        {
            Init(serverUri);
        }

        private void Init(string serverUri)
        {
            var key = Utils.GetApiKey(serverUri, this);

            _keyId = key.Value<string>("id");
            _serverUri = serverUri;

            this.DefaultRequestHeaders.Add(HeaderNames.Access_Token, "Bearer " + key.Value<string>("access_token"));
            this.DefaultRequestHeaders.Add("Accept", "application/hal+json");
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                this.DefaultRequestHeaders.Clear();
                Utils.DeleteApiKey(_serverUri, _keyId, this);
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }
}
