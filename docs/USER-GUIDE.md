# User guide (release 0.4)

## Basic workflow

Select face → Face Bend → drag or type → OK → (the new end face is selected) → Face Bend or Straight Extension → … → Flatten Tube → Export.

## Commands

| Command | What it does |
|---|---|
| **Base Tube** | Straight circular hollow tube. Enter OD and either wall thickness or ID (the other value is calculated), length, optional material and preset. Placement: start X/Y/Z and axis (±X, ±Y, ±Z, or the normal of a selected plane or planar face). |
| **Face Bend** | Select the flat end face of any circular hollow tube (TubeWorks or not). The add-in detects OD, ID, wall, centre and axis. If the tube library has a preset for this tube, the tooling radii are offered in a list. |
| **Straight Extension** | Extends a tube end: blind length (drag the arrow), up to a point/vertex, or up to a plane/planar face. |
| **Edit Bend** | Select a TubeWorks feature (tree or face) to edit it. Double-click / Edit Feature in the tree does the same. |
| **Reverse Bend** | Bends the selected bend to the opposite side. |
| **Set Bend Plane** | Opens the selected bend with the reference box active: select a plane or planar face (bend lies in that plane) or a straight edge or axis (tube end points along it). |
| **Unbend** | Straightens one, several or all bends about a fixed straight segment that you choose. Each bend keeps its developed length (R × angle). Add holes, slots and cut-outs after it, then use Rebend. |
| **Rebend** | Restores the bends of the active Unbend. Cuts made while straight go back to the bent tube. |
| **Flatten Tube** | First use: creates **Tube Flat Pattern** at the end of the tree (straight blank of the developed length, with the cuts of the straight segments). Later uses: switch between bent and straight. New bends switch back to the bent state automatically. |
| **Unwrap Tube Surface** | Export DXF (written by TubeWorks) or DWG (drawn on a 1:1 drawing sheet and saved by SOLIDWORKS, in mm). Develops the outside, inside or neutral surface of the straightened tube into a rectangle (width = π × reference diameter) and writes a 1:1 DXF (mm or inch) with layers for the outer profile, cut-outs, bend lines, centreline, engraving, construction and text. Choose the seam angle. The model is not changed. |
| **Tube Drawing** | Choose a drawing template (list of the `.drwdot` files in your SOLIDWORKS template folders, or Browse; the last one is remembered). Sheet size *From the template* keeps the template's size, border and title block; A4–A1 use the standard SOLIDWORKS sheet format of that size. Sheet 1: front, top, right and isometric views, centreline sketch, bend table, notes. Sheet 2: straightened tube (configuration **TW Straight**), the **bending layout of the straight bar** (marks from the bar start at every bend start, bend centre marks, rotate and bend angle per bend, cut length) with a manual bending sequence table (step, mark at, bend centre at, rotate, bend, command angle, CLR), and the developed surface. The part must be saved. |
| **Tube Library** | Edit presets (OD, wall, material, stock lengths, tooling radii, limits, calibration values). Import and export CSV or XML. |
| **Export Flat Pattern** | Bent or straight tube to STEP, Parasolid or a SOLIDWORKS part copy (native translators, 1:1); centreline XYZ to CSV. |
| **Bend Table** | Bend number, feed, CLR, angle, rotation, direction, start/finish XYZ, developed bend, cumulative length. Choose mm/in, rotation sign, bend/included angle. Export full CSV, XYZ or LRA. Write custom properties. |
| **Tube Properties** | OD, ID, wall, material, number of bends, developed (required stock) length. |
| **Manufacturing Check** | Errors and warnings (see below). |
| Multi Bend, Tube Trim, Tube Notch | **Not available in this release** (shown disabled). |

## Face Bend input modes

- **Bend radius and angle**: CLR, angle, plane rotation, reverse, straight after. Drag the orange arrow at the bend end to set the straight length.
- **XYZ end point**: type X, Y, Z (global absolute, global relative to the end face, or local with Z along the tube axis) or drag the triad. The add-in solves angle, plane and straight length for the CLR. If one bend cannot reach the point, the message says why and offers a valid option (smaller radius with a **Use suggested radius** button, or a second bend).
- **End direction vector**: the tube end points along (X, Y, Z).
- **Bend plane from reference**: select a plane, planar face, straight edge or axis. The value is captured once (not associative).

**Plane rotation 0°** bends toward global +Y (or toward global +Z when the tube is vertical). Positive rotation turns the bend direction right-handed about the tube direction.

## Manufacturing information and checks

- *Developed bend length* (bend allowance) = CLR × angle in radians.
- *Total developed length* = Σ straights + Σ developed bends, nominal centreline. No stretch or springback compensation is applied.
- *Minimum recommended radius* and other limits come only from `%APPDATA%\TubeWorks\library.xml`. If no limit is defined, the add-in says "not defined" and does not check it.
- Errors: CLR not greater than OD/2, self-intersection (sampled), geometry errors. Warnings: radius below minimum, angle above the machine limit (180° unless changed), no or short straight between bends, clamping length.

