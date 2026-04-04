using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FlagInjector;

sealed class AppSettings
{
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int W { get; set; } = 820;
    public int H { get; set; } = 780;
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
    public string BackgroundPreset    { get; set; } = "Stars";
    public string BackgroundImagePath { get; set; } = "";
    public double BackgroundOpacity   { get; set; } = 0.30;
    public bool   DisclaimerShown     { get; set; } = false;
    public int    ParticleCount       { get; set; } = 80;

    static readonly JsonSerializerOptions _jopt = new() { WriteIndented = true };

    private static AppSettings? _instance;
    public  static AppSettings  Instance => _instance ?? new();

    public static AppSettings Load() => Load(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlagInjectorCS", "settings.json"));

    public void Save() => Save(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlagInjectorCS", "settings.json"));

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path, Encoding.UTF8)) ?? new();
            _instance = loaded;
            return loaded;
        }
        catch { var s = new AppSettings(); _instance = s; return s; }
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