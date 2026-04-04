using System;
using System.Collections.Generic;
using System.Drawing;

namespace FlagInjector;

readonly record struct ThemeColors
{
    public Color Bg      { get; init; }
    public Color Surface { get; init; }
    public Color Row2    { get; init; }
    public Color Hover   { get; init; }
    public Color Border  { get; init; }
    public Color Fg      { get; init; }
    public Color Sub     { get; init; }
    public Color Accent  { get; init; }
    public Color Green   { get; init; }
    public Color Red     { get; init; }
    public Color Peach   { get; init; }
    public Color Yellow  { get; init; }
    public Color Purple  { get; init; }
    public Color Silver  { get; init; }

    private static readonly Dictionary<string, Func<ThemeColors, Color>> _map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Bg"]      = t => t.Bg,
            ["Surface"] = t => t.Surface,
            ["Row2"]    = t => t.Row2,
            ["Hover"]   = t => t.Hover,
            ["Border"]  = t => t.Border,
            ["Fg"]      = t => t.Fg,
            ["Sub"]     = t => t.Sub,
            ["Accent"]  = t => t.Accent,
            ["Green"]   = t => t.Green,
            ["Red"]     = t => t.Red,
            ["Peach"]   = t => t.Peach,
            ["Yellow"]  = t => t.Yellow,
            ["Purple"]  = t => t.Purple,
            ["Silver"]  = t => t.Silver,
        };

    public Color this[string name] =>
        _map.TryGetValue(name, out var fn) ? fn(this) : throw new KeyNotFoundException(name);
}

static class Theme
{
    private static readonly ThemeColors _dark = new()
    {
        Bg      = Color.FromArgb(13,  15,  28),
        Surface = Color.FromArgb(20,  22,  41),
        Row2    = Color.FromArgb(26,  30,  53),
        Hover   = Color.FromArgb(45,  53,  97),
        Border  = Color.FromArgb(74,  79, 122),
        Fg      = Color.FromArgb(232, 213, 176),
        Sub     = Color.FromArgb(154, 143, 112),
        Accent  = Color.FromArgb(201, 168,  76),
        Green   = Color.FromArgb(74,  124,  89),
        Red     = Color.FromArgb(139,  26,  46),
        Peach   = Color.FromArgb(196, 123,  62),
        Yellow  = Color.FromArgb(212, 168,  67),
        Purple  = Color.FromArgb(107,  75, 160),
        Silver  = Color.FromArgb(176, 184, 200),
    };

    private static readonly ThemeColors _light = new()
    {
        Bg      = Color.FromArgb(244, 237, 211),
        Surface = Color.FromArgb(235, 224, 194),
        Row2    = Color.FromArgb(226, 213, 176),
        Hover   = Color.FromArgb(212, 196, 138),
        Border  = Color.FromArgb(139, 115,  85),
        Fg      = Color.FromArgb(26,   18,   8),
        Sub     = Color.FromArgb(92,   74,  42),
        Accent  = Color.FromArgb(139, 105,  20),
        Green   = Color.FromArgb(45,   92,  58),
        Red     = Color.FromArgb(107,  16,  32),
        Peach   = Color.FromArgb(155,  92,  32),
        Yellow  = Color.FromArgb(139, 105,  20),
        Purple  = Color.FromArgb(85,   55, 130),
        Silver  = Color.FromArgb(100, 108, 124),
    };

    private static readonly object   _sync    = new();
    private static          ThemeColors _current = _dark;
    private static volatile bool      _isDark  = true;

    public static ThemeColors C        { get { lock (_sync) return _current; } }
    public static bool        IsDark   => _isDark;
    public static event Action? Changed;

    public static void Toggle()
    {
        lock (_sync) { _isDark = !_isDark; _current = _isDark ? _dark : _light; }
        Changed?.Invoke();
    }

    public static void Set(bool dark)
    {
        lock (_sync)
        {
            if (_isDark == dark) return;
            _isDark  = dark;
            _current = dark ? _dark : _light;
        }
        Changed?.Invoke();
    }
}