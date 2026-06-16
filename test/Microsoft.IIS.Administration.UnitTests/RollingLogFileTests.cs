// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Logging;
    using Serilog;
    using System.IO;
    using Xunit;

    public class RollingLogFileTests
    {
        [Fact]
        public void DateToken_IsTranslatedToDailyRollingInterval()
        {
            var (path, interval) = RollingLogFile.Resolve("logs", "log-{Date}.txt");

            Assert.Equal(Path.Combine("logs", "log-.txt"), path);
            Assert.Equal(RollingInterval.Day, interval);
        }

        [Fact]
        public void DateToken_MatchesCaseInsensitively()
        {
            var (path, interval) = RollingLogFile.Resolve("logs", "audit-{date}.txt");

            Assert.Equal(Path.Combine("logs", "audit-.txt"), path);
            Assert.Equal(RollingInterval.Day, interval);
        }

        [Fact]
        public void PlainFileName_RollsDailySoRetentionApplies()
        {
            //
            // A name without {Date} still rolls daily; otherwise Serilog.Sinks.File ignores
            // retainedFileCountLimit (max_files) and the single file grows unbounded.
            var (path, interval) = RollingLogFile.Resolve("logs", "log.txt");

            Assert.Equal(Path.Combine("logs", "log.txt"), path);
            Assert.Equal(RollingInterval.Day, interval);
        }
    }
}
