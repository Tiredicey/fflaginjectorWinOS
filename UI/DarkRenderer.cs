using System.Drawing;
using System.Windows.Forms;

namespace FlagInjector;

sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.C.Surface);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected && e.Item.Enabled)
        {
            using var b = new SolidBrush(Theme.C.Hover);
            e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled)
            e.TextColor = Theme.C.Border;
        else if (e.Item is ToolStripStatusLabel lbl &&
                 lbl.ForeColor != SystemColors.Control &&
                 lbl.ForeColor != SystemColors.ControlText)
            e.TextColor = lbl.ForeColor;
        else
            e.TextColor = Theme.C.Fg;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item.Enabled ? Theme.C.Accent : Theme.C.Border;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.C.Surface);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (e.Item == null) return;
        int y = e.Item.Height / 2;
        using var p = new Pen(Theme.C.Border);
        e.Graphics.DrawLine(p, 4, y, e.Item.Width - 4, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is StatusStrip) return;
        using var p = new Pen(Theme.C.Border);
        var b = e.AffectedBounds;
        e.Graphics.DrawRectangle(p, b.X, b.Y, b.Width - 1, b.Height - 1);
    }
}
