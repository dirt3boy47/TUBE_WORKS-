using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Pmp
{
    /// <summary>Shows temporary bodies (IBody2.Display3) as a live preview, and removes them (IBody2.Hide).</summary>
    internal sealed class Preview
    {
        private readonly IModelDoc2 _model;
        private readonly List<IBody2> _shown = new List<IBody2>();
        private const int Colour = 0x00C08020; // COLORREF 0x00BBGGRR: a blue tone
        private const int CentreColour = 0x000000FF; // red

        public Preview(IModelDoc2 model) { _model = model; }

        public void Show(IBody2 body, bool centreline = false)
        {
            if (body == null) return;
            body.Display3(_model, centreline ? CentreColour : Colour, (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
            _shown.Add(body);
        }

        public void Clear()
        {
            foreach (var b in _shown)
            {
                try { b.Hide(_model); } catch { }
            }
            _shown.Clear();
            try { _model.GraphicsRedraw2(); } catch { }
        }
    }

    /// <summary>
    /// Graphics-area handles. A drag arrow sets the straight length after the bend; a triad moves the XYZ end point.
    /// SOLIDWORKS calls the SwManipulatorHandler2 methods while the operator drags.
    /// </summary>
    [ComVisible(true)]
    public sealed class Handles : SwManipulatorHandler2
    {
        private readonly IModelDoc2 _model;
        private readonly IMathUtility _math;
        private IManipulator _arrow, _triad;
        internal Action<double> ArrowDragged;
        internal Action<Vec3> TriadDragged;

        internal Handles(ISldWorks app, IModelDoc2 model)
        {
            _model = model;
            _math = (IMathUtility)app.GetMathUtility();
        }

        internal void ShowArrow(Vec3 origin, Vec3 dir, double length)
        {
            try
            {
                if (_arrow == null)
                {
                    _arrow = _model.ModelViewManager.CreateManipulator((int)swManipulatorType_e.swDragArrowManipulator, this);
                    _arrow.Show(_model);
                }
                var a = _arrow.GetSpecificManipulator() as IDragArrowManipulator;
                if (a == null) return;
                a.Origin = SwUtil.Point(_math, origin);
                a.Direction = SwUtil.Vector(_math, dir);
                a.Length = Math.Max(length, 1e-4);
                a.FixedLength = false;
                a.AllowFlip = false;
                a.Update();
            }
            catch (Exception ex) { Log.Error("ShowArrow", ex); }
        }

        internal void ShowTriad(Vec3 origin)
        {
            try
            {
                if (_triad == null)
                {
                    _triad = _model.ModelViewManager.CreateManipulator((int)swManipulatorType_e.swTriadManipulator, this);
                    var t0 = _triad.GetSpecificManipulator() as ITriadManipulator;
                    if (t0 != null)
                        t0.DoNotShow = (int)(swTriadManipulatorDoNotShow_e.swTriadManipulatorDoNotShowXYRING |
                                             swTriadManipulatorDoNotShow_e.swTriadManipulatorDoNotShowYZRING |
                                             swTriadManipulatorDoNotShow_e.swTriadManipulatorDoNotShowZXRING);
                    _triad.Show(_model);
                }
                var t = _triad.GetSpecificManipulator() as ITriadManipulator;
                if (t == null) return;
                t.Origin = SwUtil.Point(_math, origin);
                t.UpdatePosition();
            }
            catch (Exception ex) { Log.Error("ShowTriad", ex); }
        }

        internal void HideTriad() { try { _triad?.Remove(); } catch { } _triad = null; }

        internal void RemoveAll()
        {
            try { _arrow?.Remove(); } catch { }
            HideTriad();
            _arrow = null;
        }

        // ------------------------------------------------------------------ SwManipulatorHandler2

        public void OnUpdateDrag(object pManipulator, int handleIndex, object newPosMathPt)
        {
            try
            {
                if (pManipulator is IDragArrowManipulator a) { ArrowDragged?.Invoke(a.Length); return; }
                if (pManipulator is ITriadManipulator t)
                {
                    var p = (newPosMathPt as IMathPoint) ?? (t.Origin as IMathPoint);
                    var arr = SwUtil.D(p?.ArrayData);
                    if (arr != null) TriadDragged?.Invoke(SwUtil.V(arr));
                }
            }
            catch (Exception ex) { Log.Error("OnUpdateDrag", ex); }
        }

        public void OnDirectionFlipped(object pManipulator) { }
        public bool OnHandleLmbSelected(object pManipulator) => true;
        public void OnHandleSelected(object pManipulator, int handleIndex) { }
        public void OnEndDrag(object pManipulator, int handleIndex) { }
        public void OnEndNoDrag(object pManipulator, int handleIndex) { }
        public void OnHandleRmbSelected(object pManipulator, int handleIndex) { }
        public bool OnDelete(object pManipulator) => false;
        public bool OnDoubleValueChanged(object pManipulator, int handleIndex, ref double Value) => true;
        public bool OnStringValueChanged(object pManipulator, int handleIndex, ref string Value) => true;
        public void OnItemSetFocus(object pManipulator, int handleIndex) { }
    }
}
