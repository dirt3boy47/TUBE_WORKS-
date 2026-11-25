# TubeWorks Designer (working title)

Native SOLIDWORKS add-in for parametric circular tube design: select a tube end face, bend it, extend it, flatten it to a straight blank, and get the bend table.

**Status: release 0.2.0 (Phase 1 + first part of Phase 2).** The geometry engine has 36 automated tests, all passing. The add-in compiles with no errors or warnings against the SOLIDWORKS 2016 and 2026 APIs. **It has not yet been run inside SOLIDWORKS.** Run `docs/TEST-PLAN.md` part B before you use it for production.

| Implemented | Planned (shown disabled, or not yet in the toolbar) |
|---|---|
| Base Tube, Face Bend, Straight Extension, Edit Bend, Reverse Bend, Set Bend Plane, **Unbend, Rebend (with exact transfer of cuts in straight segments)**, Flatten Tube (straight blank, keeps cuts), **Unwrap Tube Surface → DXF with layers**, Export (STEP, Parasolid, SLDPRT, centreline CSV), Bend Table (CSV, XYZ, LRA), Tube Properties, Manufacturing Check (now also cuts in bends), **Tube Library editor with CSV/XML import/export** | Multi Bend, Tube Trim, Tube Notch; automatic drawings; DWG; compensation; postprocessors |

## Install

Run `TubeWorksSetup-0.2.0.exe` (from the CI artifact or `installer/build-installer.sh`) as Administrator with SOLIDWORKS closed. See [docs/BUILD-AND-INSTALL.md](docs/BUILD-AND-INSTALL.md).

## Build from source

```bash
dotnet build TubeWorks.sln -c Release
```

## Documents

- [docs/DESIGN.md](docs/DESIGN.md): architecture, API feasibility, directory structure, geometry engine, UI, persistence, roadmap, risks
- [docs/BUILD-AND-INSTALL.md](docs/BUILD-AND-INSTALL.md): build, install, register, uninstall, debug
- [docs/USER-GUIDE.md](docs/USER-GUIDE.md): commands and workflow
- [docs/TEST-PLAN.md](docs/TEST-PLAN.md): automated tests and the manual SOLIDWORKS test protocol
- [docs/library-example.xml](docs/library-example.xml): tube library format (example values only)
