# User guide (release 0.1)

## Basic workflow

Select face → Face Bend → drag or type → OK → (the new end face is selected) → Face Bend or Straight Extension → … → Flatten Tube → Export.

## Commands

| Command | What it does |
|---|---|
| **Base Tube** | Straight circular hollow tube. Enter OD and either wall thickness or ID (the other value is calculated), length, optional material and preset. Placement: start X/Y/Z and axis (±X, ±Y, ±Z, or the normal of a selected plane or planar face). |
| **Face Bend** | Select the flat end face of any circular hollow tube (TubeWorks or not). The add-in detects OD, ID, wall, centre and axis. |
| **Straight Extension** | Extends a tube end: blind length (drag the arrow), up to a point/vertex, or up to a plane/planar face. |
| **Edit Bend** | Select a TubeWorks feature (tree or face) to edit it. Double-click / Edit Feature in the tree does the same. |
| **Reverse Bend** | Bends the selected bend to the opposite side. |
| **Set Bend Plane** | Opens the selected bend with the reference box active: select a plane or planar face (bend lies in that plane) or a straight edge or axis (tube end points along it). |
| **Flatten Tube** | First use: creates **Tube Flat Pattern** at the end of the tree (straight blank of the developed length). Later uses: switch between bent and straight. New bends switch back to the bent state automatically. |
| **Export Flat Pattern** | Bent or straight tube to STEP, Parasolid or a SOLIDWORKS part copy (native translators, 1:1); centreline XYZ to CSV. |
| **Bend Table** | Bend number, feed, CLR, angle, rotation, direction, start/finish XYZ, developed bend, cumulative length. Choose mm/in, rotation sign, bend/included angle. Export full CSV, XYZ or LRA. Write custom properties. |
| **Tube Properties** | OD, ID, wall, material, number of bends, developed (required stock) length. |
| **Manufacturing Check** | Errors and warnings (see below). |
| Multi Bend, Tube Trim, Tube Notch, Unbend, Rebend, Unwrap Tube Surface | **Not available in this release** (shown disabled). |

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

## Editing upstream and recovering references

Change any feature: everything downstream rebuilds from the stored parameters. If a feature reports that its parent is missing, edit it and select a tube end face above it in the tree. A tube cannot branch: two features cannot continue the same end.
