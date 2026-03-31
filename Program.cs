using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FlagInjector;

static class W32
{
    public const uint ACCESS = 0x0438;
    public const uint SNAP = 0x18;
    public const uint ALIVE = 259;
    [DllImport("kernel32", SetLastError = true)] public static extern IntPtr OpenProcess(uint a, bool b, int pid);
    [DllImport("kernel32", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32", SetLastError = true)] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int n, out int read);
    [DllImport("kernel32", SetLastError = true)] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int n, out int wrote);
    [DllImport("kernel32", SetLastError = true)] public static extern IntPtr CreateToolhelp32Snapshot(uint f, uint pid);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool Module32FirstW(IntPtr snap, ref MODENTRY me);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool Module32NextW(IntPtr snap, ref MODENTRY me);
    [DllImport("kernel32")] public static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hw, uint msg, IntPtr wp, string lp);
    [DllImport("dwmapi")] public static extern int DwmSetWindowAttribute(IntPtr hw, int attr, ref int val, int sz);
    [DllImport("uxtheme", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hw, string sub, string? id);
    [DllImport("user32")] public static extern bool DestroyIcon(IntPtr h);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct MODENTRY
{
    public uint dwSize; public uint modID; public uint procID; public uint glblCnt; public uint procCnt;
    public IntPtr modBaseAddr; public uint modBaseSize; public IntPtr hModule;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
}

enum FType { Bool, Int, Float, String }
enum ApplyMode { OnJoin, Immediate }

sealed class ThemeColors
{
    public Color Bg, Surface, Row2, Hover, Border, Fg, Sub, Accent, Green, Red, Peach, Yellow;
}

static class Theme
{
    static readonly ThemeColors _dark = new()
    {
        Bg = Color.FromArgb(30, 30, 46), Surface = Color.FromArgb(36, 36, 51),
        Row2 = Color.FromArgb(41, 41, 58), Hover = Color.FromArgb(69, 71, 90),
        Border = Color.FromArgb(88, 91, 112), Fg = Color.FromArgb(205, 214, 244),
        Sub = Color.FromArgb(166, 173, 200), Accent = Color.FromArgb(137, 180, 250),
        Green = Color.FromArgb(166, 227, 161), Red = Color.FromArgb(243, 139, 168),
        Peach = Color.FromArgb(250, 179, 135), Yellow = Color.FromArgb(249, 226, 175)
    };
    static readonly ThemeColors _light = new()
    {
        Bg = Color.FromArgb(239, 241, 245), Surface = Color.FromArgb(230, 233, 239),
        Row2 = Color.FromArgb(220, 224, 232), Hover = Color.FromArgb(188, 192, 204),
        Border = Color.FromArgb(140, 143, 161), Fg = Color.FromArgb(35, 38, 52),
        Sub = Color.FromArgb(92, 95, 119), Accent = Color.FromArgb(30, 102, 245),
        Green = Color.FromArgb(64, 160, 43), Red = Color.FromArgb(210, 15, 57),
        Peach = Color.FromArgb(254, 100, 11), Yellow = Color.FromArgb(223, 142, 29)
    };
    public static ThemeColors C { get; private set; } = _dark;
    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;
    public static void Toggle() { IsDark = !IsDark; C = IsDark ? _dark : _light; Changed?.Invoke(); }
    public static void Set(bool dark) { IsDark = dark; C = dark ? _dark : _light; Changed?.Invoke(); }
}

sealed class AppLog : IDisposable
{
    readonly string _path; readonly object _lk = new(); StreamWriter? _w;
    public AppLog(string dir)
    {
        _path = Path.Combine(dir, "log.txt");
        try
        {
            _w = new StreamWriter(_path, true, Encoding.UTF8) { AutoFlush = true };
            if (new FileInfo(_path).Length > 2 * 1024 * 1024)
            { _w.Dispose(); File.Delete(_path); _w = new StreamWriter(_path, false, Encoding.UTF8) { AutoFlush = true }; }
        }
        catch { _w = null; }
    }
    public void Info(string msg) => Write("INF", msg);
    public void Warn(string msg) => Write("WRN", msg);
    public void Error(string msg) => Write("ERR", msg);
    void Write(string lvl, string msg) { lock (_lk) { try { _w?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{lvl}] {msg}"); } catch { } } }
    public void Dispose() { lock (_lk) { _w?.Dispose(); _w = null; } }
}

sealed class AppSettings
{
    public int X { get; set; } = -1; public int Y { get; set; } = -1;
    public int W { get; set; } = 820; public int H { get; set; } = 780;
    public int Split { get; set; } = -1;
    public bool AutoApply { get; set; } = true;
    public bool Watchdog { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool DarkTheme { get; set; } = true;
    public bool ConfirmApplyAll { get; set; }
    public bool FirstMinimizeDone { get; set; }
    public string LastPreset { get; set; } = "";
    public List<string> SearchHistory { get; set; } = new();
    public int SchemaVer { get; set; } = 2;
    static readonly JsonSerializerOptions _jopt = new() { WriteIndented = true };
    public static AppSettings Load(string path)
    {
        try { if (!File.Exists(path)) return new(); return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path, Encoding.UTF8)) ?? new(); }
        catch { return new(); }
    }
    public void Save(string path)
    {
        try
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, _jopt), new UTF8Encoding(false));
            if (File.Exists(path)) try { File.Copy(path, path + ".bak", true); } catch { }
            File.Move(tmp, path, true);
        }
        catch { }
    }
}

sealed class FlagHistoryEntry
{
    [JsonPropertyName("ts")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("ov")] public string OldValue { get; set; } = "";
    [JsonPropertyName("nv")] public string NewValue { get; set; } = "";
}

sealed class FlagDto
{
    [JsonPropertyName("n")] public string Name { get; set; } = "";
    [JsonPropertyName("v")] public string Value { get; set; } = "";
    [JsonPropertyName("t")] public string Type { get; set; } = "String";
    [JsonPropertyName("e")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("m")] public string Mode { get; set; } = "OnJoin";
    [JsonPropertyName("h")] public List<FlagHistoryEntry> History { get; set; } = new();
}

