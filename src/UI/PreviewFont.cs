using System;
using System.Linq;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class PreviewFont
    {
        private static readonly string[] BodyFonts =
        {
            "SourceHanSerifTC-Bold", "SourceHanSerifTC-Regular",
            "SourceHanSerif-Bold", "SourceHanSerif-Regular", "SourceHanSerifTW-SemiBold"
        };
        private static Font _body;
        private static Font _fallback;

        internal static Font Resolve()
        {
            // Dialogues can use handwriting fonts, including inactive/prefab dialogs.
            // Select a body-font asset independently of the current scene or speaker.
            if (_body != null) return _body;
            _body = SelectBodyFont(Resources.FindObjectsOfTypeAll<Font>());
            if (_body != null)
            {
                LOMSaveProbePlugin.WriteEvent("preview_font_selected", new
                { font = _body.name, source = "game_serif" });
                return _body;
            }
            // Retry the game font on subsequent calls: it may not be loaded at title.
            if (_fallback == null)
            {
                _fallback = Font.CreateDynamicFontFromOSFont(
                    new[] { "SimSun", "PMingLiU", "Noto Serif CJK SC", "Microsoft YaHei" }, 24);
                LOMSaveProbePlugin.WriteEvent("preview_font_selected", new
                { font = _fallback == null ? null : _fallback.name, source = "system_chinese_fallback" });
            }
            return _fallback;
        }

        internal static Font SelectBodyFont(Font[] fonts)
        {
            if (fonts == null) return null;
            foreach (var name in BodyFonts)
            {
                var match = fonts.FirstOrDefault(font => font != null
                    && string.Equals(font.name, name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            // Future game versions may ship another regional variant of this family.
            // Sort explicitly; Unity's object enumeration order is not a font priority.
            return fonts.Where(font => font != null && !string.IsNullOrEmpty(font.name)
                    && font.name.StartsWith("SourceHanSerif", StringComparison.OrdinalIgnoreCase))
                .OrderBy(font => font.name, StringComparer.Ordinal).FirstOrDefault();
        }
    }
}
