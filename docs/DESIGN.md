# TubeWorks Designer — technical design

Release 0.2.0 (Phase 1 and the first part of Phase 2). This document contains the 8 deliverables that the specification asks for before the full application.

**Verification status, stated once for the whole document:**

| What | Status |
|---|---|
| Geometry engine (`TubeWorks.Geometry`) | 36 automated tests pass. Expected values come from closed-form hand calculations. The DXF output is also checked independently with ezdxf. |
| Add-in compile against SOLIDWORKS 2026 interop (34.1.1.11) | Compiles: 0 errors, 0 warnings. So every API member that the add-in uses exists with the signature that it uses. |
| Add-in compile against SOLIDWORKS 2016 interop (24.4) | Compiles: 0 errors, 0 warnings (since 0.2.0 the add-in uses `IBody2.Copy` and `SaveAs`). The setup program is built from this. |
| Setup program (`TubeWorksSetup-0.2.0.exe`, NSIS) | Built; contents checked (42 files, no interop assemblies). Not run on Windows. |
| Add-in running inside SOLIDWORKS | **Not tested.** The code was written on Linux, where SOLIDWORKS cannot run. Run the manual test plan (`TEST-PLAN.md`) before production use. |
| API semantics (argument layouts, orientation conventions) | The help pages could not be read from the build environment. Where an assumption was necessary, the add-in checks the result at run time (see §8). |

---

## 1. Technical architecture

```
SOLIDWORKS (sldworks.exe, x64)
 └─ COM ─ TubeWorks.AddIn.dll (.NET Framework 4.8, x64, COM visible)
            ├─ TubeWorksAddIn          ISwAddin: CommandManager tab "Tube Design", 17 commands, callbacks
            ├─ Features/               ISwComFeature macro features (one COM class per feature type)
            │    BaseTubeFeature, StraightFeature, FaceBendFeature, FlatPatternFeature
            ├─ Pmp/                    native PropertyManager pages + live preview + graphics handles
            │    BaseTubePage, FaceBendPage, StraightPage, SegmentPage, PageBase, Preview/Handles
            ├─ Sw/                     SOLIDWORKS adapters
            │    TubeFeatureStore (persistence, tree scan), FaceAnalyzer (end-face recognition),
            │    BodyBuilder (IModeler temporary bodies + self-checks), RefGeometry, SwUtil
            ├─ Ui/                     Bend Table and Export dialogs (WinForms, owned by the SOLIDWORKS frame)
            └─ UserSettings.cs         remembered preferences, tube library, log
                    │
                    ▼  (no SOLIDWORKS dependency)
          TubeWorks.Geometry.dll (.NET Standard 2.0)
            Vec3, TubeFrame, TubeProfile, PathPrimitive (Straight/Arc), TubeFeatureData (+codec),
            BendMath, BendSolver, TubeChainSolver, BendTable (CSV/XYZ/LRA), TubeValidator,
            RigidTransform, TubeUnbender, SurfaceUnwrapper, DxfWriter, TubeLibrary (CSV/XML)
```

Design rules:

1. **All geometry decisions are in the pure engine.** The add-in only converts SOLIDWORKS objects to engine values and engine results to bodies. The engine is unit-tested without SOLIDWORKS.
2. **Native features.** Each operation is a SOLIDWORKS *macro feature* (`IFeatureManager.InsertMacroFeature3`). It appears in the FeatureManager tree, rebuilds with the model, can be edited, suppressed, deleted, rolled back, saved and reopened, and takes part in native undo/redo. No separate undo system exists; the SOLIDWORKS transaction model is used.
3. **One body.** Each segment feature merges its own segment into the body that it continues (`EditBodies` + `IBody2.Operations2(SWBODYADD)`), like Sheet Metal features modify one sheet-metal body.

## 2. SOLIDWORKS API feasibility assessment

All members below exist in the 2026 interop (proved by compiling). "Assumption checked at run time" means that the documentation could not be read, so the code checks the result.

