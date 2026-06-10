// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core;
    using Microsoft.IIS.Administration.WebServer;
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    public class ConfigCommitGateTests
    {
        [Fact]
        public void CommitAction_IsExecuted()
        {
            bool committed = false;

            ConfigCommitGate.Commit(() => committed = true);

            Assert.True(committed);
        }

        [Fact]
        public void FileChangedOnDisk_IsSurfacedAsConfigurationConflict()
        {
            //
            // Microsoft.Web.Administration raises FileLoadException when applicationHost.config changed
            // between read and commit (https://github.com/microsoft/IIS.Administration/issues/324).
            // The gate maps it to the API's 409 Conflict error instead of an unhandled 500.
            var inner = new FileLoadException("Cannot commit configuration changes because the file has changed on disk");

            var ex = Assert.Throws<ConfigurationConflictException>(() => ConfigCommitGate.Commit(() => throw inner));

            Assert.Same(inner, ex.InnerException);

            object apiError = ex.GetApiError();
            int status = (int)apiError.GetType().GetProperty("status").GetValue(apiError);
            Assert.Equal(409, status);
        }

        [Fact]
        public void OtherExceptions_PropagateUnchanged()
        {
            var inner = new InvalidOperationException("unrelated");

            var ex = Assert.Throws<InvalidOperationException>(() => ConfigCommitGate.Commit(() => throw inner));

            Assert.Same(inner, ex);
        }

        [Fact]
        public async Task ConcurrentCommits_AreSerialized()
        {
            int active = 0;
            int maxObservedConcurrency = 0;

            Task[] commits = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
                ConfigCommitGate.Commit(() => {
                    int now = Interlocked.Increment(ref active);
                    InterlockedMax(ref maxObservedConcurrency, now);
                    Thread.Sleep(10);
                    Interlocked.Decrement(ref active);
                }))).ToArray();

            await Task.WhenAll(commits);

            Assert.Equal(1, maxObservedConcurrency);
        }

        private static void InterlockedMax(ref int location, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref location))) {
                if (Interlocked.CompareExchange(ref location, value, current) == current) {
                    break;
                }
            }
        }
    }
}
