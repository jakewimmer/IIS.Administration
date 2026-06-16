# Fast-Forward Plan: IIS.Administration Fork Modernization (.NET 10 LTS)

**Last updated:** 2026-06-10
**Fork:** jakewimmer/IIS.Administration
**Upstream:** microsoft/IIS.Administration (unmaintained; fork was in sync with upstream tip `60ca64a`, Feb 2024)

This plan targets **.NET 10 (LTS, supported to Nov 2028)** directly. All dependencies are bumped to
their latest stable versions subject to a **7-day publish cooldown** (cutoff 2026-06-03): a release
younger than seven days is skipped in favor of the previous one. That is why the ASP.NET Core
packages are at 10.0.8 (2026-05-12) rather than 10.0.9 (published 2026-06-09).

---

## 1. Dependency matrix (old → new)

| Dependency | Old | New | Notes |
|---|---|---|---|
| Target framework | net6.0 (EOL Nov 2024) | **net10.0** | `build/version.props` |
| Product version | 6.0.0 | 7.0.0 | App + installer (`installer/shared/common.wxi`) |
| Microsoft.AspNetCore.Authentication.JwtBearer | 6.0.1 | 10.0.8 | 10.0.9 excluded by cooldown |
| Microsoft.AspNetCore.Hosting.WindowsServices | 6.0.1 | **replaced by Microsoft.Extensions.Hosting.WindowsServices 10.0.8** | Generic-host service lifetime; the legacy IWebHost RunAsService package is no longer used |
| Microsoft.AspNetCore.Mvc.NewtonsoftJson | 6.0.1 | 10.0.8 | Newtonsoft.Json resolves to 13.x |
| Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation | 6.0.3 | 10.0.8 | |
| Serilog | 2.11.0 | 4.3.1 | |
| Serilog.Extensions.Logging | 3.1.0 | 10.0.0 | |
| Serilog.Sinks.RollingFile (deprecated) | 3.3.0 | **Serilog.Sinks.File 7.0.0** | `{Date}` file names translated by `RollingLogFile` |
| Microsoft.Web.Administration | 11.1.0 | 11.1.0 | Latest available; its vulnerable 4.x transitives are pruned from the graph by the .NET 10 SDK (no pins needed) |
| jQuery (vendored) | 3.2.0 (CVE-2019-11358, CVE-2020-11022/11023) | 3.7.1 | SRI hashes regenerated; verified against the official 3.7.1 sha384 |
| xunit / runner / Test SDK | 2.4.1 / 2.4.3 / 17.0.0 | 2.9.3 / 3.1.5 / 18.6.0 | |
| dotnet runtime bundled by installer | 6.0.1 | 10.0.8 | URLs updated; RemotePayload regen pending (Phase 7) |

`dotnet list package --vulnerable --include-transitive` reports **zero** vulnerable packages across
all SDK-style projects after these changes (previously: System.Text.Json 6.0.0 High,
System.Net.Http 4.1.0 High, System.Security.Cryptography.X509Certificates 4.1.0 High,
IdentityModel 6.10.0 Moderate).

---

## 2. Phases

### Phase 1 — Retarget to net10.0 and bump dependencies — DONE
`build/version.props` now sets `net10.0` / 7.0.0; every package updated per the matrix above. The
whole solution (38 SDK-style projects, app + 30 plugins + tests) compiles clean on .NET SDK 10.0.1xx.

### Phase 2 — API migrations required by the upgrade — DONE
- **JwtBearer token validation** migrated from the legacy `ISecurityTokenValidator` /
  `options.SecurityTokenValidators` API (removed from the default path in ASP.NET Core 8+) to a
  `TokenHandler` registered via `options.TokenHandlers` (`BearerTokenValidator.cs`,
  `BearerAuthentication.cs`). Invalid tokens now yield `TokenValidationResult.IsValid = false`,
  which flows to the same 401/403 challenge behavior as before.
- **Serilog** rolling files: `Serilog.Sinks.RollingFile` is deprecated and incompatible with
  Serilog 4; replaced with `Serilog.Sinks.File`. New `RollingLogFile` helper translates the legacy
  `log-{Date}.txt` configuration convention (still present in deployed appsettings.json files) to
  the equivalent file-sink path + daily rolling interval, preserving on-disk file names.
- **Build scripts**: the PowerShell `Copy-Configs.ps1` build hook was replaced with an equivalent
  cross-platform MSBuild target; the publish-time jQuery-integrity rewrite is now Windows-only
  conditioned.
- **Deprecation cleanup (CI annotations, zero warnings remain):** hosting migrated from the
  obsolete `WebHostBuilder`/`IWebHost` + `RunAsService` model to the generic host with
  `UseWindowsService` (ASPDEPR004/008; the HTTPS-only check now validates configured urls up
  front); `X509CertificateLoader` replaces the obsolete `X509Certificate2` file constructors
  (SYSLIB0057); `Rfc2898DeriveBytes.Pbkdf2` replaces the obsolete constructor with identical
  SHA1/iterations/size so stored key hashes remain valid (SYSLIB0060); the dead
  `AssemblyName.ProcessorArchitecture` plugin check was removed (SYSLIB0037); response header
  writes use the indexer instead of `Add` (ASP0019, 9 sites); workflows opt into Node 24 for
  `setup-msbuild` ahead of the June 16, 2026 forced migration.

### Phase 3 — Upstream bug fixes — DONE
- **#331 / #329 (auth broken for "never expires" tokens and non-UTC servers):** `SecurityToken`
  reported `DateTime.MaxValue` with `Unspecified` kind, which `DateTimeOffset` interprets as local
  time and overflows for negative UTC offsets. `ValidFrom`/`ValidTo` are now always UTC-kind.
