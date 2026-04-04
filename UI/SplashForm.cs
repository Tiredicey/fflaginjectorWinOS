using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FlagInjector;

sealed class SplashForm : Form
{
    private sealed class Star   { public float X, Y, R, Alpha, Delta; }
    private sealed class Mote   { public float X, Y, VX, VY, Size, Alpha, Life, MaxLife; }

    private readonly System.Windows.Forms.Timer _master    = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer _minTimer  = new() { Interval = 820 };
    private readonly List<Star>  _stars  = new();
    private readonly List<Mote>  _motes  = new();
    private readonly Random      _rng    = new();

    private float  _outerRune;
    private float  _innerRune;
    private float  _mistPhase;
    private int    _typeIdx;
    private string _titleDisplay  = "";
    private int    _typeAccum;
    private float  _fadeOpacity;
    private bool   _readyToClose;
    private bool   _closeRequested;
    private int    _moteAccum;
    private float  _progressT;

    private const string _title    = "FFlag Injector";
    private const int    _typeSpeed = 4;
    private const int    _w        = 480;
    private const int    _h        = 300;

    public SplashForm()
    {
        FormBorderStyle  = FormBorderStyle.None;
        StartPosition    = FormStartPosition.CenterScreen;
        Size             = new Size(_w, _h);
        BackColor        = Color.FromArgb(13, 15, 28);
        ShowInTaskbar    = false;
        TopMost          = true;
        Opacity          = 0;
        AutoScaleMode    = AutoScaleMode.Font;

        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);

        for (int i = 0; i < 90; i++)
            _stars.Add(new Star
            {
                X     = _rng.NextSingle() * _w,
                Y     = _rng.NextSingle() * _h,
                R     = _rng.NextSingle() * 1.4f + 0.2f,
                Alpha = _rng.NextSingle(),
                Delta = (_rng.NextSingle() * 0.014f + 0.003f)
                        * (_rng.Next(2) == 0 ? 1f : -1f),
            });

        _master.Tick += OnTick;
        _master.Start();

