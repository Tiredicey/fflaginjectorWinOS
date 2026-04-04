using System;
using System.Collections.Generic;

namespace FlagInjector;

sealed class NameResolver
{
    readonly Dictionary<string, string> _exact        = new();
    readonly Dictionary<string, string> _ci           = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _stripped     = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _norm         = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _strippedNorm = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string canonical)
    {
        if (_exact.ContainsKey(canonical)) return;
        _exact[canonical] = canonical;
        _ci[canonical]    = canonical;
        string s  = FlagPrefix.Strip(canonical);
        string n  = canonical.Replace("_", "");
        string sn = s.Replace("_", "");
        if (!_stripped.ContainsKey(s))     _stripped[s]     = canonical;
        if (!_norm.ContainsKey(n))         _norm[n]         = canonical;
        if (!_strippedNorm.ContainsKey(sn)) _strippedNorm[sn] = canonical;
    }

    public string? Resolve(string name)
    {
        if (_exact.ContainsKey(name)) return name;
        if (_ci.TryGetValue(name, out var a)) return a;
        string s = FlagPrefix.Strip(name);
        if (s != name)
        {
            if (_ci.TryGetValue(s, out var b)) return b;
            if (_stripped.TryGetValue(s, out var c)) return c;
            if (_strippedNorm.TryGetValue(s.Replace("_", ""), out var d)) return d;
        }
        if (_stripped.TryGetValue(name, out var e)) return e;
        if (_norm.TryGetValue(name.Replace("_", ""), out var f)) return f;
        if (_strippedNorm.TryGetValue(name.Replace("_", ""), out var g)) return g;
        return null;
    }

    public void Clear()
    {
        _exact.Clear(); _ci.Clear(); _stripped.Clear(); _norm.Clear(); _strippedNorm.Clear();
    }
}