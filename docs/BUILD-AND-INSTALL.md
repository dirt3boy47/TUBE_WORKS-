# Build and install

## Install with the setup program (recommended)

1. Close SOLIDWORKS.
2. Run `TubeWorksSetup-0.2.1.exe`. Windows asks for administrator rights.
   The setup program is not code-signed, so Windows SmartScreen can show "Windows protected your PC": click **More info > Run anyway**
   only if you got the file from your own build or CI. (Signing needs your organisation's code-signing certificate.)
3. The setup program checks 64-bit Windows, .NET Framework 4.8 and that SOLIDWORKS is closed; copies the files to
   `C:\Program Files\TubeWorks Designer`; registers the add-in (`RegAsm /codebase`); and adds **TubeWorks Designer** to
   *Settings > Apps* with an uninstaller and Start-menu entries (Documentation, Uninstall).
4. Start SOLIDWORKS and open a part: the **Tube Design** tab appears. If not: **Tools > Add-Ins**, tick TubeWorks Designer.

Silent install / uninstall (for IT deployment): `TubeWorksSetup-0.2.1.exe /S`, `"C:\Program Files\TubeWorks Designer\Uninstall.exe" /S`.

**SOLIDWORKS version.** Since 0.2.1 the SOLIDWORKS interface definitions are embedded in `TubeWorks.AddIn.dll`
(Embed Interop Types). The DLL references no SOLIDWORKS assembly, so it does not depend on the SOLIDWORKS version for loading
or registration. It uses only API calls that exist in SOLIDWORKS 2016 and later. (0.2.0 referenced the 2016 interop
assemblies; registration failed with RegAsm exit code 100 on a PC where they could not be resolved.)

If registration fails, the setup program writes RegAsm's output to `C:\Program Files\TubeWorks Designer\regasm.log`.

## Build the setup program

- Linux / CI: `installer/build-installer.sh` (needs .NET SDK 8, NSIS, curl, unzip). Output: `installer/out/TubeWorksSetup-<version>.exe`.
- Windows: install NSIS 3, then `powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1`. This compiles against
  your SOLIDWORKS installation (`api\redist`).
- The version number is in `Directory.Build.props` (`TubeWorksVersion`).
- The GitHub workflow builds the setup program on every push and attaches it as the artifact **TubeWorksSetup**.

# Manual build and install (developers)

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

This proves that every API member used exists with the used signature. It does not run anything. The GitHub workflow `.github/workflows/build.yml` runs the unit tests, this compile check and the setup-program build on every push.
