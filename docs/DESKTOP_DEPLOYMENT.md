# Desktop app: building, running, and redeploying

The Desktop app (`src/AiAutomations.Desktop`) is a single-project MSIX app
(`EnableMsixTooling=true`). This is the single most expensive-to-rediscover piece of knowledge
in this repo - a straightforward `dotnet build` followed by the obvious next step does **not**
get you a runnable app, and the failure mode is silent (no error, just a stale app that doesn't
reflect your latest code). Read this before touching deployment.

## The two ways to run it

### 1. Visual Studio (preferred when it works)

Open `AiAutomations.slnx`, set `AiAutomations.Desktop` as the startup project, F5. VS's own
deploy pipeline handles packaging/registration for you.

**Known current limitation**: in this environment, VS's deploy wants to **uninstall the existing
package first** before reinstalling, because the package currently on the machine was registered
via the CLI recipe below (unsigned/loose), which has a different signing/identity trust path than
VS's own signed deploy. Windows treats that as a mismatch and forces uninstall-then-reinstall.
**Uninstalling wipes the app's `LocalState` folder completely - the SQLite database and
`settings.json`, gone.** Until the project's packaging/signing is reconciled (give it a stable
test certificate VS consistently reuses), prefer the CLI recipe for iterative development if you
want to preserve local data across redeploys. Don't attempt to "fix" the signing setup without
being asked - it's a deliberate tradeoff, not an oversight.

### 2. CLI recipe (in-place update, preserves local data)

```powershell
# 1. Stop the running instance - it holds its own binaries locked, so a build/publish can't
#    overwrite them otherwise.
Get-Process -Name "AiAutomations.Desktop" -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Publish a self-contained win-x64 build. This is the key step: plain `dotnet build` does
#    NOT regenerate a loose app package layout outside Visual Studio - only this does.
dotnet publish src\AiAutomations.Desktop\AiAutomations.Desktop.csproj `
    -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:Configuration=Debug

# 3. Before registering, clear any broken/orphaned registration. A package can show
#    `Status: Ok` in Get-AppxPackage with an EMPTY InstallLocation (pointing at a folder that
#    no longer exists) - registering over that fails with 0x80070003/0x80073CF9.
Get-AppxPackage | Where-Object { $_.Name -like "*AiAutomations*" } | Remove-AppxPackage

# 4. Register the fresh loose layout in place (this is an update, not an uninstall+install -
#    LocalState/the database survive).
$manifest = "src\AiAutomations.Desktop\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\AppxManifest.xml"
Add-AppxPackage -Register $manifest

# 5. Launch it.
$pfn = (Get-AppxPackage | Where-Object { $_.Name -like "*AiAutomations*" }).PackageFamilyName
explorer.exe "shell:AppsFolder\$pfn!App"
```

Step 3's `Remove-AppxPackage` is only needed when something's actually broken (step 4 errors);
skip it on a normal redeploy and it'll just update in place.

### Verifying a redeploy actually took

Since this failure mode is silent, after any redeploy check that the running binary's timestamp
matches your latest build, and that local data (settings, DB) survived:

```powershell
(Get-Item "src\AiAutomations.Desktop\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\AiAutomations.Desktop.dll").LastWriteTime
(Get-Content "$env:LOCALAPPDATA\Packages\AiAutomations.Desktop_<publisher-hash>\LocalState\settings.json" | ConvertFrom-Json).Slack.SelfUserId
```

(Find the exact `Packages\AiAutomations.Desktop_...` folder name with
`Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter "AiAutomations.Desktop_*"` - it's derived
from the manifest's `Identity` and stays stable across redeploys as long as `Package.appxmanifest`
doesn't change.)

## Why plain `dotnet build` isn't enough

A normal `dotnet build -p:Platform=x64` compiles the assemblies fine, but the loose `AppX`
folder VS uses for F5 debugging (`bin\x64\Debug\<tfm>\AppX\AppxManifest.xml` plus a full copy of
every dependency) is only produced by Visual Studio's own project-system targets - it is **not**
wired into the SDK's plain `Build` target. If an old `AppX` folder already exists from a previous
VS session, `dotnet build` reports success and leaves it completely untouched - you get a
"successful" build that silently does nothing for deployment, and registering that stale folder
launches old code with no error anywhere. **Don't trust that folder's existence or contents as a
sign a CLI build did anything.** The `dotnet publish -p:RuntimeIdentifier=win-x64` recipe above
is the one that reliably regenerates a loose, launchable layout (at
`bin\x64\Debug\<tfm>\win-x64\`, including a fresh `AppxManifest.xml`) from the command line.

## Signing a real .msix (only if you specifically need one)

`dotnet publish ... -p:GenerateAppxPackageOnBuild=true` produces an actual `.msix` under
`AppPackages\`, but `Add-AppxPackage -Path <msix>` refuses to install it unless it's signed, and
Windows requires that signature's root certificate to be in the **LocalMachine** `TrustedPeople`
store, which needs admin elevation. The loose-registration recipe above sidesteps all of this
(unsigned loose packages install fine in Developer Mode) and is what you want for day-to-day
iteration. Only bother with a signed `.msix` if you're specifically producing something to hand
to someone else to install.

## Visual assets: a recurring trap

MSIX visual assets (`Square44x44Logo.png`, `Square150x150Logo.png`, etc., referenced only from
`Package.appxmanifest`) are normally copied into a package by the real MSIX packaging payload
step - which the `dotnet publish -p:RuntimeIdentifier=win-x64` recipe above never runs (it's a
plain self-contained publish, not a package build). Without an explicit
`<Content Include="Assets\*.png"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>`
in `AiAutomations.Desktop.csproj`, these silently never land next to the published
`AppxManifest.xml`, so the taskbar icon, the custom title bar's icon `Image`, and the About
page's icon `Image` all render blank with no error. This `.csproj` already has that `Content`
item (alongside the tray icon's `app-icon.ico`, which was always explicit) - if a new PNG asset
is added to `Package.appxmanifest`, make sure the wildcard still covers it.

## First-run / lost-settings recovery

If `LocalState` ever gets wiped (accidental uninstall, or the VS-deploy uninstall issue above),
the app starts with blank Slack/AI settings and an empty database:

- **Settings** are editable from the Settings page's "Connections" card (writes to
  `settings.json` directly via `DesktopSettingsEditor`, no need to hand-edit the file) - changes
  need an app restart (there's a button for that) since the Slack/AI services are built once at
  launch from the loaded configuration.
- **Export/Import settings** (same card) round-trip the whole `settings.json` to/from a file
  outside the packaged storage, for exactly this scenario.
- **Backup/Restore database** (Settings page's "Backup & restore" card) does the same for the
  SQLite file.

There is no way to recover a wiped `LocalState` other than restoring from a backup taken before
it was wiped - encourage taking one periodically, there's no automatic schedule for it today.
