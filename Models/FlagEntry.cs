using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlagInjector;

sealed class FlagEntry
{
    string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; _category = null; }
    }

    string? _category;
    public string Category => _category ??= FlagCategory.Categorize(_name);

    public string Value { get; set; } = "";
    public FType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public ApplyMode Mode { get; set; } = ApplyMode.OnJoin;
    public List<FlagHistoryEntry> History { get; set; } = new();
    public volatile string Status = "";
    public DateTime AddedAt = DateTime.UtcNow;

    byte[]? _cachedBytes;
    string? _cachedValue;

    public byte[] GetBytes()
    {
        string v = Value;
        if (_cachedValue == v && _cachedBytes != null) return _cachedBytes;
        var result = Type switch
        {
            FType.Bool  => new[] { (byte)(v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" ? 1 : 0) },
            FType.Int   => BitConverter.GetBytes(int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out int iv) ? iv : 0),
            FType.Float => BitConverter.GetBytes(float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out float fv) ? fv : 0f),
            _           => Encoding.UTF8.GetBytes(v + '\0')
        };
        _cachedBytes = result;
        _cachedValue = v;
        return result;
    }

    public void InvalidateCache() { _cachedBytes = null; _cachedValue = null; }

    public void RecordChange(string oldVal, string newVal)
    {
        History.Add(new FlagHistoryEntry { Timestamp = DateTime.UtcNow.ToString("o"), OldValue = oldVal, NewValue = newVal });
        if (History.Count > 20) History.RemoveAt(0);
    }

    public static FType InferFromName(string name)
    {
        foreach (var p in FlagPrefix.All)
            if (name.Length > p.Length && name.StartsWith(p, StringComparison.OrdinalIgnoreCase) && char.IsUpper(name[p.Length]))
            {
                if (p.Contains("Flag",   StringComparison.OrdinalIgnoreCase)) return FType.Bool;
                if (p.Contains("Int",    StringComparison.OrdinalIgnoreCase) ||
                    p.Contains("Log",    StringComparison.OrdinalIgnoreCase)) return FType.Int;
                if (p.Contains("String", StringComparison.OrdinalIgnoreCase)) return FType.String;
            }
        return FType.String;
    }

    public static FType InferFromValue(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return FType.String;
        string lv = v.Trim().ToLowerInvariant();
        if (lv is "true" or "false") return FType.Bool;
        if (int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return FType.Int;
        if (float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _) && v.Contains('.')) return FType.Float;
        return FType.String;
    }

    public static FType Infer(string name, string value)
    {
        var fn = InferFromName(name);
        return fn != FType.String ? fn : InferFromValue(value);
    }
}