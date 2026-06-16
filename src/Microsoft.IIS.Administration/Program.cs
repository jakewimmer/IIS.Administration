// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration {
    using AspNetCore.Hosting;
    using Microsoft.AspNetCore.Server.HttpSys;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.EventLog;
    using Serilog;
    using System;
    using System.Diagnostics;

    public class Program {
        public const string EventSourceName = "Microsoft IIS Administration API";

        private const string DefaultUrl = "https://*:55539";

        public static void Main(string[] args) {
            try
            {
                //
                // Build Config
                var configHelper = new ConfigurationHelper(args);
                IConfiguration config = configHelper.Build();

                //
                // Require HTTPS for every configured address
                RequireHttps(config);

                //
                // Initialize runAsAService local variable
                string serviceName = config.GetValue<string>("serviceName")?.Trim();
                bool runAsAService = !string.IsNullOrEmpty(serviceName);

                //
                // Host
                var hostBuilder = new HostBuilder();

                if (runAsAService)
                {
                    //
                    // Sets the Windows service lifetime; must precede UseContentRoot, which it would
                    // otherwise override with the process base directory
                    _ = hostBuilder.UseWindowsService(o => o.ServiceName = serviceName);
                    Log.Information($"Running as service: {serviceName}");
                }

                _ = hostBuilder
                    .UseContentRoot(configHelper.RootPath)
                    .ConfigureLogging((hostingContext, logging) => {
                        _ = logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));

                        //
                        // Console log is not available in running as a Service
                        if (!runAsAService)
                        {
                            _ = logging.AddConsole();
                        }

                        _ = logging.AddDebug();
                        _ = logging.AddEventLog(new EventLogSettings()
                        {
                            SourceName = EventSourceName
                        });
                    })
                    .ConfigureWebHost(webHost => {
                        _ = webHost
                            .UseUrls(DefaultUrl) // Config can override it. Use "urls":"https://*:55539"
                            .UseConfiguration(config)
                            .ConfigureServices(s => s.AddSingleton(config)) // Configuration Service
                            .UseStartup<Startup>()
                            .UseHttpSys(o => {
                                //
                                // Kernel mode Windows Authentication
                                o.Authentication.Schemes = AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM;

                                //
                                // Need anonymous to allow CORS preflight requests
                                // app.UseWindowsAuthentication ensures (if needed) the request is authenticated to proceed
                                o.Authentication.AllowAnonymous = true;
                            });
                    });

                using (IHost host = hostBuilder.Build())
                {
                    host.Run();
                }
            }
            catch (Exception ex)
            {
                using (var shutdownLog = new EventLog("Application"))
                {
                    shutdownLog.Source = Program.EventSourceName;
                    shutdownLog.WriteEntry($"Microsoft IIS Administration API has shutdown unexpectively because the error: {ex.ToString()}", EventLogEntryType.Error);
                }
                throw;
            }
        }

        //
        // Config-time HTTPS guard: validates the declared "urls" before the host is built.
        // This is a best-effort early check; the authoritative guard is the post-bind
        // validation of the actual server addresses in Startup.Configure, which also catches
        // address sources this string check cannot see (e.g. ASPNETCORE_URLS).
        private static void RequireHttps(IConfiguration config) {
            string urls = config.GetValue<string>("urls") ?? DefaultUrl;

            foreach (var address in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                if (!address.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
                    throw new ArgumentException($"{address} - HTTPS is required");
                }
            }
        }
    }
}
