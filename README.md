# TubeWorks Designer (working title)

Native SOLIDWORKS add-in for parametric circular tube design: select a tube end face, bend it, extend it, flatten it to a straight blank, and get the bend table.

**Status: release 0.1.0, Phase 1 MVP.** The geometry engine is tested (24 automated tests). The add-in compiles against the SOLIDWORKS 2026 API with no errors or warnings. **It has not yet been run inside SOLIDWORKS.** Run `docs/TEST-PLAN.md` part B before you use it for production.

| Implemented (Phase 1) | Planned (shown disabled in the toolbar) |
|---|---|
| Base Tube, Face Bend, Straight Extension, Edit Bend, Reverse Bend, Set Bend Plane, Flatten Tube (straight blank), Export (STEP, Parasolid, SLDPRT, centreline CSV), Bend Table (CSV, XYZ, LRA), Tube Properties, Manufacturing Check | Multi Bend, Tube Trim, Tube Notch, Unbend, Rebend, Unwrap Tube Surface; DXF/DWG; drawings; compensation; postprocessors |

## Quick start

```bash
dotnet build TubeWorks.sln -c Release
```

Then, as Administrator with SOLIDWORKS closed:

```bash
powershell -ExecutionPolicy Bypass -File installer\Install-TubeWorks.ps1
```

## Documents

- [docs/DESIGN.md](docs/DESIGN.md): architecture, API feasibility, directory structure, geometry engine, UI, persistence, roadmap, risks
- [docs/BUILD-AND-INSTALL.md](docs/BUILD-AND-INSTALL.md): build, install, register, uninstall, debug
- [docs/USER-GUIDE.md](docs/USER-GUIDE.md): commands and workflow
- [docs/TEST-PLAN.md](docs/TEST-PLAN.md): automated tests and the manual SOLIDWORKS test protocol
- [docs/library-example.xml](docs/library-example.xml): tube library format (example values only)
