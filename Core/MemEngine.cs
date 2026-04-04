using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace FlagInjector;

sealed class MemEngine : IDisposable
{
    IntPtr _h, _base;
    int _pid;
    uint _modSize;
    readonly object _lk = new();

    public bool On => _h != IntPtr.Zero && _base != IntPtr.Zero;
    public int  Pid     { get { lock (_lk) return _pid; } }
    public long Base    { get { lock (_lk) return _base.ToInt64(); } }
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
            Log?.Invoke($"Attached PID {pid} base 0x{b.ToInt64():X}");
            return true;
        }
    }

    public void Detach()
    {
        lock (_lk)
        {
            if (_h != IntPtr.Zero) W32.CloseHandle(_h);
            _h = _base = IntPtr.Zero; _pid = 0; _modSize = 0;
        }
    }

    public bool Alive()
    {
        bool entered = false;
        try
        {
            entered = Monitor.TryEnter(_lk, 50);
            if (!entered) return _h != IntPtr.Zero;
            return _h != IntPtr.Zero && W32.GetExitCodeProcess(_h, out uint c) && c == W32.ALIVE;
        }
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

    public bool WriteFast(long addr, byte[] data)
    {
        lock (_lk)
        {
            if (_h == IntPtr.Zero) return false;
            return W32.WriteProcessMemory(_h, (IntPtr)addr, data, data.Length, out int w) && w == data.Length;
        }
    }

    public bool WriteAbs(long addr, byte[] data, int tries = 3, int delayMs = 20)
    {
        if (!On) return false;
        byte[]? chk = null;
        try
        {
            chk = ArrayPool<byte>.Shared.Rent(data.Length);
            for (int t = 0; t < tries; t++)
            {
                if (t > 0) Thread.Sleep(delayMs);
                bool ok;
                lock (_lk)
                {
                    if (_h == IntPtr.Zero) return false;
                    if (!W32.WriteProcessMemory(_h, (IntPtr)addr, data, data.Length, out int w) || w != data.Length) continue;
                    ok = W32.ReadProcessMemory(_h, (IntPtr)addr, chk, data.Length, out int r) && r == data.Length &&
                         data.AsSpan().SequenceEqual(chk.AsSpan(0, data.Length));
                }
                if (ok) return true;
            }
        }
        finally { if (chk != null) ArrayPool<byte>.Shared.Return(chk); }
        return false;
    }

    public byte[]? ReadAbs(long addr, int n)
    {
        lock (_lk)
        {
            if (_h == IntPtr.Zero) return null;
            var buf = new byte[n];
            return W32.ReadProcessMemory(_h, (IntPtr)addr, buf, n, out int r) && r == n ? buf : null;
        }
    }

    public long ReadPtr(long addr)   { var b = ReadAbs(addr, 8); return b != null ? BitConverter.ToInt64(b, 0) : 0; }
    public int  ReadInt32(long addr) { var b = ReadAbs(addr, 4); return b != null ? BitConverter.ToInt32(b, 0) : 0; }

    public int BatchWrite(List<(long addr, byte[] data)> ops)
    {
        if (!On) return 0;
        ops.Sort((a, b) => a.addr.CompareTo(b.addr));
        int ok = 0;
        lock (_lk)
        {
            if (_h == IntPtr.Zero) return 0;
            foreach (var (addr, data) in ops)
                if (W32.WriteProcessMemory(_h, (IntPtr)addr, data, data.Length, out int w) && w == data.Length) ok++;
        }
        return ok;
    }

    public void Dispose() => Detach();
}