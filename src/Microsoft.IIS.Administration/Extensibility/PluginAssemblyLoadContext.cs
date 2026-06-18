// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.Extensibility
{
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Loader;

    // Implemented for https://github.com/dotnet/coreclr/issues/5837
    // Can move back to resolver callback when fixed
    public class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private string _pluginDir;

        public PluginAssemblyLoadContext(string pluginDirectory)
        {
            _pluginDir = pluginDirectory;
        }

        private Assembly LoadFromCurrentDomain(AssemblyName target)
        {
            foreach (var existing in AppDomain.CurrentDomain.GetAssemblies())
            {
                var existingName = existing.GetName();
                if (existingName.Name == target.Name)
                {
                    if (!existingName.CultureInfo.Equals(target.CultureInfo) && !target.CultureInfo.IsNeutralCulture)
                    {
                        throw new ApplicationException($"Conflicting cultures for {target}, app: {existingName.CultureInfo} plugin: {target.CultureInfo}");
                    }
                    if (existingName.Version < target.Version)
                    {
                        throw new ApplicationException($"Version downgrade for {target}, app: {existingName.Version} plugin: {target.Version}");
                    }
                    // Past the downgrade check the host assembly is always >= the version the
                    // plugin asked for, so a differing major version here is an *upgrade* (host
                    // newer), not a conflict. That is exactly the case for the .NET Standard 4.x
                    // facades Microsoft.Web.Administration 11.1.0 references (System.Runtime,
                    // System.Collections, Microsoft.Win32.*, …): on .NET 10 those are
                    // type-forwarding shims unified into the shared framework, so resolving to
                    // the host's in-box assembly is correct and lets the runtime's normal
                    // forwarding satisfy the plugin. Returning it (rather than throwing on the
                    // differing major) is what allows the legacy ms.web.admin.refs facade DLLs
                    // to be dropped entirely.
                    if (existingName.Version != target.Version)
                    {
                        Log.Warning($"Version mismatch for {target}, app: {existingName.Version} plugin: {target.Version}");
                    }
                    return existing;
                }
            }
            return null;
        }

        private Assembly LoadFromPluginDir(AssemblyName assemblyName)
        {
            var rootPaths = new List<string>();
            rootPaths.Add(Path.Combine(_pluginDir, $"{assemblyName.Name}.{assemblyName.Version}"));
            rootPaths.Add(_pluginDir);

            foreach (var path in rootPaths)
            {
                string asmPath = Path.Combine(path, $"{assemblyName.Name}.dll");
                Log.Logger.Debug($"Resolving plugin dependency {assemblyName} using location {asmPath}");
                if (File.Exists(asmPath))
                {
                    // If LoadFromAssemblyPath's argument does not point to a valid assembly a fatal error will occur that will not throw an exception
                    // The process will terminate ungracefully
                    return LoadFromAssemblyPath(asmPath);
                }
            }
            return null;
        }

        private Assembly LoadFromRuntimeDir(AssemblyName assemblyName)
        {
            string winRuntime = null;
            string runtimes = Path.Combine(_pluginDir, "runtimes");

            if (Directory.Exists(runtimes))
            {
                winRuntime = Directory.GetDirectories(runtimes).FirstOrDefault(d => d.ToLower().Contains("win"));
            }

            if (winRuntime != null)
            {
                foreach (var file in Directory.GetFiles(winRuntime, "*.dll", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file) == assemblyName.Name + ".dll")
                    {
                        return LoadFromAssemblyPath(file);
                    }
                }
            }
            return null;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            Assembly asm;
            try
            {
                asm = LoadFromCurrentDomain(assemblyName);               
            }
            catch (Exception)
            {
                // LoadFromCurrentDomain throws an exception if the assembly does not exist.
                // Ignore it and try again below
                asm = null;
            }

            return asm ?? LoadFromPluginDir(assemblyName) ?? LoadFromCurrentDomain(assemblyName);
        }
    }
}
