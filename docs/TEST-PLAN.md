# Test plan

## A. Automated (no SOLIDWORKS)

`dotnet test tests/TubeWorks.Geometry.Tests` — 24 tests, all pass in the build environment. Expected values are closed-form hand calculations, not engine output.

| Acceptance case | Test |
|---|---|
| Straight circular tube | `StraightTube_DevelopedLengthEqualsLength` |
| Single 90° bend | `Single90DegreeBend_EndPointTangentAndLength`: end (150, 50, 0) mm, tangent +Y, length 100 + 50·π/2 + 200 mm |
| Single 45° bend | `Single45DegreeBend` |
| Two bends, same plane | `TwoBendsSamePlane_CoplanarBack_RotationZero` (U: rotation 0°, S: 180°) |
| Bends in different planes | `TwoBends_DifferentPlanes_Rotation90` (rotation +90°) |
| Compound 3D tube | `Compound3DTube_EndPointMatchesIndependentStepByStepCalculation` (Rodrigues rotation, independent of the engine formulas) |
| Editing an upstream bend | `EditingUpstreamBend_MovesDownstreamFeatures` |
| Diameter and wall preserved | `CrossSectionPropagatesToAllSegments` |
| Invalid geometry, reference recovery | `MissingParent_SuppressedParent_Branching_AreReported`, `RadiusNotGreaterThanHalfOd_IsAnError` |
| Constrained solver | `EndpointSolver_RoundTrip` (500 random cases), `EndpointSolver_InsideBendCircle_OffersMaxRadius`, `EndpointSolver_OnAxis_SuggestsStraight`, `DirectionSolver`, `PlaneRotationForPlane_PutsBendInPlane` |
| Persistence format | `Codec_RoundTrip_AndUnknownKeysIgnored` |
| Validation | `Validator_SelfIntersection_Detected`, `Validator_TightButValidUBend_NotReported`, `Validator_UsesOnlyDefinedLimits` |
| Bend table output | `BendTable_Csv_UsesInvariantCultureAndConventions` |

Also automated: the add-in compiles against the SOLIDWORKS 2026 interop (CI workflow).

## B. Manual, in SOLIDWORKS (not yet run)

Use a new part in mm. Record pass/fail and the SOLIDWORKS version. On any failure attach `%APPDATA%\TubeWorks\tubeworks.log`.

| Id | Steps | Expected |
|---|---|---|
| IN-1 | Install, start SOLIDWORKS, open a part | Tools > Add-Ins lists TubeWorks Designer; Tube Design tab with 17 buttons; 6 planned ones disabled |
| BT-1 | Base Tube OD 25, wall 2, length 100, axis +Z | "Base Tube" in tree; Evaluate > Mass Properties volume = π(12.5² − 10.5²)·100 = 14 451.3 mm³ |
| BT-2 | Edit BT-1: switch to inside diameter, ID 20 | Wall shown as 2.5; volume = π(12.5² − 10²)·100 = 17 671.5 mm³ |
| FB-1 | Select the free end face, Face Bend, CLR 50, 90°, rotation 0, straight after 0 | Preview, then "Bend 01". Bend end centre at (0, 50, 150) — tube along +Z bends toward +Y. Volume of the bend part = 50·π/2 · π(12.5² − 10.5²) = 11 350.0 mm³ |
| FB-2 | After OK | The new end face is already selected; Face Bend starts at once |
| FB-3 | 45° bend | End point per closed form |
| FB-4 | Two bends in the same plane (U) and in different planes | Bend Table rotation 0° / 180° / ±90° as expected |
| FB-5 | XYZ mode, target inside the bend circle | Conflict message, "Use suggested radius" works, no invalid body |
| FB-6 | CLR = 12 with OD 25 | OK refused with the CLR > OD/2 message |
| FB-7 | Drag the arrow and the triad | Values and preview follow the drag |
| FB-8 | Face Bend on a tube made with standard Extrude (ring sketch) | OD/wall detected; root feature keeps the face; edit the extrude length → the bend follows |
| ED-1 | Edit Bend 01 angle 90 → 60 with Bend 02 after it | Bend 02 moves; no rebuild errors |
| ED-2 | Suppress Bend 01 | Bend 02 shows "parent feature is suppressed" error; unsuppress → OK |
| ED-3 | Delete Bend 01 | Bend 02 error "parent is missing"; Edit Bend 02, select a valid end face → recovers |
| ED-4 | Undo / redo after FB-1 and ED-1 | Native undo restores the previous state |
| SV-1 | Save, close, reopen, Ctrl+Q | Same geometry, no errors, features editable |
| SV-2 | Hole Wizard hole on a straight segment, then edit an upstream bend | Hole stays on its face (user ids) |
| FP-1 | Flatten Tube | One straight body of the developed length (check with Measure). Run again → bent state |
| FP-2 | Face Bend while flat | Switches to the bent state first |
| EX-1 | Export STEP bent and straight | Files open in another CAD system at 1:1 |
| TB-1 | Bend Table, export CSV/XYZ/LRA | Values match FB tests; CSV opens in Excel |
| MC-1 | Manufacturing Check with library-example.xml installed and CLR 30 | Minimum radius warning |
| MC-2 | Tube that crosses itself (bend 270° then long straight) | Self-intersection error |
| NEG-1 | Face Bend on a solid bar, a square tube, a non-planar face | Clear refusal message, no feature |

Not covered in release 0.1 (planned): tube with holes and notches through unbend/rebend; 1:1 DXF pattern.
