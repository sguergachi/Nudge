using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NudgeCore;

/// <summary>
/// Bayesian-smoothed per-domain / per-app reputation store for the Experimental Signal Mode.
/// Persisted to ~/.nudge/exp_reputation.json.  Unknown keys return a neutral prior.
/// </summary>
internal sealed class DomainReputationStore
{
    private const double Alpha = 2.0;
    private const double Beta = 2.0;

    private readonly string _path;
    private readonly Dictionary<string, LabelCounts> _domains;
    private readonly Dictionary<string, LabelCounts> _apps;
    private bool _dirty;

    public DomainReputationStore(string path)
    {
        _path = path;
        (_domains, _apps) = LoadOrCreate(path);
    }

    /// <summary>Smoothed productive rate for a domain (0..1). Unknown → 0.5.</summary>
    public double DomainRate(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return 0.5;
        return SmoothedRate(_domains.GetValueOrDefault(domain));
    }

    /// <summary>Total label count backing a domain's rate. Unknown → 0.</summary>
    public int DomainCount(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return 0;
        return TotalCount(_domains.GetValueOrDefault(domain));
    }

    /// <summary>Smoothed productive rate for an app (0..1). Unknown → 0.5.</summary>
    public double AppRate(string app)
    {
        if (string.IsNullOrEmpty(app)) return 0.5;
        return SmoothedRate(_apps.GetValueOrDefault(app));
    }

    /// <summary>Total label count backing an app's rate. Unknown → 0.</summary>
    public int AppCount(string app)
    {
        if (string.IsNullOrEmpty(app)) return 0;
        return TotalCount(_apps.GetValueOrDefault(app));
    }

    /// <summary>Record a user label for the given domain + app. Call after the CSV row is written.</summary>
    public void Update(string domain, string app, bool productive)
    {
        lock (_domains)
        {
            if (!string.IsNullOrEmpty(domain))
            {
                ref var counts = ref CollectionsMarshal.GetValueRefOrAddDefault(_domains, domain, out _);
                if (productive) counts.Productive++; else counts.Unproductive++;
            }
            if (!string.IsNullOrEmpty(app))
            {
                ref var counts = ref CollectionsMarshal.GetValueRefOrAddDefault(_apps, app, out _);
                if (productive) counts.Productive++; else counts.Unproductive++;
            }
            _dirty = true;
        }
    }

    /// <summary>Persist the store to disk if it has changed.</summary>
    public void Flush()
    {
        if (!_dirty) return;
        try
        {
            var dto = new ReputationDto
            {
                Domains = ToDto(_domains),
                Apps = ToDto(_apps)
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(dto));
            _dirty = false;
        }
        catch { /* never throw on the harvest path */ }
    }

    /// <summary>Clear all learned reputation data.</summary>
    public void Clear()
    {
        _domains.Clear();
        _apps.Clear();
        _dirty = true;
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    private static double SmoothedRate(LabelCounts counts)
    {
        int p = counts.Productive;
        int n = counts.Unproductive;
        return (p + Alpha) / (p + n + Alpha + Beta);
    }

    private static int TotalCount(LabelCounts counts) => counts.Productive + counts.Unproductive;

    private static (Dictionary<string, LabelCounts>, Dictionary<string, LabelCounts>) LoadOrCreate(string path)
    {
        var domains = new Dictionary<string, LabelCounts>(StringComparer.Ordinal);
        var apps = new Dictionary<string, LabelCounts>(StringComparer.Ordinal);

        try
        {
            if (File.Exists(path))
            {
                var dto = JsonSerializer.Deserialize<ReputationDto>(File.ReadAllText(path));
                if (dto != null)
                {
                    if (dto.Domains != null)
                        foreach (var (k, v) in dto.Domains)
                            domains[k] = new LabelCounts(v.P, v.N);
                    if (dto.Apps != null)
                        foreach (var (k, v) in dto.Apps)
                            apps[k] = new LabelCounts(v.P, v.N);
                }
            }
        }
        catch { /* degrade to empty store */ }

        return (domains, apps);
    }

    private static Dictionary<string, LabelCountsDto> ToDto(Dictionary<string, LabelCounts> source)
    {
        var result = new Dictionary<string, LabelCountsDto>(source.Count, StringComparer.Ordinal);
        foreach (var (k, v) in source)
            result[k] = new LabelCountsDto(v.Productive, v.Unproductive);
        return result;
    }

    private struct LabelCounts
    {
        public int Productive;
        public int Unproductive;
        public LabelCounts(int p, int n) { Productive = p; Unproductive = n; }
    }

    private sealed class ReputationDto
    {
        public Dictionary<string, LabelCountsDto>? Domains { get; set; }
        public Dictionary<string, LabelCountsDto>? Apps { get; set; }
    }

    private sealed class LabelCountsDto
    {
        public int P { get; set; }
        public int N { get; set; }
        public LabelCountsDto() { }
        public LabelCountsDto(int p, int n) { P = p; N = n; }
    }
}