## How cuts are transferred (Unbend, Rebend, Flatten, Unwrap)

TubeWorks compares the actual tube with the nominal tube made from the feature parameters. The difference is the cut material.

- A cut that lies completely in one straight segment (or in parts that do not move relative to each other) is moved with the exact
  rigid motion of that segment. The transfer is **exact**.
- A cut that touches a bend, or spans a bend, has no exact rigid motion. TubeWorks **does not move it** and lists it in a report
  (position along the tube and the bend). Rebend restores such cuts unchanged if they were made in the bent state.
- Material added to the tube (bosses, welds) is not transferred and is reported.
- The unwrapped surface is exact for cuts that were transferred exactly (a cylinder is developable). The neutral-surface option places
  outlines of the outside surface on the mean diameter by angle; this is exact for radial cuts only and is reported as approximate.
- Engraving: a cut that does not reach the bore (marking, engraving, shallow pocket) goes to the ENGRAVING layer; through cuts go to INTERNAL_CUTOUTS.
- Cuts in a bend appear in the unwrapped pattern as **approximate** outlines, placed with the centreline bending model (each one is listed as APPROXIMATE). They are not moved by Unbend or Flatten.
- Round holes drilled radially through a tube do not develop to circles (they are longer around the tube). The DXF writes true circles only where the outline really is a circle; other outlines are polylines with collinear points removed.
- Bend lines on the 3D blank (Flatten Tube) are experimental and **off** since 0.4.0, because SOLIDWORKS 2026 SP4 closed during that step. Set `MarkBendLinesOnBlank` to `true` in `%APPDATA%\TubeWorks\settings.xml` only for testing. Bend lines are always in the DXF, DWG and drawing.

After Ctrl+Q or reopening, run Ctrl+Q once before you edit a Rebend, so that the Unbend data is available.

## Editing upstream and recovering references

Change any feature: everything downstream rebuilds from the stored parameters. If a feature reports that its parent is missing, edit it and select a tube end face above it in the tree. A tube cannot branch: two features cannot continue the same end.

## Manufacturing (Phase 3)

TubeWorks applies **only data that you enter**. When a value is missing, the output says "not defined" and leaves that item out.

**Where the data lives**
- *Tube Library* (per tube and tooling): stock lengths and prices, tooling radii, limits, and calibration:
  springback points (`commanded:achieved` degrees, for example `92:90;46.5:45`), stretch per bend and per degree,
  neutral-axis shift, angle offset, trim allowances, price per metre.
- *Production Settings* (from the CNC, Report or Batch windows): hourly rate, set-up hours, times per part
  (cut, load/unload, per bend, per rotation, feed per metre), extra cost per part, saw kerf and unusable bar ends.

**Compensation model** (check it against a test part on your machine):
- commanded angle = the measured springback curve read backwards at the design angle, interpolated linearly
  between points (one point: commanded/achieved ratio), plus the angle offset;
- developed bend = (CLR + neutral-axis shift) × design angle;
- cut length = start trim + straights + developed bends − stretch + end trim.

| Command | What it does |
|---|---|
| **CNC Output** | Shows and saves the bend data in a machine format made by a postprocessor template. Two generic templates are built in; your own `*.twpost` files go in `%APPDATA%\TubeWorks\post` (format below). |
| **Manufacturing Report** | HTML report for the active part and a quantity: tube data, compensated bend table, notes, checks, cost, and nesting of the quantity into stock bars. Saved next to the part and opened in the browser (print to PDF from there). |
| **Batch & Nesting** | Choose a folder of parts and quantities. For each part: CNC file, unwrapped DXF, STEP and bend-table CSV (as ticked). Then parts of the same tube size are nested into stock bars (saw kerf and bar ends included) and one report is written to `TubeWorks output`. |

**Postprocessor template format** (`*.twpost`, plain text). Settings first, then sections:

```
name=My bender
extension=.nc
decimals=2
unit=mm            # or inch
rotation=right     # or left: sign convention of R
rotationrange=0-360   # optional: R from 0 to 360 instead of -180..180
angle=bend         # or included
[header]
; {PART} {MATERIAL} {OD} {WALL} {CUTLENGTH} {NOMINALLENGTH} {BENDS} {DATE}
[bend]
; per bend: {N} {L} {R} {A} {A0} {CLR} {X} {Y} {Z}
[footer]
; {LAST} = final feed, {CUTLENGTH}
```

An unknown placeholder or setting stops with an error, so a typing error never gives a wrong file. The templates are generic:
take the format of your controller from its documentation and prove it with a test part.

**Nesting** uses longest-part-first with best fit, then moves each bar to the shortest stock length that still holds its parts.
The result is always valid (no bar over-full, every part placed) but not proven optimal; the report shows the utilisation.
