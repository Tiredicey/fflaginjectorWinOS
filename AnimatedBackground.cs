using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace FlagInjector;

sealed class AnimatedBackground : Panel
{
    private sealed class Star
    {
        public float X, Y, Radius, Alpha, AlphaDelta;
    }

    private sealed class Sparkle
    {
        public float X, Y, VX, VY, Size, Alpha, Life, MaxLife;
    }

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Random                      _rng   = new();
    private readonly List<Star>                  _stars    = new();
    private readonly List<Sparkle>               _sparkles = new();
    private float  _runeAngle;
    private float  _runeInnerAngle;
    private int    _sparkleAccum;
    private Image? _bgImage;
    private float  _bgOpacity = 0.30f;

    public AnimatedBackground()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);
        Dock              = DockStyle.Fill;
        _timer.Tick      += OnTick;
        _timer.Start();
        Resize           += (_, _) => RegenerateStars();
        HandleCreated    += (_, _) => RegenerateStars();
    }

    public void SetBackground(Image? img, float opacity)
    {
        _bgImage?.Dispose();
        _bgImage    = img;
        _bgOpacity  = opacity;
    }

    public void SetParticleCount(int count)
    {
        RegenerateStars(count);
    }

    private void RegenerateStars(int count = 80)
    {
        _stars.Clear();
        if (Width <= 0 || Height <= 0) return;
        for (int i = 0; i < count; i++)
        {
            _stars.Add(new Star
            {
                X          = _rng.NextSingle() * Width,
                Y          = _rng.NextSingle() * Height,
                Radius     = _rng.NextSingle() * 1.6f + 0.3f,
                Alpha      = _rng.NextSingle(),
                AlphaDelta = (_rng.NextSingle() * 0.018f + 0.004f)
                             * (_rng.Next(2) == 0 ? 1f : -1f),
            });
        }
    }

    private void OnTick(object? s, EventArgs e)
    {
        if (_stars.Count == 0 && Width > 0) RegenerateStars();

        _runeAngle      = (_runeAngle      + 0.18f) % 360f;
        _runeInnerAngle = (_runeInnerAngle - 0.11f + 360f) % 360f;

        foreach (var st in _stars)
        {
            st.Alpha += st.AlphaDelta;
            if (st.Alpha > 1f)  { st.Alpha = 1f;  st.AlphaDelta = -MathF.Abs(st.AlphaDelta); }
            if (st.Alpha < 0.1f){ st.Alpha = 0.1f; st.AlphaDelta =  MathF.Abs(st.AlphaDelta); }
        }

        _sparkleAccum++;
        if (_sparkleAccum >= 7 && _sparkles.Count < 30)
        {
            _sparkleAccum = 0;
            float life = _rng.NextSingle() * 110f + 70f;
            _sparkles.Add(new Sparkle
            {
                X       = _rng.NextSingle() * Width,
                Y       = Height + 8f,
                VX      = (_rng.NextSingle() - 0.5f) * 0.7f,
                VY      = -(_rng.NextSingle() * 0.9f + 0.3f),
                Size    = _rng.NextSingle() * 3.5f + 1f,
                Life    = life,
                MaxLife = life,
            });
        }

        for (int i = _sparkles.Count - 1; i >= 0; i--)
        {
            var sp  = _sparkles[i];
            sp.X   += sp.VX;
            sp.Y   += sp.VY;
            sp.Life -= 1f;
            float t  = sp.Life / sp.MaxLife;
            sp.Alpha = t < 0.15f ? t / 0.15f : t > 0.75f ? 1f - (1f - t) / 0.25f : 1f;
            sp.Alpha = MathF.Max(0f, sp.Alpha);
            if (sp.Life <= 0 || sp.Y < -12) _sparkles.RemoveAt(i);
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var c = Theme.C;

        using (var bg = new SolidBrush(c.Bg))
            g.FillRectangle(bg, ClientRectangle);

        if (_bgImage != null)
        {
            var ia = new ImageAttributes();
            var cm = new ColorMatrix { Matrix33 = _bgOpacity };
            ia.SetColorMatrix(cm);
            g.DrawImage(_bgImage,
                new Rectangle(0, 0, Width, Height),
                0, 0, _bgImage.Width, _bgImage.Height,
                GraphicsUnit.Pixel, ia);
        }

        foreach (var st in _stars)
        {
            using var b = new SolidBrush(Color.FromArgb((int)(st.Alpha * 195), c.Fg));
            g.FillEllipse(b, st.X - st.Radius, st.Y - st.Radius,
                          st.Radius * 2f, st.Radius * 2f);
        }

        foreach (var sp in _sparkles)
        {
            int a = (int)(sp.Alpha * 245);
            if (a <= 0) continue;
            using var b = new SolidBrush(Color.FromArgb(a, c.Accent));
            float hw = sp.Size * 0.35f, hh = sp.Size;
            g.FillRectangle(b, sp.X - hw,       sp.Y - hh, hw * 2f, hh * 2f);
            g.FillRectangle(b, sp.X - sp.Size,  sp.Y - hw, sp.Size * 2f, hw * 2f);
        }

        DrawRuneCircle(g, c);
    }

    private void DrawRuneCircle(Graphics g, ThemeColors c)
    {
        if (Width < 120 || Height < 120) return;
        float cx = Width - 72f, cy = Height - 72f, r = 52f;

        using var glow = new Pen(Color.FromArgb(22, c.Accent), 5f);
        g.DrawEllipse(glow, cx - r, cy - r, r * 2f, r * 2f);
        using var outer = new Pen(Color.FromArgb(50, c.Accent), 1f);
        g.DrawEllipse(outer, cx - r, cy - r, r * 2f, r * 2f);
        using var inner = new Pen(Color.FromArgb(40, c.Purple), 0.8f);
        g.DrawEllipse(inner, cx - r + 9f, cy - r + 9f, (r - 9f) * 2f, (r - 9f) * 2f);

        var st1 = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform(_runeAngle);
        using var rp1 = new Pen(Color.FromArgb(55, c.Accent), 0.9f);
        DrawStarPoly(g, rp1, r - 5f, 7);
        g.Restore(st1);

        var st2 = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform(_runeInnerAngle);
        using var rp2 = new Pen(Color.FromArgb(45, c.Purple), 0.75f);
        DrawStarPoly(g, rp2, r - 16f, 5);
        g.Restore(st2);
    }

    private static void DrawStarPoly(Graphics g, Pen pen, float r, int sides)
    {
        for (int i = 0; i < sides; i++)
        {
            double a1 = 2 * Math.PI * i / sides;
            double a2 = 2 * Math.PI * ((i + 2) % sides) / sides;
            g.DrawLine(pen,
                (float)(Math.Cos(a1) * r), (float)(Math.Sin(a1) * r),
                (float)(Math.Cos(a2) * r), (float)(Math.Sin(a2) * r));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Stop(); _timer.Dispose(); _bgImage?.Dispose(); }
        base.Dispose(disposing);
    }
}