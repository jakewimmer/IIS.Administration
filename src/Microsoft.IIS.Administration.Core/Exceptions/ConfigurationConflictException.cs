// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.Core {
    using System;


    public class ConfigurationConflictException : Exception, IError {

        public ConfigurationConflictException(string message, Exception innerException = null) : base(message, innerException) {
        }

        public dynamic GetApiError() {
            return Http.ErrorHelper.ConflictError(Message);
        }
    }
}
