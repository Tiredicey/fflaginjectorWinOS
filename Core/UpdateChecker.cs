using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FlagInjector;

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
            string dl  = doc.RootElement.TryGetProperty("html_url",  out var u) ? (u.GetString() ?? "") : "";
            string ver = tag.TrimStart('v', 'V');
            if (ver != "" && ver != CurrentVersion && string.Compare(ver, CurrentVersion, StringComparison.OrdinalIgnoreCase) > 0)
                return (true, ver, dl);
        }
        catch { }
        return (false, "", "");
    }
}