sealed class NameResolver
{
    readonly Dictionary<string, string> _exact = new();
    readonly Dictionary<string, string> _ci = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _stripped = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _norm = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _strippedNorm = new(StringComparer.OrdinalIgnoreCase);
    public void Add(string canonical)
    {
        if (_exact.ContainsKey(canonical)) return;
        _exact[canonical] = canonical; _ci[canonical] = canonical;
        string s = FlagPrefix.Strip(canonical);
        if (!_stripped.ContainsKey(s)) _stripped[s] = canonical;
        string n = canonical.Replace("_", "");
        if (!_norm.ContainsKey(n)) _norm[n] = canonical;
        string sn = s.Replace("_", "");
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
    public void Clear() { _exact.Clear(); _ci.Clear(); _stripped.Clear(); _norm.Clear(); _strippedNorm.Clear(); }
}

sealed class MemEngine : IDisposable
{
    IntPtr _h, _base; int _pid; uint _modSize; readonly object _lk = new();
    public bool On => _h != IntPtr.Zero && _base != IntPtr.Zero;
    public int Pid { get { lock (_lk) return _pid; } }
    public long Base { get { lock (_lk) return _base.ToInt64(); } }
    public uint ModSize { get { lock (_lk) return _modSize; } }
    public event Action<string>? Log;
    public bool Attach(int pid, string mod = "RobloxPlayerBeta.exe", CancellationToken ct = default)
    {
        lock (_lk)
        {
            Detach();
            var h = W32.OpenProcess(W32.ACCESS, false, pid);
            if (h == IntPtr.Zero) { Log?.Invoke($"OpenProcess err {Marshal.GetLastWin32Error()}"); return false; }
            IntPtr b = IntPtr.Zero; uint sz = 0;
            for (int i = 0; i < 40 && b == IntPtr.Zero; i++)
            {
                if (ct.IsCancellationRequested) { W32.CloseHandle(h); return false; }
                FindMod(pid, mod, out b, out sz);
                if (b == IntPtr.Zero) Thread.Sleep(200);
            }
            if (b == IntPtr.Zero) { W32.CloseHandle(h); Log?.Invoke("Base not found"); return false; }
            _h = h; _base = b; _pid = pid; _modSize = sz;
            Log?.Invoke($"Attached PID {pid} base 0x{b.ToInt64():X}"); return true;
        }
    }
    public void Detach() { lock (_lk) { if (_h != IntPtr.Zero) W32.CloseHandle(_h); _h = _base = IntPtr.Zero; _pid = 0; _modSize = 0; } }
    public bool Alive()
    {
        bool entered = false;
        try { entered = Monitor.TryEnter(_lk, 50); if (!entered) return _h != IntPtr.Zero; return _h != IntPtr.Zero && W32.GetExitCodeProcess(_h, out uint c) && c == W32.ALIVE; }
        finally { if (entered) Monitor.Exit(_lk); }
    }
    static void FindMod(int pid, string name, out IntPtr addr, out uint size)
    {
        addr = IntPtr.Zero; size = 0; IntPtr s = IntPtr.Zero;
        try
        {
            s = W32.CreateToolhelp32Snapshot(W32.SNAP, (uint)pid);
            if (s == IntPtr.Zero || s == (IntPtr)(-1)) return;
            var me = new MODENTRY { dwSize = (uint)Marshal.SizeOf<MODENTRY>() };
            if (!W32.Module32FirstW(s, ref me)) return;
            do { if (me.szModule.Equals(name, StringComparison.OrdinalIgnoreCase)) { addr = me.modBaseAddr; size = me.modBaseSize; return; } }
            while (W32.Module32NextW(s, ref me));
        }
        catch { }
        finally { if (s != IntPtr.Zero && s != (IntPtr)(-1)) W32.CloseHandle(s); }
    }
    public bool WriteFast(long addr, byte[] data) { lock (_lk) { if (_h == IntPtr.Zero) return false; return W32.WriteProcessMemory(_h, (IntPtr)addr, data, data.Length, out int w) && w == data.Length; } }
    public bool WriteAbs(long addr, byte[] data, int tries = 3, int delayMs = 20)
    {
        if (!On) return false; byte[]? chk = null;
        try
        {
            chk = ArrayPool<byte>.Shared.Rent(data.Length);
            for (int t = 0; t < tries; t++)
            {
                if (t > 0) Thread.Sleep(delayMs); bool ok;
                lock (_lk)
                {
                    if (_h == IntPtr.Zero) return false;
                    if (!W32.WriteProcessMemory(_h, (IntPtr)addr, data, data.Length, out int w) || w != data.Length) continue;
                    ok = W32.ReadProcessMemory(_h, (IntPtr)addr, chk, data.Length, out int r) && r == data.Length && data.AsSpan().SequenceEqual(chk.AsSpan(0, data.Length));
                }
                if (ok) return true;
            }
        }
        finally { if (chk != null) ArrayPool<byte>.Shared.Return(chk); }
        return false;
    }
    public byte[]? ReadAbs(long addr, int n) { lock (_lk) { if (_h == IntPtr.Zero) return null; var buf = new byte[n]; return W32.ReadProcessMemory(_h, (IntPtr)addr, buf, n, out int r) && r == n ? buf : null; } }
    public long ReadPtr(long addr) { var b = ReadAbs(addr, 8); return b != null ? BitConverter.ToInt64(b, 0) : 0; }
    public int ReadInt32(long addr) { var b = ReadAbs(addr, 4); return b != null ? BitConverter.ToInt32(b, 0) : 0; }
    public int BatchWrite(List<(long addr, byte[] data)> ops)
    {
        if (!On) return 0;
        ops.Sort((a, b) => a.addr.CompareTo(b.addr));
        int ok = 0;
        lock (_lk) { if (_h == IntPtr.Zero) return 0; foreach (var (addr, data) in ops) { if (W32.WriteProcessMemory(_h, (IntPtr)addr, data, data.Length, out int w) && w == data.Length) ok++; } }
        return ok;
    }
    public void Dispose() => Detach();
}

static class FlagPrefix
{
    static readonly string[] _prefixes = { "DFString", "SFString", "FString", "DFFlag", "SFFlag", "DFInt", "SFInt", "DFLog", "SFLog", "FFlag", "FInt", "FLog" };
    public static ReadOnlySpan<string> All => _prefixes;
    public static string Strip(string name)
    {
        foreach (var p in _prefixes)
            if (name.Length > p.Length && name.StartsWith(p, StringComparison.OrdinalIgnoreCase) && char.IsUpper(name[p.Length]))
                return name[p.Length..];
        return name;
    }
}

static class FlagCategory
{
    static readonly (string[] keys, string cat)[] _rules =
    {
        (new[]{"Render","Graphics","Shader","Texture","Light","Shadow","Material","Mesh","Particle","PostEffect","MSAA","Antialias"}, "Rendering"),
        (new[]{"Physics","Simulation","Gravity","Collision","Velocity","Force"}, "Physics"),
        (new[]{"Network","Replicat","Packet","Latency","Bandwidth","Http","Ping"}, "Network"),
        (new[]{"Gui","Ui","Menu","Hud","Chat","TextBox","Button","Frame","Label","Scroll","TopBar","StarterGui"}, "UI"),
        (new[]{"Audio","Sound","Music","Volume"}, "Audio"),
        (new[]{"Fps","Perf","Throttle","Budget","Cache","Memory","GC","Pool","Batch","Queue","Thread"}, "Performance"),
        (new[]{"Debug","Log","Verbose","Trace","Assert","Diag","Telemetry","Analytics","Stat"}, "Debug"),
        (new[]{"Lua","Script","Module","Require","Bytecode","VM"}, "Scripting"),
        (new[]{"Camera","Zoom","Fov","ViewPort"}, "Camera"),
        (new[]{"Terrain","Water","Sky","Atmosphere","Cloud","Sun","Moon","Star"}, "Environment"),
        (new[]{"Anim","IK","Humanoid","Character","Avatar","R15","R6","Emote"}, "Character"),
        (new[]{"Place","Teleport","Game","Universe","Server","DataStore","DataModel"}, "Engine")
    };
    public static string Categorize(string flagName)
    {
        string s = FlagPrefix.Strip(flagName);
        foreach (var (keys, cat) in _rules)
            foreach (var k in keys)
                if (s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return cat;
        return "Other";
    }
}

sealed class FlogBank
{
    readonly MemEngine _mem; readonly NameResolver _resolver = new(); readonly object _lk = new();
    long _oPointer = 0x7e71128, _oToFlag = 0x30, _oToValue = 0xc0;
    long _oNodeFwd = 0x0, _oNodeBwd = 0x8, _oNodePair = 0x10;
    long _oPairKey = 0x0, _oPairValue = 0x30;
    long _oStrData = 0x0, _oStrSize = 0x10, _oStrCap = 0x18;
    long _oFirstNode = 0x0, _oMapSize = 0x20;
    long _hashMapOff = 0x8;
    readonly Dictionary<string, long> _descMap = new(); readonly List<string> _names = new();
    public bool Ready { get; private set; } public int Count { get { lock (_lk) return _descMap.Count; } }
    public IReadOnlyList<string> Names { get { lock (_lk) return _names.ToArray(); } }
    public event Action<string>? Log;
    public FlogBank(MemEngine mem) => _mem = mem;
    public void ApplyOffsets(Dictionary<string, long> offsets)
    {
        foreach (var kv in offsets)
            switch (kv.Key)
            {
                case "Pointer": _oPointer = kv.Value; break; case "ToFlag": _oToFlag = kv.Value; break;
                case "ToValue": _oToValue = kv.Value; break; case "NodeForward": _oNodeFwd = kv.Value; break;
                case "NodeBackward": _oNodeBwd = kv.Value; break; case "NodePair": _oNodePair = kv.Value; break;
                case "Key": _oPairKey = kv.Value; break; case "Value": _oPairValue = kv.Value; break;
                case "Data": _oStrData = kv.Value; break; case "Size": _oStrSize = kv.Value; break;
                case "Capacity": _oStrCap = kv.Value; break; case "FirstNode": _oFirstNode = kv.Value; break;
                case "MapSize": _oMapSize = kv.Value; break; case "HashMapOff": _hashMapOff = kv.Value; break;
            }
    }
    public bool Init()
    {
        lock (_lk)
        {
            _descMap.Clear(); _resolver.Clear(); _names.Clear(); Ready = false;
            if (!_mem.On) { Log?.Invoke("Bank: mem not attached"); return false; }
            long singleton = _mem.ReadPtr(_mem.Base + _oPointer);
            if (singleton < 0x10000) { Log?.Invoke($"Bank: bad singleton 0x{singleton:X}"); return false; }
            long mapBase = singleton + _hashMapOff; long sentinel = mapBase;
            long firstNode = _mem.ReadPtr(mapBase + _oFirstNode);
            int mapSize = _mem.ReadInt32(mapBase + _oMapSize);
            if (firstNode < 0x10000) { Log?.Invoke("Bank: bad firstNode"); return false; }
            if (mapSize <= 0 || mapSize > 100000) { Log?.Invoke($"Bank: suspect mapSize {mapSize}"); return false; }
            long node = firstNode; var visited = new HashSet<long>();
            int maxIter = Math.Min(mapSize + 100, 100000), count = 0;
            for (int i = 0; i < maxIter && node != 0 && node != sentinel; i++)
            {
                if (!visited.Add(node)) break;
                long pairAddr = node + _oNodePair, keyAddr = pairAddr + _oPairKey;
                string? key = ReadMsvcString(keyAddr);
                if (key != null && key.Length > 0 && key.Length < 512)
                {
                    long descPtr = _mem.ReadPtr(pairAddr + _oPairValue);
                    if (descPtr > 0x10000 && !_descMap.ContainsKey(key)) { _descMap[key] = descPtr; _resolver.Add(key); _names.Add(key); count++; }
                }
                node = _mem.ReadPtr(node + _oNodeFwd);
            }
            Ready = count > 0; Log?.Invoke($"Bank: {count} flags discovered"); return Ready;
        }
    }
    string? ReadMsvcString(long addr)
    {
        var raw = _mem.ReadAbs(addr, 32); if (raw == null) return null;
        long size = BitConverter.ToInt64(raw, (int)_oStrSize), cap = BitConverter.ToInt64(raw, (int)_oStrCap);
        if (size < 0 || size > 4096 || cap < 0) return null; if (size == 0) return "";
        byte[]? strBytes;
        if (cap < 16) { if (size > 15) return null; strBytes = new byte[size]; Array.Copy(raw, (int)_oStrData, strBytes, 0, (int)size); }
        else { long heapPtr = BitConverter.ToInt64(raw, (int)_oStrData); if (heapPtr < 0x10000) return null; strBytes = _mem.ReadAbs(heapPtr, (int)size); if (strBytes == null) return null; }
        try { return Encoding.UTF8.GetString(strBytes); } catch { return null; }
    }
    public string? Resolve(string name) { lock (_lk) { return Ready ? _resolver.Resolve(name) : null; } }
    public long GetValueAddr(string resolvedName) { lock (_lk) { return _descMap.TryGetValue(resolvedName, out long desc) ? desc + _oToValue : 0; } }
    public void Reset() { lock (_lk) { _descMap.Clear(); _resolver.Clear(); _names.Clear(); Ready = false; } }
}

sealed class OffsetStore
{
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly Regex _rxUintptr = new(@"(?:inline\s+)?(?:constexpr\s+)?uintptr_t\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+);", RegexOptions.Compiled);
    static readonly Regex _rxNsFFlags = new(@"namespace\s+FFlags\s*\{([^}]+)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly Regex _rxNsFFlList = new(@"namespace\s+FFlagList\s*\{([\s\S]*?)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly Regex _rxNsFFlOff = new(@"namespace\s+FFlagOffsets\s*\{([\s\S]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    readonly Dictionary<string, long> _map = new(); readonly NameResolver _resolver = new();
    readonly HashSet<string> _seenStripped = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _names = new();
    public IReadOnlyDictionary<string, long> Map => _map; public IReadOnlyList<string> Names => _names;
    public int Count => _map.Count; public string Cache1 { get; set; } = ""; public string Cache2 { get; set; } = "";
    public string Url1 { get; set; } = "https://imtheo.lol/Offsets/FFlags.hpp";
    public string Url2 { get; set; } = "https://npdrlaufeimrkvdnjijl.supabase.co/functions/v1/get-offsets";
    public long FlogPointer { get; private set; }
    public Dictionary<string, long> StructOffsets { get; } = new();
    public event Action<string>? Log;
    async Task<string?> FetchUrlAsync(string url, int retries, CancellationToken ct)
    {
        for (int i = 0; i <= retries; i++)
        {
            try { if (i > 0) await Task.Delay(500 * i, ct); return await _http.GetStringAsync(url, ct); }
            catch (OperationCanceledException) { throw; } catch { if (i == retries) return null; }
        }
        return null;
    }
    public bool Fetch(CancellationToken ct = default)
    {
        string? body1 = null, body2 = null; bool cached1 = false, cached2 = false;
        try { var t1 = FetchUrlAsync(Url1, 2, ct); var t2 = FetchUrlAsync(Url2, 2, ct); Task.WhenAll(t1, t2).GetAwaiter().GetResult(); body1 = t1.Result; body2 = t2.Result; }
        catch (OperationCanceledException) { return false; } catch (Exception ex) { Log?.Invoke("Net: " + ex.Message); }
        if (!string.IsNullOrEmpty(body1) && !string.IsNullOrEmpty(Cache1)) try { Directory.CreateDirectory(Path.GetDirectoryName(Cache1)!); File.WriteAllText(Cache1, body1, Encoding.UTF8); } catch { }
        if (!string.IsNullOrEmpty(body2) && !string.IsNullOrEmpty(Cache2)) try { Directory.CreateDirectory(Path.GetDirectoryName(Cache2)!); File.WriteAllText(Cache2, body2, Encoding.UTF8); } catch { }
        if (string.IsNullOrEmpty(body1) && File.Exists(Cache1)) try { body1 = File.ReadAllText(Cache1); cached1 = true; } catch { }
        if (string.IsNullOrEmpty(body2) && File.Exists(Cache2)) try { body2 = File.ReadAllText(Cache2); cached2 = true; } catch { }
        _map.Clear(); _resolver.Clear(); _seenStripped.Clear(); _names.Clear(); FlogPointer = 0; StructOffsets.Clear();
        int c1 = 0, c2 = 0;
        if (!string.IsNullOrEmpty(body1)) c1 = ParseSource1(body1);
        if (!string.IsNullOrEmpty(body2)) c2 = ParseSource2(body2);
        Log?.Invoke($"Src1:{c1}{(cached1 ? "(c)" : "")} Src2:{c2}{(cached2 ? "(c)" : "")} Total:{_map.Count}"); return _map.Count > 0;
    }
    int ParseSource1(string body)
    {
        int count = 0; var ns = _rxNsFFlags.Match(body); string region = ns.Success ? ns.Groups[1].Value : body;
        foreach (Match m in _rxUintptr.Matches(region))
        { if (!long.TryParse(m.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, null, out long v)) continue; if (v < 0x100000) continue; if (AddOffset(m.Groups[1].Value, v)) count++; }
        return count;
    }
    int ParseSource2(string body)
    {
        int count = 0; var flogNs = _rxNsFFlList.Match(body);
        if (flogNs.Success)
        {
            foreach (Match m in _rxUintptr.Matches(flogNs.Groups[1].Value))
            { if (!long.TryParse(m.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, null, out long v)) continue; string key = m.Groups[1].Value; if (key == "Pointer") FlogPointer = v; StructOffsets[key] = v; }
            body = body.Remove(flogNs.Index, flogNs.Length);
        }
        var outerNs = _rxNsFFlOff.Match(body); string region = outerNs.Success ? outerNs.Groups[1].Value : body;
        foreach (Match m in _rxUintptr.Matches(region))
        { if (!long.TryParse(m.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, null, out long v)) continue; string key = m.Groups[1].Value; if (StructOffsets.ContainsKey(key)) continue; if (v < 0x100000) continue; if (AddOffset(key, v)) count++; }
        return count;
    }
    bool AddOffset(string name, long offset)
    {
        if (_map.ContainsKey(name)) return false;
        _map[name] = offset; _resolver.Add(name);
        string s = FlagPrefix.Strip(name); if (_seenStripped.Add(s)) _names.Add(name); return true;
    }
    public string? Resolve(string n) => _resolver.Resolve(n);
    public long Offset(string resolved) => _map.TryGetValue(resolved, out long v) ? v : -1;
}

sealed class FlagEntry
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public FType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public ApplyMode Mode { get; set; } = ApplyMode.OnJoin;
    public List<FlagHistoryEntry> History { get; set; } = new();
    public volatile string Status = "";
    public DateTime AddedAt = DateTime.UtcNow;
    byte[]? _cachedBytes; string? _cachedValue;
    public byte[] GetBytes()
    {
        string v = Value; if (_cachedValue == v && _cachedBytes != null) return _cachedBytes;
        var result = Type switch
        {
            FType.Bool => new[] { (byte)(v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" ? 1 : 0) },
            FType.Int => BitConverter.GetBytes(int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out int iv) ? iv : 0),
            FType.Float => BitConverter.GetBytes(float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out float fv) ? fv : 0f),
            _ => Encoding.UTF8.GetBytes(v + '\0')
        };
        _cachedBytes = result; _cachedValue = v; return result;
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
                if (p.Contains("Flag", StringComparison.OrdinalIgnoreCase)) return FType.Bool;
                if (p.Contains("Int", StringComparison.OrdinalIgnoreCase) || p.Contains("Log", StringComparison.OrdinalIgnoreCase)) return FType.Int;
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
    public static FType Infer(string name, string value) { var fn = InferFromName(name); return fn != FType.String ? fn : InferFromValue(value); }
}

sealed class UndoStack
{
    const int MaxDepth = 50;
    readonly List<FlagSnapshot[]> _undo = new(), _redo = new();
    public bool CanUndo => _undo.Count > 0; public bool CanRedo => _redo.Count > 0;
    public void Push(List<FlagEntry> flags) { _undo.Add(Snap(flags)); if (_undo.Count > MaxDepth) _undo.RemoveAt(0); _redo.Clear(); }
    public FlagSnapshot[]? Undo(List<FlagEntry> current) { if (_undo.Count == 0) return null; _redo.Add(Snap(current)); var s = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); return s; }
    public FlagSnapshot[]? Redo(List<FlagEntry> current) { if (_redo.Count == 0) return null; _undo.Add(Snap(current)); var s = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); return s; }
    static FlagSnapshot[] Snap(List<FlagEntry> flags) => flags.Select(f => new FlagSnapshot(f.Name, f.Value, f.Type, f.Enabled, f.Mode, f.History.ToList())).ToArray();
    public record FlagSnapshot(string Name, string Value, FType Type, bool Enabled, ApplyMode Mode, List<FlagHistoryEntry> History);
}

sealed class PresetManager
{
    readonly string _dir;
    public PresetManager(string dir) { _dir = Path.Combine(dir, "presets"); Directory.CreateDirectory(_dir); }
    public string[] List() { try { return Directory.GetFiles(_dir, "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => n != null).Select(n => n!).OrderBy(n => n).ToArray(); } catch { return Array.Empty<string>(); } }
    public void Save(string name, List<FlagEntry> flags)
    {
        var dtos = flags.Select(f => new FlagDto { Name = f.Name, Value = f.Value, Type = f.Type.ToString(), Enabled = f.Enabled, Mode = f.Mode.ToString(), History = f.History }).ToArray();
        File.WriteAllText(Path.Combine(_dir, name + ".json"), JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
    public FlagDto[]? Load(string name)
    {
        string p = Path.Combine(_dir, name + ".json"); if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<FlagDto[]>(File.ReadAllText(p, Encoding.UTF8), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch { return null; }
    }
    public void Delete(string name) { try { File.Delete(Path.Combine(_dir, name + ".json")); } catch { } }
}

sealed class UpdateChecker
{
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public static readonly string CurrentVersion = "2.0.0";
    public string CheckUrl { get; set; } = "";
    public async Task<(bool available, string ver, string url)> CheckAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(CheckUrl)) return (false, "", "");
        try
        {
            string json = await _http.GetStringAsync(CheckUrl, ct);
            using var doc = JsonDocument.Parse(json);
            string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            string dl = doc.RootElement.TryGetProperty("html_url", out var u) ? (u.GetString() ?? "") : "";
            string ver = tag.TrimStart('v', 'V');
            if (ver != "" && ver != CurrentVersion && string.Compare(ver, CurrentVersion, StringComparison.OrdinalIgnoreCase) > 0)
                return (true, ver, dl);
        }
        catch { }
        return (false, "", "");
    }
}

sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using var b = new SolidBrush(Theme.C.Surface); e.Graphics.FillRectangle(b, e.AffectedBounds); }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { if (e.Item.Selected && e.Item.Enabled) { using var b = new SolidBrush(Theme.C.Hover); e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size)); } }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { if (!e.Item.Enabled) e.TextColor = Theme.C.Border; else if (e.Item is ToolStripStatusLabel lbl) e.TextColor = lbl.ForeColor; else e.TextColor = Theme.C.Fg; base.OnRenderItemText(e); }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { int y = e.Item.Height / 2; using var p = new Pen(Theme.C.Border); e.Graphics.DrawLine(p, 4, y, e.Item.Width - 4, y); }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { if (e.ToolStrip is StatusStrip) return; using var p = new Pen(Theme.C.Border); e.Graphics.DrawRectangle(p, e.AffectedBounds.X, e.AffectedBounds.Y, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1); }
}

sealed class SplashForm : Form
{
    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(340, 180); BackColor = Theme.C.Surface; ShowInTaskbar = false; TopMost = true;
        var lbl = new Label { Text = "FFlag Injector", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 18f), ForeColor = Theme.C.Accent };
        var sub = new Label { Text = $"v{UpdateChecker.CurrentVersion} \u2014 Loading...", Dock = DockStyle.Bottom, Height = 40, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9f), ForeColor = Theme.C.Sub };
        Controls.Add(lbl); Controls.Add(sub);
    }
}

