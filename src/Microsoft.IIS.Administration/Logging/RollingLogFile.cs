// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.Logging
{
    using Serilog;
    using System;
    using System.IO;

    /// <summary>
    /// Translates the legacy Serilog.Sinks.RollingFile "{Date}" file name convention, which is still
    /// used in existing appsettings.json files, into a Serilog.Sinks.File path and rolling interval.
    /// </summary>
    static class RollingLogFile
    {
        private const string DateToken = "{Date}";

        public static (string Path, RollingInterval Interval) Resolve(string root, string fileName)
        {
            int tokenIndex = fileName.IndexOf(DateToken, StringComparison.OrdinalIgnoreCase);

            if (tokenIndex < 0) {
                //
                // No {Date} token. Still roll daily so the configured retainedFileCountLimit
                // (max_files) keeps pruning old files; Serilog.Sinks.File ignores retention when
                // the interval is Infinite, which would let a single file grow unbounded.
                return (Path.Combine(root, fileName), RollingInterval.Day);
            }

            //
            // The file sink appends the date at the roll point, producing the same
            // "name-20180101.txt" layout the rolling file sink generated for "name-{Date}.txt"
            string sinkFileName = fileName.Remove(tokenIndex, DateToken.Length);
            return (Path.Combine(root, sinkFileName), RollingInterval.Day);
        }
    }
}
