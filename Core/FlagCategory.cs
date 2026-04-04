using System;

namespace FlagInjector;

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