# User guide (release 0.3)

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
| **Tube Drawing** | Creates a drawing (A4, A3, A2 or A1; first or third angle) from your drawing template. Sheet 1: front, top, right and isometric views, a centreline sketch with bend numbers, the bend table and notes (OD, wall, ID, material, required stock length, envelope, scale). Sheet 2: the straightened tube (configuration **TW Straight**, added to the part) and the developed surface. The part must be saved. Dimensions are given as table and notes, not as driven dimensions; add dimensions you need by hand. |
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
- Flatten Tube marks the bend start and finish lines on the blank's outside face.

After Ctrl+Q or reopening, run Ctrl+Q once before you edit a Rebend, so that the Unbend data is available.

## Editing upstream and recovering references

Change any feature: everything downstream rebuilds from the stored parameters. If a feature reports that its parent is missing, edit it and select a tube end face above it in the tree. A tube cannot branch: two features cannot continue the same end.
