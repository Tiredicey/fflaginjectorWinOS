using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FlagInjector;

sealed class GlowButton : Control
{
    private float _hoverT;
    private bool  _hovered;
    private bool  _pressed;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 14 };
    private Font  _buttonFont = HpFont.Body(9f, FontStyle.Bold);

    public Color  BaseColor  { get; set; }
    public Color  HoverColor { get; set; }
    public Color  FgColor    { get; set; }
    public Color  GlowColor  { get; set; }

    public string ButtonText
    {
        get => Text;
        set { Text = value; AccessibleName = value; Invalidate(); }
    }

    public Font ButtonFont
    {
        get => _buttonFont;
        set { _buttonFont?.Dispose(); _buttonFont = value; Invalidate(); }
    }

    public event EventHandler? Clicked;

    public GlowButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint             |
                 ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.PushButton;
        Cursor         = Cursors.Hand;
        Height         = 34;
        BaseColor      = Theme.C.Surface;
        HoverColor     = Theme.C.Hover;
        FgColor        = Theme.C.Fg;
        GlowColor      = Theme.C.Accent;
        _anim.Tick    += OnTick;
    }

    private void OnTick(object? s, EventArgs e)
    {
        float target = _hovered ? 1f : 0f;
        float delta  = 0.09f;
        _hoverT = _hoverT < target
            ? MathF.Min(_hoverT + delta, target)
            : MathF.Max(_hoverT - delta, target);
        Invalidate();
        if (MathF.Abs(_hoverT - target) < 0.005f) _anim.Stop();
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true;  _anim.Start(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _anim.Start(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool wasPressed = _pressed;
        _pressed = false;
        Invalidate();
        if (wasPressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            Clicked?.Invoke(this, EventArgs.Empty);
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { _pressed = true; Invalidate(); }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            _pressed = false;
            Clicked?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
        base.OnKeyUp(e);
    }

    private static Color Lerp(Color a, Color b, float t) =>
        Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g  = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rc = ClientRectangle;

        if (_hoverT > 0.02f)
        {
            using var glow = new SolidBrush(Color.FromArgb((int)(_hoverT * 55), GlowColor));
            g.FillRectangle(glow, Rectangle.Inflate(rc, 4, 4));
        }

        var fill = Lerp(BaseColor, HoverColor, _hoverT);
        if (_pressed) fill = Lerp(fill, GlowColor, 0.28f);

        using var bgBrush = new SolidBrush(fill);
        g.FillRectangle(bgBrush, rc);

        float ba = 0.25f + _hoverT * 0.55f;
        using var borderPen = new Pen(Color.FromArgb((int)(ba * 255), GlowColor), 1f);
        g.DrawRectangle(borderPen, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);

        var sf = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var fgBrush = new SolidBrush(FgColor);
        g.DrawString(Text, _buttonFont, fgBrush, (RectangleF)rc, sf);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _anim.Stop(); _anim.Dispose(); _buttonFont.Dispose(); }
        base.Dispose(disposing);
    }
}