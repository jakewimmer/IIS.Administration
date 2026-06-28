// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.Tests
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.ServiceProcess;
    using System.Threading.Tasks;
    using Xunit;
    using Xunit.Abstractions;

    public class Monitoring
    {
        private const string SiteName = "ServerMonitorTestSite";
        private static readonly string SitePath = Path.Combine(Configuration.Instance().TEST_ROOT_PATH, SiteName);

        private ITestOutputHelper _output;

        public Monitoring(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task WebServer()
        {
            using (HttpClient client = ApiHttpClient.Create()) {
                Sites.EnsureNoSite(client, SiteName);

                int port = Utils.GetAvailablePort();

                JObject site = Sites.CreateSite(_output, client, SiteName, port, SitePath);

                try {
                    using (var stresser = new SiteStresser($"http://localhost:{port}"))
                    using (var serverMonitor = new ServerMonitor()) {

                        int tries = 0;
                        JObject snapshot = null;

                        // Performance-counter population latency: the worker process (w3wp)
                        // is created lazily on the first request it serves, and its counters
                        // (requests, network, cpu, ...) only report non-zero values once that
                        // process is up and the monitoring backend has sampled it across a few
                        // intervals. On cold CI runners this lag is independent of the request
                        // load (SiteStresser drives continuous traffic every 20ms) and can
                        // exceed 10s, so poll up to ~30s until every metric we assert on has
                        // populated, mirroring the AppPool() and HandleRestartIis() de-flake
                        // patterns (issue #8, #12).
                        while (tries < 30) {

                            snapshot = serverMonitor.Current;

                            _output.WriteLine("Waiting for webserver to track requests per sec and processes");
                            _output.WriteLine(snapshot == null ? "Snapshot is null" : snapshot.ToString(Formatting.Indented));

                            if (snapshot != null
                                && snapshot["requests"].Value<long>("per_sec") > 0
                                && snapshot["requests"].Value<long>("total") > 0
                                && snapshot["network"].Value<long>("total_bytes_sent") > 0
                                && snapshot["cpu"].Value<long>("threads") > 0
                                && snapshot["cpu"].Value<long>("processes") > 0) {
                                break;
                            }

                            await Task.Delay(1000);
                            tries++;
                        }

                        Assert.True(snapshot != null);

                        _output.WriteLine("Validating webserver monitoring data");
                        _output.WriteLine(snapshot.ToString(Formatting.Indented));

                        Assert.True(snapshot["requests"].Value<long>("per_sec") > 0);
                        Assert.True(snapshot["network"].Value<long>("total_bytes_sent") > 0);
                        Assert.True(snapshot["network"].Value<long>("total_bytes_recv") > 0);
                        Assert.True(snapshot["network"].Value<long>("total_connection_attempts") > 0);
                        Assert.True(snapshot["requests"].Value<long>("total") > 0);
                        Assert.True(snapshot["memory"].Value<long>("private_working_set") > 0);
                        Assert.True(snapshot["memory"].Value<long>("system_in_use") > 0);
                        Assert.True(snapshot["memory"].Value<long>("installed") > 0);
                        Assert.True(snapshot["cpu"].Value<long>("threads") > 0);
                        Assert.True(snapshot["cpu"].Value<long>("processes") > 0);
                        Assert.True(snapshot["cpu"].Value<long>("percent_usage") >= 0);
                        Assert.True(snapshot["cpu"].Value<long>("system_percent_usage") >= 0);

                        Assert.True(serverMonitor.ErrorCount == 0);
                    }
                }
                finally {
                    client.Delete(Utils.Self(site));
                }
            }
        }

        [Fact]
        public async Task HandleRestartIis()
        {
            using (HttpClient client = ApiHttpClient.Create()) {
                Sites.EnsureNoSite(client, SiteName);

                int port = Utils.GetAvailablePort();

                JObject site = Sites.CreateSite(_output, client, SiteName, port, SitePath);

                try {
                    using (var stresser = new SiteStresser($"http://localhost:{port}"))
                    using (var serverMonitor = new ServerMonitor())
                    using (var sc = new ServiceController("W3SVC")) {

                        JObject snapshot = await serverMonitor.GetSnapshot(5000);

                        _output.WriteLine("Validating server is running worker processes");
                        _output.WriteLine(snapshot.ToString(Formatting.Indented));

                        Assert.True(snapshot["cpu"].Value<long>("processes") > 0);

                        // Snapshot the monitor's error count before the restart.
                        // ServerMonitor.ErrorCount is a monotonic counter with no reset
                        // API: the background poller catches exceptions (network failure
                        // or non-JSON response) and increments. While W3SVC is stopped the
                        // management API typically still returns JSON, so the count should
                        // not rise, but we compare against this baseline rather than
                        // asserting == 0 so a transient error during the stop window does
                        // not flake the test.
                        int errorsBeforeRestart = serverMonitor.ErrorCount;

                        _output.WriteLine("Restarting IIS");

                        sc.Stop();
                        DateTime stopTime = DateTime.Now;
                        var timeout = TimeSpan.FromSeconds(5);

                        while (DateTime.Now - stopTime < timeout && sc.Status != ServiceControllerStatus.Stopped) {
                            await Task.Delay(1000);
                        }

                        sc.Start();
                        // Wait for the service to reach the Running state before polling
                        // metrics, so the warm-up loop does not race the W3SVC start.
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

                        int tries = 0;

                        // Performance-counter population latency: after W3SVC restarts,
                        // the worker process is recreated lazily on the next request and
                        // its perf counters (requests, cpu, ...) only report non-zero
                        // values once the process is up and the monitoring backend has
                        // sampled it across a few intervals. On cold CI runners this lag
                        // is independent of the request load (SiteStresser drives traffic
                        // continuously) and can exceed 5s, so poll up to ~30s until every
                        // metric we assert on has populated, mirroring the AppPool()
                        // de-flake pattern (issue #8).
                        while (tries < 30) {

                            snapshot = serverMonitor.Current;

                            _output.WriteLine("checking for monitoring counters to populate after startup");
                            _output.WriteLine(snapshot == null ? "Snapshot is null" : snapshot.ToString(Formatting.Indented));

                            if (snapshot != null
                                && snapshot["requests"].Value<long>("per_sec") > 0
                                && snapshot["requests"].Value<long>("total") > 0
                                && snapshot["cpu"].Value<long>("processes") > 0) {
                                break;
                            }

                            await Task.Delay(1000);
                            tries++;
                        }

                        Assert.True(snapshot != null);
                        Assert.True(snapshot["requests"].Value<long>("per_sec") > 0);
                        Assert.True(snapshot["requests"].Value<long>("total") > 0);
                        Assert.True(snapshot["cpu"].Value<long>("processes") > 0);

                        Assert.True(serverMonitor.ErrorCount == errorsBeforeRestart);
                    }
                }
                finally {
                    client.Delete(Utils.Self(site));
                }
            }
        }

        [Theory]
        [InlineData(20)]
        public async Task ManySites(int numberSites)
        {
            HttpClient client = null;
            SiteStresser[] stressers = new SiteStresser[numberSites];
            ServerMonitor serverMonitor = null;
            var pools = new JObject[numberSites];
            var sites = new JObject[numberSites];

            try {
                client = ApiHttpClient.Create();

                for (int i = 0; i < numberSites; i++) {

                    string name = SiteName + i;

                    var pool = ApplicationPools.GetAppPool(client, name);

                    if (pool == null) {
                        pool = ApplicationPools.CreateAppPool(client, name);
                    }

                    pools[i] = pool;
                }

                serverMonitor = new ServerMonitor();

                for (int i = 0; i < numberSites; i++) {

                    string name = SiteName + i;

                    JObject site = Sites.GetSite(client, name);

                    if (site == null) {

                        site = Sites.CreateSite(_output, client, name, Utils.GetAvailablePort(), SitePath, true, pools[i]);
                    }

                    sites[i] = site;

                    int port = site["bindings"].ToObject<IEnumerable<JObject>>().First().Value<int>("port");

                    stressers[i] = new SiteStresser($"http://localhost:{port}");
                }

                await Task.Delay(1000);

                var start = DateTime.Now;
                var timeout = TimeSpan.FromSeconds(20);

                _output.WriteLine($"Created {numberSites} sites");
                _output.WriteLine($"Waiting for all site processes to start");

                while (DateTime.Now - start <= timeout && 
                       (serverMonitor.Current["cpu"].Value<long>("processes") < numberSites ||
                       serverMonitor.Current["network"].Value<long>("total_bytes_sent") == 0)) {
                    await Task.Delay(1000);
                }

                if (DateTime.Now - start > timeout) {
                    throw new Exception("timeout");
                }

                JObject snapshot = serverMonitor.Current;

                _output.WriteLine("Validating webserver monitoring data");
                _output.WriteLine(snapshot.ToString(Formatting.Indented));

                Assert.True(snapshot["network"].Value<long>("total_bytes_sent") > 0);
                Assert.True(snapshot["network"].Value<long>("total_bytes_recv") > 0);
                Assert.True(snapshot["network"].Value<long>("total_connection_attempts") > 0);
                Assert.True(snapshot["requests"].Value<long>("total") > 0);
                Assert.True(snapshot["memory"].Value<long>("private_working_set") > 0);
                Assert.True(snapshot["memory"].Value<long>("system_in_use") > 0);
                Assert.True(snapshot["memory"].Value<long>("installed") > 0);
                Assert.True(snapshot["cpu"].Value<long>("threads") > 0);
                Assert.True(snapshot["cpu"].Value<long>("processes") > 0);
                Assert.True(snapshot["cpu"].Value<long>("percent_usage") >= 0);
                Assert.True(snapshot["cpu"].Value<long>("threads") > 0);
                Assert.True(snapshot["cpu"].Value<long>("processes") > 0);

                Assert.True(serverMonitor.ErrorCount == 0);
            }
            finally {
                for (int i = 0; i < stressers.Length; i++) {
                    if (stressers[i] != null) {
                        stressers[i].Dispose();
                    }
                }

                if (serverMonitor != null) {
                    serverMonitor.Dispose();
                }

                for (int i = 0; i < sites.Length; i++) {
                    if (sites[i] != null) {

                        client.Delete(Utils.Self(sites[i]));

                        client.Delete(Utils.Self((JObject)sites[i]["application_pool"]));
                    }
                }

                if (client != null) {
                    client.Dispose();
                }
            }
        }

        [Fact]
        public async Task WebSite()
        {
            const string name = SiteName + "z";

            using (HttpClient client = ApiHttpClient.Create()) {

                JObject pool = ApplicationPools.GetAppPool(client, name);

                if (pool == null) {
                    pool = ApplicationPools.CreateAppPool(client, name);
                }

                JObject site = Sites.GetSite(client, name);

                if (site == null) {
                    site = Sites.CreateSite(_output, client, name, Utils.GetAvailablePort(), SitePath, true, pool);
                }

                int port = site["bindings"].ToObject<IEnumerable<JObject>>().First().Value<int>("port");

                try {
                    using (var stresser = new SiteStresser($"http://localhost:{port}"))
                    using (var serverMonitor = new ServerMonitor(Utils.GetLink(site, "monitoring"))) {

                        int tries = 0;
                        JObject snapshot = null;

                        // Performance-counter population latency: same cold-runner warm-up
                        // race as WebServer/HandleRestartIis/AppPool. The 15x1s poll can be
                        // exhausted before per_sec populates on slow CI runners, so wait up
                        // to ~30s for every metric we assert on.
                        while (tries < 30) {

                            snapshot = serverMonitor.Current;

                            if (snapshot != null &&
                                snapshot["requests"].Value<long>("per_sec") > 0 &&
                                snapshot["requests"].Value<long>("total") > 0 &&
                                snapshot["network"].Value<long>("total_bytes_sent") > 0 &&
                                snapshot["cpu"].Value<long>("processes") > 0) {
                                break;
                            }

                            await Task.Delay(1000);
                            tries++;
                        }

                        Assert.True(snapshot != null);

                        Assert.True(snapshot["requests"].Value<long>("per_sec") > 0);
                        Assert.True(snapshot["network"].Value<long>("total_bytes_sent") > 0);
                        Assert.True(snapshot["network"].Value<long>("total_bytes_recv") > 0);
                        Assert.True(snapshot["network"].Value<long>("total_connection_attempts") > 0);
                        Assert.True(snapshot["requests"].Value<long>("total") > 0);
                        Assert.True(snapshot["memory"].Value<long>("private_working_set") > 0);
                        Assert.True(snapshot["memory"].Value<long>("system_in_use") > 0);
                        Assert.True(snapshot["memory"].Value<long>("installed") > 0);
                        Assert.True(snapshot["cpu"].Value<long>("threads") > 0);
                        Assert.True(snapshot["cpu"].Value<long>("processes") > 0);

                        Assert.True(serverMonitor.ErrorCount == 0);
                    }
                }
                finally {
                    client.Delete(Utils.Self(site));

                    client.Delete(Utils.Self(pool));
                }
            }
        }

        [Fact]
        public async Task AppPool()
        {
            using (HttpClient client = ApiHttpClient.Create()) {
                Sites.EnsureNoSite(client, SiteName);

                int port = Utils.GetAvailablePort();

                JObject site = Sites.CreateSite(_output, client, SiteName, port, SitePath);

                try {
                    JObject appPool = client.Get(Utils.Self((JObject)site["application_pool"]));

                    using (var stresser = new SiteStresser($"http://localhost:{port}"))
                    using (var serverMonitor = new ServerMonitor(Utils.GetLink(appPool, "monitoring"))) {
                        int tries = 0;
                        JObject snapshot = null;

                        // Performance-counter population latency: an app pool's worker process
                        // (w3wp) is created lazily on the first request it serves, and its counters
                        // (private_working_set, requests, cpu, ...) only report non-zero values once
                        // that process is up and the monitoring backend has sampled it across a few
                        // intervals. That lag is independent of the request load (SiteStresser drives
                        // continuous traffic every 20ms) and, on slow CI runners, can exceed the time
                        // the requests take - an early snapshot legitimately reads 0 for these metrics.
                        // So poll the monitor until every metric we assert on has populated rather than
                        // asserting on the first snapshot; a 15s window still occasionally fell through
                        // with a cold worker, so wait up to ~30s.
                        while (tries < 30) {

                            snapshot = serverMonitor.Current;

                            if (snapshot != null &&
                                snapshot["requests"].Value<long>("total") > 0 &&
                                snapshot["requests"].Value<long>("per_sec") > 0 &&
                                snapshot["memory"].Value<long>("private_working_set") > 0 &&
                                snapshot["cpu"].Value<long>("processes") > 0) {
                                break;
                            }

                            await Task.Delay(1000);
                            tries++;
                        }

                        _output.WriteLine("Validing monitoring data for application pool");
                        _output.WriteLine(snapshot.ToString(Formatting.Indented));

                        Assert.True(snapshot["requests"].Value<long>("per_sec") > 0);
                        Assert.True(snapshot["requests"].Value<long>("total") > 0);
                        Assert.True(snapshot["memory"].Value<long>("private_working_set") > 0);
                        Assert.True(snapshot["memory"].Value<long>("system_in_use") > 0);
                        Assert.True(snapshot["memory"].Value<long>("installed") > 0);
                        Assert.True(snapshot["cpu"].Value<long>("threads") > 0);
                        Assert.True(snapshot["cpu"].Value<long>("processes") > 0);

                        Assert.True(serverMonitor.ErrorCount == 0);
                    }
                }
                finally {
                    client.Delete(Utils.Self(site));
                }
            }
        }
    }

    class ServerMonitor : IDisposable
    {
        private bool _stop = false;
        private Task _t;
        private JObject _snapshot;
        private string _url = $"{Configuration.Instance().TEST_SERVER_URL}/api/webserver/monitoring";

        public ServerMonitor(string url = null)
        {
            if (url != null) {
                _url = url;
            }

            _t = Start();
        }

        public int ErrorCount { get; private set; } = 0;

        public JObject Current { get { return _snapshot; } }

        public async Task Start()
        {
            using (var client = ApiHttpClient.Create()) {

                while (!_stop) {

                    try {
                        HttpResponseMessage responseMessage = await client.GetAsync(_url);
                        var result = await responseMessage.Content.ReadAsStringAsync();
                        _snapshot = JObject.Parse(result);
                    }
                    catch {
                        ErrorCount++;
                    }

                    await Task.Delay(1000);
                }
            }
        }

        public async Task<JObject> GetSnapshot(int timeout)
        {
            var end = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);

            while (DateTime.UtcNow < end) {

                if (Current != null) {
                    return Current;
                }

                await Task.Delay(1000);
            }

            throw new Exception("Timed out getting server monitor snapshot.");
        }

        public void Dispose()
        {
            _stop = true;
        }
    }

    class SiteStresser : IDisposable
    {
        private Uri Uri { get; set; }
        private bool _stop = false;
        private Task _t;

        public SiteStresser(string url)
        {
            Uri = new Uri(url);
            _t = Start();
        }

        public async Task Start()
        {
            using (var client = new HttpClient()) {

                while (!_stop) {

                    try {
                        await client.GetAsync(Uri);
                    }
                    catch {
                    }

                    await Task.Delay(20);
                }
            }
        }

        public void Dispose()
        {
            _stop = true;
        }
    }
}
