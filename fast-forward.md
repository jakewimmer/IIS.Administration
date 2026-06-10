# Fast-Forward Plan: IIS.Administration Fork Modernization

**Date:** 2026-06-10
**Fork:** jakewimmer/IIS.Administration (`dev` @ `60ca64a`)
**Upstream:** microsoft/IIS.Administration

---

## 1. Current State Assessment

### Repository state
- The fork's `dev` branch is **identical to upstream's tip** (`60ca64a`, Feb 29 2024). There is nothing to merge from upstream; "fast-forwarding" means modernizing the codebase itself.
- Upstream is effectively unmaintained: last commit Feb 2024 (build-pipeline only), last functional release **v6.0.0 (Sept 2022)**, 12 open issues with no maintainer responses since 2024.
- The product is a Windows-only ASP.NET Core REST API (plus ~30 plugin assemblies) that manages IIS via `Microsoft.Web.Administration`, shipped as a WiX MSI/bundle running as a Windows service.
- Tests are a single xUnit integration-test project (`test/Microsoft.IIS.Administration.Tests`) that requires a live IIS + the running service on Windows. There is no fork CI; upstream CI is an internal Azure Pipelines + MicroBuild signing setup that a fork cannot use.

### Dependency graph (key facts)
| Dependency | Current | Status |
|---|---|---|
| Target framework (`build/version.props`) | `net6.0` | **EOL since Nov 12, 2024** — no security patches |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 6.0.1 | Vulnerable transitive `Microsoft.IdentityModel.*` / `System.IdentityModel.Tokens.Jwt` (e.g. CVE-2024-21319) |
| `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | 6.0.1 | EOL package line (resolved `Newtonsoft.Json` is 13.0.1 — already past the known DoS advisory) |
| `Microsoft.AspNetCore.Hosting.WindowsServices` | 6.0.1 | EOL package line |
| `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` | 6.0.3 | EOL package line |
| `Microsoft.Web.Administration` | 11.1.0 | Current; verify against latest on NuGet |
| `Serilog` 2.11.0 + `Serilog.Sinks.RollingFile` 3.3.0 | — | RollingFile sink is **deprecated**; replace with `Serilog.Sinks.File` |
| ~20 `System.*` / `Microsoft.Win32.*` packages | 4.0.x–4.1.0 | Obsolete .NET Core 1.0-era refs; all are part of the shared framework now — should be deleted |
| jQuery (vendored, `wwwroot/lib/jquery-3.2.0.min`) | 3.2.0 | CVE-2019-11358 (prototype pollution), CVE-2020-11022/11023 (XSS) |
| xunit 2.4.1 / Microsoft.NET.Test.Sdk 17.0.0 | — | Old but functional |
| Installer | WiX, `packages.config`, bundles .NET 6 hosting prereqs | Must be updated alongside any retarget |

### NuGet vulnerability audit (verified with `dotnet list package --vulnerable --include-transitive`, June 2026)
| Vulnerable package (transitive) | Resolved | Severity | Advisory | Pulled in by |
|---|---|---|---|---|
| `System.Text.Json` | 6.0.0 | **High** | GHSA-8g4q-xg66-9fp4 | ASP.NET Core 6.0.1 packages (main app) |
| `System.Net.Http` | 4.1.0 | **High** | GHSA-7jgj-8wvc-jh57 | obsolete `System.*` 4.x refs — flagged in nearly every plugin project |
| `System.Security.Cryptography.X509Certificates` | 4.1.0 | **High** | GHSA-7mfr-774f-w5r9 | obsolete `System.*` 4.x refs — same blast radius |
| `Microsoft.IdentityModel.JsonWebTokens` / `System.IdentityModel.Tokens.Jwt` | 6.10.0 | Moderate | GHSA-59j7-ghrg-fj52 | `Microsoft.AspNetCore.Authentication.JwtBearer` 6.0.1 |

All four findings are resolved by two checklist items: removing the obsolete `System.*` 4.x references and bumping the ASP.NET Core package set on a supported TFM.

### Pressing upstream issues (triaged)
- **#331 / #329 (same root cause, confirmed locally):** `SecurityToken.cs:57` returns `_key.ExpiresOn ?? DateTime.MaxValue` for never-expiring access keys. When the JWT layer applies a non-zero UTC offset, `DateTimeOffset` overflows → `ArgumentOutOfRangeException` → HTTP 500. Breaks auth entirely on servers with negative UTC offsets and for "never expires" tokens. **Fix is small and well-understood.**
- **#324:** Concurrent PATCHes to application pools race on `applicationHost.config` → "Cannot commit configuration changes because the file has changed on disk" (unhandled `FileLoadException`). Needs serialization of config commits and/or retry + a proper 409/503 response instead of a 500.
- **#333:** ARRHelper crashes on Windows 11 with 32-bit application pools (installer-side component).
- **#315:** 500 on login (2022, likely environment/config; needs repro attempt — may be a duplicate of the #329 timezone failure).
- **#330 / #328:** Requests to retarget .NET 8+ — addressed by this plan.
- **#321 / #322 / #334:** Site-config edit bug, CPU percent metric, multi-React-app pool issue — lower confidence/lower priority; triage only.

---

## 2. Checklist of Changes (by priority)

### P0 — Security / EOL (do first)
- [ ] Retarget all projects from `net6.0` to a supported LTS (**.NET 8 as the mechanical step; .NET 10 LTS as the end state** — .NET 8 itself hits EOL Nov 2026, so don't stop there).
- [ ] Bump `Microsoft.AspNetCore.*` packages (JwtBearer, NewtonsoftJson, WindowsServices, RuntimeCompilation) to the matching supported versions; verify `Newtonsoft.Json` resolves to ≥ 13.0.3 and `Microsoft.IdentityModel.*` to patched versions.
- [ ] Fix the JWT expiry overflow (`SecurityToken.cs:57`) — cap "never expires" to a safe UTC value (e.g. `DateTimeOffset.MaxValue.UtcDateTime` with explicit `DateTimeKind.Utc`, or `UtcNow + 100 years`). Resolves #331 and #329.
- [ ] Upgrade vendored jQuery 3.2.0 → current 3.x; regenerate the SRI `integrity-local`/`integrity-publish` hashes in `Microsoft.IIS.Administration.csproj`.
- [ ] Remove all obsolete `System.*` / `Microsoft.Win32.*` 4.0.x package references (dead weight, old vulnerable assets in the dependency graph).

### P1 — Stability / correctness
- [ ] Serialize `applicationHost.config` commits (process-wide lock or retry-with-backoff around `CommitChanges()`); map the conflict to HTTP 409 instead of an unhandled 500. Resolves #324.
- [ ] Replace deprecated `Serilog.Sinks.RollingFile` with `Serilog.Sinks.File`; bump Serilog packages.
- [ ] Attempt repro/triage of #315 (login 500) after the #329 fix lands — likely related or environmental.
- [ ] Investigate ARRHelper 32-bit crash on Win11 (#333) in the installer tree.
- [ ] Update installer (WiX bundle + setup project) to carry/check the new .NET hosting prerequisites; update version gates and docs' "remove preview .NET" guidance.

### P2 — Maintenance / hygiene
- [ ] Add fork CI: GitHub Actions `windows-latest` workflow doing restore + build (+ unit-testable subset); strip/neutralize MicroBuild signing for non-signed builds.
- [ ] Bump test stack (xunit 2.x latest, Test SDK 17.x latest) and document how to run the integration suite.
- [ ] Bump `IISAdministrationVersion` (6.0.0 → 6.1.0 or 7.0.0 given the TFM change) in `build/version.props`.
- [ ] Refresh README (VS 2022+ instructions, supported OS matrix, fork status note) and add a CHANGELOG.
- [ ] Triage/close-or-document remaining upstream issues in fork terms (#320 docs example, #321, #322, #334).
- [ ] Evaluate dropping `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` (only needed for the handful of bundled Razor views) to shrink attack surface.

---

## 3. Implementation Plan (7 phases)

> Constraint: this project only builds and runs on Windows with IIS installed. Phases are ordered so each lands as a separately verifiable commit/PR, with CI established early to validate everything after it.

### Phase 1 — Baseline & CI scaffolding
Get a reproducible fork build before changing anything: add a GitHub Actions workflow (`windows-latest`) that restores and builds `Microsoft.IIS.Administration.sln` with signing disabled (`SignType` empty), and record the baseline build/test state. This is the safety net for all later phases.

### Phase 2 — Framework-independent dependency hygiene
Low-risk cleanup on net6.0 so the retarget diff stays readable: delete the ~20 obsolete `System.*`/`Microsoft.Win32.*` 4.0.x references across all csproj files, swap `Serilog.Sinks.RollingFile` → `Serilog.Sinks.File` (code change in logging setup), bump Serilog, xunit, and Test SDK. Build must stay green.

### Phase 3 — Retarget to supported .NET LTS
Flip `IISAdministrationTargetFramework` in `build/version.props` to `net8.0`, bump the four `Microsoft.AspNetCore.*` packages to 8.0.x, fix any compile breaks/obsoletions (hosting and Startup patterns from 6 generally carry forward), then — once green — evaluate the further flip to `net10.0` LTS in the same phase (single-property change; the package bumps repeat). Verify `Microsoft.Web.Administration` 11.1.0 still resolves and loads. Confirm transitive `Newtonsoft.Json` ≥ 13.0.3.

### Phase 4 — Security bug fixes
Fix `SecurityToken.cs` expiry overflow (#331/#329) with a regression test around token creation/validation at negative UTC offsets and "never expires" keys. Upgrade vendored jQuery and regenerate both SRI hashes in the main csproj. Re-check `ApiKeyProvider`/`AccessKeysController` date comparisons for `DateTimeKind` consistency while in the area.

### Phase 5 — Concurrency & stability
Wrap `ManagementUnit`/config commit paths with serialization (single-writer lock or bounded retry on `FileLoadException`) and surface conflicts as HTTP 409; add a concurrent-PATCH test against app pools (#324). Re-test #315 and #321 scenarios after Phases 3–4 and document findings.

### Phase 6 — Installer & deployment
Update the WiX bundle/setup projects: bundle or prerequisite-check the new ASP.NET Core hosting components, bump product version, and validate fresh install + uninstall on a Windows test VM. Investigate the ARRHelper 32-bit crash (#333) here, since it lives in the installer tree.

### Phase 7 — Validation, docs & release
Full integration-test run (`test/Microsoft.IIS.Administration.Tests`) against the installed service on Windows, plus manual smoke of auth flows (bearer token via PowerShell per #320 — fold the answer into docs). Refresh README, add CHANGELOG summarizing all phases, set the final version in `build/version.props`, and tag a fork release.

---

## 4. Risks & notes
- **.NET 8 EOL is Nov 2026** — five months away. The plan treats net8.0 as a validation stepping stone inside Phase 3 and recommends landing on **.NET 10 LTS** (supported to Nov 2028) before release.
- `Microsoft.Web.Administration` (COM interop with IIS) is the least replaceable dependency; it is version-stable but must be smoke-tested on every TFM change.
- Newtonsoft.Json is used in ~53 places across the codebase; migrating to System.Text.Json is intentionally **out of scope** — updating to 13.x via the NewtonsoftJson MVC package is sufficient and safe.
- Nothing in this repo can be built or integration-tested on Linux; CI (Phase 1) and a Windows test VM are prerequisites for verifying Phases 3–7.
- Upstream is unresponsive, so none of these fixes should block on upstreaming; they can be offered back as PRs opportunistically.