| Requirement | API used | Feasibility | Release 0.1 status |
|---|---|---|---|
| Native add-in, tab, icons | `ISwAddin`, `ICommandManager.CreateCommandGroup2`, `AddCommandItem2`, `IconList`/`MainIconList`, `AddCommandTab`, `ICommandTabBox.AddCommands` | Standard | Implemented |
| Parametric features in the tree | `InsertMacroFeature3`, `ISwComFeature.Regenerate/Edit/Security`, `IMacroFeatureData` | Standard | Implemented |
| Persistent parameters | one string parameter per macro feature (`GetStringByName`/`SetStringByName`) | Standard | Implemented |
| Persistent face references | macro feature selections (`GetSelections3`, `SetSelections`) for faces of non-TubeWorks tubes; `SetFaceUserId`/`SetEdgeUserId` on our bodies | Standard | Implemented |
| Straight tube body | `IModeler.CreateBodyFromCyl` + `SWBODYCUT` | Standard | Implemented; array layout checked by volume and centroid |
| Bend body (torus segment) | `IModeler.CreateToroidalSurface` + `CreateSheetFromSurface` + `ThickenSheet` | Feasible; no direct torus-solid call exists in `IModeler` | Implemented; parameter direction and thicken side found by probing; result checked by exact volume |
| Live preview | `IBody2.Display3` / `Hide` | Standard | Implemented |
| Graphics handles | `IModelViewManager.CreateManipulator` (drag arrow, triad), `SwManipulatorHandler2` | Standard | Implemented: arrow = straight length; triad = XYZ end point. No native single-ring rotation handle; plane rotation uses the number box (spin with mouse wheel). |
| Edit dialogs | `IPropertyManagerPage2`, `PropertyManagerPage2Handler9` | Standard | Implemented |
| End-face recognition | `IFace2.GetSurface/IsPlane`, `IEdge.GetCurve`, `ICurve.CircleParams`, `IFace2.Normal`, `GetClosestPointOn` | Standard | Implemented; circle data checked against an evaluated point; normal direction checked geometrically |
| Flat pattern (straight blank) | macro feature that replaces the tube body; `swMacroFeatureAlwaysAtEnd`; suppression toggles bent/flat | Feasible | Implemented (Base Tube roots only) |
| STEP / Parasolid / SLDPRT export | `IModelDocExtension.SaveAs3` with file extension | Standard | Implemented |
| Bend table, CSV, XYZ, LRA | engine + WinForms | Pure code | Implemented |
| Custom properties | `ICustomPropertyManager.Add3` | Standard | Implemented |
| Material | `IPartDoc.SetMaterialPropertyName2` | Standard | Implemented (part material) |
| Unbend/Rebend single bends, feature transfer | Unbend/Rebend macro features; cut material = nominal − actual body (`Operations2`); rigid motion with `IBody2.ApplyTransform` (`IMathUtility.CreateTransform`, layout checked with a test point) | Feasible | **Implemented 0.2** (exact for cuts in straight segments; cuts in bends reported, not moved) |
| 2D surface unwrap, DXF with layers | in-memory straightened body; loops of the cylindrical face (`IFace2.GetLoops`, `IEdge.GetCurveParams3`, `ICurve.Evaluate2`); engine maps and writes DXF R12 | Feasible | **Implemented 0.2** |
| DWG | needs a licensed DWG library (for example ODA) or a SOLIDWORKS drawing export | Feasible with a licence | Not planned without a library decision |
| Library editor, import/export | WinForms; engine CSV/XML codec | Pure code | **Implemented 0.2** |
| Installer | NSIS setup program; RegAsm /codebase; Apps entry; silent mode | Standard | **Implemented 0.2** |
| Drawings (views, bend table, sizes A4–A1) | `ISldWorks.NewDocument`, `IDrawingDoc.CreateDrawViewFromModelView3`, tables | Feasible | Planned Phase 2 |
| Square / rectangular / elliptical tube | engine `ProfileShape` enum reserved; body builder needs a profile sweep | Feasible | Planned |
| Associative display dimensions on features | macro feature dimensions (`DimTypes`/`DimValues`) | Feasible, needs testing in SOLIDWORKS | Planned |

## 3. Project directory structure

