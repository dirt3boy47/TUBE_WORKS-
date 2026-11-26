# TubeWorks Designer (working title)

Native SOLIDWORKS add-in for parametric circular tube design: select a tube end face, bend it, extend it, flatten it to a straight blank, and get the bend table.

**Status: release 0.3.0 — every Phase 2 item of the specification is implemented.** 43 engine tests pass; the add-in compiles with no errors or warnings against the SOLIDWORKS 2016 and 2026 APIs. 0.2.1 was installed and used in SOLIDWORKS 2026 SP4 (Face Bend, Straight, Edit, Unbend, Flatten, Rebend, Export, Bend Table worked; one crash in Face Bend is addressed in 0.3.0). Drawings, DWG export, engraving and the 0.3.0 changes have **not** yet run in SOLIDWORKS: use `docs/TEST-PLAN.md`.

| Implemented | Not in Phase 2 / planned |
|---|---|
| Base Tube, Face Bend (tooling radius list), Straight Extension, Edit/Reverse Bend, Set Bend Plane, Unbend/Rebend with exact transfer of cuts in straight segments, Flatten Tube (keeps cuts, bend lines on the blank), Unwrap Tube Surface → DXF and DWG with layers (engraving layer, approximate outlines of cuts in bends, flagged), Tube Drawing (views A4–A1, centreline, straightened view, developed surface, bend table, notes), Export (STEP, Parasolid, SLDPRT, CSV), Bend Table (CSV, XYZ, LRA), Tube Library (editor, CSV/XML), Manufacturing Check (radius, tooling, stock length, straights, clamping, angles, self-intersection, cuts in or near bends) | Multi Bend, Tube Trim, Tube Notch; driven dimensions on drawings; Phase 3 (postprocessors, compensation, nesting, costing) |

## Install

Run `TubeWorksSetup-0.3.0.exe` (from the CI artifact or `installer/build-installer.sh`) as Administrator with SOLIDWORKS closed. See [docs/BUILD-AND-INSTALL.md](docs/BUILD-AND-INSTALL.md).

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
