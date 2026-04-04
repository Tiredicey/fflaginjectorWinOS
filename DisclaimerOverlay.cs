using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FlagInjector;

sealed class DisclaimerOverlay : Form
{
    private sealed class Spark { public float X, Y, VX, VY, Life, MaxLife; }

    private readonly System.Windows.Forms.Timer _master  = new() { Interval = 16 };
    private readonly List<Spark>                _sparks  = new();
    private readonly Random                     _rng     = new();

    private float  _slideY;
    private float  _targetY;
    private bool   _slideDone;
    private int    _typeIdx;
    private string _displayed = "";
    private int    _typeAccum;
    private float  _glowPhase;
    private float  _borderGlow;
    private int    _autoClose;

    private const string _fullText   = "made by tiredicey";
    private const int    _typeSpeed  = 3;
    private const int    _holdFrames = 300;

    public DisclaimerOverlay()
    {
        FormBorderStyle  = FormBorderStyle.None;
        StartPosition    = FormStartPosition.Manual;
        Size             = new Size(430, 230);
        BackColor        = Theme.C.Surface;
        ShowInTaskbar    = false;
        TopMost          = true;
        AutoScaleMode    = AutoScaleMode.Font;

        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        _targetY   = screen.Bottom - Height - 24f;
        _slideY    = screen.Bottom + 20f;
        Location   = new Point(screen.Right - Width - 24, (int)_slideY);

        _master.Tick += OnTick;
        _master.Start();
    }

    private void OnTick(object? s, EventArgs e)
    {
        if (!_slideDone)
        {
            _slideY += (_targetY - _slideY) * 0.13f;
            Location = new Point(Location.X, (int)_slideY);
            if (MathF.Abs(_slideY - _targetY) < 0.8f)
            {
                Location   = new Point(Location.X, (int)_targetY);
                _slideDone = true;
            }
        }

        _glowPhase   = (_glowPhase + 0.055f) % (MathF.PI * 2f);
        _borderGlow  = (MathF.Sin(_glowPhase) * 0.5f + 0.5f);

        if (_slideDone && _typeIdx < _fullText.Length)
        {
            _typeAccum++;
            if (_typeAccum >= _typeSpeed)
            {
                _typeAccum = 0;
                _displayed += _fullText[_typeIdx++];
                SpawnSparks();
            }
        }
        else if (_typeIdx >= _fullText.Length)
        {
            _autoClose++;
            if (_autoClose >= _holdFrames) Close();
        }

        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var sp  = _sparks[i];
            sp.X   += sp.VX;
            sp.Y   += sp.VY;
            sp.Life -= 1f;
            if (sp.Life <= 0) _sparks.RemoveAt(i);
        }

        Invalidate();
    }

    private void SpawnSparks()
    {
        for (int i = 0; i < 4; i++)
        {
            _sparks.Add(new Spark
            {
                X       = Width * 0.5f + (_rng.NextSingle() - 0.5f) * 160f,
                Y       = 105f,
                VX      = (_rng.NextSingle() - 0.5f) * 2.8f,
                VY      = -(_rng.NextSingle() * 2.2f + 0.4f),
                Life    = 35f + _rng.NextSingle() * 25f,
                MaxLife = 60f,
            });
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var c = Theme.C;

        using (var bg = new SolidBrush(c.Surface))
            g.FillRectangle(bg, ClientRectangle);

        int glowA = (int)((_borderGlow * 0.6f + 0.2f) * 255);
        using var borderPen = new Pen(Color.FromArgb(glowA, c.Accent), 1.8f);
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        using var cornerPen = new Pen(Color.FromArgb(glowA / 2, c.Purple), 0.8f);
        g.DrawRectangle(cornerPen, 3, 3, Width - 7, Height - 7);

        var sf = new StringFormat { Alignment = StringAlignment.Center };

        using var titleFont  = HpFont.Heading(12f);
        using var subFont    = HpFont.Body(8.5f, FontStyle.Italic);
        using var bigFont    = HpFont.Heading(17f);
        using var accentBrush = new SolidBrush(c.Accent);
        using var subBrush    = new SolidBrush(c.Sub);
        using var fgBrush     = new SolidBrush(c.Fg);

        g.DrawString("⚡ FFlag Injector ⚡", titleFont, accentBrush,
            new RectangleF(0, 16, Width, 28), sf);

        g.DrawString("A tool of considerable power. Use it wisely.",
            subFont, subBrush, new RectangleF(0, 50, Width, 22), sf);

        using var divPen = new Pen(Color.FromArgb(55, c.Accent));
        g.DrawLine(divPen, 22, 78, Width - 22, 78);

        string cursor = _typeIdx < _fullText.Length ? "▌" : "";
        g.DrawString(_displayed + cursor, bigFont, accentBrush,
            new RectangleF(0, 88, Width, 40), sf);

        g.DrawString("click anywhere to dismiss — auto-closes shortly",
            subFont, subBrush, new RectangleF(0, 186, Width, 22), sf);

        foreach (var sp in _sparks)
        {
            float t = sp.Life / sp.MaxLife;
            int a   = (int)(t * 210);
            if (a <= 0) continue;
            using var sb = new SolidBrush(Color.FromArgb(a, c.Accent));
            g.FillEllipse(sb, sp.X - 2.5f, sp.Y - 2.5f, 5f, 5f);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); Close(); }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _master.Stop();
        _master.Dispose();
        base.OnFormClosed(e);
    }
}