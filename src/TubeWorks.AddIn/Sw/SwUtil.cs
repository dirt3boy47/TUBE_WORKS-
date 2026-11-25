using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Sw
{
    internal static class SwUtil
    {
        public static double[] D(object o) => o as double[];

        public static Vec3 V(double[] a, int offset = 0) => new Vec3(a[offset], a[offset + 1], a[offset + 2]);

        public static T[] Arr<T>(object o) where T : class
        {
            if (!(o is object[] a)) return new T[0];
            var list = new List<T>();
            foreach (var x in a) if (x is T t) list.Add(t);
            return list.ToArray();
        }

        public static MathPoint Point(IMathUtility mu, Vec3 p) => (MathPoint)mu.CreatePoint(p.ToArray());
        public static MathVector Vector(IMathUtility mu, Vec3 v) => (MathVector)mu.CreateVector(v.ToArray());

        public static void Message(ISldWorks app, string text, swMessageBoxIcon_e icon = swMessageBoxIcon_e.swMbInformation) =>
            app.SendMsgToUser2(text, (int)icon, (int)swMessageBoxBtn_e.swMbOk);

        public static bool IsSuppressed(IFeature f)
        {
            try
            {
                var s = f.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null) as bool[];
                return s != null && s.Length > 0 && s[0];
            }
            catch { return false; }
        }

        /// <summary>Length in the document's linear unit, for labels. Number boxes convert by themselves.</summary>
        public static string FormatLength(IModelDoc2 model, double metres)
        {
            int unit = 0;
            try { unit = model.Extension.GetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitsLinear, (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified); }
            catch { }
            switch ((swLengthUnit_e)unit)
            {
                case swLengthUnit_e.swCM: return (metres * 100).ToString("0.####") + " cm";
                case swLengthUnit_e.swMETER: return metres.ToString("0.######") + " m";
                case swLengthUnit_e.swINCHES: return (metres / 0.0254).ToString("0.####") + " in";
                case swLengthUnit_e.swFEET: return (metres / 0.3048).ToString("0.#####") + " ft";
                default: return (metres * 1000).ToString("0.###") + " mm";
            }
        }

        public static string FormatAngle(double rad) => Angles.ToDeg(rad).ToString("0.###") + "°";
    }
}