```
tubeworks/
├─ TubeWorks.sln
├─ README.md
├─ docs/            DESIGN.md (this file), BUILD-AND-INSTALL.md, USER-GUIDE.md, TEST-PLAN.md, library-example.xml
├─ installer/       Install-TubeWorks.ps1, Uninstall-TubeWorks.ps1, Register-Dev.ps1
├─ tools/           make_icons.py (icon generator), fetch-interop.sh (CI compile check)
├─ src/
│  ├─ TubeWorks.Geometry/   pure engine, netstandard2.0
│  └─ TubeWorks.AddIn/      SOLIDWORKS add-in, net48 x64
│     ├─ Features/ Pmp/ Sw/ Ui/ Icons/
│     ├─ TubeWorksAddIn.cs, UserSettings.cs
└─ tests/
   └─ TubeWorks.Geometry.Tests/  xUnit, net8.0
```

## 4. Geometry engine design

Units: metres and radians everywhere (SOLIDWORKS API internal units). Formatting converts at the edge.

**Frame.** A tube end is `TubeFrame(Origin, Tangent)`: the end-face centre and the unit tangent that points out of the tube.

**Reference direction for bend plane rotation.** `RefX` = projection of global +Y onto the end-face plane; if the tube axis is within 0.1° of global Y, global +Z is projected instead. `RefY = Tangent × RefX`. This depends only on the tangent, so it is the same on every rebuild. (The bend table reports rotation relative to the previous bend plane, which is the convention benders use.)

**Bend.** With start `P0`, tangent `T0`, radius `R`, angle `θ`, rotation `φ` (+π if reversed):

```
D  = cos φ · RefX + sin φ · RefY          (from start point toward the bend centre)
C  = P0 + R·D                             (bend centre)
P(a) = C + R(−cos a · D + sin a · T0)     a ∈ [0, θ]
T(a) = cos a · T0 + sin a · D
N  = T0 × D                               (bend plane normal; rotation axis)
```

The start tangent is never changed, so every bend is tangent-continuous by construction.

**Constrained solver (XYZ end point).** In the bend plane basis `(T0, D)`, the target `Q` relative to the centre is `w = (a, b − R)` where `a = (Q−P0)·T0`, `b` = distance of `Q` from the axis. A bend followed by a straight of length `L ≥ 0` reaches `Q` only when `|w| ≥ R`. Then `L = √(|w|² − R²)` and `θ = atan2(w) − atan2(−R, L)` (mod 2π). If `|w| < R`, the solver does not return geometry; it explains the conflict and offers `R_max = (a² + b²)/(2b)` (at this radius one bend reaches `Q` exactly) or a second bend. Points on the axis give "use Straight Extension" or "use two bends". The direction solver gives `θ = ∠(T0, T1)` and `D` = the component of `T1` perpendicular to `T0`.

**Chain.** `TubeChainSolver` reads the features in tree order. Each feature stores the *Id of its parent feature* and which end it continues (`End`, or `Start` of a Base Tube). The solver computes start and end frames, primitives and errors (missing parent, suppressed parent, branching, invalid radius). It then joins the features into paths from one free end to the other (the side behind a Base Tube start is reversed). An upstream edit therefore moves all downstream features at the next rebuild.

**Development.** `Total developed length = Σ straight lengths + Σ (CLR × θ in radians)`, nominal centreline. No compensation is applied in release 0.1. Compensation (stretch, springback, neutral axis, tooling calibration, trim allowance) will come only from user-defined or calibrated data (Phase 3); there are no built-in values.

**Bend table.** Feed = straight before the bend (LRA "L"); rotation = signed angle from the previous bend-plane normal to this one about the feed direction (LRA "R"); angle (LRA "A"); start/finish tangent points; tangent-line intersections (XYZ vertices, `P0 + T0·R·tan(θ/2)`, undefined at ≥ 180°); developed bend length; cumulative length. Conventions: rotation sign (right- or left-hand), bend angle or included angle, mm or inch.

**Validation.** Errors: geometry errors; CLR ≤ OD/2 (the inside of the bend would self-intersect); self-intersection (sampled at OD/8: two centreline points that are more than π·OD/2 apart along the path must be at least OD apart). Warnings: radius below the library minimum, angle above the machine limit (default 180°, can be cleared), no straight between bends, straight shorter than the library minimum, clamping length. Limits that are not defined are not checked.

## 5. User interface design

