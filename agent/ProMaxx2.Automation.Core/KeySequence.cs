namespace ProMaxx2.Automation.Core;

/// <summary>หนึ่งจังหวะของการกดคีย์: ข้อความธรรมดา (<see cref="Text"/>) หรือปุ่มพิเศษ (<see cref="Key"/>) พร้อม modifier.
/// ตัวอักษรเดี่ยวที่มี modifier (เช่น Alt+F) เก็บใน <see cref="Text"/> ยาว 1 ตัวพร้อม flag ของ modifier</summary>
public sealed record KeyStroke(string? Text, string? Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    public bool HasModifier => Ctrl || Alt || Shift;
}

/// <summary>AUT-AGT-002: แปลง key string แบบ SendKeys (<c>{ENTER}</c>, <c>{F8}</c>, <c>%F</c>, <c>^S</c>, <c>+{TAB}</c>) เป็นลำดับ
/// <see cref="KeyStroke"/> — เดิม driver ส่งสตริงทั้งก้อนให้ <c>Keyboard.Type</c> ซึ่งพิมพ์ "{Enter}" ออกไปเป็นตัวอักษรตรง ๆ
/// ทำให้ LOGIN/OPEN_MENU/SAVE_DOCUMENT/SELECT_ITEM ที่พึ่งปุ่มพิเศษล้มเหลวเงียบ ๆ</summary>
public static class KeySequence
{
    public static readonly IReadOnlySet<string> SpecialKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ENTER", "TAB", "ESC", "ESCAPE", "BACKSPACE", "BS", "DELETE", "DEL", "INSERT", "INS", "HOME", "END",
        "PGUP", "PGDN", "UP", "DOWN", "LEFT", "RIGHT", "SPACE",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
    };

    public static IReadOnlyList<KeyStroke> Parse(string input)
    {
        var result = new List<KeyStroke>();
        var text = new System.Text.StringBuilder();
        bool ctrl = false, alt = false, shift = false;
        void FlushText() { if (text.Length > 0) { result.Add(new KeyStroke(text.ToString(), null)); text.Clear(); } }

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c is '^' or '%' or '+')
            {
                FlushText();
                if (c == '^') ctrl = true; else if (c == '%') alt = true; else shift = true;
                continue;
            }
            string? key = null;
            string? literal = null;
            if (c == '{')
            {
                if (input.AsSpan(i).StartsWith("{{}")) { literal = "{"; i += 2; }
                else if (input.AsSpan(i).StartsWith("{}}")) { literal = "}"; i += 2; }
                else
                {
                    var close = input.IndexOf('}', i + 1);
                    if (close < 0) throw new ArgumentException($"Unclosed '{{' in key sequence '{input}' (AUT-DSL-002).");
                    var name = input[(i + 1)..close].Trim();
                    if (!SpecialKeys.Contains(name)) throw new ArgumentException($"Unsupported key '{{{name}}}' (AUT-DSL-002).");
                    key = Normalize(name);
                    i = close;
                }
            }
            else literal = c.ToString();

            if (ctrl || alt || shift)
            {
                result.Add(new KeyStroke(literal, key, ctrl, alt, shift));
                ctrl = alt = shift = false;
            }
            else if (key is not null)
            {
                FlushText();
                result.Add(new KeyStroke(null, key));
            }
            else text.Append(literal);
        }
        if (ctrl || alt || shift) throw new ArgumentException($"Modifier without a key in '{input}' (AUT-DSL-002).");
        FlushText();
        return result;
    }

    private static string Normalize(string name) => name.ToUpperInvariant() switch
    {
        "ESCAPE" => "ESC",
        "BS" => "BACKSPACE",
        "DEL" => "DELETE",
        "INS" => "INSERT",
        var other => other,
    };
}
