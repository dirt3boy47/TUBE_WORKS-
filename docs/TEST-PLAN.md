# Test plan

## A. Automated (no SOLIDWORKS)

`dotnet test tests/TubeWorks.Geometry.Tests` — 51 tests, all pass in the build environment. Expected values are closed-form hand calculations, not engine output.

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
| Unbend: straight blank of developed length | `UnbendAll_GivesStraightTubeOfDevelopedLength` |
| Unbend one bend, rigid motion of straights | `UnbendOne_KeepsOtherBend_AndMovesStraightsRigidly`, `Unbend_FixedLastSegment_DoesNotMoveIt`, `RigidTransform_ComposeAndInverse` |
| Hole and cut-out mapping | `HoleTransfer_RoundTripIsExact_ForStraightRegions_AndRefusedInBends` |
| Surface development | `Unwrap_Width_AndExactMapping`, `Unwrap_HoleCrossingSeam_IsSplitIntoTwoPieces_AtBothEdges`, `Unwrap_EndCircle_IsRecognisedAsPlainEnd`, `Unwrap_InteriorFaceBoundaryCircle_IsIgnored_AndMergeCollinearJoinsStraights` |
| 1:1 DXF pattern | `Dxf_IsWellFormed_AndUsesLayersAndMillimetres`; the file is also checked independently with ezdxf (`tools/check_dxf.py`): AC1009, 0 audit errors, extents 410.8997 × 78.5398 mm = developed length × π·OD |
| Library | `Library_CsvAndXmlRoundTrip_AndChecks`, `Codec_UnbendFields` |
| Unbend with any fixed segment (bug in 0.2.1, fixed in 0.2.2) | `Unbend_AnyFixedSegment_IsContinuousAndKeepsLength` (fails on 0.2.1 for a fixed segment after a bend) |
| Approximate mapping of points in bends | `MapPoint_ExactInStraights_ContinuousAcrossBends_AndArcLengthInBends` |
| Tooling, stock, cut distance checks | `Validator_ToolingStockAndCutDistance` |
| Springback interpolation, compensation formula | `Springback_Interpolation`, `NoCalibration_GivesNominalValues_AndSaysSo`, `Compensation_CutLengthFormula` |
| Postprocessor templates | `Postprocessor_Template_OutputAndErrors` |
| Stock nesting | `Nesting_ValidAndUsesShortestFittingStock`, `Nesting_MatchesOptimum_OnSmallRandomCases` (200 random cases against the exact optimum, first-fit-decreasing bound) |
| Cost model | `Cost_OnlyDefinedItems` |
| Report | `Report_IsHtml_AndEscapesText` |
| DXF clean-up and true circles | `Pattern_Simplify_RemovesCollinearPoints_AndFindsTrueCircles_Only` |

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
| INS-1 | Run TubeWorksSetup-0.2.0.exe on a PC with SOLIDWORKS (note the version); then Settings > Apps > Uninstall | Tab appears (this also proves the 2016-API build loads in your version); uninstall removes the tab, the files and the Apps entry |
| UB-1 | Tube Base 100 + Bend 90° R50 + straight 100; Hole Wizard Ø6 hole through one wall in the last straight; Unbend all, fixed = Base Tube | Straight tube of 278.540 mm; hole at 228.540 mm from the start (100 + 78.540 + 50) when the hole was 50 mm into the last straight |
| UB-2 | After UB-1 add an extruded cut slot in the straightened tube, then Rebend | Bent tube with the original hole and the new slot at the corresponding position on the last straight |
| UB-3 | Hole in the bend region, then Unbend | Report lists the cut "in Bend 01 (bend region)"; after Rebend the hole is back unchanged |
| UB-4 | Unbend only Bend 02 of a 3-bend tube, fixed = last straight | Bend 01 and Bend 03 unchanged in shape; last straight does not move |
| FP-3 | Flatten with the hole of UB-1 | Blank shows the hole at the same developed position |
| UW-1 | Unwrap, outside surface, seam 0°, mm; open the DXF in a DXF viewer | Rectangle 278.540 × 78.540 mm (OD 25); hole outline on INTERNAL_CUTOUTS; bend start/finish lines at 100 and 178.540 mm on BEND_LINES; label text |
| UW-2 | Hole centred on the seam (choose the seam angle through the hole) | Hole outline split into two parts at the two long edges |
| LIB-1 | Tube Library: add a preset, save, close, reopen; export CSV; import it into an empty library | Values identical; invalid wall (≥ OD/2) refused on save |
| MC-3 | Manufacturing Check with a hole in a bend | Warning "Feature in a bend region …" |

| FB-9 | (crash in 0.2.1) Base Tube → Face Bend OK → Face Bend again on the new end face; change values in all boxes | No crash; preview follows |
| UB-5 | Unbend with the LAST straight fixed (0.2.1 bug) | Bent part unchanged; the straightened part is continuous and its total length equals the developed length |
| ENG-1 | Shallow extruded cut (0.3 mm deep text or mark) on a straight, then Unwrap | Outline on ENGRAVING layer, not INTERNAL_CUTOUTS |
| UW-3 | Hole in a bend, then Unwrap | Outline present, listed as APPROXIMATE |
| FP-4 | (experimental, off by default; set MarkBendLinesOnBlank=true) Flatten a two-bend tube | Lines (split edges) on the blank at the bend start and finish positions of the bend table |
| TL-1 | Library preset with radii 38.1 and 50.8; Face Bend on that tube | Radius list offers both; choosing one sets the CLR; a radius not in the list gives a NO_TOOLING warning in Manufacturing Check |
| DRW-1 | (crashed in 0.3.0; fixed in 0.4.0; layout fixed in 0.4.1) Save the part; Tube Drawing with your template, sheet size From the template | Drawing with 2 sheets: 4 views + centreline + bend table + notes; sheet 2 straightened view (TW Straight) + developed surface |
| DWG-1 | Unwrap → Export DWG… | DWG opens in a DWG viewer at 1:1 mm with the TubeWorks layer names |
| CNC-1 | Library preset with springback `92:90`, trim start 5 mm; CNC Output, Generic LRA | Bend 1 commanded 92.00 for a 90° bend; first feed +5 mm; notes list what was applied |
| CNC-2 | Copy `docs/postprocessors/example-ybc.twpost` to `%APPDATA%\TubeWorks\post`, edit it, select it | Output follows the template; an unknown placeholder shows an error |
| REP-1 | Production Settings: hourly rate, times; library stock 6000 mm with price; Manufacturing Report, quantity 20 | HTML opens; cost lines only for defined items; nest uses 6000 mm bars |
| BAT-1 | Folder with 3 TubeWorks parts and 1 non-tube part; Batch & Nesting with all outputs | Output folder with CNC, DXF, STEP, CSV per tube part; the non-tube part listed as not processed; one combined report with a nest per tube size |
| DRW-2 | Sheet 2 of DRW-1 | Bending layout bar as long as the cut length; marks at the bend starts (for the screenshot tube: 50.0 and 128.5 mm); labels with rotate and bend angle; sequence table; the centreline sketch on sheet 1 is at its label, not shrunk; tables and notes in the default colour |
| RB-2 | Unbend with a hole in a bend; save, close, reopen; edit a cut between Unbend and Rebend | Rebend rebuilds without the "Ctrl+Q" error (when SOLIDWORKS provides the Unbend input body) |