- **Tab "Tube Design"** (part documents) with the 17 commands. The 6 planned commands are shown disabled, with "Planned" in the tooltip; they are never shown as working.
- **Workflow:** select an end face → Face Bend (or Straight Extension) → drag the handle or type values → OK. The new free end face is selected automatically, so the next command starts at once. Then Flatten Tube → Export.
- **Face Bend PropertyManager** — groups as specified: *Selected geometry* (face box, detected OD, wall/ID, axis, what it continues); *Bend parameters* (input mode, CLR, angle, plane rotation, reverse, straight after, reference box); *Position control* (shown for XYZ and direction modes: global absolute / global relative / local; X, Y, Z; conflict text; "Use suggested radius"); *Options* (merge, tangent continuity (always on, read-only), show centreline, live preview); *Manufacturing information* (collapsed: developed bend length, total developed length, minimum radius from the library, warnings).
- **Feedback:** the preview (blue temporary body, red centreline) updates on every change; errors appear in the PropertyManager message box; OK is refused while the input is invalid.
- **Remembered preferences:** last profile, radius, angle, lengths, preview options, bend-table conventions (`%APPDATA%\TubeWorks\settings.xml`).
- No modal dialogs in the modelling workflow. The Bend Table and Export dialogs are output windows only.

## 6. Parametric feature persistence strategy

- Each TubeWorks feature is a macro feature with **one string parameter** `TubeWorksData` = `TubeFeatureData.Encode()`: `key=value;…`, invariant culture, round-trip number format, escaped separators. Keys: schema version, GUID Id, kind, parent Id, parent end, profile, placement, lengths, end condition, bend radius/angle/rotation/reverse, straight after, merge, material.
- **Schema version** `v`. Unknown keys are ignored (forward-compatible). Data with a newer version than the add-in is refused with a clear message instead of being misread.
- **Derived values are not stored** (positions, tangents, developed lengths). They are computed on every rebuild, so they cannot become stale. The bend table writes them to custom properties on request.
- **Links between TubeWorks features use feature Ids, not face ids.** This is the "do not depend on temporary face ids" rule. A face is stored (as a native, persistent macro-feature selection) only when a tube starts on a body that TubeWorks did not create.
- **User ids** on every face and edge of TubeWorks bodies (`SetFaceUserId`), so that other SOLIDWORKS features (holes, sketches, mates) keep their references through rebuilds. The free end face of each feature always has id (1, 0).
- **Copied features:** a duplicated Id is reported as an error, never silently accepted.

## 7. Implementation roadmap

| Phase | Content | Status |
|---|---|---|
| 1 (MVP) | Base Tube; existing-tube face recognition; Face Bend (radius/angle, plane rotation, reverse, straight after); XYZ end-point and direction solving with conflict handling; plane/axis alignment; Straight Extension (blind, up to point, up to plane); sequential bends; parametric editing and reference recovery; Flatten Tube (straight blank); STEP/Parasolid/SLDPRT export; bend table (CSV, XYZ, LRA); Tube Properties; basic Manufacturing Check | **Implemented in this repository, compile-verified, not yet tested in SOLIDWORKS** |
| 1b | Run `TEST-PLAN.md` in SOLIDWORKS; fix runtime issues; macro feature display dimensions; Multi Bend | Next |
| 2a | Individual unbend/rebend with transfer of cuts; hole and cut-out mapping; Unwrap Tube Surface; DXF with layers; library editor with import/export; validation of cuts in bends; setup program | **Implemented in 0.2.0, compile-verified, not yet tested in SOLIDWORKS** |
| 2b | Automatic drawings (views, bend table, A4–A1 templates); engraving detection; DWG decision; mapping of cuts inside bends (approximate, flagged) | Next |
| 3 | Machine postprocessors; springback and stretch compensation from calibration data; stock optimisation; nesting; cost; batch; reports | Planned |

## 8. Technical risks and limitations

**Risks (need confirmation in SOLIDWORKS):**

1. *API semantics not confirmed from documentation* (help site unreachable from the build environment). Each one is protected by a run-time check that refuses the result instead of producing wrong geometry:
   - `CreateBodyFromCyl` array layout: volume and centroid checked; mid-point semantics corrected automatically.
   - Toroidal surface parameterisation and `CreateSheetFromSurface` range order: parameter direction found with `ISurface.ReverseEvaluate`; both range orders tried; the exact Pappus volume decides.
   - `ThickenSheet` side: both sides tried; the volume decides.
   - `IBody2.GetMassProperties` layout (volume at index 3): if wrong, every body fails the check with a diagnostic message. `VerifyGeometry` in `settings.xml` can switch the check off for diagnosis only.
   - `ICurve.CircleParams` layout: checked against a point evaluated on the curve.