sealed class DiffForm : Form
{
    readonly DataGridView _grid = new();
    readonly List<FlagEntry> _current;
    readonly Dictionary<string, string> _imported = new();
    readonly List<(string name, string curVal, string impVal, string status)> _rows = new();
    public List<(string name, string value)> ToApply { get; } = new();
    public DiffForm(List<FlagEntry> current, Dictionary<string, string> imported)
    {
        _current = current; _imported = imported;
        Text = "Flag Comparison"; Size = new Size(700, 500); StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.C.Bg; ForeColor = Theme.C.Fg; MinimumSize = new Size(500, 300);
        var curMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in current) curMap[f.Name] = f.Value;
        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in current) allKeys.Add(f.Name);
        foreach (var kv in imported) allKeys.Add(kv.Key);
        foreach (var k in allKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            bool inCur = curMap.TryGetValue(k, out var cv);
            bool inImp = imported.TryGetValue(k, out var iv);
            string st = inCur && inImp ? (cv == iv ? "Same" : "Modified") : inCur ? "Removed" : "New";
            _rows.Add((k, cv ?? "", iv ?? "", st));
        }
        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AllowUserToAddRows = false;
        _grid.BackgroundColor = Theme.C.Bg; _grid.DefaultCellStyle.BackColor = Theme.C.Bg;
        _grid.DefaultCellStyle.ForeColor = Theme.C.Fg; _grid.DefaultCellStyle.SelectionBackColor = Theme.C.Hover;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.C.Surface; _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.C.Sub;
        _grid.EnableHeadersVisualStyles = false; _grid.RowHeadersVisible = false; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = true;
        _grid.Columns.Add("Flag", "Flag"); _grid.Columns.Add("Current", "Current"); _grid.Columns.Add("Imported", "Imported"); _grid.Columns.Add("Status", "Status");
        foreach (var r in _rows)
        {
            int idx = _grid.Rows.Add(r.name, r.curVal, r.impVal, r.status);
            var row = _grid.Rows[idx];
            row.DefaultCellStyle.ForeColor = r.status switch { "New" => Theme.C.Green, "Modified" => Theme.C.Yellow, "Removed" => Theme.C.Red, _ => Theme.C.Sub };
        }
        var pnl = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Theme.C.Surface };
        var btnApply = new Button { Text = "Apply Selected Differences", Width = 200, Height = 30, Left = 10, Top = 5, FlatStyle = FlatStyle.Flat, BackColor = Theme.C.Accent, ForeColor = Theme.C.Bg };
        btnApply.FlatAppearance.BorderSize = 0;
        btnApply.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                string st = row.Cells[3].Value?.ToString() ?? "";
                if (st is "New" or "Modified")
                    ToApply.Add((row.Cells[0].Value?.ToString() ?? "", row.Cells[2].Value?.ToString() ?? ""));
            }
            DialogResult = DialogResult.OK; Close();
        };
        var btnClose = new Button { Text = "Close", Width = 80, Height = 30, Left = 220, Top = 5, FlatStyle = FlatStyle.Flat, BackColor = Theme.C.Surface, ForeColor = Theme.C.Fg };
        btnClose.FlatAppearance.BorderColor = Theme.C.Border; btnClose.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        pnl.Controls.Add(btnApply); pnl.Controls.Add(btnClose);
        Controls.Add(_grid); Controls.Add(pnl);
    }
}

sealed class HistoryForm : Form
{
    public HistoryForm(FlagEntry f)
    {
        Text = $"History \u2014 {f.Name}"; Size = new Size(500, 350); StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.C.Bg; ForeColor = Theme.C.Fg;
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
            BackgroundColor = Theme.C.Bg, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false
        };
        grid.DefaultCellStyle.BackColor = Theme.C.Bg; grid.DefaultCellStyle.ForeColor = Theme.C.Fg;
        grid.DefaultCellStyle.SelectionBackColor = Theme.C.Hover;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.C.Surface; grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.C.Sub;
        grid.Columns.Add("Time", "Time"); grid.Columns.Add("Old", "Old Value"); grid.Columns.Add("New", "New Value");
        foreach (var h in f.History.AsEnumerable().Reverse())
        {
            string ts = DateTime.TryParse(h.Timestamp, out var dt) ? dt.ToLocalTime().ToString("g") : h.Timestamp;
            grid.Rows.Add(ts, h.OldValue, h.NewValue);
        }
        if (f.History.Count == 0) grid.Rows.Add("", "No history", "");
        Controls.Add(grid);
    }
}

sealed class InputDialog : Form
{
    public string Value => _tb.Text.Trim();
    readonly TextBox _tb = new();
    public InputDialog(string title, string prompt, string defaultVal = "")
    {
        Text = title; Size = new Size(380, 160); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = Theme.C.Bg; ForeColor = Theme.C.Fg;
        var lbl = new Label { Text = prompt, Left = 12, Top = 14, AutoSize = true };
        _tb.Left = 12; _tb.Top = 40; _tb.Width = 340; _tb.Text = defaultVal;
        _tb.BackColor = Theme.C.Surface; _tb.ForeColor = Theme.C.Fg; _tb.BorderStyle = BorderStyle.FixedSingle;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 190, Top = 80, Width = 75, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Theme.C.Accent, ForeColor = Theme.C.Bg };
        ok.FlatAppearance.BorderSize = 0;
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 275, Top = 80, Width = 75, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Theme.C.Surface, ForeColor = Theme.C.Fg };
        cancel.FlatAppearance.BorderColor = Theme.C.Border;
        AcceptButton = ok; CancelButton = cancel;
        Controls.AddRange(new Control[] { lbl, _tb, ok, cancel });
    }
}

sealed class MainForm : Form
{
    readonly AppLog _log;
    readonly AppSettings _settings;
    readonly CancellationTokenSource _cts = new();
    readonly MemEngine _mem = new();
    readonly OffsetStore _off = new();
    readonly FlogBank _bank;
    readonly UndoStack _undo = new();
    readonly PresetManager _presets;
    readonly UpdateChecker _updater = new();
    readonly List<FlagEntry> _flags = new();
    readonly List<int> _botMap = new();
    readonly List<string> _topFiltered = new();
    readonly HashSet<string> _highlightFlags = new();
    readonly AutoCompleteStringCollection _searchAcTop = new(), _searchAcBot = new();
    readonly string _dir, _savePath, _settingsPath;
    readonly Font _hdrFont = new("Segoe UI Semibold", 9f);
    readonly ToolTip _tips = new() { InitialDelay = 300, ReshowDelay = 200 };
    int _monLock, _busyLock, _wdLock, _saveVer;
    volatile bool _autoApply = true, _watchdog = true, _realExit, _gameJoined;
    volatile int _lastPid; volatile bool _attaching;
    int _sortCol = -1; bool _sortAsc = true;
    string _selPreset = "";
    System.Threading.Timer _saveDebounce;
    System.Windows.Forms.Timer _monTimer = new(), _wdTimer = new(), _graceTimer = new(),
        _searchDebounce = new(), _toastTimer = new(), _highlightTimer = new();
    NotifyIcon _tray = new();
    SplitContainer _split = new();
    ListView _lvTop = new(), _lvBot = new();
    TextBox _searchTop = new(), _searchBot = new(), _edVal = new(), _edUpd = new(), _inlineEdit = new();
    Label _lblTopHdr = new(), _lblBotHdr = new(), _lblSel = new(), _lblMod = new();
    Button _btnAdd = new(), _btnUpd = new(), _btnTog = new(), _btnRem = new();
    ComboBox _cmbTypeTop = new(), _cmbTypeBot = new(), _cmbStatusBot = new();
    CheckBox _chkOnTop = new(), _chkTheme = new(), _chkSearchVal = new();
    ToolStripStatusLabel _st1 = new(), _st2 = new(), _st3 = new();
    ToolStripProgressBar _progress = new();
    ContextMenuStrip _ctxTop = new(), _ctxBot = new();
    Panel _legendPanel = new();
    int _inlineEditIdx = -1, _inlineEditSubIdx = -1;
    int _dragStartIdx = -1;
    string _cliImport = "", _cliPreset = "";
    bool _cliAutoApply, _cliMinimized;
    const int GraceIntervalMs = 1500, GraceMaxAttempts = 30, GraceStableNeeded = 6, BackupRotationCount = 5;
    int _graceAttempts, _graceStableCount;
    static readonly JsonSerializerOptions _jopt = new() { PropertyNameCaseInsensitive = true };

