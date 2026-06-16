// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core.Http;
    using Newtonsoft.Json.Linq;
    using Xunit;

    public class ErrorHelperTests
    {
        // ErrorHelper returns anonymous / ExpandoObject instances; JObject.FromObject lets us
        // inspect their public shape without taking a compile-time dependency on the internal types.
        private static JObject AsJson(object error) => JObject.FromObject(error);

        [Fact]
        public void Error_BuildsServerError()
        {
            JObject j = AsJson(ErrorHelper.Error("boom", "thing"));

            Assert.Equal("Server error", (string)j["title"]);
            Assert.Equal("boom", (string)j["detail"]);
            Assert.Equal("thing", (string)j["name"]);
            Assert.Equal(500, (int)j["status"]);
        }

        [Fact]
        public void Error_NullMessage_DefaultsToEmptyDetail()
        {
            JObject j = AsJson(ErrorHelper.Error(null, null));

            Assert.Equal(string.Empty, (string)j["detail"]);
            Assert.Equal(string.Empty, (string)j["name"]);
        }

        [Fact]
        public void ArgumentError_ForModel_IsUnsupportedMediaType()
        {
            JObject j = AsJson(ErrorHelper.ArgumentError("model"));

            Assert.Equal("Invalid JSON request object", (string)j["title"]);
            Assert.Equal(415, (int)j["status"]);
            Assert.Null(j["name"]);
        }

        [Fact]
        public void ArgumentError_WithoutMessage_OmitsDetail()
        {
            JObject j = AsJson(ErrorHelper.ArgumentError("foo"));

            Assert.Equal("Invalid parameter", (string)j["title"]);
            Assert.Equal("foo", (string)j["name"]);
            Assert.Equal(400, (int)j["status"]);
            Assert.Null(j["detail"]);
        }

        [Fact]
        public void ArgumentError_WithMessage_IncludesDetail()
        {
            JObject j = AsJson(ErrorHelper.ArgumentError("foo", "bad value"));

            Assert.Equal("bad value", (string)j["detail"]);
            Assert.Equal(400, (int)j["status"]);
        }

        [Fact]
        public void ArgumentOutOfRangeError_IncludesBounds()
        {
            JObject j = AsJson(ErrorHelper.ArgumentOutOfRangeError("p", 0, 10, "msg"));

            Assert.Equal("Out of range", (string)j["title"]);
            Assert.Equal(0, (long)j["min_value"]);
            Assert.Equal(10, (long)j["max_value"]);
            Assert.Equal(400, (int)j["status"]);
        }

        [Fact]
        public void NotFoundError_WithoutName_OmitsName()
        {
            JObject j = AsJson(ErrorHelper.NotFoundError(""));

            Assert.Equal("Not found", (string)j["title"]);
            Assert.Equal(404, (int)j["status"]);
            Assert.Null(j["name"]);
        }

        [Fact]
        public void NotFoundError_WithName_IncludesName()
        {
            JObject j = AsJson(ErrorHelper.NotFoundError("p"));

            Assert.Equal("p", (string)j["name"]);
            Assert.Equal(404, (int)j["status"]);
        }

        [Fact]
        public void AlreadyExistsError_IsConflict()
        {
            JObject j = AsJson(ErrorHelper.AlreadyExistsError("p"));

            Assert.Equal("Conflict", (string)j["title"]);
            Assert.Equal("Already exists", (string)j["detail"]);
            Assert.Equal(409, (int)j["status"]);
        }

        [Fact]
        public void ConflictError_NullMessage_DefaultsToEmptyDetail()
        {
            JObject j = AsJson(ErrorHelper.ConflictError(null));

            Assert.Equal(string.Empty, (string)j["detail"]);
            Assert.Equal(409, (int)j["status"]);
        }

        [Fact]
        public void LockedError_IsForbidden()
        {
            JObject j = AsJson(ErrorHelper.LockedError("n"));

            Assert.Equal("Object is locked", (string)j["title"]);
            Assert.Equal(403, (int)j["status"]);
        }

        [Fact]
        public void UnauthorizedArgumentError_IncludesValueUnderParamName()
        {
            JObject j = AsJson(ErrorHelper.UnauthorizedArgumentError("password", "nope", "secret"));

            Assert.Equal("Unauthorized", (string)j["title"]);
            Assert.Equal("password", (string)j["name"]);
            Assert.Equal("nope", (string)j["detail"]);
            Assert.Equal("secret", (string)j["password"]);
            Assert.Equal(401, (int)j["status"]);
        }

        [Fact]
        public void ForbiddenArgumentError_OmitsDetailWhenMessageEmpty()
        {
            JObject j = AsJson(ErrorHelper.ForbiddenArgumentError("p", "", null));

            Assert.Equal("Forbidden", (string)j["title"]);
            Assert.Equal(403, (int)j["status"]);
            Assert.Null(j["detail"]);
        }

        [Fact]
        public void NotAllowedError_IsMethodNotAllowed()
        {
            JObject j = AsJson(ErrorHelper.NotAllowedError("nope"));

            Assert.Equal("Not Allowed", (string)j["title"]);
            Assert.Equal("nope", (string)j["message"]);
            Assert.Equal(405, (int)j["status"]);
        }
    }
}
