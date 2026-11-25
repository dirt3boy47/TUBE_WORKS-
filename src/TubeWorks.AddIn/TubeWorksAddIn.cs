using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using TubeWorks.AddIn.Pmp;
using TubeWorks.AddIn.Sw;
using TubeWorks.AddIn.Ui;
using TubeWorks.Geometry;
using BendTable = TubeWorks.Geometry.BendTable;

namespace TubeWorks.AddIn
{
    /// <summary>
    /// TubeWorks Designer add-in entry point. Registers the "Tube Design" CommandManager tab.
    /// SOLIDWORKS calls the command callbacks by name through IDispatch, so they are public methods of this class.
    /// </summary>
    [ComVisible(true)]
    [Guid("6478F06E-71AB-4E73-9A87-8A6503767FF1")]
    [ProgId("TubeWorks.AddIn")]
    public sealed class TubeWorksAddIn : ISwAddin
    {
        public const string Title = "TubeWorks Designer";
        private const int GroupId = 7741;
        private const string TabName = "Tube Design";

        internal static TubeWorksAddIn Instance { get; private set; }
        internal ISldWorks App { get; private set; }
        private ICommandManager _cmdMgr;
        private int _cookie;

        /// <summary>The 17 commands of the Tube Design tab. Planned ones are shown disabled, never as working.</summary>
        internal sealed class Cmd
        {
            public int Index; public string Name, Tip, Callback; public bool Implemented;
        }

        internal static readonly Cmd[] Commands =
        {
            new Cmd { Name = "Base Tube", Tip = "Create a straight circular hollow tube", Callback = nameof(OnBaseTube), Implemented = true },
            new Cmd { Name = "Face Bend", Tip = "Bend from a selected tube end face", Callback = nameof(OnFaceBend), Implemented = true },
            new Cmd { Name = "Straight Extension", Tip = "Extend a tube end along its axis", Callback = nameof(OnStraight), Implemented = true },
            new Cmd { Name = "Edit Bend", Tip = "Edit the selected TubeWorks feature", Callback = nameof(OnEditBend), Implemented = true },
            new Cmd { Name = "Reverse Bend", Tip = "Bend the selected bend to the opposite side", Callback = nameof(OnReverseBend), Implemented = true },
            new Cmd { Name = "Multi Bend", Tip = "Planned (not in this release): several bends in one command", Callback = nameof(OnPlanned) },
            new Cmd { Name = "Set Bend Plane", Tip = "Align the selected bend with a plane, face, edge or axis", Callback = nameof(OnSetBendPlane), Implemented = true },
            new Cmd { Name = "Tube Trim", Tip = "Planned (Phase 2): trim a tube end", Callback = nameof(OnPlanned) },
            new Cmd { Name = "Tube Notch", Tip = "Planned (Phase 2): notch a tube end", Callback = nameof(OnPlanned) },
            new Cmd { Name = "Unbend", Tip = "Straighten one, several or all bends about a fixed segment", Callback = nameof(OnUnbend), Implemented = true },
            new Cmd { Name = "Rebend", Tip = "Restore the bends of the active Unbend; transfer cuts made while straight", Callback = nameof(OnRebend), Implemented = true },
            new Cmd { Name = "Flatten Tube", Tip = "Switch between the bent design and the straight manufacturing blank", Callback = nameof(OnFlatten), Implemented = true },
            new Cmd { Name = "Unwrap Tube Surface", Tip = "2D development of the tube surface with cut-outs and bend markings, to DXF", Callback = nameof(OnUnwrap), Implemented = true },
            new Cmd { Name = "Export Flat Pattern", Tip = "Export the bent or straight tube (STEP, Parasolid, SOLIDWORKS part)", Callback = nameof(OnExport), Implemented = true },
            new Cmd { Name = "Bend Table", Tip = "Bend table with XYZ and LRA data; CSV export", Callback = nameof(OnBendTable), Implemented = true },
            new Cmd { Name = "Tube Properties", Tip = "Profile, material and developed length", Callback = nameof(OnTubeProperties), Implemented = true },
            new Cmd { Name = "Manufacturing Check", Tip = "Check bend radii, straights, angles, self-intersection and cuts in bends", Callback = nameof(OnManufacturingCheck), Implemented = true },
            new Cmd { Name = "Tube Library", Tip = "Edit tube presets, tooling radii and limits; import and export", Callback = nameof(OnLibrary), Implemented = true },
        };

