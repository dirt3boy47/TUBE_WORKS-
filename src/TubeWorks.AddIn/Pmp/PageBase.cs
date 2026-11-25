using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace TubeWorks.AddIn.Pmp
{
    /// <summary>
    /// Base class of the TubeWorks PropertyManager pages. Implements every handler method with no action, and
    /// gives small helpers to add native PropertyManager controls. Model changes are made in AfterClose, never in OnClose.
    /// </summary>
    [ComVisible(true)]
    public abstract class PageBase : PropertyManagerPage2Handler9
    {
        protected readonly ISldWorks App;
        protected readonly IModelDoc2 Model;
        protected IPropertyManagerPage2 Page;
        protected int CloseReason = -1;
        protected bool Building = true; // true while controls are being created: handlers ignore events

        protected const short Left = (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
        protected const int Vis = (int)(swAddControlOptions_e.swControlOptions_Visible | swAddControlOptions_e.swControlOptions_Enabled);

        protected PageBase(ISldWorks app, IModelDoc2 model) { App = app; Model = model; }

        protected bool CreatePage(string title)
        {
            int errors = 0;
            int opts = (int)(swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                             swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton |
                             swPropertyManagerPageOptions_e.swPropertyManagerOptions_UndoButton |
                             swPropertyManagerPageOptions_e.swPropertyManagerOptions_RedoButton);
            Page = App.CreatePropertyManagerPage(title, opts, this, ref errors) as IPropertyManagerPage2;
            return Page != null;
        }

        protected IPropertyManagerPageGroup Group(int id, string caption, bool expanded = true) =>
            (IPropertyManagerPageGroup)Page.AddGroupBox(id, caption,
                (int)(swAddGroupBoxOptions_e.swGroupBoxOptions_Visible | (expanded ? swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded : 0)));

        protected static T Add<T>(IPropertyManagerPageGroup g, int id, swPropertyManagerPageControlType_e type, string caption, string tip) where T : class =>
            g.AddControl2(id, (short)type, caption, Left, Vis, tip) as T;

        protected static IPropertyManagerPageNumberbox Number(IPropertyManagerPageGroup g, int id, string caption, string tip,
            swNumberboxUnitType_e unit, double min, double max, double value, double inc)
        {
            var n = Add<IPropertyManagerPageNumberbox>(g, id, swPropertyManagerPageControlType_e.swControlType_Numberbox, caption, tip);
            n.SetRange2((int)unit, min, max, true, inc, inc * 10, inc / 10);
            n.Value = value;
            return n;
        }

        protected static IPropertyManagerPageLabel Label(IPropertyManagerPageGroup g, int id, string text) =>
            Add<IPropertyManagerPageLabel>(g, id, swPropertyManagerPageControlType_e.swControlType_Label, text, text);

        protected static IPropertyManagerPageCheckbox Check(IPropertyManagerPageGroup g, int id, string caption, string tip, bool value)
        {
            var c = Add<IPropertyManagerPageCheckbox>(g, id, swPropertyManagerPageControlType_e.swControlType_Checkbox, caption, tip);
            c.Checked = value;
            return c;
        }

        protected static IPropertyManagerPageCombobox Combo(IPropertyManagerPageGroup g, int id, string tip, string[] items, int selected)
        {
            var c = Add<IPropertyManagerPageCombobox>(g, id, swPropertyManagerPageControlType_e.swControlType_Combobox, "", tip);
            c.AddItems(items);
            c.CurrentSelection = (short)selected;
            return c;
        }

        protected static IPropertyManagerPageSelectionbox Selection(IPropertyManagerPageGroup g, int id, string tip, int mark, swSelectType_e[] filters, bool single)
        {
            var s = Add<IPropertyManagerPageSelectionbox>(g, id, swPropertyManagerPageControlType_e.swControlType_Selectionbox, "", tip);
            s.Mark = mark;
            s.SingleEntityOnly = single;
            s.Height = 30;
            var f = new int[filters.Length];
            for (int i = 0; i < f.Length; i++) f[i] = (int)filters[i];
            s.SetSelectionFilters(f);
            return s;
        }

        protected static void Show(object control, bool visible)
        {
            if (control is IPropertyManagerPageControl c) c.Visible = visible;
        }

        protected static void Enable(object control, bool enabled)
        {
            if (control is IPropertyManagerPageControl c) c.Enabled = enabled;
        }

        protected void Message(string text, bool important = false) =>
            Page?.SetMessage3(text ?? "",
                (int)(string.IsNullOrEmpty(text) ? swPropertyManagerPageMessageVisibility.swNoMessageBox
                    : important ? swPropertyManagerPageMessageVisibility.swImportantMessageBox : swPropertyManagerPageMessageVisibility.swMessageBoxVisible),
                (int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand, "TubeWorks");

        /// <summary>Selected objects of a selection box, by its mark.</summary>
        protected object[] Selected(int mark)
        {
            var sm = (ISelectionMgr)Model.SelectionManager;
            int n = sm.GetSelectedObjectCount2(mark);
            var r = new object[n];
            for (int i = 1; i <= n; i++) r[i - 1] = sm.GetSelectedObject6(i, mark);
            return r;
        }

        public void ShowPage() { Building = false; Page.Show2(0); }

        /// <summary>Called after the page closed with OK. Make model changes here.</summary>
        protected abstract void OnOk();
        /// <summary>Called after the page closed without OK.</summary>
        protected virtual void OnCancel() { }
        protected virtual void OnChanged(int id) { }
        /// <summary>Return false to refuse closing with OK (input is not valid).</summary>
        protected virtual bool CanAccept(out string reason) { reason = null; return true; }

        // ------------------------------------------------------------------ handler

        public virtual void AfterActivation() { }

        public void OnClose(int Reason)
        {
            CloseReason = Reason;
            if (Reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay && !CanAccept(out string why))
            {
                Message(why, true);
                // A failing HRESULT from OnClose keeps the page open.
                throw new COMException(why, unchecked((int)0x80004005));
            }
        }

        public void AfterClose()
        {
            try
            {
                if (CloseReason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay) OnOk();
                else OnCancel();
            }
            catch (Exception ex)
            {
                Log.Error(GetType().Name, ex);
                App.SendMsgToUser2("TubeWorks: " + ex.Message, (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        public bool OnHelp() => false;
        public bool OnPreviousPage() => false;
        public bool OnNextPage() => false;
        public bool OnPreview() => true;
        public void OnWhatsNew() { }
        public void OnUndo() { }
        public void OnRedo() { }
        public bool OnTabClicked(int Id) => true;
        public void OnGroupExpand(int Id, bool Expanded) { }
        public void OnGroupCheck(int Id, bool Checked) { }
        public void OnCheckboxCheck(int Id, bool Checked) { if (!Building) Safe(Id); }
        public void OnOptionCheck(int Id) { if (!Building) Safe(Id); }
        public virtual void OnButtonPress(int Id) { if (!Building) Safe(Id); }
        public void OnTextboxChanged(int Id, string Text) { if (!Building) Safe(Id); }
        public void OnNumberboxChanged(int Id, double Value) { if (!Building) Safe(Id); }
        public void OnComboboxEditChanged(int Id, string Text) { }
        public void OnComboboxSelectionChanged(int Id, int Item) { if (!Building) Safe(Id); }
        public void OnListboxSelectionChanged(int Id, int Item) { }
        public void OnSelectionboxFocusChanged(int Id) { }
        public void OnSelectionboxListChanged(int Id, int Count) { if (!Building) Safe(Id); }
        public void OnSelectionboxCalloutCreated(int Id) { }
        public void OnSelectionboxCalloutDestroyed(int Id) { }
        public virtual bool OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText) => true;
        public int OnActiveXControlCreated(int Id, bool Status) => 0;
        public void OnSliderPositionChanged(int Id, double Value) { }
        public void OnSliderTrackingCompleted(int Id, double Value) { }
        public bool OnKeystroke(int Wparam, int Message, int Lparam, int Id) => false;
        public void OnPopupMenuItem(int Id) { }
        public void OnPopupMenuItemUpdate(int Id, ref int retval) { }
        public void OnGainedFocus(int Id) { }
        public void OnLostFocus(int Id) { }
        public int OnWindowFromHandleControlCreated(int Id, bool Status) => 0;
        public void OnListboxRMBUp(int Id, int PosX, int PosY) { }
        public void OnNumberBoxTrackingCompleted(int Id, double Value) { }

        private void Safe(int id)
        {
            try { OnChanged(id); }
            catch (Exception ex) { Log.Error(GetType().Name + ".OnChanged", ex); Message(ex.Message, true); }
        }
    }
}
