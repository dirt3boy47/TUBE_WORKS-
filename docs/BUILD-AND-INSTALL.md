# Build and install

## Requirements

- Windows 10/11 x64 with SOLIDWORKS installed (compile-checked against 2026; see DESIGN.md §8 for older versions).
- .NET Framework 4.8 (installed with current Windows and SOLIDWORKS).
- .NET SDK 8 (or later) for `dotnet build`, **or** Visual Studio 2022 (17.8 or later) with the ".NET desktop development" workload.
- Administrator rights to register the add-in.

The SOLIDWORKS interop assemblies are not in this repository. The project takes them from
`C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist`. If SOLIDWORKS is installed in a different folder, pass
`-p:SwInteropDir=<folder>` to `dotnet build`.

## Build

From the `tubeworks` folder:

```bash
dotnet test tests/TubeWorks.Geometry.Tests
```

```bash
dotnet build TubeWorks.sln -c Release
```

Output: `src\TubeWorks.AddIn\bin\Release\net48\` (`TubeWorks.AddIn.dll`, `TubeWorks.Geometry.dll`, `Icons\`).

In Visual Studio: open `TubeWorks.sln`, select **Release**, **Build > Build Solution**.

## Install (per machine)

Close SOLIDWORKS. In an **Administrator** PowerShell, in the `tubeworks` folder:

```bash
powershell -ExecutionPolicy Bypass -File installer\Install-TubeWorks.ps1
```

The script copies the build output to `C:\Program Files\TubeWorks Designer` and runs the 64-bit
`RegAsm.exe /codebase TubeWorks.AddIn.dll`. Registration writes:

- the COM classes `TubeWorks.AddIn`, `TubeWorks.BaseTubeFeature`, `TubeWorks.StraightFeature`, `TubeWorks.FaceBendFeature`, `TubeWorks.FlatPatternFeature`;
- `HKLM\SOFTWARE\SolidWorks\Addins\{6478F06E-71AB-4E73-9A87-8A6503767FF1}` (SOLIDWORKS add-in list);
- `HKCU\Software\SolidWorks\AddInsStartup\{6478F06E-71AB-4E73-9A87-8A6503767FF1}` = 1 (load at start-up, for the user who ran the script).

RegAsm shows warning RA0000 for an unsigned assembly with `/codebase`. This is expected.

Start SOLIDWORKS. **Tools > Add-Ins** lists **TubeWorks Designer**. Open a part: the **Tube Design** tab appears.
If the tab does not appear, right-click the CommandManager tabs and enable **Tube Design**.

Other users of the same PC: tick the add-in once in **Tools > Add-Ins** (start-up column).

## Development registration

Register the Debug build in place (Administrator):

```bash
powershell -ExecutionPolicy Bypass -File installer\Register-Dev.ps1
```

To debug: in Visual Studio set the add-in project's debug target to `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe`
(Project > Properties > Debug > Start external program), then F5.

## Uninstall

```bash
powershell -ExecutionPolicy Bypass -File installer\Uninstall-TubeWorks.ps1
```

Parts that contain TubeWorks features still open without the add-in. The features cannot rebuild until the add-in is installed again; their last bodies stay visible.

## Files used at run time

| File | Purpose |
|---|---|
| `%APPDATA%\TubeWorks\settings.xml` | Remembered values and options. `VerifyGeometry` switches the body self-check (keep it `true`). |
| `%APPDATA%\TubeWorks\library.xml` | Tube presets and manufacturing limits (format: `docs/library-example.xml`). Optional. |
| `%APPDATA%\TubeWorks\tubeworks.log` | Error log. Attach it to problem reports. |

## Compile check without SOLIDWORKS (CI, Linux)

`tools/fetch-interop.sh` downloads the interop assemblies from the public `Xarial.XCad.SolidWorks` NuGet package into `interop/` (git-ignored). Then:

```bash
dotnet build src/TubeWorks.AddIn/TubeWorks.AddIn.csproj -p:SwInteropDir=$PWD/interop
```

This proves that every API member used exists with the used signature. It does not run anything. The GitHub workflow `.github/workflows/tubeworks.yml` runs the unit tests and this compile check on every change under `tubeworks/`.
