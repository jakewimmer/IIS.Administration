// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.WebServer
{
    using Core;
    using System;
    using System.IO;

    /// <summary>
    /// Serializes applicationHost.config commits across requests. IIS stores configuration in a single
    /// file, so concurrent commits race: the loser observes "the file has changed on disk" and
    /// Microsoft.Web.Administration surfaces it as a FileLoadException
    /// (https://github.com/microsoft/IIS.Administration/issues/324). The gate prevents interleaved
    /// writes and converts the remaining read-modify-write conflicts into HTTP 409 so clients can retry.
    /// </summary>
    static class ConfigCommitGate
    {
        private static readonly object _lock = new object();

        public static void Commit(Action commitChanges)
        {
            if (commitChanges == null) {
                throw new ArgumentNullException(nameof(commitChanges));
            }

            lock (_lock) {
                try {
                    commitChanges();
                }
                catch (FileLoadException e) {
                    throw new ConfigurationConflictException(
                        "IIS configuration was modified by another change. Retry the request.", e);
                }
            }
        }
    }
}
