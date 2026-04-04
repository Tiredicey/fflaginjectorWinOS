using System;

namespace FlagInjector;

static class FlagPrefix
{
    static readonly string[] _prefixes =
    {
        "DFString","SFString","FString",
        "DFFlag","SFFlag","DFInt","SFInt",
        "DFLog","SFLog","FFlag","FInt","FLog"
    };

    public static ReadOnlySpan<string> All => _prefixes;

    public static string Strip(string name)
    {
        foreach (var p in _prefixes)
            if (name.Length > p.Length &&
                name.StartsWith(p, StringComparison.OrdinalIgnoreCase) &&
                char.IsUpper(name[p.Length]))
                return name[p.Length..];
        return name;
    }
}