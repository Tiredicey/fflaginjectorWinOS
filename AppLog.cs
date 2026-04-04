using System;
using System.IO;
using System.Text;

namespace FlagInjector;

sealed class AppLog : IDisposable
{
    readonly string _path;
    readonly object _lk = new();
    StreamWriter? _w;

    public AppLog(string dir)
    {
        _path = Path.Combine(dir, "log.txt");
        try
        {
            _w = new StreamWriter(_path, true, Encoding.UTF8) { AutoFlush = true };
            if (new FileInfo(_path).Length > 2 * 1024 * 1024)
            {
                _w.Dispose();
                File.Delete(_path);
                _w = new StreamWriter(_path, false, Encoding.UTF8) { AutoFlush = true };
            }
        }
        catch { _w = null; }
    }

    public void Info(string msg)  => Write("INF", msg);
    public void Warn(string msg)  => Write("WRN", msg);
    public void Error(string msg) => Write("ERR", msg);

    void Write(string lvl, string msg)
    {
        lock (_lk)
        {
            try { _w?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{lvl}] {msg}"); }
            catch { }
        }
    }

    public void Dispose()
    {
        lock (_lk) { _w?.Dispose(); _w = null; }
    }
}