        _minTimer.Tick += (_, _) => { _minTimer.Stop(); _readyToClose = true; if (_closeRequested) base.Close(); };
        _minTimer.Start();
    }

    public void RequestClose()
    {
        _closeRequested = true;
        if (_readyToClose) base.Close();
    }

    private void OnTick(object? s, EventArgs e)
    {
        _fadeOpacity = MathF.Min(1f, _fadeOpacity + 0.04f);
        Opacity      = _fadeOpacity;

        _outerRune  = (_outerRune  + 0.22f) % 360f;
        _innerRune  = (_innerRune  - 0.14f + 360f) % 360f;
        _mistPhase  = (_mistPhase  + 0.018f) % (MathF.PI * 2f);

        foreach (var st in _stars)
        {
            st.Alpha += st.Delta;
            if (st.Alpha > 1f)  { st.Alpha = 1f;  st.Delta = -MathF.Abs(st.Delta); }
            if (st.Alpha < 0.1f){ st.Alpha = 0.1f; st.Delta =  MathF.Abs(st.Delta); }
        }

        if (_typeIdx < _title.Length)
        {
            _typeAccum++;
            if (_typeAccum >= _typeSpeed)
            {
                _typeAccum = 0;
                _titleDisplay += _title[_typeIdx++];
                SpawnMotes(4);
            }
        }

        _progressT = MathF.Min(1f, _progressT + 0.004f);

        _moteAccum++;
        if (_moteAccum >= 9 && _motes.Count < 20) { _moteAccum = 0; SpawnMotes(1); }

        for (int i = _motes.Count - 1; i >= 0; i--)
        {
            var m   = _motes[i];
            m.X    += m.VX;
            m.Y    += m.VY;
            m.Life -= 1f;
            float t = m.Life / m.MaxLife;
            m.Alpha = t < 0.15f ? t / 0.15f : t;
            if (m.Life <= 0) _motes.RemoveAt(i);
        }

        Invalidate();
    }

    private void SpawnMotes(int count)
    {
        for (int i = 0; i < count; i++)
        {
            float life = _rng.NextSingle() * 90f + 60f;
            _motes.Add(new Mote
            {
                X       = _rng.NextSingle() * _w,
                Y       = _h * 0.55f + (_rng.NextSingle() - 0.5f) * 40f,
                VX      = (_rng.NextSingle() - 0.5f) * 1.2f,
                VY      = -(_rng.NextSingle() * 1.0f + 0.2f),
                Size    = _rng.NextSingle() * 3f + 1f,
                Life    = life,
                MaxLife = life,
            });
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bgBrush = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(13, 15, 28),
            Color.FromArgb(22, 14, 45),
            LinearGradientMode.ForwardDiagonal))
            g.FillRectangle(bgBrush, ClientRectangle);

        float mistA = (MathF.Sin(_mistPhase) * 0.5f + 0.5f) * 18f;
        using (var mistBrush = new SolidBrush(Color.FromArgb((int)mistA,
            Color.FromArgb(107, 75, 160))))
        {
            for (int xi = 0; xi < 3; xi++)
            for (int yi = 0; yi < 2; yi++)
            {
                float phase2 = _mistPhase + xi * 1.1f + yi * 0.7f;
                float ox = MathF.Sin(phase2) * 30f;
                g.FillEllipse(mistBrush,
                    xi * (_w / 3f) + ox - 60f,
                    yi * (_h / 2f) - 40f,
                    160f, 140f);
            }
        }

        foreach (var st in _stars)
        {
            using var sb = new SolidBrush(Color.FromArgb((int)(st.Alpha * 185),
                Color.FromArgb(232, 213, 176)));
            g.FillEllipse(sb, st.X - st.R, st.Y - st.R, st.R * 2f, st.R * 2f);
        }

        float cx = _w * 0.5f, cy = _h * 0.44f;
        DrawRuneRings(g, cx, cy);

        foreach (var m in _motes)
        {
            int a = (int)(m.Alpha * 220);
            if (a <= 0) continue;
            using var mb = new SolidBrush(Color.FromArgb(a, Color.FromArgb(201, 168, 76)));
            float hw = m.Size * 0.35f;
            g.FillRectangle(mb, m.X - hw,     m.Y - m.Size, hw * 2f,     m.Size * 2f);
            g.FillRectangle(mb, m.X - m.Size, m.Y - hw,     m.Size * 2f, hw * 2f);
        }

        string cursor = _typeIdx < _title.Length ? "▌" : "";
        using var titleFont = HpFont.Heading(22f);
        using var tBrush    = new SolidBrush(Color.FromArgb(201, 168, 76));
        var sf = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(_titleDisplay + cursor, titleFont, tBrush,
            new RectangleF(0, _h * 0.62f, _w, 40f), sf);

        using var subFont  = HpFont.Body(9f, FontStyle.Italic);
        using var subBrush = new SolidBrush(Color.FromArgb(154, 143, 112));

        string version = "";
        try { version = $"v{UpdateChecker.CurrentVersion}"; } catch { version = "v1.0"; }
        g.DrawString(version + " — Preparing...", subFont, subBrush,
            new RectangleF(0, _h * 0.62f + 44f, _w, 22f), sf);

        float barW  = _w * 0.55f;
        float barX  = (_w - barW) * 0.5f;
        float barY  = _h - 44f;
        float fillW = barW * _progressT;
        using var barBg   = new SolidBrush(Color.FromArgb(30, Color.FromArgb(201, 168, 76)));
        using var barFill = new LinearGradientBrush(
            new RectangleF(barX, barY, barW + 1, 3f),
            Color.FromArgb(107, 75, 160),
            Color.FromArgb(201, 168, 76),
            LinearGradientMode.Horizontal);
        g.FillRectangle(barBg, barX, barY, barW, 3f);
        if (fillW > 1f) g.FillRectangle(barFill, barX, barY, fillW, 3f);

        using var creditFont   = HpFont.Body(7.5f, FontStyle.Italic);
        using var creditBrush  = new SolidBrush(Color.FromArgb(90, 154, 143, 112));
        g.DrawString("made by tiredicey", creditFont, creditBrush,
            new RectangleF(0, _h - 20f, _w, 16f), sf);
    }

    private void DrawRuneRings(Graphics g, float cx, float cy)
    {
        float r1 = 68f, r2 = 52f, r3 = 36f;

        using var glow1 = new Pen(Color.FromArgb(20, Color.FromArgb(201, 168, 76)), 7f);
        g.DrawEllipse(glow1, cx - r1, cy - r1, r1 * 2f, r1 * 2f);

        using var ring1 = new Pen(Color.FromArgb(55, Color.FromArgb(201, 168, 76)), 1f);
        g.DrawEllipse(ring1, cx - r1, cy - r1, r1 * 2f, r1 * 2f);

        using var ring2 = new Pen(Color.FromArgb(40, Color.FromArgb(107, 75, 160)), 0.8f);
        g.DrawEllipse(ring2, cx - r2, cy - r2, r2 * 2f, r2 * 2f);

        using var ring3 = new Pen(Color.FromArgb(30, Color.FromArgb(201, 168, 76)), 0.6f);
        g.DrawEllipse(ring3, cx - r3, cy - r3, r3 * 2f, r3 * 2f);

        var st1 = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform(_outerRune);
        using var rp1 = new Pen(Color.FromArgb(60, Color.FromArgb(201, 168, 76)), 0.9f);
        DrawStarPoly(g, rp1, r1 - 4f, 7);
        g.Restore(st1);

        var st2 = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform(_innerRune);
        using var rp2 = new Pen(Color.FromArgb(50, Color.FromArgb(107, 75, 160)), 0.8f);
        DrawStarPoly(g, rp2, r2 - 4f, 5);
        g.Restore(st2);

        var st3 = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform(_outerRune * 1.3f);
        using var rp3 = new Pen(Color.FromArgb(35, Color.FromArgb(176, 184, 200)), 0.6f);
        DrawStarPoly(g, rp3, r3 - 3f, 6);
        g.Restore(st3);

        using var centerGlow = new SolidBrush(Color.FromArgb(18, Color.FromArgb(201, 168, 76)));
        g.FillEllipse(centerGlow, cx - 12f, cy - 12f, 24f, 24f);
        using var centerDot  = new SolidBrush(Color.FromArgb(80, Color.FromArgb(201, 168, 76)));
        g.FillEllipse(centerDot, cx - 3f, cy - 3f, 6f, 6f);
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _master.Stop();   _master.Dispose();
        _minTimer.Stop(); _minTimer.Dispose();
        base.OnFormClosed(e);
    }
}