using System;

namespace LOMSave.Probe
{
    internal static class DialogueTiming
    {
        // Conservative dialogue-reading defaults; research context is in README.
        private const float StartBufferSeconds = 0.5f;
        private const float SecondsPerCharacter = 0.20f;

        internal static float Delay(string text, float speed, bool dynamic)
        {
            speed = Math.Max(0.5f, Math.Min(5f, speed));
            if (!dynamic || string.IsNullOrEmpty(text)) return 8f / speed;
            var count = 0;
            var pause = 0f;
            var angle = false;
            var brace = false;
            foreach (var c in text)
            {
                if (c == '<') { angle = true; continue; }
                if (c == '>' && angle) { angle = false; continue; }
                if (c == '{') { brace = true; continue; }
                if (c == '}' && brace) { brace = false; continue; }
                if (angle || brace || char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                count++;
                if (",，、;；:：".IndexOf(c) >= 0) pause += 0.2f;
                else if (".。!！?？".IndexOf(c) >= 0) pause += 0.4f;
                else if (c == '…') pause += 0.3f;
            }
            if (count == 0) return 8f / speed;
            // Clamp the baseline, then scale the entire reading interval. Clamping
            // afterward made several slider values indistinguishable on short lines.
            return Math.Max(1.2f, Math.Min(15f, StartBufferSeconds + count * SecondsPerCharacter + pause)) / speed;
        }
    }
}