2. *Edit-body replacement by the flat pattern:* the flat pattern returns a new body in place of the bent body. If SOLIDWORKS keeps both, the flat state shows two bodies (test FP-1).
3. *Keeping the PropertyManager open on invalid input* uses a failing HRESULT from `OnClose`. If SOLIDWORKS closes the page anyway, `AfterClose` does not create invalid geometry, and the error is shown.
4. *Foreign-root anchor after reopening:* a feature that starts on a non-TubeWorks tube measures that face on each rebuild. If a downstream feature rebuilds alone directly after the file is opened, it uses the anchor stored at the last edit. Ctrl+Q (forced rebuild) always gives the live value.
5. *Performance:* each feature re-reads the tree on rebuild (linear in the number of features). Expected to be fine for tubes with tens of bends; not measured.

6. *Rebend needs Unbend data from this session:* cuts that were in bends before an Unbend are kept in memory by the Unbend feature. After reopening a file, rebuild all (Ctrl+Q) before a Rebend rebuilds on its own; otherwise the Rebend reports an error instead of losing those cuts.
7. *Boolean comparison of nominal and actual bodies:* cut detection subtracts bodies with coincident faces. Slivers below 0.01 mm³ are ignored. If the modeler gives noise above that, false "cuts" appear in the reports (test UB-1).
8. *Older-API build on newer SOLIDWORKS:* the setup program's add-in references the 2016 interop assemblies; loading in newer versions depends on SOLIDWORKS resolving them (test INS-1). Fallback: build against your own installation.
9. *Unsigned setup program:* SmartScreen warns until it is signed with your certificate.

**Limitations of release 0.2 (by design):**

- Circular hollow tube only. Square, rectangular and elliptical are reserved in the engine.
- Base Tube placement, reference alignment and "up to" targets are captured values, not associative links.
- Flat pattern needs a tube that starts with a Base Tube. Tubes that start on a foreign body show their TubeWorks part in the bend table with a note.
- Developed length is nominal centreline; no compensation.
- One tube per Flatten Tube command (the first path that starts with a Base Tube).
- Cuts in bend regions are not transferred by Unbend/Flatten/Unwrap (reported). Drawings: Phase 2b.
- Labels show values in mm, cm, m, in or ft according to the document; number boxes use the document units natively.
- Minimum SOLIDWORKS version not determined at run time. Compiles against the 2016 and 2026 APIs.

## Phase 2 design notes (release 0.2)

**Unbend.** For a path and a set of bends to straighten, with one fixed primitive, `TubeUnbender` walks away from the fixed primitive
in both directions. Crossing a straightened bend (forward) applies the rigid motion *rotate by −θ about the bend axis through the arc
end, then translate the end to start + T0·Rθ* (backward: the mirror motion). Every primitive gets the composed motion `BentToUnbent`;
a straightened arc becomes a straight of length R·θ. Tests check continuity, collinearity, total length and that the fixed segment does not move.

**Cut transfer.** cut material = nominal body − actual body. A piece is classified with its vertices and box corners: when all of them
lie in primitives that share one rigid motion (and none in a straightened bend), the piece is moved by that motion and subtracted from the
nominal unbent body. Rebend does the inverse with the inverse motion, and re-applies the bent-state cuts in bends that the Unbend kept back.
The result is exact for transferred pieces because the motion is rigid.

**Unwrap.** The straightened body's cylindrical face at the reference radius is developed with x = axial position, y = r_ref·angle
(angle from the seam). This is an isometry of the cylinder, so it is exact. Loops are made continuous in angle and split at multiples of 2π
(seam crossing). Loops that go once around the tube are end profiles: plain circles at the ends become straight end lines; circles inside
the length (face boundaries of collinear segments) are ignored; notched ends become profile polylines. Bend start, finish and centre
lines, an "inside of bend" mark at the bend direction angle, and a label (number, angle, radius, rotation) are added.

**DXF.** ASCII R12 (AC1009), LTYPE and LAYER tables, LINE/POLYLINE/TEXT, coordinates in mm (or inch) at 1:1. Layer names and
colours are configurable.