        // ------------------------------------------------------------------ ISwAddin

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            try
            {
                Instance = this;
                App = (ISldWorks)ThisSW;
                _cookie = Cookie;
                App.SetAddinCallbackInfo2(0, this, _cookie);
                _cmdMgr = App.GetCommandManager(_cookie);
                AddCommands();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("ConnectToSW", ex);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try { _cmdMgr?.RemoveCommandGroup2(GroupId, true); }
            catch (Exception ex) { Log.Error("DisconnectFromSW", ex); }
            if (_cmdMgr != null) Marshal.ReleaseComObject(_cmdMgr);
            _cmdMgr = null;
            App = null;
            Instance = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        private void AddCommands()
        {
            string iconDir = Path.Combine(Path.GetDirectoryName(typeof(TubeWorksAddIn).Assembly.Location) ?? "", "Icons");
            int[] sizes = { 20, 32, 40, 64, 96, 128 };

            int[] knownIds = Commands.Select((c, i) => i).ToArray();
            bool ignorePrevious = false;
            if (_cmdMgr.GetGroupDataFromRegistry(GroupId, out object regIds))
            {
                var reg = regIds as int[];
                ignorePrevious = reg == null || !reg.SequenceEqual(knownIds);
            }

            int err = 0;
            var grp = _cmdMgr.CreateCommandGroup2(GroupId, TabName, Title, Title, -1, ignorePrevious, ref err);
            grp.IconList = sizes.Select(s => Path.Combine(iconDir, $"toolbar_{s}.png")).ToArray();
            grp.MainIconList = sizes.Select(s => Path.Combine(iconDir, $"main_{s}.png")).ToArray();

            int menuTb = (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem);
            for (int i = 0; i < Commands.Length; i++)
            {
                var c = Commands[i];
                c.Index = grp.AddCommandItem2(c.Name, -1, c.Tip, c.Name, i, c.Callback, c.Implemented ? nameof(EnablePart) : nameof(EnableNever), i, menuTb);
            }
            grp.HasToolbar = true;
            grp.HasMenu = true;
            grp.Activate();

            foreach (int docType in new[] { (int)swDocumentTypes_e.swDocPART })
            {
                var tab = _cmdMgr.GetCommandTab(docType, TabName);
                if (tab != null && ignorePrevious) { _cmdMgr.RemoveCommandTab((CommandTab)tab); tab = null; }
                if (tab != null) continue;
                tab = _cmdMgr.AddCommandTab(docType, TabName);
                var box = tab.AddCommandTabBox();
                var ids = Commands.Select(c => grp.CommandID[c.Index]).ToArray();
                var styles = Commands.Select(c => (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow).ToArray();
                box.AddCommands(ids, styles);
            }
        }

        // ------------------------------------------------------------------ enable callbacks (1 = enabled, 0 = disabled)

        public int EnablePart() => App?.ActiveDoc is IPartDoc ? 1 : 0;
        public int EnableNever() => 0;

        // ------------------------------------------------------------------ command callbacks

        private IModelDoc2 Part
        {
            get
            {
                var m = App.ActiveDoc as IModelDoc2;
                return m != null && m.GetType() == (int)swDocumentTypes_e.swDocPART ? m : null;
            }
        }

        private void Run(string name, Action<IModelDoc2> a)
        {
            var model = Part;
            if (model == null) { SwUtil.Message(App, "Open a part first."); return; }
            try { a(model); }
            catch (Exception ex)
            {
                Log.Error(name, ex);
                SwUtil.Message(App, $"TubeWorks {name}: {ex.Message}", swMessageBoxIcon_e.swMbStop);
            }
        }

        private static IFace2 SelectedFace(IModelDoc2 model)
        {
            var sm = (ISelectionMgr)model.SelectionManager;
            for (int i = 1; i <= sm.GetSelectedObjectCount2(-1); i++)
                if (sm.GetSelectedObjectType3(i, -1) == (int)swSelectType_e.swSelFACES) return sm.GetSelectedObject6(i, -1) as IFace2;
            return null;
        }

        /// <summary>The TubeWorks feature that is selected in the tree, or that owns a selected face.</summary>
        private static StoredFeature SelectedTubeFeature(IModelDoc2 model)
        {
            var sm = (ISelectionMgr)model.SelectionManager;
            for (int i = 1; i <= sm.GetSelectedObjectCount2(-1); i++)
            {
                var o = sm.GetSelectedObject6(i, -1);
                var f = o as IFeature ?? (o as IFace2)?.GetFeature() as IFeature;
                var s = TubeFeatureStore.TryRead(f);
                if (s != null) return s;
            }
            return null;
        }

        public void OnBaseTube() => Run("Base Tube", m => { TubeFeatureStore.EnsureBentState(m); BaseTubePage.Create(App, m); });

        private bool BlockWhenUnbent(IModelDoc2 m)
        {
            if (!Features.StateFeatureBase.ActiveUnbends(TubeFeatureStore.Scan(m)).Any()) return false;
            SwUtil.Message(App, "The tube is unbent. Add a Rebend before you add tube segments.");
            return true;
        }

        public void OnUnbend() => Run("Unbend", m => { TubeFeatureStore.EnsureBentState(m); UnbendPage.Create(App, m); });

        public void OnRebend() => Run("Rebend", m =>
        {
            var active = Features.StateFeatureBase.ActiveUnbends(TubeFeatureStore.Scan(m)).LastOrDefault();
            if (active == null) { SwUtil.Message(App, "There is no active Unbend to rebend."); return; }
            var all = TubeFeatureStore.Scan(m);
            var path = TubeFeatureStore.Solve(all).Paths.FirstOrDefault(p => p.RootId == active.Data.ParentId);
            var body = (path != null ? TubeFeatureStore.TubeBody(all, path) : null) ?? TubeFeatureStore.BodyOf(active.Feature);
            m.ClearSelection2(true);
            var data = new TubeFeatureData { Kind = TubeFeatureKind.Rebend, ParentId = active.Data.Id };
            if (TubeFeatureStore.Insert(m, data, body, TubeFeatureStore.NextName(m, TubeFeatureKind.Rebend)) == null)
                SwUtil.Message(App, "SOLIDWORKS could not create the Tube Rebend feature.", swMessageBoxIcon_e.swMbStop);
            else UnbendPage.ShowReport(App, m, data.Id, "Rebend");
        });

        public void OnUnwrap() => Run("Unwrap Tube Surface", m => UnwrapForm.ShowFor(App, m));

        public void OnLibrary() { try { LibraryForm.ShowFor(App); } catch (Exception ex) { Log.Error("Library", ex); SwUtil.Message(App, ex.Message, swMessageBoxIcon_e.swMbStop); } }

        public void OnFaceBend() => Run("Face Bend", m =>
        {
            if (BlockWhenUnbent(m)) return;
            var face = SelectedFace(m);
            if (TubeFeatureStore.EnsureBentState(m)) face = null; // the face of the flat blank is not valid
            FaceBendPage.Create(App, m, face);
        });

        public void OnStraight() => Run("Straight Extension", m =>
        {
            if (BlockWhenUnbent(m)) return;
            var face = SelectedFace(m);
            if (TubeFeatureStore.EnsureBentState(m)) face = null;
            StraightPage.Create(App, m, face);
        });

        public void OnEditBend() => Run("Edit Bend", m =>
        {
            var s = SelectedTubeFeature(m);
            if (s == null) { SwUtil.Message(App, "Select a TubeWorks feature in the FeatureManager tree, or a face that it created."); return; }
            EditFeature(m, s.Feature);
        });

        public void OnReverseBend() => Run("Reverse Bend", m =>
        {
            var s = SelectedTubeFeature(m);
            if (s == null || s.Data.Kind != TubeFeatureKind.Bend) { SwUtil.Message(App, "Select a TubeWorks bend feature."); return; }
            var md = (IMacroFeatureData)s.Feature.GetDefinition();
            md.AccessSelections(m, null);
            var d = TubeFeatureStore.Read(md);
            d.ReverseDirection = !d.ReverseDirection;
            if (!TubeFeatureStore.Modify(m, s.Feature, md, d)) { md.ReleaseSelectionAccess(); SwUtil.Message(App, "SOLIDWORKS did not accept the change."); }
        });

        public void OnSetBendPlane() => Run("Set Bend Plane", m =>
        {
            var s = SelectedTubeFeature(m);
            if (s == null || s.Data.Kind != TubeFeatureKind.Bend) { SwUtil.Message(App, "Select a TubeWorks bend feature, then run Set Bend Plane."); return; }
            var md = (IMacroFeatureData)s.Feature.GetDefinition();
            md.AccessSelections(m, null);
            FaceBendPage.Edit(App, m, s.Feature, md, TubeFeatureStore.Read(md), focusReference: true);
        });

        public void OnPlanned() => SwUtil.Message(App, "This command is planned for a later TubeWorks release. It is not available yet.");

        public void OnFlatten() => Run("Flatten Tube", m =>
        {
            var all = TubeFeatureStore.Scan(m);
            var flat = all.FirstOrDefault(x => x.Data.Kind == TubeFeatureKind.FlatPattern);
            if (flat != null)
            {
                int action = flat.Suppressed ? (int)swFeatureSuppressionAction_e.swUnSuppressFeature : (int)swFeatureSuppressionAction_e.swSuppressFeature;
                flat.Feature.SetSuppression2(action, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                m.ForceRebuild3(false);
                return;
            }
            var chain = TubeFeatureStore.Solve(all);
            var path = chain.Paths.FirstOrDefault(p => p.RootIsBaseTube);
            if (path == null) { SwUtil.Message(App, "Flatten Tube needs a valid tube that starts with a Base Tube feature."); return; }
            var body = TubeFeatureStore.TubeBody(all, path);
            if (body == null) { SwUtil.Message(App, "The tube body was not found."); return; }
            m.ClearSelection2(true);
            var data = new TubeFeatureData { Kind = TubeFeatureKind.FlatPattern, ParentId = path.RootId };
            if (TubeFeatureStore.Insert(m, data, body, "Tube Flat Pattern") == null)
                SwUtil.Message(App, "SOLIDWORKS could not create the Tube Flat Pattern feature.", swMessageBoxIcon_e.swMbStop);
            else UnbendPage.ShowReport(App, m, data.Id, "Flat Pattern");
        });

        public void OnExport() => Run("Export", m => ExportForm.ShowFor(App, m));

        public void OnBendTable() => Run("Bend Table", m => BendTableForm.ShowFor(App, m));

        public void OnTubeProperties() => Run("Tube Properties", m =>
        {
            var chain = TubeFeatureStore.Solve(m);
            if (chain.Paths.Count == 0) { SwUtil.Message(App, "No valid TubeWorks tube in this part."); return; }
            var lines = new List<string>();
            var part = m as IPartDoc;
            string mat = part?.GetMaterialPropertyName2("", out string _) ?? "";
            int n = 1;
            foreach (var p in chain.Paths)
            {
                var t = BendTable.Build(p.Primitives);
                lines.Add($"Tube {n++}:");
                lines.Add($"  Outside diameter {SwUtil.FormatLength(m, p.Profile.OuterDiameter)}, inside diameter {SwUtil.FormatLength(m, p.Profile.InnerDiameter)}, wall {SwUtil.FormatLength(m, p.Profile.WallThickness)}");
                lines.Add($"  Bends: {t.Rows.Count}; developed length (required stock length, nominal centreline): {SwUtil.FormatLength(m, t.TotalDevelopedLength)}");
                if (!p.RootIsBaseTube) lines.Add("  Note: the tube starts on a body that TubeWorks did not create; that body is not included in the length.");
            }
            lines.Add("Material: " + (string.IsNullOrEmpty(mat) ? "not set" : mat));
            SwUtil.Message(App, string.Join("\n", lines));
        });

        public void OnManufacturingCheck() => Run("Manufacturing Check", m =>
        {
            var chain = TubeFeatureStore.Solve(m);
            var lib = LibraryStore.Load();
            var issues = new List<ValidationIssue>();
            foreach (var p in chain.Paths.Select(x => x.Profile).Distinct())
                issues.AddRange(TubeValidator.Validate(chain, lib.RulesFor(p)));
            if (chain.Paths.Count == 0) issues.AddRange(TubeValidator.Validate(chain, new ManufacturingRules()));
            issues.AddRange(CutsInBends(m, chain));
            issues = issues.GroupBy(i => i.Message).Select(g => g.First()).ToList();
            if (issues.Count == 0) { SwUtil.Message(App, "Manufacturing check: no errors and no warnings.\n(Checks that need limits use the tube library; undefined limits are not checked.)"); return; }
            var errors = issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
            var warnings = issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();
            string text = $"Manufacturing check: {errors.Count} error(s), {warnings.Count} warning(s).\n\n" +
                          string.Join("\n", errors.Concat(warnings).Select(i => "• " + i));
            SwUtil.Message(App, text, errors.Count > 0 ? swMessageBoxIcon_e.swMbStop : swMessageBoxIcon_e.swMbWarning);
        });

        /// <summary>Cuts (holes, slots, notches) that lie in a bend region: they distort in bending and cannot be unbent exactly.</summary>
        private IEnumerable<ValidationIssue> CutsInBends(IModelDoc2 m, ChainResult chain)
        {
            var list = new List<ValidationIssue>();
            try
            {
                var all = TubeFeatureStore.Scan(m);
                if (Features.StateFeatureBase.ActiveUnbends(all).Any() || all.Any(x => x.Data.Kind == TubeFeatureKind.FlatPattern && !x.Suppressed)) return list;
                var builder = new BodyBuilder(App) { VerifyGeometry = false };
                foreach (var path in chain.Paths.Where(p => p.RootIsBaseTube))
                {
                    var actual = TubeFeatureStore.TubeBody(all, path);
                    var nominal = builder.Build(path.Primitives, path.Profile, out string _);
                    if (actual == null || nominal == null) continue;
                    var u = TubeUnbender.ForFeature(path, null);
                    foreach (var p in MaterialTransfer.Removed(nominal, actual, out bool _))
                        if (TubeUnbender.ClassifyRigid(u, p.Samples) < 0)
                            list.Add(new ValidationIssue
                            {
                                Severity = IssueSeverity.Warning, Code = "CUT_IN_BEND", FeatureId = path.RootId,
                                Message = "Feature in a bend region, distorts in bending and is not unbent exactly: " +
                                          MaterialTransfer.Describe(path.Primitives, p, id => all.FirstOrDefault(x => x.Data.Id == id)?.Feature.Name ?? id),
                            });
                }
            }
            catch (Exception ex) { Log.Error("CutsInBends", ex); }
            return list;
        }

        /// <summary>Open the PropertyManager page of a TubeWorks feature for editing (also used by ISwComFeature.Edit).</summary>
        internal void EditFeature(IModelDoc2 model, IFeature feature)
        {
            var md = (IMacroFeatureData)feature.GetDefinition();
            var data = TubeFeatureStore.Read(md);
            if (data.Kind == TubeFeatureKind.FlatPattern) { SwUtil.Message(App, "The Tube Flat Pattern has no parameters. Use Flatten Tube to switch states."); return; }
            if (data.Kind == TubeFeatureKind.Rebend) { SwUtil.Message(App, "Tube Rebend has no parameters. Edit its Unbend feature."); return; }
            if (!md.AccessSelections(model, null)) { SwUtil.Message(App, "SOLIDWORKS could not open the feature for editing.", swMessageBoxIcon_e.swMbStop); return; }
            switch (data.Kind)
            {
                case TubeFeatureKind.BaseTube: BaseTubePage.Edit(App, model, feature, md, data); break;
                case TubeFeatureKind.Straight: StraightPage.Edit(App, model, feature, md, data); break;
                case TubeFeatureKind.Unbend: UnbendPage.Edit(App, model, feature, md, data); break;
                default: FaceBendPage.Edit(App, model, feature, md, data); break;
            }
        }

        // ------------------------------------------------------------------ COM registration

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            if (t != typeof(TubeWorksAddIn)) return;
            string g = "{" + t.GUID.ToString().ToUpperInvariant() + "}";
            using (var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\Addins\" + g))
            {
                k.SetValue(null, 0);
                k.SetValue("Description", "Parametric tube and pipe bending, development and bend tables");
                k.SetValue("Title", Title);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\" + g))
                k.SetValue(null, 1, RegistryValueKind.DWord);
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            if (t != typeof(TubeWorksAddIn)) return;
            string g = "{" + t.GUID.ToString().ToUpperInvariant() + "}";
            try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\SolidWorks\Addins\" + g, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\SolidWorks\AddInsStartup\" + g, false); } catch { }
        }
    }
}
