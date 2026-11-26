# TubeWorks Designer (working title)

Native SOLIDWORKS add-in for parametric circular tube design: select a tube end face, bend it, extend it, flatten it to a straight blank, and get the bend table.

**Status: release 0.4.0 — Phases 1, 2 and 3 of the specification are implemented.** 56 engine tests pass; the add-in compiles with no errors or warnings against the SOLIDWORKS 2016 and 2026 APIs. 0.2.1 was used in SOLIDWORKS 2026 SP4. Everything added since 0.3.0 (drawings, DWG, Phase 3) has **not** yet run in SOLIDWORKS: use `docs/TEST-PLAN.md`.

| Implemented | Not implemented |
|---|---|
| **Design:** Base Tube, Face Bend (XYZ/direction solving, tooling radius list), Straight Extension, Edit/Reverse Bend, Set Bend Plane. **Development:** Unbend/Rebend with cut transfer, Flatten Tube (cuts, bend lines), Unwrap → DXF/DWG with layers. **Output:** Bending Guide (rolled-out tube with marks and steps for manual bending), Tube Drawing (template of your choice, bending layout of the bar), Export (STEP, Parasolid, SLDPRT), Bend Table (CSV, XYZ, LRA). **Manufacturing (Phase 3):** CNC Output through postprocessor templates with springback, stretch, neutral-axis, angle-offset and trim compensation from calibration data; Manufacturing Report (HTML, cost, stock); Batch & Nesting (folder of parts, CNC/DXF/STEP/CSV, nesting into stock bars per tube size, cost, one report). **Data:** Tube Library (presets, tooling, limits, calibration, prices), Production Settings (rates, times, saw). **Checks:** Manufacturing Check. | Multi Bend, Tube Trim, Tube Notch; driven dimensions on drawings; certified formats of specific machine controllers (templates are generic) |

## Install

Run `TubeWorksSetup-0.4.3.exe` (from the CI artifact or `installer/build-installer.sh`) as Administrator with SOLIDWORKS closed. See [docs/BUILD-AND-INSTALL.md](docs/BUILD-AND-INSTALL.md).

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
