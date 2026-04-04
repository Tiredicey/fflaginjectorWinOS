using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace FlagInjector;

sealed class AnimatedBackground : IDisposable
{
    private enum Preset { Stars, Mist, Runes, None, Custom }

    private sealed class Star     { public float X, Y, R, Alpha, Delta; }
    private sealed class Sparkle  { public float X, Y, VX, VY, Size, Alpha, Life, MaxLife; }
    private sealed class MistBlob { public float X, Y, W, H, Phase, Speed, Alpha; public Color Tint; }

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Random        _rng      = new();
    private readonly List<Star>    _stars    = new();
    private readonly List<Sparkle> _sparkles = new();
    private readonly List<MistBlob> _mist    = new();

    private float  _runeAngle;
    private float  _runeInnerAngle;
    private int    _sparkleAccum;
    private Image? _bgImage;
    private float  _bgOpacity = 0.30f;
    private Preset _preset    = Preset.Stars;
    private int    _w, _h;

    public event Action? FrameUpdated;

    public AnimatedBackground()
    {
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void SetRenderSize(int w, int h)
    {
        if (w == _w && h == _h) return;
        _w = w; _h = h;
        Rebuild();
    }

    public void SetPreset(string name)
    {
        _preset = name switch
        {
            "Mist"   => Preset.Mist,
            "Runes"  => Preset.Runes,
            "None"   => Preset.None,
            "Custom" => Preset.Custom,
            _        => Preset.Stars,
        };
        Rebuild();
    }

    public void SetBackground(Image? img, float opacity)
    {
        _bgImage?.Dispose();
        _bgImage   = img;
        _bgOpacity = opacity;
    }

    public void SetParticleCount(int count) => Rebuild(count);

    public void Invalidate() => FrameUpdated?.Invoke();

    private void Rebuild(int starCount = 80)
    {
        _stars.Clear(); _sparkles.Clear(); _mist.Clear();
        if (_w <= 0 || _h <= 0) return;

        if (_preset is Preset.Stars or Preset.Custom)
        {
            for (int i = 0; i < starCount; i++)
                _stars.Add(new Star
                {
                    X     = _rng.NextSingle() * _w,
                    Y     = _rng.NextSingle() * _h,
                    R     = _rng.NextSingle() * 1.5f + 0.2f,
                    Alpha = _rng.NextSingle(),
                    Delta = (_rng.NextSingle() * 0.016f + 0.004f) * (_rng.Next(2) == 0 ? 1f : -1f),
                });
        }

        if (_preset == Preset.Mist)
        {
            for (int i = 0; i < starCount / 5; i++)
                _stars.Add(new Star
                {
                    X     = _rng.NextSingle() * _w,
                    Y     = _rng.NextSingle() * _h,
                    R     = _rng.NextSingle() * 0.8f + 0.2f,
                    Alpha = _rng.NextSingle() * 0.4f,
                    Delta = (_rng.NextSingle() * 0.006f + 0.001f) * (_rng.Next(2) == 0 ? 1f : -1f),
                });

            Color[] palette =
            {
                Color.FromArgb(107, 75,  160),
                Color.FromArgb(45,  53,   97),
                Color.FromArgb(20,  22,   41),
                Color.FromArgb(74,  79,  122),
                Color.FromArgb(26,  30,   53),
            };
            for (int i = 0; i < 12; i++)
            {
                float bw = _rng.NextSingle() * _w * 0.6f + _w * 0.2f;
                _mist.Add(new MistBlob
                {
                    X     = _rng.NextSingle() * _w,
                    Y     = _rng.NextSingle() * _h,
                    W     = bw,
                    H     = bw * (_rng.NextSingle() * 0.4f + 0.25f),
                    Phase = _rng.NextSingle() * MathF.PI * 2f,
                    Speed = _rng.NextSingle() * 0.008f + 0.003f,
                    Alpha = _rng.NextSingle() * 0.10f + 0.04f,
                    Tint  = palette[_rng.Next(palette.Length)],
                });
            }
        }
    }

    private void OnTick(object? s, EventArgs e)
    {
        if (_preset == Preset.None || _w <= 0) return;

        _runeAngle      = (_runeAngle      + 0.18f) % 360f;
        _runeInnerAngle = (_runeInnerAngle - 0.11f + 360f) % 360f;

        foreach (var st in _stars)
        {
            st.Alpha += st.Delta;
            if (st.Alpha > 1f)    { st.Alpha = 1f;    st.Delta = -MathF.Abs(st.Delta); }
            if (st.Alpha < 0.05f) { st.Alpha = 0.05f; st.Delta =  MathF.Abs(st.Delta); }
        }

        if (_preset == Preset.Mist)
            foreach (var m in _mist)
            {
                m.Phase += m.Speed;
                m.X     += MathF.Sin(m.Phase * 0.7f) * 0.35f;
                m.Y     += MathF.Cos(m.Phase * 0.5f) * 0.18f;
                if (m.X < -m.W)         m.X = _w + m.W * 0.5f;
                if (m.X >  _w + m.W)    m.X = -m.W * 0.5f;
                if (m.Y < -m.H)         m.Y = _h + m.H * 0.5f;
                if (m.Y >  _h + m.H)    m.Y = -m.H * 0.5f;
            }

        if (_preset is Preset.Stars or Preset.Custom)
        {
            _sparkleAccum++;
            if (_sparkleAccum >= 7 && _sparkles.Count < 28)
            {
                _sparkleAccum = 0;
                float life = _rng.NextSingle() * 110f + 70f;
                _sparkles.Add(new Sparkle
                {
                    X       = _rng.NextSingle() * _w,
                    Y       = _h + 8f,
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
                sp.X   += sp.VX; sp.Y += sp.VY; sp.Life -= 1f;
                float t  = sp.Life / sp.MaxLife;
                sp.Alpha = t < 0.15f ? t / 0.15f : t > 0.75f ? 1f - (1f - t) / 0.25f : 1f;
                sp.Alpha = MathF.Max(0f, sp.Alpha);
                if (sp.Life <= 0 || sp.Y < -12) _sparkles.RemoveAt(i);
            }
        }

        FrameUpdated?.Invoke();
    }

    public void RenderTo(Graphics g, int w, int h)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var c = Theme.C;

        using (var bg = new SolidBrush(c.Bg))
            g.FillRectangle(bg, 0, 0, w, h);

        if (_preset == Preset.None) return;

        if (_bgImage != null && _preset == Preset.Custom)
        {
            var ia = new ImageAttributes();
            var cm = new ColorMatrix { Matrix33 = _bgOpacity };
            ia.SetColorMatrix(cm);
            g.DrawImage(_bgImage, new Rectangle(0, 0, w, h),
                0, 0, _bgImage.Width, _bgImage.Height, GraphicsUnit.Pixel, ia);
        }

        if (_preset == Preset.Mist)
            foreach (var m in _mist)
            {
                int a = (int)(m.Alpha * 255);
                if (a <= 0) continue;
                using var br = new SolidBrush(Color.FromArgb(a, m.Tint));
                g.FillEllipse(br, m.X - m.W * 0.5f, m.Y - m.H * 0.5f, m.W, m.H);
            }

        foreach (var st in _stars)
        {
            int a = (int)(st.Alpha * (_preset == Preset.Mist ? 120 : 195));
            using var b = new SolidBrush(Color.FromArgb(a, c.Fg));
            g.FillEllipse(b, st.X - st.R, st.Y - st.R, st.R * 2f, st.R * 2f);
        }

        if (_preset is Preset.Stars or Preset.Custom)
            foreach (var sp in _sparkles)
            {
                int a = (int)(sp.Alpha * 245);
                if (a <= 0) continue;
                using var b = new SolidBrush(Color.FromArgb(a, c.Accent));
                float hw = sp.Size * 0.35f;
                g.FillRectangle(b, sp.X - hw,      sp.Y - sp.Size, hw * 2f,      sp.Size * 2f);
                g.FillRectangle(b, sp.X - sp.Size, sp.Y - hw,      sp.Size * 2f, hw * 2f);
            }

        if (_preset == Preset.Runes) DrawScatteredRunes(g, c, w, h);
        DrawRuneCircle(g, c, w, h);
    }

    private void DrawScatteredRunes(Graphics g, ThemeColors c, int w, int h)
    {
        float[] xs = { 0.15f, 0.5f, 0.82f };
        float[] ys = { 0.2f,  0.75f, 0.5f };
        for (int ri = 0; ri < xs.Length; ri++)
        {
            float cx = w * xs[ri], cy = h * ys[ri], r = 38f + ri * 12f;
            using var glow = new Pen(Color.FromArgb(18, c.Purple), 6f);
            g.DrawEllipse(glow, cx - r, cy - r, r * 2f, r * 2f);
            using var ring = new Pen(Color.FromArgb(35, c.Accent), 0.9f);
            g.DrawEllipse(ring, cx - r, cy - r, r * 2f, r * 2f);
            var st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(_runeAngle * (ri % 2 == 0 ? 1f : -1f));
            using var rp = new Pen(Color.FromArgb(45, ri % 2 == 0 ? c.Accent : c.Purple), 0.8f);
            DrawStarPoly(g, rp, r - 5f, ri % 2 == 0 ? 7 : 5);
            g.Restore(st);
        }
    }

    private void DrawRuneCircle(Graphics g, ThemeColors c, int w, int h)
    {
        if (w < 120 || h < 120) return;
        float cx = w - 72f, cy = h - 72f, r = 52f;
        bool isMist = _preset == Preset.Mist;
        int baseA = isMist ? 15 : 22;
        using var glow  = new Pen(Color.FromArgb(baseA, c.Accent), 5f);
        g.DrawEllipse(glow, cx - r, cy - r, r * 2f, r * 2f);
        using var outer = new Pen(Color.FromArgb(baseA + 28, c.Accent), 1f);
        g.DrawEllipse(outer, cx - r, cy - r, r * 2f, r * 2f);
        using var inner = new Pen(Color.FromArgb(baseA + 18, c.Purple), 0.8f);
        g.DrawEllipse(inner, cx - r + 9f, cy - r + 9f, (r - 9f) * 2f, (r - 9f) * 2f);
        var st1 = g.Save();
        g.TranslateTransform(cx, cy); g.RotateTransform(_runeAngle);
        using var rp1 = new Pen(Color.FromArgb(baseA + 33, c.Accent), 0.9f);
        DrawStarPoly(g, rp1, r - 5f, 7);
        g.Restore(st1);
        var st2 = g.Save();
        g.TranslateTransform(cx, cy); g.RotateTransform(_runeInnerAngle);
        using var rp2 = new Pen(Color.FromArgb(baseA + 23, c.Purple), 0.75f);
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

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _bgImage?.Dispose();
    }
}
