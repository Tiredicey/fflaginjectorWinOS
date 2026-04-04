using System;
using System.Runtime.InteropServices;

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
    public uint dwSize, modID, procID, glblCnt, procCnt;
    public IntPtr modBaseAddr;
    public uint modBaseSize;
    public IntPtr hModule;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
}