    public MainForm(string[] args)
    {
        ParseArgs(args);
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlagInjectorCS");
        _savePath = Path.Combine(_dir, "flags.json");
        _settingsPath = Path.Combine(_dir, "settings.json");
        _off.Cache1 = Path.Combine(_dir, "offset_cache1.hpp");
        _off.Cache2 = Path.Combine(_dir, "offset_cache2.hpp");
        Directory.CreateDirectory(_dir);
        _log = new AppLog(_dir);
        _settings = AppSettings.Load(_settingsPath);
        _bank = new FlogBank(_mem);
        _presets = new PresetManager(_dir);
        _autoApply = _settings.AutoApply;
        _watchdog = _settings.Watchdog;
        Theme.Set(_settings.DarkTheme);
        _saveDebounce = new System.Threading.Timer(SaveDebounceCallback, null, Timeout.Infinite, Timeout.Infinite);
        Text = "FFlag Injector"; MinimumSize = new Size(640, 520); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f); BackColor = Theme.C.Bg; ForeColor = Theme.C.Fg;
        Icon = MakeIcon(); AllowDrop = true; TopMost = _settings.AlwaysOnTop;
        if (_settings.W > 0 && _settings.H > 0) Size = new Size(_settings.W, _settings.H); else Size = new Size(820, 780);
        if (_settings.X >= 0 && _settings.Y >= 0)
        {
            var r = new Rectangle(_settings.X, _settings.Y, Size.Width, Size.Height);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r))) { StartPosition = FormStartPosition.Manual; Location = new Point(_settings.X, _settings.Y); }
        }
        _mem.Log += s => { _log.Info(s); Post(() => Toast(s)); };
        _off.Log += s => { _log.Info(s); Post(() => SetStatus(2, s, Theme.C.Sub)); };
        _bank.Log += s => { _log.Info(s); Post(() => SetStatus(3, s, Theme.C.Sub)); };
        Theme.Changed += () => Post(ApplyTheme);
        LoadFlags();
        var ct = _cts.Token;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            _off.Fetch(ct);
            Post(() => { SetStatus(2, $"{_off.Count} offsets loaded", Theme.C.Green); RefreshTop(); RefreshBot(); TryInitBank(); });
        });
        if (!string.IsNullOrEmpty(_updater.CheckUrl))
            Task.Run(async () =>
            {
                var (avail, ver, url) = await _updater.CheckAsync(ct);
                if (avail) Post(() => Toast($"Update available: v{ver}", 6000));
            }, ct);
        _monTimer.Interval = 1500; _monTimer.Tick += (_, _) => MonitorTick(); _monTimer.Start();
        _wdTimer.Interval = 4000; _wdTimer.Tick += (_, _) => WatchdogTick(); _wdTimer.Enabled = _watchdog;
        _graceTimer.Interval = GraceIntervalMs; _graceTimer.Tick += (_, _) => GraceTick();
        _searchDebounce.Interval = 200; _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); RefreshTop(); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); SetStatus(3, "", Theme.C.Sub); };
        _highlightTimer.Interval = 3000; _highlightTimer.Tick += (_, _) => { _highlightTimer.Stop(); _highlightFlags.Clear(); _lvBot.Invalidate(); };
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && files[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ImportJson(files[0]); };
        SystemEvents.PowerModeChanged += OnPowerMode;
        SetupTray();
        BuildUI();
        if (!string.IsNullOrEmpty(_cliImport)) Post(() => ImportJson(_cliImport));
        if (!string.IsNullOrEmpty(_cliPreset)) Post(() => LoadPreset(_cliPreset));
        if (_cliAutoApply) _autoApply = true;
        if (_cliMinimized) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; }
    }

    void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--import" when i + 1 < args.Length: _cliImport = args[++i]; break;
                case "--auto-apply": _cliAutoApply = true; break;
                case "--minimized": _cliMinimized = true; break;
                case "--preset" when i + 1 < args.Length: _cliPreset = args[++i]; break;
            }
        }
    }

    void Post(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch (ObjectDisposedException) { } }
    bool TrySetBusy() => Interlocked.CompareExchange(ref _busyLock, 1, 0) == 0;
    void ClearBusy() => Interlocked.Exchange(ref _busyLock, 0);

    void OnPowerMode(object? s, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Post(() => { if (_mem.On && !_mem.Alive()) { _graceTimer.Stop(); _gameJoined = false; _bank.Reset(); _mem.Detach(); _lastPid = 0; SetStatus(1, "Process lost after resume", Theme.C.Red); } });
    }

    void TryInitBank()
    {
        if (_bank.Ready || !_mem.On) return;
        if (_off.FlogPointer <= 0 && _off.StructOffsets.Count == 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (_off.StructOffsets.Count > 0) _bank.ApplyOffsets(_off.StructOffsets);
            if (_bank.Init()) Post(() => { Toast($"FlogBank: {_bank.Count} flags"); SetStatus(3, $"Bank: {_bank.Count} flags", Theme.C.Green); RefreshTop(); });
            else Post(() => SetStatus(3, "Bank: init failed", Theme.C.Peach));
        });
    }

    void SetupTray()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), BackColor = Theme.C.Surface, ForeColor = Theme.C.Fg };
        menu.Items.Add("Show", null, (_, _) => ShowWindow());
        menu.Items.Add("-");
        menu.Items.Add("Apply All", null, (_, _) => { ShowWindow(); ApplyAll(); });
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => { _realExit = true; Close(); });
        _tray.Icon = Icon; _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => ShowWindow(); _tray.Visible = true;
        UpdateTrayText();
    }

    void UpdateTrayText()
    {
        int active = _flags.Count(f => f.Enabled);
        string pid = _lastPid != 0 ? $" \u2014 PID {_lastPid}" : "";
        _tray.Text = $"FFlag Injector \u2014 {active} active{pid}";
    }

    void ShowWindow() { Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; Activate(); }

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp)) { g.SmoothingMode = SmoothingMode.AntiAlias; using var brush = new SolidBrush(Theme.C.Accent); g.FillEllipse(brush, 1, 1, 13, 13); }
        var h = bmp.GetHicon(); var ico = (Icon)Icon.FromHandle(h).Clone(); W32.DestroyIcon(h); return ico;
    }

    static void SetDouble(Control c) => typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(c, true);
    static void ApplyDarkScrollbars(Control c) { try { W32.SetWindowTheme(c.Handle, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int val = Theme.IsDark ? 1 : 0;
        if (W32.DwmSetWindowAttribute(Handle, 20, ref val, sizeof(int)) != 0)
            W32.DwmSetWindowAttribute(Handle, 19, ref val, sizeof(int));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_settings.Split > 0 && _settings.Split < ClientSize.Height) _split.SplitterDistance = _settings.Split;
        else _split.SplitterDistance = (int)(ClientSize.Height * 0.45);
        FixCols();
        W32.SendMessage(_searchTop.Handle, 0x1501, (IntPtr)1, "Search flags...");
        W32.SendMessage(_searchBot.Handle, 0x1501, (IntPtr)1, "Search modified flags (name or value)...");
        W32.SendMessage(_edVal.Handle, 0x1501, (IntPtr)1, "Value");
        W32.SendMessage(_edUpd.Handle, 0x1501, (IntPtr)1, "New value");
        ApplyDarkScrollbars(_lvTop); ApplyDarkScrollbars(_lvBot);
        if (_cliMinimized) { Hide(); WindowState = FormWindowState.Minimized; }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            if (!_settings.FirstMinimizeDone) { _tray.ShowBalloonTip(3000, "FFlag Injector", "Still running in tray", ToolTipIcon.Info); _settings.FirstMinimizeDone = true; }
            Hide(); return;
        }
        FixCols(); CommitInlineEdit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_realExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        _settings.AutoApply = _autoApply; _settings.Watchdog = _watchdog; _settings.AlwaysOnTop = TopMost; _settings.DarkTheme = Theme.IsDark;
        if (WindowState == FormWindowState.Normal) { _settings.X = Location.X; _settings.Y = Location.Y; _settings.W = Size.Width; _settings.H = Size.Height; }
        try { _settings.Split = _split.SplitterDistance; } catch { }
        _settings.Save(_settingsPath);
        FlushSave();
        _cts.Cancel();
        SystemEvents.PowerModeChanged -= OnPowerMode;
        _monTimer.Stop(); _wdTimer.Stop(); _graceTimer.Stop(); _searchDebounce.Stop(); _toastTimer.Stop(); _highlightTimer.Stop();
        _monTimer.Dispose(); _wdTimer.Dispose(); _graceTimer.Dispose(); _searchDebounce.Dispose(); _toastTimer.Dispose(); _highlightTimer.Dispose();
        _saveDebounce.Dispose();
        _tray.ContextMenuStrip?.Dispose(); _tray.Visible = false; _tray.Dispose();
        _ctxTop.Dispose(); _ctxBot.Dispose(); _tips.Dispose(); _hdrFont.Dispose();
        _mem.Dispose(); _log.Dispose(); _cts.Dispose();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => keyData switch
    {
        Keys.Control | Keys.Shift | Keys.A => Do(ApplyAll),
        Keys.Control | Keys.O => Do(() => ImportJson()),
        Keys.Control | Keys.S => Do(ExportJson),
        Keys.Control | Keys.Z when !IsTextBoxFocused() => Do(PerformUndo),
        Keys.Control | Keys.Y when !IsTextBoxFocused() => Do(PerformRedo),
        Keys.Control | Keys.F => Do(() => { if (_split.Panel1.ContainsFocus) _searchTop.Focus(); else _searchBot.Focus(); }),
        Keys.Control | Keys.Shift | Keys.C => Do(CopyAllJson),
        Keys.F5 => Do(() => { RefreshTop(); RefreshBot(); }),
        Keys.Delete when _lvBot.Focused => Do(RemoveSelected),
        _ => base.ProcessCmdKey(ref msg, keyData)
    };

    bool IsTextBoxFocused() => ActiveControl is TextBox;
    bool Do(Action a) { a(); return true; }

    void FixCols()
    {
        if (_lvTop.Columns.Count >= 2) { int w = _lvTop.ClientSize.Width; _lvTop.Columns[0].Width = (int)(w * 0.72); _lvTop.Columns[1].Width = (int)(w * 0.26); }
        if (_lvBot.Columns.Count >= 5)
        {
            int w = _lvBot.ClientSize.Width;
            _lvBot.Columns[0].Width = (int)(w * 0.42); _lvBot.Columns[1].Width = (int)(w * 0.18);
            _lvBot.Columns[2].Width = (int)(w * 0.10); _lvBot.Columns[3].Width = (int)(w * 0.17); _lvBot.Columns[4].Width = (int)(w * 0.11);
        }
    }

    Button MakeBtn(string text, int width, bool primary = false)
    {
        var b = new Button { Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, BackColor = primary ? Theme.C.Accent : Theme.C.Surface, ForeColor = primary ? Theme.C.Bg : Theme.C.Fg };
        b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = primary ? Theme.C.Accent : Theme.C.Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(160, 200, 255) : Theme.C.Hover; return b;
    }

    void BuildUI()
    {
        var status = new StatusStrip { BackColor = Theme.C.Surface, Renderer = new DarkRenderer(), SizingGrip = false };
        _st1.Spring = false; _st1.AutoSize = false; _st1.Width = 290; _st1.TextAlign = ContentAlignment.MiddleLeft; _st1.Text = "  Not detected"; _st1.ForeColor = Theme.C.Red;
        _st2.Spring = false; _st2.AutoSize = false; _st2.Width = 220; _st2.TextAlign = ContentAlignment.MiddleLeft; _st2.Text = "  Offsets: loading..."; _st2.ForeColor = Theme.C.Sub;
        _st3.Spring = true; _st3.TextAlign = ContentAlignment.MiddleLeft; _st3.Text = ""; _st3.ForeColor = Theme.C.Sub;
        _progress.Visible = false; _progress.Width = 120; _progress.Style = ProgressBarStyle.Continuous;
        status.Items.AddRange(new ToolStripItem[] { _st1, _st2, _progress, _st3 });

        _legendPanel = new Panel { Dock = DockStyle.Bottom, Height = 22, BackColor = Theme.C.Surface, Padding = new Padding(6, 2, 6, 2) };
        var legendFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = false };
        void AddLegendDot(Color c, string txt) { var lbl = new Label { AutoSize = true, Text = $"\u25CF {txt}", ForeColor = c, Padding = new Padding(4, 2, 8, 0), Font = new Font("Segoe UI", 7.5f) }; legendFlow.Controls.Add(lbl); }
        AddLegendDot(Theme.C.Green, "Applied"); AddLegendDot(Theme.C.Peach, "No Offset"); AddLegendDot(Theme.C.Red, "Failed"); AddLegendDot(Theme.C.Border, "Disabled"); AddLegendDot(Theme.C.Fg, "Active"); AddLegendDot(Theme.C.Yellow, "Highlight");
        _legendPanel.Controls.Add(legendFlow);

        var actPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 74, Padding = new Padding(6, 4, 6, 2), BackColor = Theme.C.Surface, WrapContents = true };
        var btnApply = MakeBtn("\u25B6 Apply All (Ctrl+Shift+A)", 190, true);
        var btnImp = MakeBtn("Import (Ctrl+O)", 120);
        var btnExp = MakeBtn("Export (Ctrl+S)", 120);
        var btnCAS = MakeBtn("ClientAppSettings", 130);
        var btnCopy = MakeBtn("Copy JSON (Ctrl+Shift+C)", 180);
        var btnCompare = MakeBtn("Compare...", 90);
        var btnClear = MakeBtn("Clear All", 85); btnClear.ForeColor = Theme.C.Red; btnClear.FlatAppearance.BorderColor = Theme.C.Red;
        var btnPreset = MakeBtn("Presets \u25BC", 90);
        btnApply.Click += (_, _) => ApplyAll();
        btnImp.Click += (_, _) => ImportJson();
        btnExp.Click += (_, _) => ExportJson();
        btnCAS.Click += (_, _) => ExportClientAppSettings();
        btnCopy.Click += (_, _) => CopyAllJson();
        btnCompare.Click += (_, _) => ShowDiff();
        btnClear.Click += (_, _) => RemoveAll();
        btnPreset.Click += (_, _) => ShowPresetMenu(btnPreset);
        _tips.SetToolTip(btnApply, "Apply all enabled flags"); _tips.SetToolTip(btnImp, "Import flags from JSON file");
        _tips.SetToolTip(btnExp, "Export flags to JSON file"); _tips.SetToolTip(btnCAS, "Export to Roblox ClientAppSettings folder");
        _tips.SetToolTip(btnCopy, "Copy all flags as JSON to clipboard"); _tips.SetToolTip(btnCompare, "Compare current flags against a JSON file");
        _tips.SetToolTip(btnPreset, "Save/Load named presets");

        var chkAuto = new CheckBox { Text = "Auto-apply", Checked = _autoApply, AutoSize = true, ForeColor = Theme.C.Sub, Padding = new Padding(8, 7, 0, 0) };
        var chkWd = new CheckBox { Text = "Watchdog", Checked = _watchdog, AutoSize = true, ForeColor = Theme.C.Sub, Padding = new Padding(4, 7, 0, 0) };
        _chkOnTop = new CheckBox { Text = "On Top", Checked = _settings.AlwaysOnTop, AutoSize = true, ForeColor = Theme.C.Sub, Padding = new Padding(4, 7, 0, 0) };
        _chkTheme = new CheckBox { Text = "Light", Checked = !Theme.IsDark, AutoSize = true, ForeColor = Theme.C.Sub, Padding = new Padding(4, 7, 0, 0) };
        var chkConfirm = new CheckBox { Text = "Confirm", Checked = _settings.ConfirmApplyAll, AutoSize = true, ForeColor = Theme.C.Sub, Padding = new Padding(4, 7, 0, 0) };
        chkAuto.CheckedChanged += (_, _) => _autoApply = chkAuto.Checked;
        chkWd.CheckedChanged += (_, _) => { _watchdog = chkWd.Checked; _wdTimer.Enabled = chkWd.Checked; };
        _chkOnTop.CheckedChanged += (_, _) => TopMost = _chkOnTop.Checked;
        _chkTheme.CheckedChanged += (_, _) => Theme.Toggle();
        chkConfirm.CheckedChanged += (_, _) => _settings.ConfirmApplyAll = chkConfirm.Checked;
        _tips.SetToolTip(chkAuto, "Auto-apply on game join"); _tips.SetToolTip(chkWd, "Re-apply reverted flags");
        _tips.SetToolTip(_chkOnTop, "Keep window on top"); _tips.SetToolTip(_chkTheme, "Toggle light/dark theme");
        _tips.SetToolTip(chkConfirm, "Ask before Apply All");

        actPanel.Controls.AddRange(new Control[] { btnApply, btnImp, btnExp, btnCAS, btnCopy, btnCompare, btnClear, btnPreset, chkAuto, chkWd, _chkOnTop, _chkTheme, chkConfirm });

        _split.Dock = DockStyle.Fill; _split.Orientation = Orientation.Horizontal; _split.BackColor = Theme.C.Border; _split.SplitterWidth = 3;
        _split.Panel1.BackColor = Theme.C.Bg; _split.Panel2.BackColor = Theme.C.Bg;

        _lblTopHdr = new Label { Text = "AVAILABLE FLAGS", Dock = DockStyle.Top, Height = 26, Padding = new Padding(6, 6, 0, 0), Font = _hdrFont, ForeColor = Theme.C.Sub, BackColor = Theme.C.Bg };

        var topFilterPanel = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Theme.C.Bg };
        _searchTop.Width = 300; _searchTop.BackColor = Theme.C.Surface; _searchTop.ForeColor = Theme.C.Fg; _searchTop.BorderStyle = BorderStyle.FixedSingle;
        _searchTop.Left = 0; _searchTop.Top = 2;
        _searchTop.AutoCompleteMode = AutoCompleteMode.Suggest; _searchTop.AutoCompleteSource = AutoCompleteSource.CustomSource; _searchTop.AutoCompleteCustomSource = _searchAcTop;
        _searchTop.TextChanged += (_, _) => { _searchDebounce.Stop(); _searchDebounce.Start(); AddSearchHistory(_searchTop.Text); };
        _cmbTypeTop = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Left = 306, Top = 2, BackColor = Theme.C.Surface, ForeColor = Theme.C.Fg, FlatStyle = FlatStyle.Flat };
        _cmbTypeTop.Items.AddRange(new object[] { "All Types", "Bool", "Int", "Float", "String" });
        _cmbTypeTop.SelectedIndex = 0; _cmbTypeTop.SelectedIndexChanged += (_, _) => RefreshTop();
        topFilterPanel.Controls.Add(_searchTop); topFilterPanel.Controls.Add(_cmbTypeTop);
        topFilterPanel.Resize += (_, _) => { _searchTop.Width = Math.Max(topFilterPanel.Width - 110, 100); _cmbTypeTop.Left = _searchTop.Right + 6; };

        _lvTop.Dock = DockStyle.Fill; _lvTop.View = View.Details; _lvTop.FullRowSelect = true; _lvTop.MultiSelect = false;
        _lvTop.OwnerDraw = true; _lvTop.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _lvTop.BackColor = Theme.C.Bg; _lvTop.ForeColor = Theme.C.Fg; _lvTop.BorderStyle = BorderStyle.None; _lvTop.HideSelection = false;
        _lvTop.VirtualMode = true; _lvTop.VirtualListSize = 0;
        _lvTop.Columns.Add("Flag Name", 500); _lvTop.Columns.Add("Category", 120);
        _lvTop.RetrieveVirtualItem += (_, e) =>
        {
            if (e.ItemIndex < _topFiltered.Count)
            {
                string n = _topFiltered[e.ItemIndex]; string cat = FlagCategory.Categorize(n);
                string desc = $"{n} [{cat}]";
                e.Item = new ListViewItem(new[] { n, cat }) { ForeColor = Theme.C.Fg, ToolTipText = desc };
            }
            else e.Item = new ListViewItem(new[] { "", "" }) { ForeColor = Theme.C.Fg };
        };
        _lvTop.SearchForVirtualItem += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            for (int i = 0; i < _topFiltered.Count; i++) if (_topFiltered[i].StartsWith(e.Text, StringComparison.OrdinalIgnoreCase)) { e.Index = i; return; }
        };
        _lvTop.SelectedIndexChanged += (_, _) => TopClick();
        _lvTop.DoubleClick += (_, _) => { TopClick(); if (_selPreset != "") _edVal.Focus(); };
        _lvTop.DrawColumnHeader += LvDrawHeader;
        _lvTop.DrawItem += (_, _) => { };
        _lvTop.DrawSubItem += LvDrawSub;
        _lvTop.ShowItemToolTips = true;
        _lvTop.HandleCreated += (_, _) => ApplyDarkScrollbars(_lvTop);
        SetDouble(_lvTop);

        _ctxTop.Renderer = new DarkRenderer(); _ctxTop.BackColor = Theme.C.Surface; _ctxTop.ForeColor = Theme.C.Fg;
        _ctxTop.Items.Add("Add selected flag", null, (_, _) => AddFlag());
        _ctxTop.Items.Add("Copy name", null, (_, _) => { if (_selPreset != "") Clipboard.SetText(_selPreset); });
        _ctxTop.Opening += (_, e) => { if (_selPreset == "") e.Cancel = true; };
        _lvTop.ContextMenuStrip = _ctxTop;

        var addRow = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = Theme.C.Surface, Padding = new Padding(6, 4, 6, 4) };
        _lblSel = new Label { Text = "No flag selected", AutoSize = false, Width = 200, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.C.Sub, AutoEllipsis = true };
        _edVal.Width = 160; _edVal.BackColor = Theme.C.Bg; _edVal.ForeColor = Theme.C.Fg; _edVal.BorderStyle = BorderStyle.FixedSingle;
        _edVal.TextChanged += (_, _) => _btnAdd.Enabled = _selPreset != "" && _edVal.Text.Trim() != "";
        _edVal.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddFlag(); } };
        _btnAdd = MakeBtn("Add", 60); _btnAdd.Enabled = false; _btnAdd.Click += (_, _) => AddFlag();
        var addFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = false };
        addFlow.Controls.AddRange(new Control[] { _lblSel, _edVal, _btnAdd });
        addRow.Controls.Add(addFlow);

        _split.Panel1.Controls.Add(_lvTop); _split.Panel1.Controls.Add(addRow); _split.Panel1.Controls.Add(topFilterPanel); _split.Panel1.Controls.Add(_lblTopHdr);

        _lblBotHdr = new Label { Text = "MODIFIED FLAGS", Dock = DockStyle.Top, Height = 26, Padding = new Padding(6, 6, 0, 0), Font = _hdrFont, ForeColor = Theme.C.Sub, BackColor = Theme.C.Bg };

        var botFilterPanel = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Theme.C.Bg };
        _searchBot.Width = 220; _searchBot.BackColor = Theme.C.Surface; _searchBot.ForeColor = Theme.C.Fg; _searchBot.BorderStyle = BorderStyle.FixedSingle;
        _searchBot.Left = 0; _searchBot.Top = 2;
        _searchBot.AutoCompleteMode = AutoCompleteMode.Suggest; _searchBot.AutoCompleteSource = AutoCompleteSource.CustomSource; _searchBot.AutoCompleteCustomSource = _searchAcBot;
        _searchBot.TextChanged += (_, _) => RefreshBot();
        _chkSearchVal = new CheckBox { Text = "Search value", AutoSize = true, Checked = true, Left = 226, Top = 4, ForeColor = Theme.C.Sub };
        _chkSearchVal.CheckedChanged += (_, _) => RefreshBot();
        _cmbTypeBot = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85, Left = 340, Top = 2, BackColor = Theme.C.Surface, ForeColor = Theme.C.Fg, FlatStyle = FlatStyle.Flat };
        _cmbTypeBot.Items.AddRange(new object[] { "All Types", "Bool", "Int", "Float", "String" }); _cmbTypeBot.SelectedIndex = 0; _cmbTypeBot.SelectedIndexChanged += (_, _) => RefreshBot();
        _cmbStatusBot = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Left = 431, Top = 2, BackColor = Theme.C.Surface, ForeColor = Theme.C.Fg, FlatStyle = FlatStyle.Flat };
        _cmbStatusBot.Items.AddRange(new object[] { "All", "Enabled", "Disabled" }); _cmbStatusBot.SelectedIndex = 0; _cmbStatusBot.SelectedIndexChanged += (_, _) => RefreshBot();
        botFilterPanel.Controls.AddRange(new Control[] { _searchBot, _chkSearchVal, _cmbTypeBot, _cmbStatusBot });
        botFilterPanel.Resize += (_, _) =>
        {
            int x = 0;
            _searchBot.Width = Math.Max((int)(botFilterPanel.Width * 0.35), 120); x = _searchBot.Right + 6;
            _chkSearchVal.Left = x; x = _chkSearchVal.Right + 6;
            _cmbTypeBot.Left = x; x = _cmbTypeBot.Right + 6;
            _cmbStatusBot.Left = x;
        };

        _lvBot.Dock = DockStyle.Fill; _lvBot.View = View.Details; _lvBot.FullRowSelect = true; _lvBot.MultiSelect = true;
        _lvBot.OwnerDraw = true; _lvBot.HeaderStyle = ColumnHeaderStyle.Clickable;
        _lvBot.BackColor = Theme.C.Bg; _lvBot.ForeColor = Theme.C.Fg; _lvBot.BorderStyle = BorderStyle.None; _lvBot.HideSelection = false;
        _lvBot.VirtualMode = true; _lvBot.VirtualListSize = 0;
        _lvBot.Columns.Add("Flag", 300); _lvBot.Columns.Add("Value", 100); _lvBot.Columns.Add("Type", 60); _lvBot.Columns.Add("Status", 100); _lvBot.Columns.Add("Mode", 70);
        _lvBot.ShowItemToolTips = true;
        _lvBot.RetrieveVirtualItem += BotRetrieveItem;
        _lvBot.SearchForVirtualItem += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            for (int i = 0; i < _botMap.Count; i++) if (_flags[_botMap[i]].Name.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase)) { e.Index = i; return; }
        };
        _lvBot.SelectedIndexChanged += (_, _) => BotClick();
        _lvBot.ColumnClick += (_, e) => { if (_sortCol == e.Column) _sortAsc = !_sortAsc; else { _sortCol = e.Column; _sortAsc = true; } RefreshBot(); };
        _lvBot.MouseDoubleClick += BotDoubleClick;
        _lvBot.MouseDown += BotMouseDown;
        _lvBot.DragOver += BotDragOver;
        _lvBot.DragDrop += BotDragDrop;
        _lvBot.AllowDrop = true;
        _lvBot.DrawColumnHeader += LvDrawHeader;
        _lvBot.DrawItem += (_, _) => { };
        _lvBot.DrawSubItem += LvDrawSubBot;
        _lvBot.HandleCreated += (_, _) => ApplyDarkScrollbars(_lvBot);
        SetDouble(_lvBot);

        _inlineEdit.Visible = false; _inlineEdit.BorderStyle = BorderStyle.FixedSingle;
        _inlineEdit.BackColor = Theme.C.Bg; _inlineEdit.ForeColor = Theme.C.Fg;
        _inlineEdit.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitInlineEdit(); } else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelInlineEdit(); } };
        _inlineEdit.LostFocus += (_, _) => CommitInlineEdit();
        _lvBot.Controls.Add(_inlineEdit);

        _ctxBot.Renderer = new DarkRenderer(); _ctxBot.BackColor = Theme.C.Surface; _ctxBot.ForeColor = Theme.C.Fg;
        _ctxBot.Items.Add("Apply selected", null, (_, _) => ApplySelected());
        _ctxBot.Items.Add("Update value", null, (_, _) => UpdateFlag());
        _ctxBot.Items.Add("Toggle on/off", null, (_, _) => ToggleSelected());
        _ctxBot.Items.Add("Duplicate", null, (_, _) => DuplicateFlag());
        _ctxBot.Items.Add("Set mode: OnJoin", null, (_, _) => SetModeSelected(ApplyMode.OnJoin));
        _ctxBot.Items.Add("Set mode: Immediate", null, (_, _) => SetModeSelected(ApplyMode.Immediate));
        _ctxBot.Items.Add("Reset to memory value", null, (_, _) => ResetToDefault());
        _ctxBot.Items.Add("View history", null, (_, _) => ViewHistory());
        _ctxBot.Items.Add("-");
        _ctxBot.Items.Add("Copy name", null, (_, _) => CopySelectedNames());
        _ctxBot.Items.Add("Copy value", null, (_, _) => CopySelectedValues());
        _ctxBot.Items.Add("-");
        _ctxBot.Items.Add("Enable all", null, (_, _) => BulkEnable(true));
        _ctxBot.Items.Add("Disable all", null, (_, _) => BulkEnable(false));
        _ctxBot.Items.Add("Move up", null, (_, _) => MoveSelected(-1));
        _ctxBot.Items.Add("Move down", null, (_, _) => MoveSelected(1));
        _ctxBot.Items.Add("-");
        _ctxBot.Items.Add("Remove selected (Del)", null, (_, _) => RemoveSelected());
        _ctxBot.Opening += (_, e) => { if (SelectedBotIndices().Length == 0) e.Cancel = true; };
        _lvBot.ContextMenuStrip = _ctxBot;

        var modRow = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = Theme.C.Surface, Padding = new Padding(6, 4, 6, 4) };
        _lblMod = new Label { Text = "No flag selected", AutoSize = false, Width = 180, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.C.Sub, AutoEllipsis = true };
        _edUpd.Width = 130; _edUpd.BackColor = Theme.C.Bg; _edUpd.ForeColor = Theme.C.Fg; _edUpd.BorderStyle = BorderStyle.FixedSingle;
        _edUpd.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UpdateFlag(); } };
        _btnUpd = MakeBtn("Update", 65); _btnUpd.Enabled = false; _btnUpd.Click += (_, _) => UpdateFlag();
        _btnTog = MakeBtn("Disable", 65); _btnTog.Enabled = false; _btnTog.Click += (_, _) => ToggleSelected();
        _btnRem = MakeBtn("Remove", 65); _btnRem.Enabled = false; _btnRem.ForeColor = Theme.C.Red; _btnRem.FlatAppearance.BorderColor = Theme.C.Red;
        _btnRem.Click += (_, _) => RemoveSelected();
        var modFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = false };
        modFlow.Controls.AddRange(new Control[] { _lblMod, _edUpd, _btnUpd, _btnTog, _btnRem });
        modRow.Controls.Add(modFlow);

        _split.Panel2.Controls.Add(_lvBot); _split.Panel2.Controls.Add(modRow); _split.Panel2.Controls.Add(botFilterPanel); _split.Panel2.Controls.Add(_lblBotHdr);

        Controls.Add(_split); Controls.Add(_legendPanel); Controls.Add(actPanel); Controls.Add(status);

        foreach (var s in _settings.SearchHistory) { _searchAcTop.Add(s); _searchAcBot.Add(s); }
        RefreshTop(); RefreshBot();
    }

    void ApplyTheme()
    {
        BackColor = Theme.C.Bg; ForeColor = Theme.C.Fg;
        int val = Theme.IsDark ? 1 : 0;
        try
        {
            if (W32.DwmSetWindowAttribute(Handle, 20, ref val, sizeof(int)) != 0)
                W32.DwmSetWindowAttribute(Handle, 19, ref val, sizeof(int));
        }
        catch { }
        ApplyThemeRecursive(this);
        _lvTop.Invalidate(); _lvBot.Invalidate();
        ApplyDarkScrollbars(_lvTop); ApplyDarkScrollbars(_lvBot);
        Invalidate(true);
    }

    void ApplyThemeRecursive(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is ListView lv) { lv.BackColor = Theme.C.Bg; lv.ForeColor = Theme.C.Fg; }
            else if (c is TextBox tb) { tb.BackColor = Theme.C.Surface; tb.ForeColor = Theme.C.Fg; }
            else if (c is ComboBox cb) { cb.BackColor = Theme.C.Surface; cb.ForeColor = Theme.C.Fg; }
            else if (c is Button btn) { if (btn.BackColor != Theme.C.Accent) { btn.BackColor = Theme.C.Surface; btn.ForeColor = Theme.C.Fg; btn.FlatAppearance.BorderColor = Theme.C.Border; } }
            else if (c is Label lbl) { if (lbl.ForeColor != Theme.C.Red && lbl.ForeColor != Theme.C.Green && lbl.ForeColor != Theme.C.Peach) lbl.ForeColor = Theme.C.Sub; }
            else if (c is CheckBox chk) { chk.ForeColor = Theme.C.Sub; }
            else if (c is Panel pnl) { pnl.BackColor = (pnl.Parent is SplitContainer) ? Theme.C.Bg : Theme.C.Surface; }
            else if (c is SplitContainer sp) { sp.BackColor = Theme.C.Border; sp.Panel1.BackColor = Theme.C.Bg; sp.Panel2.BackColor = Theme.C.Bg; }
            else if (c is StatusStrip ss) { ss.BackColor = Theme.C.Surface; ss.Renderer = new DarkRenderer(); }
            else if (c is FlowLayoutPanel fp) { fp.BackColor = fp.Parent is Panel p && p.Dock == DockStyle.Bottom ? Theme.C.Surface : Theme.C.Bg; }
            if (c.HasChildren) ApplyThemeRecursive(c);
        }
    }

    void AddSearchHistory(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 3) return;
        if (_settings.SearchHistory.Contains(text, StringComparer.OrdinalIgnoreCase)) return;
        _settings.SearchHistory.Add(text);
        if (_settings.SearchHistory.Count > 50) _settings.SearchHistory.RemoveAt(0);
        _searchAcTop.Add(text); _searchAcBot.Add(text);
    }

    void BotRetrieveItem(object? s, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < _botMap.Count)
        {
            var f = _flags[_botMap[e.ItemIndex]];
            string st = f.Enabled ? (f.Status == "" ? "Active" : f.Status) : "Disabled";
            if (f.Enabled && st == "Active" && _off.Resolve(f.Name) == null && (!_bank.Ready || _bank.Resolve(f.Name) == null)) st = "No Offset";
            string cat = FlagCategory.Categorize(f.Name);
            var item = new ListViewItem(new[] { f.Name, f.Value, f.Type.ToString(), st, f.Mode.ToString() }) { ToolTipText = $"{f.Name} [{cat}]\n{f.Value}" };
            if (!f.Enabled) item.ForeColor = Theme.C.Border;
            else if (st.StartsWith("Applied")) item.ForeColor = Theme.C.Green;
            else if (st == "Failed") item.ForeColor = Theme.C.Red;
            else if (st == "No Offset") item.ForeColor = Theme.C.Peach;
            else item.ForeColor = Theme.C.Fg;
            e.Item = item;
        }
        else e.Item = new ListViewItem(new[] { "", "", "", "", "" });
    }

    void LvDrawHeader(object? s, DrawListViewColumnHeaderEventArgs e)
    {
        using var bgBr = new SolidBrush(Theme.C.Surface);
        e.Graphics.FillRectangle(bgBr, e.Bounds);
        string txt = e.Header!.Text;
        if (s == _lvBot && _sortCol == e.ColumnIndex) txt += _sortAsc ? " \u25B2" : " \u25BC";
        TextRenderer.DrawText(e.Graphics, txt, Font, e.Bounds, Theme.C.Sub, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.LeftAndRightPadding);
        using var bp = new Pen(Theme.C.Border);
        e.Graphics.DrawLine(bp, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    void LvDrawSub(object? s, DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null) return;
        Color bg = e.Item.Selected ? Theme.C.Hover : (e.ItemIndex % 2 == 0 ? Theme.C.Bg : Theme.C.Row2);
        using (var brush = new SolidBrush(bg)) e.Graphics.FillRectangle(brush, e.Bounds);
        var fg = e.Item.Selected ? Theme.C.Fg : e.SubItem.ForeColor;
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, e.Bounds, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.LeftAndRightPadding);
    }

    void LvDrawSubBot(object? s, DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null) return;
        bool highlighted = e.ItemIndex < _botMap.Count && _highlightFlags.Contains(_flags[_botMap[e.ItemIndex]].Name);
        Color bg;
        if (e.Item.Selected) bg = Theme.C.Hover;
        else if (highlighted) bg = Color.FromArgb(60, Theme.C.Yellow.R, Theme.C.Yellow.G, Theme.C.Yellow.B);
        else bg = e.ItemIndex % 2 == 0 ? Theme.C.Bg : Theme.C.Row2;
        using (var brush = new SolidBrush(bg)) e.Graphics.FillRectangle(brush, e.Bounds);
        var fg = e.Item.Selected ? Theme.C.Fg : e.SubItem.ForeColor;
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, e.Bounds, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.LeftAndRightPadding);
    }

    void SetStatus(int idx, string txt, Color? col = null)
    {
        if (InvokeRequired) { Post(() => SetStatus(idx, txt, col)); return; }
        switch (idx)
        {
            case 1: _st1.Text = "  " + txt; if (col.HasValue) _st1.ForeColor = col.Value; break;
            case 2: _st2.Text = "  " + txt; if (col.HasValue) _st2.ForeColor = col.Value; break;
            case 3: _st3.Text = txt; if (col.HasValue) _st3.ForeColor = col.Value; break;
        }
    }

    void Toast(string msg, int ms = 3500) { SetStatus(3, msg, Theme.C.Sub); _toastTimer.Stop(); _toastTimer.Interval = ms; _toastTimer.Start(); }

    void MonitorTick()
    {
        if (Interlocked.CompareExchange(ref _monLock, 1, 0) != 0) return;
        if (_attaching) { Interlocked.Exchange(ref _monLock, 0); return; }
        Task.Run(() =>
        {
            int pid = 0;
            try
            {
                var procs = Process.GetProcessesByName("RobloxPlayerBeta");
                int bestPid = 0; bool hasWindow = false;
                foreach (var p in procs)
                {
                    try { if (!p.HasExited) { if (p.MainWindowHandle != IntPtr.Zero && !hasWindow) { bestPid = p.Id; hasWindow = true; } else if (bestPid == 0) bestPid = p.Id; } }
                    catch { } finally { p.Dispose(); }
                }
                pid = bestPid;
            }
            catch { }
            return pid;
        }).ContinueWith(t =>
        {
            try { int result = 0; try { result = t.Result; } catch { } Post(() => HandleMonResult(result)); }
            finally { Interlocked.Exchange(ref _monLock, 0); }
        });
    }

    void HandleMonResult(int pid)
    {
        if (pid != 0 && pid != _lastPid)
        {
            _lastPid = pid; _gameJoined = false; _graceTimer.Stop(); _bank.Reset(); _attaching = true;
            SetStatus(1, $"Attaching PID {pid}...", Theme.C.Yellow); _log.Info($"Detected PID {pid}");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try { ok = _mem.Attach(pid, "RobloxPlayerBeta.exe", _cts.Token); } finally { _attaching = false; }
                Post(() =>
                {
                    if (ok)
                    {
                        SetStatus(1, $"PID {pid} \u2014 0x{_mem.Base:X}", Theme.C.Green);
                        UpdateTrayText(); TryInitBank();
                        if (_autoApply && _off.Count > 0) StartGrace();
                        ApplyImmediateFlags();
                    }
                    else { SetStatus(1, "Attach failed", Theme.C.Red); _lastPid = 0; }
                });
            });
        }
        else if (pid == 0 && _lastPid != 0)
        {
            _graceTimer.Stop(); _gameJoined = false; _bank.Reset(); _mem.Detach(); _lastPid = 0;
            SetStatus(1, "Not detected", Theme.C.Red); UpdateTrayText(); Toast("Roblox disconnected"); _log.Info("Process exited");
        }
        else if (pid != 0 && !_attaching && _mem.On && !_mem.Alive())
        {
            _graceTimer.Stop(); _gameJoined = false; _bank.Reset(); _mem.Detach(); _lastPid = 0;
            SetStatus(1, "Process exited", Theme.C.Red); _log.Warn("Process exited unexpectedly");
        }
    }

    void ApplyImmediateFlags()
    {
        if (!_mem.On) return;
        var imm = _flags.Where(f => f.Enabled && f.Mode == ApplyMode.Immediate).ToArray();
        if (imm.Length == 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            int ok = 0;
            foreach (var f in imm) if (ApplySingle(f)) ok++;
            if (ok > 0) Post(() => { RefreshBot(); Toast($"Immediate: {ok} applied"); });
        });
    }

    void StartGrace()
    {
        _graceTimer.Stop(); _graceAttempts = 0; _graceStableCount = 0; _gameJoined = false;
        SetStatus(3, "Waiting for game join...", Theme.C.Yellow); _graceTimer.Start();
    }

    void GraceTick()
    {
        _graceAttempts++;
        if (!_mem.On || !_mem.Alive()) { _graceTimer.Stop(); _gameJoined = false; return; }
        bool windowReady = false;
        try { using var p = Process.GetProcessById(_mem.Pid); windowReady = p.MainWindowHandle != IntPtr.Zero && !p.HasExited; } catch { }
        if (windowReady) _graceStableCount++; else _graceStableCount = 0;
        if (_graceStableCount >= GraceStableNeeded)
        {
            _graceTimer.Stop(); _gameJoined = true;
            if (_mem.On && _autoApply && (_off.Count > 0 || _bank.Ready)) { SetStatus(3, "Game joined, applying...", Theme.C.Green); ApplyAll(); }
            _tray.ShowBalloonTip(3000, "FFlag Injector", "Game detected, applying flags", ToolTipIcon.Info); return;
        }
        if (_graceAttempts >= GraceMaxAttempts)
        {
            _graceTimer.Stop(); _gameJoined = true;
            if (_mem.On && _autoApply && (_off.Count > 0 || _bank.Ready)) { SetStatus(3, "Grace timeout, applying...", Theme.C.Yellow); ApplyAll(); } return;
        }
        string phase = windowReady ? $"Stabilizing {_graceStableCount}/{GraceStableNeeded}" : "Waiting for window";
        SetStatus(3, $"Grace {_graceAttempts}/{GraceMaxAttempts}: {phase}", Theme.C.Yellow);
    }

    bool ResolveFlagAddr(FlagEntry f, out long addr, out string method)
    {
        addr = 0; method = "";
        var r = _off.Resolve(f.Name);
        if (r != null) { long o = _off.Offset(r); if (o > 0) { addr = _mem.Base + o; method = "offset"; return true; } }
        if (_bank.Ready) { var br = _bank.Resolve(f.Name); if (br != null) { long va = _bank.GetValueAddr(br); if (va > 0) { addr = va; method = "bank"; return true; } } }
        return false;
    }

    void WatchdogTick()
    {
        if (!_watchdog || !_gameJoined || !_mem.On) return;
        if (Interlocked.CompareExchange(ref _wdLock, 1, 0) != 0) return;
        if (_busyLock != 0) { Interlocked.Exchange(ref _wdLock, 0); return; }
        var snapshot = _flags.Where(f => f.Enabled).ToArray();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (_busyLock != 0 || !_gameJoined) return;
                int fix = 0;
                foreach (var f in snapshot)
                {
                    if (_busyLock != 0 || _cts.IsCancellationRequested) break;
                    if (!ResolveFlagAddr(f, out long addr, out string dummy)) continue;
                    var want = f.GetBytes(); var cur = _mem.ReadAbs(addr, want.Length);
                    if (cur != null && want.AsSpan().SequenceEqual(cur)) continue;
                    if (_mem.WriteFast(addr, want)) fix++;
                }
                if (fix > 0) { _log.Info($"Watchdog re-applied {fix}"); Post(() => { Toast($"Watchdog re-applied {fix}"); RefreshBot(); }); }
            }
            finally { Interlocked.Exchange(ref _wdLock, 0); }
        });
    }

    void ApplyAll()
    {
        if (!_mem.On) { Toast("Roblox not attached"); return; }
        if (_settings.ConfirmApplyAll && MessageBox.Show($"Apply {_flags.Count(f => f.Enabled)} enabled flags?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!TrySetBusy()) { Toast("Apply already in progress"); return; }
        var snapshot = _flags.Where(f => f.Enabled && (f.Mode == ApplyMode.OnJoin || _gameJoined)).ToArray();
        int total = snapshot.Length;
        Post(() => { _progress.Maximum = Math.Max(total, 1); _progress.Value = 0; _progress.Visible = true; });
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var ops = new List<(long addr, byte[] data, FlagEntry flag, string method)>();
                int skip = 0;
                foreach (var f in snapshot)
                {
                    if (!ResolveFlagAddr(f, out long addr, out string method)) { f.Status = "No Offset"; skip++; continue; }
                    ops.Add((addr, f.GetBytes(), f, method));
                }
                int ok = 0, fail = 0;
                for (int i = 0; i < ops.Count; i++)
                {
                    if (_cts.IsCancellationRequested) break;
                    var (addr, data, flag, method) = ops[i];
                    if (_mem.WriteFast(addr, data)) { flag.Status = $"Applied ({method})"; ok++; } else { flag.Status = "Failed"; fail++; }
                    if (i % 10 == 0 || i == ops.Count - 1) { int prog = i + 1; Post(() => { try { _progress.Value = Math.Min(prog, _progress.Maximum); } catch { } }); }
                }
                int dis = _flags.Count(f => !f.Enabled);
                _log.Info($"ApplyAll: ok={ok} fail={fail} skip={skip} dis={dis}");
                Post(() =>
                {
                    _progress.Visible = false; RefreshBot();
                    Toast($"Applied:{ok}  Failed:{fail}  Skip:{skip}  Off:{dis}", 4000);
                    _tray.ShowBalloonTip(2000, "FFlag Injector", $"Applied {ok} flags", ToolTipIcon.Info);
                });
            }
            finally { ClearBusy(); }
        });
    }

    bool ApplySingle(FlagEntry f)
    {
        if (!_mem.On) return false;
        if (!ResolveFlagAddr(f, out long addr, out string method)) { f.Status = "No Offset"; return false; }
        if (_mem.WriteAbs(addr, f.GetBytes())) { f.Status = $"Applied ({method})"; return true; }
        f.Status = "Failed"; return false;
    }

    int[] SelectedBotIndices()
    {
        var result = new List<int>();
        foreach (int vi in _lvBot.SelectedIndices) if (vi < _botMap.Count) result.Add(_botMap[vi]);
        return result.ToArray();
    }

    void RefreshTop()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingStripped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _flags) { existing.Add(f.Name); existingStripped.Add(FlagPrefix.Strip(f.Name)); }
        string filter = _searchTop.Text;
        string typeFilter = _cmbTypeTop.SelectedIndex > 0 ? _cmbTypeTop.SelectedItem?.ToString() ?? "" : "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _topFiltered.Clear();
        void TryAdd(string n)
        {
            if (existing.Contains(n)) return;
            string s = FlagPrefix.Strip(n);
            if (existingStripped.Contains(s)) return;
            if (!seen.Add(s)) return;
            if (filter.Length > 0 && n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && s.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;
            if (typeFilter.Length > 0) { var ft = FlagEntry.InferFromName(n); if (!ft.ToString().Equals(typeFilter, StringComparison.OrdinalIgnoreCase)) return; }
            _topFiltered.Add(n);
        }
        foreach (var n in _off.Names) TryAdd(n);
        if (_bank.Ready && filter.Length > 0) foreach (var n in _bank.Names) TryAdd(n);
        _topFiltered.Sort(StringComparer.OrdinalIgnoreCase);
        _lvTop.VirtualListSize = 0;
        _lvTop.VirtualListSize = _topFiltered.Count;
        _lvTop.Invalidate();
        _lblTopHdr.Text = $"AVAILABLE FLAGS ({_topFiltered.Count})";
        _selPreset = ""; _lblSel.Text = "No flag selected"; _btnAdd.Enabled = false;
    }

    void RefreshBot()
    {
        CommitInlineEdit();
        _botMap.Clear();
        string filter = _searchBot.Text;
        bool searchVal = _chkSearchVal.Checked;
        string typeFilter = _cmbTypeBot.SelectedIndex > 0 ? _cmbTypeBot.SelectedItem?.ToString() ?? "" : "";
        int statusFilter = _cmbStatusBot.SelectedIndex;
        for (int i = 0; i < _flags.Count; i++)
        {
            var f = _flags[i];
            if (statusFilter == 1 && !f.Enabled) continue;
            if (statusFilter == 2 && f.Enabled) continue;
            if (typeFilter.Length > 0 && !f.Type.ToString().Equals(typeFilter, StringComparison.OrdinalIgnoreCase)) continue;
            if (filter.Length > 0)
            {
                bool match = f.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || FlagPrefix.Strip(f.Name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match && searchVal) match = f.Value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match) continue;
            }
            _botMap.Add(i);
        }
        if (_sortCol >= 0 && _sortCol < 5)
        {
            _botMap.Sort((a, b) =>
            {
                var fa = _flags[a]; var fb = _flags[b];
                string va = _sortCol switch { 0 => fa.Name, 1 => fa.Value, 2 => fa.Type.ToString(), 3 => fa.Status, 4 => fa.Mode.ToString(), _ => "" };
                string vb = _sortCol switch { 0 => fb.Name, 1 => fb.Value, 2 => fb.Type.ToString(), 3 => fb.Status, 4 => fb.Mode.ToString(), _ => "" };
                int cmp = string.Compare(va, vb, StringComparison.OrdinalIgnoreCase);
                return _sortAsc ? cmp : -cmp;
            });
        }
        _lvBot.VirtualListSize = 0;
        _lvBot.VirtualListSize = _botMap.Count;
        _lvBot.Invalidate();
        _lblBotHdr.Text = $"MODIFIED FLAGS ({_flags.Count})";
        _lblMod.Text = "No flag selected"; _edUpd.Text = "";
        _btnUpd.Enabled = _btnTog.Enabled = _btnRem.Enabled = false;
        UpdateTrayText();
    }

    void TopClick()
    {
        if (_lvTop.SelectedIndices.Count == 0 || _lvTop.SelectedIndices[0] >= _topFiltered.Count)
        { _selPreset = ""; _lblSel.Text = "No flag selected"; _btnAdd.Enabled = false; return; }
        _selPreset = _topFiltered[_lvTop.SelectedIndices[0]];
        _lblSel.Text = _selPreset; _btnAdd.Enabled = _edVal.Text.Trim().Length > 0;
    }

    void BotClick()
    {
        var sel = SelectedBotIndices();
        if (sel.Length == 0) { _lblMod.Text = "No flag selected"; _edUpd.Text = ""; _btnUpd.Enabled = _btnTog.Enabled = _btnRem.Enabled = false; return; }
        if (sel.Length == 1)
        {
            var f = _flags[sel[0]];
            _lblMod.Text = $"{f.Name} = {f.Value}"; _edUpd.Text = f.Value;
            _btnTog.Text = f.Enabled ? "Disable" : "Enable";
        }
        else _lblMod.Text = $"{sel.Length} flags selected";
        _btnUpd.Enabled = sel.Length == 1; _btnTog.Enabled = _btnRem.Enabled = true;
    }

    void BotDoubleClick(object? s, MouseEventArgs e)
    {
        if (_botMap.Count == 0) return;
        var hit = _lvBot.HitTest(e.Location);
        if (hit.Item == null || hit.SubItem == null) return;
        int vi = hit.Item.Index; if (vi >= _botMap.Count) return;
        int si = hit.Item.SubItems.IndexOf(hit.SubItem);
        if (si == 1) StartInlineEdit(vi, si);
        else if (si == 4)
        {
            int fi = _botMap[vi]; var f = _flags[fi];
            f.Mode = f.Mode == ApplyMode.OnJoin ? ApplyMode.Immediate : ApplyMode.OnJoin;
            DebounceSave(); _lvBot.Invalidate();
        }
    }

    void StartInlineEdit(int virtualIdx, int subIdx)
    {
        if (virtualIdx >= _botMap.Count) return;
        _inlineEditIdx = virtualIdx; _inlineEditSubIdx = subIdx;
        var itemBounds = _lvBot.GetItemRect(virtualIdx, ItemBoundsPortion.Entire);
        int x = 0;
        for (int c = 0; c < subIdx; c++) x += _lvBot.Columns[c].Width;
        int w = _lvBot.Columns[subIdx].Width;
        _inlineEdit.SetBounds(x, itemBounds.Top, w, itemBounds.Height);
        _inlineEdit.Text = _flags[_botMap[virtualIdx]].Value;
        _inlineEdit.Visible = true; _inlineEdit.Focus(); _inlineEdit.SelectAll();
    }

    void CommitInlineEdit()
    {
        if (!_inlineEdit.Visible || _inlineEditIdx < 0) return;
        string nv = _inlineEdit.Text.Trim();
        _inlineEdit.Visible = false;
        if (_inlineEditIdx < _botMap.Count && nv.Length > 0)
        {
            int fi = _botMap[_inlineEditIdx]; var f = _flags[fi];
            if (nv != f.Value)
            {
                _undo.Push(_flags);
                string old = f.Value; f.Value = nv; f.Type = FlagEntry.Infer(f.Name, nv); f.InvalidateCache();
                f.RecordChange(old, nv); DebounceSave();
                if (f.Enabled && _gameJoined && _mem.On) ThreadPool.QueueUserWorkItem(_ => { ApplySingle(f); Post(() => RefreshBot()); });
                else RefreshBot();
            }
        }
        _inlineEditIdx = -1; _inlineEditSubIdx = -1;
    }

    void CancelInlineEdit() { _inlineEdit.Visible = false; _inlineEditIdx = -1; _inlineEditSubIdx = -1; }

    void BotMouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var hit = _lvBot.HitTest(e.Location);
        if (hit.Item == null) return;
        _dragStartIdx = hit.Item.Index;
    }

    void BotDragOver(object? s, DragEventArgs e)
    {
        if (_dragStartIdx < 0 || _sortCol >= 0 || _searchBot.Text.Length > 0) { e.Effect = DragDropEffects.None; return; }
        e.Effect = DragDropEffects.Move;
    }

    void BotDragDrop(object? s, DragEventArgs e)
    {
        if (_dragStartIdx < 0 || _sortCol >= 0 || _searchBot.Text.Length > 0) return;
        var pt = _lvBot.PointToClient(new Point(e.X, e.Y));
        int targetVisual = -1;
        if (_botMap.Count > 0)
        {
            int itemH = _lvBot.GetItemRect(0).Height;
            if (itemH > 0) targetVisual = Math.Min(pt.Y / itemH, _botMap.Count - 1);
        }
        if (targetVisual < 0 || targetVisual == _dragStartIdx) { _dragStartIdx = -1; return; }
        int srcFlag = _dragStartIdx < _botMap.Count ? _botMap[_dragStartIdx] : -1;
        int dstFlag = targetVisual < _botMap.Count ? _botMap[targetVisual] : -1;
        if (srcFlag < 0 || dstFlag < 0 || srcFlag == dstFlag) { _dragStartIdx = -1; return; }
        _undo.Push(_flags);
        var item = _flags[srcFlag]; _flags.RemoveAt(srcFlag);
        int insert = dstFlag > srcFlag ? dstFlag - 1 : dstFlag;
        if (insert < 0) insert = 0; if (insert > _flags.Count) insert = _flags.Count;
        _flags.Insert(insert, item);
        _sortCol = -1; DebounceSave(); RefreshBot();
        _dragStartIdx = -1;
    }

    void AddFlag()
    {
        string v = _edVal.Text.Trim();
        if (_selPreset == "" || v == "") return;
        if (_flags.Any(f => f.Name.Equals(_selPreset, StringComparison.OrdinalIgnoreCase))) { Toast($"'{_selPreset}' already exists"); return; }
        var t = FlagEntry.Infer(_selPreset, v);
        if (t == FType.Int && !int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) { Toast("Invalid integer"); return; }
        if (t == FType.Float && !float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) { Toast("Invalid float"); return; }
        _undo.Push(_flags);
        var fe = new FlagEntry { Name = _selPreset, Value = v, Type = t, AddedAt = DateTime.UtcNow };
        _flags.Add(fe); DebounceSave();
        _highlightFlags.Add(fe.Name); _highlightTimer.Stop(); _highlightTimer.Start();
        RefreshTop(); RefreshBot(); _edVal.Text = "";
        if (_autoApply && _gameJoined && _mem.On) ThreadPool.QueueUserWorkItem(_ => { ApplySingle(fe); Post(() => RefreshBot()); });
        else if (_mem.On && fe.Mode == ApplyMode.Immediate) ThreadPool.QueueUserWorkItem(_ => { ApplySingle(fe); Post(() => RefreshBot()); });
        Toast($"Added: {fe.Name} = {v} [{t}]"); _log.Info($"Added: {fe.Name}={v}");
    }

    void UpdateFlag()
    {
        var sel = SelectedBotIndices();
        if (sel.Length != 1) return;
        string nv = _edUpd.Text.Trim(); if (nv == "") { Toast("Value cannot be empty"); return; }
        var f = _flags[sel[0]];
        var nt = FlagEntry.Infer(f.Name, nv);
        if (nt == FType.Int && !int.TryParse(nv, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) { Toast("Invalid integer"); return; }
        if (nt == FType.Float && !float.TryParse(nv, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) { Toast("Invalid float"); return; }
        _undo.Push(_flags);
        string old = f.Value; f.Value = nv; f.Type = nt; f.InvalidateCache();
        f.RecordChange(old, nv);
        if (f.Enabled && _gameJoined && _mem.On) ThreadPool.QueueUserWorkItem(_ => { ApplySingle(f); Post(() => RefreshBot()); });
        DebounceSave(); RefreshBot(); Toast($"Updated: {f.Name} = {nv}");
    }

    void ToggleSelected()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        _undo.Push(_flags);
        foreach (int i in sel) _flags[i].Enabled = !_flags[i].Enabled;
        DebounceSave(); RefreshBot(); Toast(sel.Length == 1 ? $"{(_flags[sel[0]].Enabled ? "Enabled" : "Disabled")}: {_flags[sel[0]].Name}" : $"Toggled {sel.Length} flags");
    }

    void RemoveSelected()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        _undo.Push(_flags);
        foreach (int i in sel.OrderByDescending(x => x)) _flags.RemoveAt(i);
        DebounceSave(); RefreshTop(); RefreshBot(); Toast($"Removed {sel.Length} flag(s)");
    }

    void RemoveAll()
    {
        if (_flags.Count == 0) return;
        if (MessageBox.Show($"Remove all {_flags.Count} flags?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _undo.Push(_flags); _flags.Clear(); DebounceSave(); RefreshTop(); RefreshBot(); Toast("All flags removed");
    }

    void BulkEnable(bool enable)
    {
        if (_flags.Count == 0) return;
        _undo.Push(_flags);
        foreach (var f in _flags) f.Enabled = enable;
        DebounceSave(); RefreshBot(); Toast(enable ? "All enabled" : "All disabled");
    }

    void ApplySelected()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        if (!_mem.On) { Toast("Not attached"); return; }
        var snapshot = sel.Select(i => _flags[i]).Where(f => f.Enabled).ToArray();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            int ok = 0;
            foreach (var f in snapshot) if (ApplySingle(f)) ok++;
            Post(() => { RefreshBot(); Toast($"Applied {ok}/{snapshot.Length}"); });
        });
    }

    void DuplicateFlag()
    {
        var sel = SelectedBotIndices(); if (sel.Length != 1) return;
        var orig = _flags[sel[0]];
        _undo.Push(_flags);
        string newName = orig.Name + "_copy";
        int n = 1; while (_flags.Any(f => f.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))) { n++; newName = orig.Name + $"_copy{n}"; }
        _flags.Add(new FlagEntry { Name = newName, Value = orig.Value, Type = orig.Type, Enabled = orig.Enabled, Mode = orig.Mode });
        DebounceSave(); RefreshBot(); Toast($"Duplicated: {newName}");
    }

    void SetModeSelected(ApplyMode mode)
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        _undo.Push(_flags);
        foreach (int i in sel) _flags[i].Mode = mode;
        DebounceSave(); RefreshBot(); Toast($"Set {sel.Length} flag(s) to {mode}");
    }

    void ResetToDefault()
    {
        var sel = SelectedBotIndices(); if (sel.Length != 1) return;
        var f = _flags[sel[0]];
        if (!_mem.On) { Toast("Not attached"); return; }
        if (!ResolveFlagAddr(f, out long addr, out _)) { Toast("No offset for this flag"); return; }
        var data = f.GetBytes();
        var cur = _mem.ReadAbs(addr, Math.Max(data.Length, 64));
        if (cur == null) { Toast("Cannot read memory"); return; }
        string memVal;
        switch (f.Type)
        {
            case FType.Bool: memVal = cur[0] != 0 ? "true" : "false"; break;
            case FType.Int: memVal = BitConverter.ToInt32(cur, 0).ToString(); break;
            case FType.Float: memVal = BitConverter.ToSingle(cur, 0).ToString(CultureInfo.InvariantCulture); break;
            default:
                int len = Array.IndexOf(cur, (byte)0);
                memVal = len >= 0 ? Encoding.UTF8.GetString(cur, 0, len) : Encoding.UTF8.GetString(cur); break;
        }
        _undo.Push(_flags);
        string old = f.Value; f.Value = memVal; f.InvalidateCache();
        f.RecordChange(old, memVal); DebounceSave(); RefreshBot(); Toast($"Reset {f.Name} to memory value: {memVal}");
    }

    void ViewHistory()
    {
        var sel = SelectedBotIndices(); if (sel.Length != 1) return;
        using var dlg = new HistoryForm(_flags[sel[0]]); dlg.ShowDialog(this);
    }

    void CopySelectedNames()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(i => _flags[i].Name)));
    }

    void CopySelectedValues()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(i => _flags[i].Value)));
    }

    void MoveSelected(int dir)
    {
        var sel = SelectedBotIndices(); if (sel.Length != 1) return;
        int idx = sel[0]; int newIdx = idx + dir;
        if (newIdx < 0 || newIdx >= _flags.Count) return;
        _undo.Push(_flags);
        var tmp = _flags[idx]; _flags[idx] = _flags[newIdx]; _flags[newIdx] = tmp;
        _sortCol = -1; DebounceSave(); RefreshBot();
    }

    void PerformUndo() { var snap = _undo.Undo(_flags); if (snap == null) { Toast("Nothing to undo"); return; } RestoreSnapshot(snap); DebounceSave(); RefreshTop(); RefreshBot(); Toast("Undo"); }
    void PerformRedo() { var snap = _undo.Redo(_flags); if (snap == null) { Toast("Nothing to redo"); return; } RestoreSnapshot(snap); DebounceSave(); RefreshTop(); RefreshBot(); Toast("Redo"); }

    void RestoreSnapshot(UndoStack.FlagSnapshot[] snap)
    {
        _flags.Clear();
        foreach (var s in snap) _flags.Add(new FlagEntry { Name = s.Name, Value = s.Value, Type = s.Type, Enabled = s.Enabled, Mode = s.Mode, History = s.History });
    }

    void ShowDiff()
    {
        using var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = "Select JSON to compare" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            var imported = ParseFlatJson(File.ReadAllText(dlg.FileName, Encoding.UTF8));
            if (imported.Count == 0) { Toast("No flags found in file"); return; }
            using var diff = new DiffForm(_flags, imported);
            if (diff.ShowDialog(this) == DialogResult.OK && diff.ToApply.Count > 0)
            {
                _undo.Push(_flags);
                int added = 0, updated = 0;
                foreach (var (name, value) in diff.ToApply)
                {
                    string resolved = _off.Resolve(name) ?? (_bank.Ready ? _bank.Resolve(name) : null) ?? name;
                    int idx = _flags.FindIndex(f => f.Name.Equals(resolved, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) { string old = _flags[idx].Value; _flags[idx].Value = value; _flags[idx].Type = FlagEntry.Infer(resolved, value); _flags[idx].InvalidateCache(); _flags[idx].RecordChange(old, value); updated++; }
                    else { _flags.Add(new FlagEntry { Name = resolved, Value = value, Type = FlagEntry.Infer(resolved, value) }); added++; }
                }
                DebounceSave(); RefreshTop(); RefreshBot(); Toast($"Diff applied: +{added} ~{updated}");
            }
        }
        catch (Exception ex) { Toast("Diff error: " + ex.Message); }
    }

    Dictionary<string, string> ParseFlatJson(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    string val = p.Value.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Number => p.Value.GetRawText(), JsonValueKind.String => p.Value.GetString() ?? "", _ => "" };
                    if (val != "") result[p.Name] = val;
                }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind != JsonValueKind.Object) continue;
                    string n = "", v = "";
                    if (elem.TryGetProperty("n", out var np)) n = np.GetString() ?? "";
                    else if (elem.TryGetProperty("Name", out var np2)) n = np2.GetString() ?? "";
                    if (elem.TryGetProperty("v", out var vp)) v = vp.GetString() ?? "";
                    else if (elem.TryGetProperty("Value", out var vp2)) v = vp2.GetString() ?? "";
                    if (n != "" && v != "") result[n] = v;
                }
        }
        catch { }
        return result;
    }

    void ImportJson(string? path = null)
    {
        if (path == null) { using var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = "Import FFlags JSON" }; if (dlg.ShowDialog() != DialogResult.OK) return; path = dlg.FileName; }
        try
        {
            string raw = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            _undo.Push(_flags);
            var existByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var existByStripped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _flags.Count; i++) { existByName[_flags[i].Name] = i; existByStripped[FlagPrefix.Strip(_flags[i].Name)] = i; }
            int added = 0, updated = 0;
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string val = prop.Value.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Number => prop.Value.GetRawText(), JsonValueKind.String => prop.Value.GetString() ?? "", _ => "" };
                    if (val == "") continue;
                    ProcessImportEntry(prop.Name, val, true, existByName, existByStripped, ref added, ref updated);
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind != JsonValueKind.Object) continue;
                    string jn = "", val = ""; bool enabled = true; string typeStr = "", modeStr = "";
                    if (elem.TryGetProperty("n", out var nProp)) jn = nProp.GetString() ?? "";
                    else if (elem.TryGetProperty("Name", out var nameProp)) jn = nameProp.GetString() ?? "";
                    if (elem.TryGetProperty("v", out var vProp)) val = vProp.GetString() ?? "";
                    else if (elem.TryGetProperty("Value", out var valProp)) val = valProp.GetString() ?? "";
                    if (elem.TryGetProperty("t", out var tProp)) typeStr = tProp.GetString() ?? "";
                    if (elem.TryGetProperty("m", out var mProp)) modeStr = mProp.GetString() ?? "";
                    if (elem.TryGetProperty("e", out var eProp) && eProp.ValueKind == JsonValueKind.False) enabled = false;
                    if (string.IsNullOrEmpty(jn)) continue;
                    string resolved = _off.Resolve(jn) ?? (_bank.Ready ? _bank.Resolve(jn) : null) ?? jn;
                    var t = Enum.TryParse<FType>(typeStr, true, out var parsed) ? parsed : FlagEntry.Infer(resolved, val);
                    Enum.TryParse<ApplyMode>(modeStr, true, out var mode);
                    string rs = FlagPrefix.Strip(resolved);
                    int idx = -1;
                    if (existByName.TryGetValue(resolved, out int i1)) idx = i1;
                    else if (existByStripped.TryGetValue(rs, out int i2)) idx = i2;
                    if (idx >= 0) { string old = _flags[idx].Value; _flags[idx].Value = val; _flags[idx].Type = t; _flags[idx].Enabled = enabled; _flags[idx].Mode = mode; _flags[idx].InvalidateCache(); _flags[idx].RecordChange(old, val); updated++; }
                    else
                    {
                        var fe = new FlagEntry { Name = resolved, Value = val, Type = t, Enabled = enabled, Mode = mode };
                        _flags.Add(fe); int ni = _flags.Count - 1; existByName[resolved] = ni; existByStripped[rs] = ni;
                        _highlightFlags.Add(resolved); added++;
                    }
                }
            }
            if (added + updated == 0) { Toast("No matching flags found"); return; }
            if (_highlightFlags.Count > 0) { _highlightTimer.Stop(); _highlightTimer.Start(); }
            DebounceSave(); RefreshTop(); RefreshBot();
            if (_autoApply && _gameJoined && _mem.On) ApplyAll();
            Toast($"Imported: +{added} ~{updated}"); _log.Info($"Imported from {path}: +{added} ~{updated}");
        }
        catch (Exception ex) { Toast("Import error: " + ex.Message); _log.Error("Import: " + ex.Message); }
    }

    void ProcessImportEntry(string jn, string val, bool enabled, Dictionary<string, int> byName, Dictionary<string, int> byStripped, ref int added, ref int updated)
    {
        string resolved = _off.Resolve(jn) ?? (_bank.Ready ? _bank.Resolve(jn) : null) ?? jn;
        var t = FlagEntry.Infer(resolved, val);
        string rs = FlagPrefix.Strip(resolved);
        int idx = -1;
        if (byName.TryGetValue(resolved, out int i1)) idx = i1;
        else if (byStripped.TryGetValue(rs, out int i2)) idx = i2;
        if (idx >= 0) { string old = _flags[idx].Value; _flags[idx].Value = val; _flags[idx].Type = t; _flags[idx].Enabled = enabled; _flags[idx].InvalidateCache(); _flags[idx].RecordChange(old, val); updated++; }
        else
        {
            _flags.Add(new FlagEntry { Name = resolved, Value = val, Type = t }); int ni = _flags.Count - 1;
            byName[resolved] = ni; byStripped[rs] = ni; _highlightFlags.Add(resolved); added++;
        }
    }

    void ExportJson()
    {
        if (_flags.Count == 0) { Toast("No flags to export"); return; }
        using var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "flags.json" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        WriteExportJson(dlg.FileName); Toast($"Exported {_flags.Count} flags");
    }

    void ExportClientAppSettings()
    {
        if (_flags.Count == 0) { Toast("No flags"); return; }
        string robloxDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "ClientSettings");
        Directory.CreateDirectory(robloxDir);
        string path = Path.Combine(robloxDir, "ClientAppSettings.json");
        WriteExportJson(path);
        Toast($"Exported to {path}"); _log.Info($"ClientAppSettings exported to {path}");
    }

    void WriteExportJson(string path)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            foreach (var f in _flags.Where(f => f.Enabled))
            {
                switch (f.Type)
                {
                    case FType.Bool: w.WriteBoolean(f.Name, f.Value.Equals("true", StringComparison.OrdinalIgnoreCase) || f.Value == "1"); break;
                    case FType.Int: if (int.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int iv)) w.WriteNumber(f.Name, iv); else w.WriteString(f.Name, f.Value); break;
                    case FType.Float: if (float.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float fv)) w.WriteNumber(f.Name, fv); else w.WriteString(f.Name, f.Value); break;
                    default: w.WriteString(f.Name, f.Value); break;
                }
            }
            w.WriteEndObject();
        }
        File.WriteAllBytes(path, ms.ToArray());
    }

    void CopyAllJson()
    {
        if (_flags.Count == 0) { Toast("No flags"); return; }
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            foreach (var f in _flags.Where(f => f.Enabled))
            {
                switch (f.Type)
                {
                    case FType.Bool: w.WriteBoolean(f.Name, f.Value.Equals("true", StringComparison.OrdinalIgnoreCase) || f.Value == "1"); break;
                    case FType.Int: if (int.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int iv)) w.WriteNumber(f.Name, iv); else w.WriteString(f.Name, f.Value); break;
                    case FType.Float: if (float.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float fv)) w.WriteNumber(f.Name, fv); else w.WriteString(f.Name, f.Value); break;
                    default: w.WriteString(f.Name, f.Value); break;
                }
            }
            w.WriteEndObject();
        }
        Clipboard.SetText(Encoding.UTF8.GetString(ms.ToArray()));
        Toast("Copied to clipboard");
    }

    void ShowPresetMenu(Control anchor)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), BackColor = Theme.C.Surface, ForeColor = Theme.C.Fg };
        menu.Items.Add("Save current as preset...", null, (_, _) =>
        {
            using var dlg = new InputDialog("Save Preset", "Preset name:", _settings.LastPreset);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Value.Length > 0)
            {
                _presets.Save(dlg.Value, _flags); _settings.LastPreset = dlg.Value; Toast($"Preset saved: {dlg.Value}");
            }
        });
        menu.Items.Add("-");
        var names = _presets.List();
        if (names.Length == 0) { var empty = menu.Items.Add("(no presets)"); empty.Enabled = false; }
        foreach (var name in names)
        {
            var sub = new ToolStripMenuItem(name);
            sub.Click += (_, _) => LoadPreset(name);
            var del = new ToolStripMenuItem("Delete");
            del.Click += (_, _) => { _presets.Delete(name); Toast($"Deleted preset: {name}"); };
            sub.DropDownItems.Add(del);
            menu.Items.Add(sub);
        }
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    void LoadPreset(string name)
    {
        var dtos = _presets.Load(name);
        if (dtos == null) { Toast("Preset not found"); return; }
        _undo.Push(_flags); _flags.Clear();
        foreach (var d in dtos)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) continue;
            Enum.TryParse(d.Type, true, out FType t);
            Enum.TryParse(d.Mode, true, out ApplyMode m);
            _flags.Add(new FlagEntry { Name = d.Name, Value = d.Value, Type = t, Enabled = d.Enabled, Mode = m, History = d.History });
        }
        _settings.LastPreset = name;
        DebounceSave(); RefreshTop(); RefreshBot();
        if (_autoApply && _gameJoined && _mem.On) ApplyAll();
        Toast($"Loaded preset: {name} ({_flags.Count} flags)");
    }

    void DebounceSave() { _saveDebounce.Change(500, Timeout.Infinite); }

    void SaveDebounceCallback(object? state)
    {
        int ver = Interlocked.Increment(ref _saveVer);
        var dtos = _flags.Select(f => new FlagDto { Name = f.Name, Value = f.Value, Type = f.Type.ToString(), Enabled = f.Enabled, Mode = f.Mode.ToString(), History = f.History }).ToArray();
        string data = JsonSerializer.Serialize(dtos);
        string sp = _savePath;
        Task.Run(() =>
        {
            if (Volatile.Read(ref _saveVer) != ver) return;
            try
            {
                RotateBackups(sp);
                string tmp = sp + ".tmp";
                File.WriteAllText(tmp, data, new UTF8Encoding(false));
                File.Move(tmp, sp, true);
            }
            catch (Exception ex) { _log.Error("Save: " + ex.Message); }
        });
    }

    void FlushSave()
    {
        var dtos = _flags.Select(f => new FlagDto { Name = f.Name, Value = f.Value, Type = f.Type.ToString(), Enabled = f.Enabled, Mode = f.Mode.ToString(), History = f.History }).ToArray();
        string data = JsonSerializer.Serialize(dtos);
        try
        {
            RotateBackups(_savePath);
            string tmp = _savePath + ".tmp";
            File.WriteAllText(tmp, data, new UTF8Encoding(false));
            File.Move(tmp, _savePath, true);
        }
        catch { }
    }

    void RotateBackups(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            string dir = Path.GetDirectoryName(path)!;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = BackupRotationCount - 1; i >= 1; i--)
            {
                string src = Path.Combine(dir, $"{name}.bak{i}{ext}");
                string dst = Path.Combine(dir, $"{name}.bak{i + 1}{ext}");
                if (File.Exists(src)) { try { File.Delete(dst); } catch { } try { File.Move(src, dst); } catch { } }
            }
            string first = Path.Combine(dir, $"{name}.bak1{ext}");
            try { File.Copy(path, first, true); } catch { }
        }
        catch { }
    }

    void LoadFlags()
    {
        if (!File.Exists(_savePath)) return;
        try
        {
            string raw = File.ReadAllText(_savePath, Encoding.UTF8);
            var dtos = JsonSerializer.Deserialize<FlagDto[]>(raw, _jopt);
            if (dtos == null) return;
            foreach (var d in dtos)
            {
                if (string.IsNullOrWhiteSpace(d.Name)) continue;
                Enum.TryParse(d.Type, true, out FType t);
                Enum.TryParse(d.Mode, true, out ApplyMode m);
                _flags.Add(new FlagEntry { Name = d.Name, Value = d.Value, Type = t, Enabled = d.Enabled, Mode = m, History = d.History });
            }
            _log.Info($"Loaded {_flags.Count} flags");
        }
        catch (Exception ex) { _log.Error("Load: " + ex.Message); }
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            try { Process.Start(new ProcessStartInfo { FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "", Verb = "runas", UseShellExecute = true, Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)) }); }
            catch { }
            return;
        }
        if (!Environment.Is64BitProcess) { MessageBox.Show("64-bit build required.", "Architecture Mismatch"); return; }
        using var mtx = new Mutex(true, $"Local\\FlagInjectorCS_{Environment.UserName}", out bool created);
        if (!created) { MessageBox.Show("Already running."); return; }
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SplashForm? splash = null;
        try { splash = new SplashForm(); splash.Show(); Application.DoEvents(); } catch { }
        var form = new MainForm(args);
        form.Shown += (_, _) => { try { splash?.Close(); splash?.Dispose(); } catch { } };
        Application.Run(form);
    }
}