using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FlagInjector;

sealed class OffsetStore
{
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static readonly Regex _rxUintptr  = new(@"(?:inline\s+)?(?:constexpr\s+)?uintptr_t\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+);", RegexOptions.Compiled);
    static readonly Regex _rxNsFFlags = new(@"namespace\s+FFlags\s*\{([^}]+)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly Regex _rxNsFFlList= new(@"namespace\s+FFlagList\s*\{([\s\S]*?)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly Regex _rxNsFFlOff = new(@"namespace\s+FFlagOffsets\s*\{([\s\S]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);

    readonly Dictionary<string, long> _map      = new();
    readonly NameResolver _resolver              = new();
    readonly HashSet<string> _seenStripped       = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _names                 = new();

    public IReadOnlyDictionary<string, long> Map => _map;
    public IReadOnlyList<string> Names => _names;
    public int Count => _map.Count;
    public string Cache1 { get; set; } = "";
    public string Cache2 { get; set; } = "";
    public string Url1   { get; set; } = "https://imtheo.lol/Offsets/FFlags.hpp";
    public string Url2   { get; set; } = "https://npdrlaufeimrkvdnjijl.supabase.co/functions/v1/get-offsets";
    public long FlogPointer { get; private set; }
    public Dictionary<string, long> StructOffsets { get; } = new();

    public event Action<string>? Log;

    async Task<string?> FetchUrlAsync(string url, int retries, CancellationToken ct)
    {
        for (int i = 0; i <= retries; i++)
        {
            try
            {
                if (i > 0) await Task.Delay(500 * i, ct);
                return await _http.GetStringAsync(url, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch { if (i == retries) return null; }
        }
        return null;
    }

    public bool Fetch(CancellationToken ct = default)
    {
        string? body1 = null, body2 = null;
        bool cached1 = false, cached2 = false;

        try
        {
            var t1 = FetchUrlAsync(Url1, 2, ct);
            var t2 = FetchUrlAsync(Url2, 2, ct);
            Task.WhenAll(t1, t2).GetAwaiter().GetResult();
            body1 = t1.Result; body2 = t2.Result;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { Log?.Invoke("Net: " + ex.Message); }

        if (!string.IsNullOrEmpty(body1) && !string.IsNullOrEmpty(Cache1))
            try { Directory.CreateDirectory(Path.GetDirectoryName(Cache1)!); File.WriteAllText(Cache1, body1, Encoding.UTF8); } catch { }
        if (!string.IsNullOrEmpty(body2) && !string.IsNullOrEmpty(Cache2))
            try { Directory.CreateDirectory(Path.GetDirectoryName(Cache2)!); File.WriteAllText(Cache2, body2, Encoding.UTF8); } catch { }

        if (string.IsNullOrEmpty(body1) && File.Exists(Cache1)) try { body1 = File.ReadAllText(Cache1); cached1 = true; } catch { }
        if (string.IsNullOrEmpty(body2) && File.Exists(Cache2)) try { body2 = File.ReadAllText(Cache2); cached2 = true; } catch { }

        _map.Clear(); _resolver.Clear(); _seenStripped.Clear(); _names.Clear(); FlogPointer = 0; StructOffsets.Clear();
        int c1 = 0, c2 = 0;
        if (!string.IsNullOrEmpty(body1)) c1 = ParseSource1(body1);
        if (!string.IsNullOrEmpty(body2)) c2 = ParseSource2(body2);

        Log?.Invoke($"Src1:{c1}{(cached1?"(c)":"")} Src2:{c2}{(cached2?"(c)":"")} Total:{_map.Count}");
        return _map.Count > 0;
    }

    int ParseSource1(string body)
    {
        int count = 0;
        var ns = _rxNsFFlags.Match(body);
        string region = ns.Success ? ns.Groups[1].Value : body;
        foreach (Match m in _rxUintptr.Matches(region))
        {
            if (!long.TryParse(m.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, null, out long v)) continue;
            if (v < 0x100000) continue;
            if (AddOffset(m.Groups[1].Value, v)) count++;
        }
        return count;
    }

    int ParseSource2(string body)
    {
        int count = 0;
        var flogNs = _rxNsFFlList.Match(body);
        if (flogNs.Success)
        {
            foreach (Match m in _rxUintptr.Matches(flogNs.Groups[1].Value))
            {
                if (!long.TryParse(m.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, null, out long v)) continue;
                string key = m.Groups[1].Value;
                if (key == "Pointer") FlogPointer = v;
                StructOffsets[key] = v;
            }
            body = body.Remove(flogNs.Index, flogNs.Length);
        }
        var outerNs = _rxNsFFlOff.Match(body);
        string region = outerNs.Success ? outerNs.Groups[1].Value : body;
        foreach (Match m in _rxUintptr.Matches(region))
        {
            if (!long.TryParse(m.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, null, out long v)) continue;
            string key = m.Groups[1].Value;
            if (StructOffsets.ContainsKey(key)) continue;
            if (v < 0x100000) continue;
            if (AddOffset(key, v)) count++;
        }
        return count;
    }

    bool AddOffset(string name, long offset)
    {
        if (_map.ContainsKey(name)) return false;
        _map[name] = offset;
        _resolver.Add(name);
        string s = FlagPrefix.Strip(name);
        if (_seenStripped.Add(s)) _names.Add(name);
        return true;
    }

    public string? Resolve(string n) => _resolver.Resolve(n);
    public long Offset(string resolved) => _map.TryGetValue(resolved, out long v) ? v : -1;
}