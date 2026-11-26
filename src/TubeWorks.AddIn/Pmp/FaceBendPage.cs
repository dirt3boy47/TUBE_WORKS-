using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Pmp
{
    /// <summary>Face Bend PropertyManager: a bend from the selected tube end face, with live preview and graphics handles.</summary>
    public sealed class FaceBendPage : SegmentPage
    {
        private enum InputMode { RadiusAngle = 0, Endpoint = 1, Direction = 2, Reference = 3 }
        private enum CoordMode { GlobalAbsolute = 0, GlobalRelative = 1, Local = 2 }

        private const int RefMark = 2;
        private const int IdFace = 10, IdOd = 11, IdWall = 12, IdAxis = 13, IdAttach = 14;
        private const int IdTooling = 29, IdMode = 20, IdRadius = 21, IdAngle = 22, IdRot = 23, IdReverse = 24, IdAfter = 25, IdRef = 26, IdRefLabel = 27;
        private const int IdCoord = 30, IdX = 31, IdY = 32, IdZ = 33, IdApply = 34, IdStatus = 35;
        private const int IdMerge = 40, IdTangent = 41, IdCentre = 42, IdLive = 43;
        private const int IdBendLen = 50, IdTotal = 51, IdMinR = 52, IdWarn = 53;

        private IPropertyManagerPageSelectionbox _face, _ref;
        private IPropertyManagerPageLabel _od, _wall, _axis, _attach, _status, _bendLen, _total, _minR, _warn, _refLabel;
        private IPropertyManagerPageCombobox _mode, _coord, _tooling;
        private List<double> _toolRadii = new List<double>();
        private TubeProfile _toolingFor;
        private IPropertyManagerPageNumberbox _radius, _angle, _rot, _after, _x, _y, _z;
        private IPropertyManagerPageCheckbox _reverse, _merge, _tangent, _centre, _live;
        private IPropertyManagerPageButton _apply;
        private IPropertyManagerPageGroup _posGroup;
        private double? _suggestedRadius;
        private readonly bool _focusReference;

        private FaceBendPage(ISldWorks app, IModelDoc2 model, TubeFeatureData data, IFeature f, IMacroFeatureData md, bool focusReference)
            : base(app, model, data, f, md) { _focusReference = focusReference; }

        public static void Create(ISldWorks app, IModelDoc2 model, IFace2 preselected)
        {
            var s = UserSettings.Current;
            var data = new TubeFeatureData
            {
                Kind = TubeFeatureKind.Bend, BendRadius = s.LastBendRadius, BendAngle = s.LastBendAngle, StraightAfter = s.LastStraightAfter,
            };
            var page = new FaceBendPage(app, model, data, null, null, false);
            page.Build("Face Bend");
            page.ShowPage();
            if (preselected != null)
            {
                string err = page.ResolveFromFace(preselected);
                page.Selectface(preselected);
                if (err != null) { page.Message(err, true); page.Start = null; }
            }
            page.Refresh();
        }

        public static void Edit(ISldWorks app, IModelDoc2 model, IFeature f, IMacroFeatureData md, TubeFeatureData data, bool focusReference = false)
        {
            var page = new FaceBendPage(app, model, data, f, md, focusReference);
            page.ResolveForEdit();
            page.Build("Edit " + f.Name);
            page.ShowPage();
            if (page.ForeignFace != null) page.Selectface(page.ForeignFace);
            if (focusReference) { page._mode.CurrentSelection = (short)InputMode.Reference; page._ref.SetSelectionFocus(); }
            page.Refresh();
        }

        private void Selectface(IFace2 face)
        {
            var sm = (ISelectionMgr)Model.SelectionManager;
            var sd = sm.CreateSelectData();
            sd.Mark = FaceMark;
            ((IEntity)face).Select4(true, sd);
        }

        private void Build(string title)
        {
            if (!CreatePage(title)) throw new InvalidOperationException("Could not create the PropertyManager page.");
            var s = UserSettings.Current;

            var g1 = Group(1, "Selected geometry");
            _face = Selection(g1, IdFace, "Tube end face", FaceMark, new[] { swSelectType_e.swSelFACES }, true);
            _od = Label(g1, IdOd, "Outside diameter: -");
            _wall = Label(g1, IdWall, "Wall thickness: -");
            _axis = Label(g1, IdAxis, "Tube axis: -");
            _attach = Label(g1, IdAttach, "");

            var g2 = Group(2, "Bend parameters");
            _mode = Combo(g2, IdMode, "How to define the bend",
                new[] { "Bend radius and angle", "XYZ end point", "End direction vector", "Bend plane from reference" }, 0);
            _radius = Number(g2, IdRadius, "Centreline bend radius", "Centreline bend radius (CLR)", swNumberboxUnitType_e.swNumberBox_Length, 1e-5, 100, Data.BendRadius, 0.001);
            _tooling = Combo(g2, IdTooling, "Tooling radius from the tube library (select to use it)", new[] { "(no tooling radii in the library)" }, 0);
            _angle = Number(g2, IdAngle, "Bend angle", "Bend angle", swNumberboxUnitType_e.swNumberBox_Angle, Angles.ToRad(0.01), Angles.ToRad(359.99), Data.BendAngle, Angles.ToRad(1));
            _rot = Number(g2, IdRot, "Bend plane rotation", "Rotation of the bend plane about the tube axis, from the reference direction", swNumberboxUnitType_e.swNumberBox_Angle, -2 * Math.PI, 2 * Math.PI, Data.PlaneRotation, Angles.ToRad(5));
            _reverse = Check(g2, IdReverse, "Reverse bend direction", "Bend to the opposite side", Data.ReverseDirection);
            _after = Number(g2, IdAfter, "Straight after bend", "Straight extension after the bend", swNumberboxUnitType_e.swNumberBox_Length, 0, 100, Data.StraightAfter, 0.001);
            _refLabel = Label(g2, IdRefLabel, "Reference: plane / planar face (bend plane) or edge / axis (end direction)");
            _ref = Selection(g2, IdRef, "Reference geometry", RefMark,
                new[] { swSelectType_e.swSelDATUMPLANES, swSelectType_e.swSelFACES, swSelectType_e.swSelEDGES, swSelectType_e.swSelDATUMAXES }, true);

            _posGroup = Group(3, "Position control");
            _coord = Combo(_posGroup, IdCoord, "Coordinate system of X, Y, Z",
                new[] { "Global, absolute", "Global, relative to end face", "Local (Z = tube axis), relative" }, 1);
            _x = Number(_posGroup, IdX, "X", "X", swNumberboxUnitType_e.swNumberBox_Length, -100, 100, 0, 0.001);
            _y = Number(_posGroup, IdY, "Y", "Y", swNumberboxUnitType_e.swNumberBox_Length, -100, 100, 0, 0.001);
            _z = Number(_posGroup, IdZ, "Z", "Z", swNumberboxUnitType_e.swNumberBox_Length, -100, 100, 0, 0.001);
            _status = Label(_posGroup, IdStatus, "");
            _apply = Add<IPropertyManagerPageButton>(_posGroup, IdApply, swPropertyManagerPageControlType_e.swControlType_Button, "Use suggested radius", "Apply the radius that reaches the point");

            var g4 = Group(4, "Options");
            _merge = Check(g4, IdMerge, "Merge with existing tube", "Merge the bend into the selected tube body", Data.Merge);
            _tangent = Check(g4, IdTangent, "Maintain tangent continuity (always on)", "The bend always starts tangent to the tube", true);
            Enable(_tangent, false);
            _centre = Check(g4, IdCentre, "Show centreline", "Show the tube centreline in the preview", s.ShowCentreline);
            _live = Check(g4, IdLive, "Live preview", "Show the bend while you change values", s.LivePreview);

            var g5 = Group(5, "Manufacturing information", expanded: false);
            _bendLen = Label(g5, IdBendLen, "Developed bend length: -");
            _total = Label(g5, IdTotal, "Total developed length: -");
            _minR = Label(g5, IdMinR, "Minimum recommended radius: -");
            _warn = Label(g5, IdWarn, "");

            if (IsEdit) Enable(_merge, false); // merging is fixed when the feature is created
        }

        // ------------------------------------------------------------------ events

        protected override bool IsFaceBox(int id) => id == IdFace;

        protected override void OnChanged(int id)
        {
            var s = UserSettings.Current;
            switch (id)
            {
                case IdFace:
                    var face = Selected(FaceMark).OfType<IFace2>().FirstOrDefault();
                    if (face == null) { if (!IsEdit) Start = null; break; }
                    string err = ResolveFromFace(face);
                    if (err != null) { Message(err, true); Start = null; }
                    break;
                case IdRadius: Data.BendRadius = _radius.Value; break;
                case IdTooling:
                    int t = _tooling.CurrentSelection - 1;
                    if (t >= 0 && t < _toolRadii.Count) { Data.BendRadius = _toolRadii[t]; Building = true; _radius.Value = Data.BendRadius; Building = false; }
                    break;
                case IdAngle: Data.BendAngle = _angle.Value; break;
                case IdRot: Data.PlaneRotation = _rot.Value; break;
                case IdReverse: Data.ReverseDirection = _reverse.Checked; break;
                case IdAfter: Data.StraightAfter = _after.Value; break;
                case IdMerge: Data.Merge = _merge.Checked; break;
                case IdLive: s.LivePreview = _live.Checked; break;
                case IdCentre: s.ShowCentreline = _centre.Checked; break;
                case IdApply:
                    if (_suggestedRadius.HasValue) { Data.BendRadius = _suggestedRadius.Value; _radius.Value = Data.BendRadius; }
                    break;
                case IdRef: ApplyReference(); break;
                case IdCoord:
                case IdMode: SetXyzFromCurrent(); break;
            }
            if (id == IdX || id == IdY || id == IdZ || id == IdRadius || id == IdApply || id == IdMode) SolveFromTarget();
            Refresh();
        }

        public override void OnButtonPress(int Id) => OnChanged(Id);

        private InputMode Mode => (InputMode)Math.Max(0, (int)_mode.CurrentSelection);
        private CoordMode Coord => (CoordMode)Math.Max(0, (int)_coord.CurrentSelection);

        /// <summary>The X, Y, Z boxes, converted to a global point (end point mode) or a global direction (direction mode).</summary>
        private Vec3 InputVector(bool isDirection)
        {
            var v = new Vec3(_x.Value, _y.Value, _z.Value);
            if (Start == null) return v;
            var st = Start.Value;
            switch (Coord)
            {
                case CoordMode.Local:
                    var g = st.ReferenceX * v.X + st.ReferenceY * v.Y + st.Tangent * v.Z;
                    return isDirection ? g : st.Origin + g;
                case CoordMode.GlobalRelative:
                    return isDirection ? v : st.Origin + v;
                default:
                    return v;
            }
        }

        private void ShowVector(Vec3 global, bool isDirection)
        {
            if (Start == null) return;
            var st = Start.Value;
            Vec3 v = global;
            if (!isDirection && Coord != CoordMode.GlobalAbsolute) v = global - st.Origin;
            if (Coord == CoordMode.Local) v = new Vec3(v.Dot(st.ReferenceX), v.Dot(st.ReferenceY), v.Dot(st.Tangent));
            Building = true;
            _x.Value = v.X; _y.Value = v.Y; _z.Value = v.Z;
            Building = false;
        }

        /// <summary>Fill X, Y, Z from the current bend (so that switching modes keeps the geometry).</summary>
        private void SetXyzFromCurrent()
        {
            if (Start == null || TubeChainSolver.ValidateBendInput(Data, Profile) != null) return;
            var prims = BendMath.BuildBend(Start.Value, Data.BendRadius, Data.BendAngle, Data.PlaneRotation, Data.ReverseDirection, Data.StraightAfter);
            if (Mode == InputMode.Direction) ShowVector(prims.Last().EndTangent, true);
            else ShowVector(prims.Last().EndPoint, false);
        }

        private void SolveFromTarget()
        {
            _suggestedRadius = null;
            _status.Caption = "";
            Show(_apply, false);
            if (Start == null) return;
            BendSolution sol;
            if (Mode == InputMode.Endpoint)
                sol = BendSolver.SolveForEndpoint(Start.Value, Data.BendRadius, InputVector(false));
            else if (Mode == InputMode.Direction)
                sol = BendSolver.SolveForDirection(Start.Value, Data.BendRadius, InputVector(true), Data.PlaneRotation, Data.StraightAfter);
            else return;

            if (!sol.Success)
            {
                _status.Caption = sol.Conflict;
                Message(sol.Conflict + "\n" + string.Join("\n", sol.Alternatives.Select(a => "• " + a.Description)), true);
                var r = sol.Alternatives.FirstOrDefault(a => a.Radius.HasValue);
                if (r != null && Profile != null && r.Radius.Value > Profile.OuterRadius) { _suggestedRadius = r.Radius; Show(_apply, true); }
                return;
            }
            Data.BendAngle = sol.Angle;
            Data.PlaneRotation = sol.PlaneRotation;
            Data.ReverseDirection = false;
            if (Mode == InputMode.Endpoint) Data.StraightAfter = sol.StraightAfter;
            Building = true;
            _angle.Value = Data.BendAngle; _rot.Value = Data.PlaneRotation; _reverse.Checked = false; _after.Value = Data.StraightAfter;
            Building = false;
            _status.Caption = "Solved: one bend" + (Data.StraightAfter > 0 ? " and a straight." : ".");
        }

        /// <summary>Bend plane or end direction from a selected plane, planar face, linear edge or axis (one-time alignment, not associative).</summary>
        private void ApplyReference()
        {
            if (Start == null) return;
            var obj = Selected(RefMark).FirstOrDefault();
            if (obj == null) return;
            Vec3? normal = null, dir = null;
            if (RefGeometry.TryPlane(obj, out Vec3 _, out Vec3 n)) normal = n;
            else if (RefGeometry.TryDirection(obj, out Vec3 d)) dir = d;

            if (normal.HasValue)
            {
                var r = BendSolver.PlaneRotationForPlane(Start.Value, normal.Value, Data.PlaneRotation);
                if (r == null) { Message("The tube axis does not lie in the selected plane, so the bend cannot be in that plane.", true); return; }
                Data.PlaneRotation = r.Value; Data.ReverseDirection = false;
                Building = true; _rot.Value = r.Value; _reverse.Checked = false; Building = false;
            }
            else if (dir.HasValue)
            {
                var sol = BendSolver.SolveForDirection(Start.Value, Data.BendRadius, dir.Value, Data.PlaneRotation, Data.StraightAfter);
                if (!sol.Success) { Message(sol.Conflict, true); return; }
                Data.BendAngle = sol.Angle; Data.PlaneRotation = sol.PlaneRotation; Data.ReverseDirection = false;
                Building = true; _angle.Value = sol.Angle; _rot.Value = sol.PlaneRotation; _reverse.Checked = false; Building = false;
            }
            else Message("Select a plane, a planar face, a straight edge or an axis.", true);
        }

        // ------------------------------------------------------------------ display

        private void Refresh()
        {
            bool xyz = Mode == InputMode.Endpoint || Mode == InputMode.Direction;
            _posGroup.Visible = xyz;
            Show(_ref, Mode == InputMode.Reference);
            Show(_refLabel, Mode == InputMode.Reference);
            Enable(_angle, Mode == InputMode.RadiusAngle || Mode == InputMode.Reference);
            Enable(_rot, Mode != InputMode.Endpoint);
            Enable(_after, Mode != InputMode.Endpoint);
            if (_suggestedRadius == null) Show(_apply, false);

            if (Profile != null && Start.HasValue)
            {
                _od.Caption = "Outside diameter: " + SwUtil.FormatLength(Model, Profile.OuterDiameter);
                _wall.Caption = "Wall thickness: " + SwUtil.FormatLength(Model, Profile.WallThickness) + "  (ID " + SwUtil.FormatLength(Model, Profile.InnerDiameter) + ")";
                var t = Start.Value.Tangent;
                _axis.Caption = $"Tube axis: ({t.X:0.###}, {t.Y:0.###}, {t.Z:0.###})";
                _attach.Caption = AttachText;
                var preset = Library.Match(Profile);
                if (!ReferenceEquals(_toolingFor, Profile))
                {
                    _toolingFor = Profile;
                    _toolRadii = preset?.BendRadii?.OrderBy(r => r).ToList() ?? new List<double>();
                    Building = true;
                    _tooling.Clear();
                    _tooling.AddItems(_toolRadii.Count == 0 ? new[] { "(no tooling radii in the library for this tube)" }
                        : new[] { "Tooling radius…" }.Concat(_toolRadii.Select(r => "CLR " + SwUtil.FormatLength(Model, r))).ToArray());
                    _tooling.CurrentSelection = 0;
                    Building = false;
                }
                _minR.Caption = "Minimum recommended radius: " + (preset?.MinBendRadius != null ? SwUtil.FormatLength(Model, preset.MinBendRadius.Value) : "not defined in the tube library");
            }
            UpdatePreview(UserSettings.Current.LivePreview, UserSettings.Current.ShowCentreline);
        }

        protected override List<PathPrimitive> CurrentPrimitives(out string error)
        {
            error = TubeChainSolver.ValidateBendInput(Data, Profile);
            if (error != null) return null;
            return BendMath.BuildBend(Start.Value, Data.BendRadius, Data.BendAngle, Data.PlaneRotation, Data.ReverseDirection, Data.StraightAfter);
        }

        protected override void AfterPreview(List<PathPrimitive> prims)
        {
            if (prims == null) { Handles.HideTriad(); return; }
            var arc = (ArcPrimitive)prims[0];
            _bendLen.Caption = $"Developed bend length (bend allowance, CLR × angle): {SwUtil.FormatLength(Model, arc.Length)}";
            _total.Caption = ManufacturingSummary(out string warnings);
            _warn.Caption = warnings;

            Handles.ArrowDragged = len => { Data.StraightAfter = len; Building = true; _after.Value = len; Building = false; Refresh(); };
            Handles.TriadDragged = p =>
            {
                ShowVector(p, false);
                SolveFromTarget();
                Refresh();
            };
            if (Mode != InputMode.Endpoint) Handles.ShowArrow(arc.EndPoint, arc.EndTangent, Data.StraightAfter);
            if (Mode == InputMode.Endpoint) Handles.ShowTriad(prims.Last().EndPoint); else Handles.HideTriad();
        }

        protected override bool CanAccept(out string reason)
        {
            reason = null;
            if (Start == null) { reason = "Select the end face of a tube."; return false; }
            reason = TubeChainSolver.ValidateBendInput(Data, Profile);
            return reason == null;
        }

        protected override void SaveRemembered()
        {
            var s = UserSettings.Current;
            s.LastBendRadius = Data.BendRadius; s.LastBendAngle = Data.BendAngle; s.LastStraightAfter = Data.StraightAfter;
            s.Save();
        }
    }
}