- **#324 (concurrent applicationHost.config commits → unhandled 500):** new `ConfigCommitGate`
  serializes `ServerManager.CommitChanges()` across requests and maps the residual
  `FileLoadException` ("file has changed on disk") to a structured **HTTP 409 Conflict**
  (`ConfigurationConflictException` + `ErrorHelper.ConflictError`) so clients can retry.

### Phase 4 — New tests — DONE
New cross-platform unit test project `test/Microsoft.IIS.Administration.UnitTests` (12 tests, all
passing) covering: the timezone/never-expires JWT regression, token validation success/failure
paths, the `{Date}` log-name translation, and commit-gate serialization + 409 mapping. The legacy
`test/Microsoft.IIS.Administration.Tests` integration suite still requires Windows + IIS and is
unchanged (versions bumped).

### Phase 5 — CI: GitHub Actions alongside Azure Pipelines — DONE
`.github/workflows/ci.yml` is the fork's CI. The upstream `.azure/pipelines/build.yml` and
`azure-pipelines/` signing helpers are retained untouched (per review: they are Microsoft-internal
MicroBuild/1ES infrastructure a fork cannot run, but keeping them intact preserves a clean diff for
upstreaming PRs). The workflow:
- **windows-latest:** nuget restore → `msbuild /t:publish` → unit tests → Clean-BuildDir →
  installer restore/build (WiX 3.11 comes from `packages.config`) → uploads the same three
  artifacts the old pipeline published (dist, bundle, MSI).
- **ubuntu-latest:** `dotnet build` of `Microsoft.IIS.Administration.CrossPlatform.slnf` (all
  projects except the legacy `Microsoft.Web.Administration.Refs` shim) + unit tests.

### Phase 6 — Installer updates — DONE
`installer/IISAdministrationBundle/iisadministration.wxs` now requires .NET Runtime ≥ 10.0 and the
v10.0 ASP.NET Core shared framework, downloading 10.0.8 from the official
`builds.dotnet.microsoft.com` URLs; product version bumped to 7.0. The MSI file manifest
(`installer/IISAdministrationSetup/files.wxs`) was reconciled against the actual net10.0 publish
closure: stale entries removed (RollingFile sink, net6.0 runtime path, jQuery 3.2.0) and newly
required files added (Microsoft.IdentityModel.Abstractions, EventLog messages DLL, 19 new
reference assemblies for Razor runtime compilation). The two `<RemotePayload>` blocks carry
heat.exe-generated metadata for the real 10.0.8 executables, and the `installer-payload-metadata`
CI job re-verifies them on every run, failing with corrected XML if they ever drift.

### Phase 7 — Windows runtime validation — AUTOMATED (`.github/workflows/validation.yml`)
Runs on a `windows-2025` / `windows-2022` matrix (the supported-OS spread available on hosted
runners) plus a payload-metadata job:
1. **RemotePayload verification:** downloads the bundle's runtime installers, regenerates payload
   metadata with `heat.exe`, and fails with the correct XML in the job summary if the committed
   values drift.
2. **Real install:** minimal IIS (`Web-Server` role only), machine-wide .NET 10 runtimes (the
   service runs as LocalSystem and cannot see user-scoped installs), silent MSI install, service
   startup under HTTP.sys.
3. **Regression smoke (`scripts/ci/Invoke-ValidationSmoke.ps1`):** runner timezone is set to
   UTC-10 before install, then a never-expiring access key is created and used (#329/#331 repro),
   concurrent app-pool PATCHes must return only 200/409 (#324 repro), webserver endpoints confirm
   plugin loading (including the `ms.web.admin.refs` fallback), and the access-keys UI must render.
4. **Integration suite:** `test/Microsoft.IIS.Administration.Tests` runs informationally
   (continue-on-error) with TRX results uploaded, until its behavior on hosted runners is triaged.

Hosted runners cannot cover: older Windows Server versions (README claims 2008 R2+),
domain-joined Windows-auth scenarios, or in-place upgrades from 6.0.0 — those still warrant a
one-time manual pass on representative VMs before shipping a release.

### Phase 8 — Release — TODO
Tag v7.0.0 from the fork once Phase 7 passes; include a changelog summarizing this document.

---

## 3. Known debt / decisions
- **`Microsoft.Web.Administration.Refs`** (legacy net461 shim) still ships 2016-era facade DLLs into
  `plugins/ms.web.admin.refs` because `PluginAssemblyLoadContext` probes that folder as a last-resort
  fallback for `Microsoft.Web.Administration` 11.1.0 dependencies. On .NET 10 these are almost
  certainly dead weight (the default load context satisfies every facade), but removal must be
  validated at runtime on Windows (Phase 7.4) before deleting the project.
- The MicroBuild signing infrastructure (`sign.props`, MicroBuild.Core references) is retained but
  inert without Microsoft-internal pipelines; CI produces unsigned builds.
- jQuery's `integrity-publish` hash now equals `integrity-local` — the upstream divergence existed
  only because Microsoft's signing pipeline rewrote the published file.
- Local Linux builds against Canonical's apt-packaged SDK need
  `/p:NetCoreTargetingPackRoot=<empty dir> /p:AllowMissingPrunePackageData=true /p:UseAppHost=false`
  because Ubuntu's source-built ASP.NET targeting pack strips Windows-only servers (HTTP.sys) from
  the reference assemblies. Microsoft SDK installs (and `actions/setup-dotnet`) are unaffected.
