using System;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Pmp
{
    /// <summary>Base Tube PropertyManager: outside diameter, wall or inside diameter, length, material, placement.</summary>
    public sealed class BaseTubePage : PageBase
    {
        private const int PlaneMark = 1;
        private const int IdPreset = 5, IdOd = 10, IdSizeMode = 11, IdWall = 12, IdId = 13, IdDerived = 14, IdLen = 15, IdMat = 16;
        private const int IdPlane = 20, IdAxis = 21, IdX = 22, IdY = 23, IdZ = 24, IdFlip = 25;

        private readonly TubeFeatureData _data;
        private readonly IFeature _editFeature;
        private readonly IMacroFeatureData _editDef;
        private readonly Preview _preview;
        private readonly BodyBuilder _builder;
        private readonly TubeLibrary _library = TubeLibrary.Load();

        private IPropertyManagerPageNumberbox _od, _wall, _id, _len, _x, _y, _z;
        private IPropertyManagerPageCombobox _sizeMode, _axis, _preset;
        private IPropertyManagerPageLabel _derived;
        private IPropertyManagerPageTextbox _mat;
        private IPropertyManagerPageCheckbox _flip;
        private Vec3? _planeNormal;

        private static readonly Vec3[] AxisDirs = { Vec3.UnitX, -Vec3.UnitX, Vec3.UnitY, -Vec3.UnitY, Vec3.UnitZ, -Vec3.UnitZ };

        private BaseTubePage(ISldWorks app, IModelDoc2 model, TubeFeatureData d, IFeature f, IMacroFeatureData md) : base(app, model)
        {
            _data = d; _editFeature = f; _editDef = md;
            _preview = new Preview(model);
            _builder = new BodyBuilder(app) { VerifyGeometry = UserSettings.Current.VerifyGeometry };
        }

        public static void Create(ISldWorks app, IModelDoc2 model)
        {
            var s = UserSettings.Current;
            var d = new TubeFeatureData
            {
                Kind = TubeFeatureKind.BaseTube, OuterDiameter = s.LastOuterDiameter, WallThickness = s.LastWallThickness, Length = s.LastLength,
                Axis = Vec3.UnitZ, Origin = Vec3.Zero, Material = s.LastMaterial,
            };
            var p = new BaseTubePage(app, model, d, null, null);
            p.Build("Base Tube");
            p.ShowPage();
            p.Refresh();
        }

        public static void Edit(ISldWorks app, IModelDoc2 model, IFeature f, IMacroFeatureData md, TubeFeatureData d)
        {
            var p = new BaseTubePage(app, model, d, f, md);
            p.Build("Edit " + f.Name);
            p.ShowPage();
            p.Refresh();
        }

        private void Build(string title)
        {
            if (!CreatePage(title)) throw new InvalidOperationException("Could not create the PropertyManager page.");
            var g1 = Group(1, "Tube profile (circular hollow section)");
            var presetNames = new[] { "(no preset)" }.Concat(_library.Presets.Select(x => x.Name)).ToArray();
            _preset = Combo(g1, IdPreset, "Tube library preset (%APPDATA%\\TubeWorks\\library.xml)", presetNames, 0);
            _od = Number(g1, IdOd, "Outside diameter", "Outside diameter", swNumberboxUnitType_e.swNumberBox_Length, 1e-4, 10, _data.OuterDiameter, 0.001);
            _sizeMode = Combo(g1, IdSizeMode, "Second size value", new[] { "Wall thickness", "Inside diameter" }, 0);
            _wall = Number(g1, IdWall, "Wall thickness", "Wall thickness", swNumberboxUnitType_e.swNumberBox_Length, 1e-5, 5, _data.WallThickness, 0.0001);
            _id = Number(g1, IdId, "Inside diameter", "Inside diameter", swNumberboxUnitType_e.swNumberBox_Length, 1e-5, 10, _data.OuterDiameter - 2 * _data.WallThickness, 0.001);
            _derived = Label(g1, IdDerived, "");
            _len = Number(g1, IdLen, "Starting length", "Length of the starting straight tube", swNumberboxUnitType_e.swNumberBox_Length, 1e-5, 100, _data.Length, 0.001);
            _mat = Add<IPropertyManagerPageTextbox>(g1, IdMat, swPropertyManagerPageControlType_e.swControlType_Textbox, "Material", "SOLIDWORKS material name (optional), for example AISI 304");
            _mat.Text = _data.Material ?? "";

            var g2 = Group(2, "Placement");
            Label(g2, 30, "Starting plane or planar face (optional):");
            Selection(g2, IdPlane, "Starting plane: the tube axis is its normal, the start point its nearest point to the origin", PlaneMark,
                new[] { swSelectType_e.swSelDATUMPLANES, swSelectType_e.swSelFACES }, true);
            _axis = Combo(g2, IdAxis, "Tube axis direction", new[] { "+X", "-X", "+Y", "-Y", "+Z", "-Z", "Normal of selected plane" }, AxisIndex(_data.Axis));
            _flip = Check(g2, IdFlip, "Reverse direction", "Reverse the tube axis", false);
            _x = Number(g2, IdX, "Start X", "Starting position X", swNumberboxUnitType_e.swNumberBox_Length, -100, 100, _data.Origin.X, 0.001);
            _y = Number(g2, IdY, "Start Y", "Starting position Y", swNumberboxUnitType_e.swNumberBox_Length, -100, 100, _data.Origin.Y, 0.001);
            _z = Number(g2, IdZ, "Start Z", "Starting position Z", swNumberboxUnitType_e.swNumberBox_Length, -100, 100, _data.Origin.Z, 0.001);
            Show(_id, false);
        }

        private static int AxisIndex(Vec3 a)
        {
            for (int i = 0; i < AxisDirs.Length; i++) if (AxisDirs[i].AlmostEquals(a, 1e-9)) return i;
            return 6;
        }

        protected override void OnChanged(int id)
        {
            switch (id)
            {
                case IdPreset:
                    int k = _preset.CurrentSelection - 1;
                    if (k >= 0 && k < _library.Presets.Count)
                    {
                        var pr = _library.Presets[k];
                        _data.OuterDiameter = pr.OuterDiameter; _data.WallThickness = pr.WallThickness; _data.Material = pr.Material;
                        Building = true; _od.Value = pr.OuterDiameter; _wall.Value = pr.WallThickness; _id.Value = pr.OuterDiameter - 2 * pr.WallThickness; _mat.Text = pr.Material; Building = false;
                    }
                    break;
                case IdOd:
                    _data.OuterDiameter = _od.Value;
                    if (_sizeMode.CurrentSelection == 1) _data.WallThickness = (_od.Value - _id.Value) / 2;
                    break;
                case IdWall: _data.WallThickness = _wall.Value; break;
                case IdId: _data.WallThickness = (_data.OuterDiameter - _id.Value) / 2; break;
                case IdSizeMode:
                    bool byId = _sizeMode.CurrentSelection == 1;
                    Building = true; _id.Value = _data.OuterDiameter - 2 * _data.WallThickness; _wall.Value = _data.WallThickness; Building = false;
                    Show(_wall, !byId); Show(_id, byId);
                    break;
                case IdLen: _data.Length = _len.Value; break;
                case IdMat: _data.Material = _mat.Text ?? ""; break;
                case IdPlane:
                    var obj = Selected(PlaneMark).FirstOrDefault();
                    if (RefGeometry.TryPlane(obj, out Vec3 p, out Vec3 n))
                    {
                        _planeNormal = n;
                        // Start point: projection of the current start point onto the plane.
                        var o = new Vec3(_x.Value, _y.Value, _z.Value);
                        o = o - n * (o - p).Dot(n);
                        Building = true; _x.Value = o.X; _y.Value = o.Y; _z.Value = o.Z; _axis.CurrentSelection = 6; Building = false;
                    }
                    break;
            }
            Refresh();
        }

        private void Refresh()
        {
            int ai = Math.Max(0, (int)_axis.CurrentSelection);
            Vec3 axis = ai < 6 ? AxisDirs[ai] : (_planeNormal ?? _data.Axis);
            if (_flip.Checked) axis = -axis;
            _data.Axis = axis;
            _data.Origin = new Vec3(_x.Value, _y.Value, _z.Value);

            _preview.Clear();
            var prof = new TubeFeatureData { OuterDiameter = _data.OuterDiameter, WallThickness = _data.WallThickness };
            string err = TubeProfile_Validate(prof);
            if (err == null && !(_data.Length > 0)) err = "Starting length must be greater than zero.";
            if (err != null) { Message(err, true); _derived.Caption = ""; return; }
            Message(null);
            var profile = _data.GetProfile();
            _derived.Caption = $"ID {SwUtil.FormatLength(Model, profile.InnerDiameter)}, wall {SwUtil.FormatLength(Model, profile.WallThickness)}, mean Ø {SwUtil.FormatLength(Model, profile.MeanDiameter)}";
            if (UserSettings.Current.LivePreview)
            {
                var body = _builder.Build(new[] { new StraightPrimitive(_data.Origin, _data.Axis, _data.Length) }, profile, out err);
                if (body == null) Message(err, true); else _preview.Show(body);
            }
        }

        private static string TubeProfile_Validate(TubeFeatureData d)
        {
            if (!(d.OuterDiameter > 0)) return "Outside diameter must be greater than zero.";
            if (!(d.WallThickness > 0)) return "Wall thickness must be greater than zero.";
            if (!(d.WallThickness < d.OuterDiameter / 2)) return "Wall thickness must be less than half the outside diameter.";
            return null;
        }

        protected override bool CanAccept(out string reason)
        {
            reason = TubeProfile_Validate(_data) ?? (_data.Length > 0 ? null : "Starting length must be greater than zero.");
            return reason == null;
        }

        protected override void OnCancel()
        {
            _preview.Clear();
            if (_editFeature != null) _editDef.ReleaseSelectionAccess();
        }

        protected override void OnOk()
        {
            _preview.Clear();
            var s = UserSettings.Current;
            s.LastOuterDiameter = _data.OuterDiameter; s.LastWallThickness = _data.WallThickness; s.LastLength = _data.Length; s.LastMaterial = _data.Material;
            s.Save();

            if (_editFeature != null)
            {
                TubeFeatureStore.Modify(Model, _editFeature, _editDef, _data);
            }
            else
            {
                Model.ClearSelection2(true);
                var f = TubeFeatureStore.Insert(Model, _data, null, TubeFeatureStore.NextName(Model, TubeFeatureKind.BaseTube));
                if (f == null) { SwUtil.Message(App, "TubeWorks: SOLIDWORKS could not create the Base Tube.", swMessageBoxIcon_e.swMbStop); return; }
            }
            if (!string.IsNullOrWhiteSpace(_data.Material) && Model is IPartDoc part)
            {
                // Applies to the part. The SOLIDWORKS material database is used; an unknown name is reported.
                part.SetMaterialPropertyName2("", string.IsNullOrEmpty(_data.MaterialDatabase) ? "SOLIDWORKS Materials" : _data.MaterialDatabase, _data.Material);
                string got = part.GetMaterialPropertyName2("", out string _);
                if (!string.Equals(got, _data.Material, StringComparison.OrdinalIgnoreCase))
                    SwUtil.Message(App, $"TubeWorks: material '{_data.Material}' was not found in the SOLIDWORKS material database. The part material was not changed.", swMessageBoxIcon_e.swMbWarning);
            }
        }
    }
}
