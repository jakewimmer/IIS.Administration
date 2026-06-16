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
        public void UnrelatedFileLoadException_PropagatesUnchanged()
        {
            //
            // FileLoadException is also raised for assembly-load failures. Only the
            // applicationHost.config "changed on disk" case maps to a retryable 409; anything
            // else must surface as the original error instead of a misleading conflict.
            var inner = new FileLoadException("Could not load file or assembly 'Foo'.");

            var ex = Assert.Throws<FileLoadException>(() => ConfigCommitGate.Commit(() => throw inner));

            Assert.Same(inner, ex);
        }

        [Fact]
        public async Task ConcurrentCommits_AreSerialized()
        {
            const int Workers = 16;
            const int IterationsPerWorker = 200;

            int inside = 0;
            int violations = 0;

            //
            // Force every worker to start contending at the same instant so the critical section is
            // actually exercised under parallelism, then assert the invariant directly on entry: the
            // count must transition 0 -> 1. Any other value means a second thread is concurrently
            // inside, which proves serialization is broken regardless of scheduler timing.
            using var start = new Barrier(Workers);

            Task[] commits = Enumerable.Range(0, Workers).Select(_ => Task.Run(() => {
                start.SignalAndWait();
                for (int i = 0; i < IterationsPerWorker; i++) {
                    ConfigCommitGate.Commit(() => {
                        if (Interlocked.Increment(ref inside) != 1) {
                            Interlocked.Increment(ref violations);
                        }

                        // Widen the window during which an unserialized commit would overlap.
                        Thread.SpinWait(2000);

                        Interlocked.Decrement(ref inside);
                    });
                }
            })).ToArray();

            await Task.WhenAll(commits);

            Assert.Equal(0, violations);
            Assert.Equal(0, inside);
        }
    }
}
