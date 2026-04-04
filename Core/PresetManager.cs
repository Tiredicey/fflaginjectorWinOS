using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FlagInjector;

sealed class PresetManager
{
    readonly string _dir;

    public PresetManager(string dir)
    {
        _dir = Path.Combine(dir, "presets");
        Directory.CreateDirectory(_dir);
    }

    public string[] List()
    {
        try { return Directory.GetFiles(_dir, "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => n != null).Select(n => n!).OrderBy(n => n).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    public void Save(string name, List<FlagEntry> flags)
    {
        var dtos = flags.Select(f => new FlagDto { Name = f.Name, Value = f.Value, Type = f.Type.ToString(), Enabled = f.Enabled, Mode = f.Mode.ToString(), History = f.History }).ToArray();
        File.WriteAllText(Path.Combine(_dir, name + ".json"), JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    public FlagDto[]? Load(string name)
    {
        string p = Path.Combine(_dir, name + ".json");
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<FlagDto[]>(File.ReadAllText(p, Encoding.UTF8), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return null; }
    }

    public void Delete(string name) { try { File.Delete(Path.Combine(_dir, name + ".json")); } catch { } }
}