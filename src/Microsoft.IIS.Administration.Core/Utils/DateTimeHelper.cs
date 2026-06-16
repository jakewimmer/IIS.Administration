// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.Core.Utils
{
    using System;

    public static class DateTimeHelper
    {
        //
        // Deserialized timestamps (IConfiguration, JSON) can carry DateTimeKind.Unspecified; force UTC
        // so expiry comparisons against DateTime.UtcNow are correct on hosts with a non-zero UTC offset
        // (see https://github.com/microsoft/IIS.Administration/issues/329 and /331). An Unspecified value
        // is treated as already-UTC (SpecifyKind preserves ticks); any other kind is converted with
        // ToUniversalTime. DateTime.MinValue stays MinValue because ticks are preserved.
        public static DateTime AsUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
        }

        public static DateTime? AsUtc(DateTime? value)
        {
            return value.HasValue ? AsUtc(value.Value) : (DateTime?)null;
        }
    }
}
