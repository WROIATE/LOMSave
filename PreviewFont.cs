using Fungus;
using Mortal.Core;
using System.Linq;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class PreviewFont
    {
        private static Font _fallback;

        internal static Font Resolve()
        {
            // Option source fields can be hidden localization keys with a decorative
            // prefab font. Use the actual dialogue body after localization instead.
            var active = SayDialog.ActiveSayDialog;
            if (active != null && active.StoryTextObject != null && active.StoryTextObject.font != null)
                return active.StoryTextObject.font;
            var dialog = Resources.FindObjectsOfTypeAll<CustomSayDialog>()
                .FirstOrDefault(x => x.gameObject.scene.IsValid()
                    && x.StoryTextObject != null && x.StoryTextObject.font != null);
            if (dialog != null) return dialog.StoryTextObject.font;
            if (_fallback == null) _fallback = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 24);
            return _fallback;
        }
    }
}
