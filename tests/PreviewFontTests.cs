using System;
using System.Reflection;
using UnityEngine;

// Only asset discovery and system-font creation are substituted. Font selection
// and caching run from the production PreviewFont.cs, without Unity native code.
namespace UnityEngine
{
    public sealed class Font
    {
        public string name;
        public static int Creations;
        public static string[] FallbackNames;
        public static Font CreateDynamicFontFromOSFont(string[] names, int size)
        { Creations++; FallbackNames = names; return new Font { name = "system-font" }; }
    }
    public static class Resources
    {
        public static Font[] Fonts;
        public static T[] FindObjectsOfTypeAll<T>() { return (T[])(object)Fonts; }
    }
}
namespace LOMSave.Probe
{
    internal static class LOMSaveProbePlugin
    {
        internal static void WriteEvent(string name, object data) { }
    }
    public static class PreviewFontTests
    {
        private static int _checks;
        private static void Check(bool value, string message)
        { _checks++; if (!value) throw new Exception(message); }
        private static void Reset()
        {
            foreach (var name in new[] { "_body", "_fallback" })
                typeof(PreviewFont).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            Font.Creations = 0;
        }
        public static string Run()
        {
            var handwriting = new Font { name = "DFXingShuSC24-W5" };
            var body = new Font { name = "SourceHanSerifTC-Bold" };
            var regular = new Font { name = "SourceHanSerifTC-Regular" };
            Check(PreviewFont.SelectBodyFont(new[] { handwriting, body, regular }) == body,
                "A loaded handwriting dialog must not determine the preview font");
            Check(PreviewFont.SelectBodyFont(new[] { regular, body, handwriting }) == body,
                "Font choice is independent of Unity enumeration order");
            Check(PreviewFont.SelectBodyFont(new[] { handwriting, regular }) == regular,
                "Use a game body face when the preferred bold face is unavailable");
            Check(PreviewFont.SelectBodyFont(new[] { handwriting }) == null,
                "Do not silently use handwriting when the body family is missing");
            Check(PreviewFont.SelectBodyFont(new[] { new Font { name = "Exo2-Bold" }, handwriting }) == null,
                "Latin/decorative fonts are not acceptable Chinese body fonts");
            Check(PreviewFont.SelectBodyFont(null) == null
                && PreviewFont.SelectBodyFont(new Font[0]) == null,
                "Missing asset lists allow the Chinese system fallback");
            Check(PreviewFont.SelectBodyFont(new[] { null, new Font(), body }) == body,
                "Null/destroyed or unnamed font entries do not hide a valid body font");
            var futureA = new Font { name = "SourceHanSerifSC-NewA" };
            var futureB = new Font { name = "SourceHanSerifSC-NewB" };
            Check(PreviewFont.SelectBodyFont(new[] { handwriting, futureB, futureA }) == futureA
                && PreviewFont.SelectBodyFont(new[] { futureA, futureB }) == futureA,
                "New regional body variants have a deterministic family fallback");
            Reset();
            Resources.Fonts = new[] { handwriting, body };
            Check(PreviewFont.Resolve() == body && Font.Creations == 0,
                "Use the actual game font without creating an OS font");
            Resources.Fonts = new[] { handwriting };
            Check(PreviewFont.Resolve() == body,
                "Opening a different dialog or menu cannot switch previews to handwriting");
            Reset();
            Resources.Fonts = new[] { handwriting };
            var fallback = PreviewFont.Resolve();
            Check(fallback != handwriting && fallback.name == "system-font"
                && Font.FallbackNames[0] == "SimSun", "Use a Chinese reading-font fallback instead of a decorative dialog");
            Check(PreviewFont.Resolve() == fallback && Font.Creations == 1,
                "Repeated early previews reuse the system font");
            Resources.Fonts = new[] { body, handwriting };
            Check(PreviewFont.Resolve() == body,
                "An early system fallback must not prevent the later loaded game body font");
            return _checks + " preview font regression assertions passed";
        }
    }
}
