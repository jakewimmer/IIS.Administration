// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Monitoring;
    using Microsoft.IIS.Administration.WebServer.Monitoring;
    using System.Collections.Generic;
    using Xunit;

    //
    // Regression coverage for upstream IIS.Administration#322: the app-pool monitoring endpoint always
    // reported a CPU percent_usage of 0. The root cause was in AppPoolMonitor's counter aggregation, which
    // is exercised here directly via AppPoolMonitor.AggregateCounters (kept free of any IIS/OS dependency).
    public class AppPoolMonitorTests
    {
        [Fact]
        public void PercentCpu_IsSummedAcrossWorkerProcesses_AndNormalizedByProcessorCount()
        {
            // Two worker processes reporting CPU; the pool's CPU is their sum divided by the processor count.
            var counters = new List<IPerfCounter> {
                Cpu(40),
                Cpu(60),
            };

            var snapshot = new AppPoolSnapshot();
            AppPoolMonitor.AggregateCounters(snapshot, counters, processorCount: 2);

            Assert.Equal((40 + 60) / 2, snapshot.PercentCpuTime);
        }

        [Fact]
        public void PercentCpu_IsNonZero_ForSingleProcessor()
        {
            // The original bug accumulated into the wrong field, so CPU came back as 0 regardless of load.
            var snapshot = new AppPoolSnapshot();
            AppPoolMonitor.AggregateCounters(snapshot, new List<IPerfCounter> { Cpu(75) }, processorCount: 1);

            Assert.Equal(75, snapshot.PercentCpuTime);
        }

        [Fact]
        public void PercentCpu_IsNotClobbered_ByLaterCounters()
        {
            // The original bug also assigned CPU inside the counter loop, so any counter processed after the
            // CPU counters overwrote the value. Trailing non-CPU counters must not reset the accumulated CPU.
            var counters = new List<IPerfCounter> {
                Cpu(100),
                new FakePerfCounter(WorkerProcessCounterNames.Category, WorkerProcessCounterNames.ActiveRequests, 5),
                new FakePerfCounter(MemoryCounterNames.Category, MemoryCounterNames.AvailableBytes, 1024),
            };

            var snapshot = new AppPoolSnapshot();
            AppPoolMonitor.AggregateCounters(snapshot, counters, processorCount: 1);

            Assert.Equal(100, snapshot.PercentCpuTime);
            Assert.Equal(5, snapshot.ActiveRequests);
            Assert.Equal(1024, snapshot.AvailableMemory);
        }

        private static FakePerfCounter Cpu(long value)
        {
            return new FakePerfCounter(ProcessCounterNames.Category, ProcessCounterNames.PercentCpu, value);
        }

        private sealed class FakePerfCounter : IPerfCounter
        {
            public FakePerfCounter(string categoryName, string name, long value)
            {
                CategoryName = categoryName;
                Name = name;
                Value = value;
            }

            public string Name { get; }
            public string InstanceName => string.Empty;
            public string CategoryName { get; }
            public string Path => string.Empty;
            public long Value { get; set; }
        }
    }
}
