using System.Drawing;

namespace FlagInjector;

static class HpFont
{
    public static Font Heading(float size, FontStyle style = FontStyle.Bold | FontStyle.Italic)
    {
        foreach (var name in new[] { "Palatino Linotype", "Book Antiqua", "Georgia" })
            try { return new Font(name, size, style); } catch { }
        return new Font(FontFamily.GenericSerif, size, style);
    }

    public static Font Body(float size, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style);

    public static Font Mono(float size, FontStyle style = FontStyle.Regular) =>
        new("Consolas", size, style);
}