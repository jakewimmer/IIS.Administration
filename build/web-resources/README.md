# Web-resource version tracking (Dependabot shim)

The client-side libraries served from
`src/Microsoft.IIS.Administration/wwwroot/lib/` are acquired with
[LibMan](https://learn.microsoft.com/aspnet/core/client-side/libman/) from the
**cdnjs** provider, as declared in
`src/Microsoft.IIS.Administration/libman.json`.

Dependabot has **no native ecosystem** for LibMan or cdnjs, so it cannot watch
`libman.json` directly. The `package.json` in this folder is a **tracking-only
shim**: it lists the same libraries at the same pinned versions in npm form, and
`.github/dependabot.yml` points an `npm` update entry at this directory. The
shim is never `npm install`-ed and is not part of any build - npm is just the
registry Dependabot uses to discover newer versions of libraries that also
happen to ship on cdnjs.

## When Dependabot opens a PR here

A bump to `package.json` (e.g. `jquery` `3.7.1` -> `3.x.y`) is a **signal**, not
a finished change. To actually adopt it, update the real source of truth:

1. Bump the version in `src/Microsoft.IIS.Administration/libman.json`.
2. Re-restore the asset (Visual Studio restores on build, or run the LibMan
   CLI: `dotnet tool restore && dotnet libman restore` from
   `src/Microsoft.IIS.Administration`). This rewrites
   `wwwroot/lib/jquery-<version>.min/jquery.min.js`.
3. Update the version-coupled references in
   `src/Microsoft.IIS.Administration/Views/Shared/_Layout.cshtml`:
   - the script `src` path (the folder name embeds the version), and
   - the Subresource Integrity (`integrity="sha384-..."`) hash for the new file.
4. Fold the Dependabot PR's `package.json` change into the same commit so this
   shim stays in lockstep with `libman.json`.

> Keep the version here identical to `libman.json`. If they drift, Dependabot
> will report against the wrong baseline.
