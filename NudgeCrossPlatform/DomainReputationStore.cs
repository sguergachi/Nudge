using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NudgeCore;

/// <summary>
/// Bayesian-smoothed per-domain / per-app reputation store for the Experimental Signal Mode.
/// Persisted to ~/.nudge/exp_reputation.json.  Unknown keys return the shipped distraction
/// prior when one exists (see distraction_priors.tsv), else a neutral 0.5.  User labels blend
/// smoothly with the prior and dominate once they outweigh its pseudo-count mass.
/// Thread-safe: labels arrive on the UDP response thread while rates are read from the
/// harvest loop.
/// </summary>
internal sealed class DomainReputationStore
{
    /// <summary>Total pseudo-count mass behind the prior (was α+β with α=β=2).</summary>
    private const double PriorStrength = 4.0;
    private const double NeutralPrior = 0.5;

    private static readonly Dictionary<string, double> EmptyPriors = new(StringComparer.Ordinal);

    private readonly object _lock = new();
    private readonly string _path;
    private readonly Dictionary<string, LabelCounts> _domains;
    private readonly Dictionary<string, LabelCounts> _apps;
    private readonly IReadOnlyDictionary<string, double> _domainPriors;
    private readonly IReadOnlyDictionary<string, double> _appPriors;
    private bool _dirty;

    public DomainReputationStore(string path,
                                 IReadOnlyDictionary<string, double>? domainPriors = null,
                                 IReadOnlyDictionary<string, double>? appPriors = null)
    {
        _path = path;
        _domainPriors = domainPriors ?? EmptyPriors;
        _appPriors = appPriors ?? EmptyPriors;
        (_domains, _apps) = LoadOrCreate(path);
    }

    /// <summary>Smoothed productive rate for a domain (0..1). Unknown → prior or 0.5.</summary>
    public double DomainRate(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return NeutralPrior;
        return SmoothedRate(_domains, _domainPriors, domain);
    }

    /// <summary>Total label count backing a domain's rate. Unknown → 0.</summary>
    public int DomainCount(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return 0;
        lock (_lock) return TotalCount(_domains.GetValueOrDefault(domain));
    }

    /// <summary>Smoothed productive rate for an app (0..1). Unknown → prior or 0.5.</summary>
    public double AppRate(string app)
    {
        if (string.IsNullOrEmpty(app)) return NeutralPrior;
        return SmoothedRate(_apps, _appPriors, app);
    }

    /// <summary>Total label count backing an app's rate. Unknown → 0.</summary>
    public int AppCount(string app)
    {
        if (string.IsNullOrEmpty(app)) return 0;
        lock (_lock) return TotalCount(_apps.GetValueOrDefault(app));
    }

    /// <summary>Record a user label for the given domain + app. Call after the CSV row is written.</summary>
    public void Update(string domain, string app, bool productive)
    {
        lock (_lock)
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
        string json;
        lock (_lock)
        {
            if (!_dirty) return;
            var dto = new ReputationDto
            {
                Domains = ToDto(_domains),
                Apps = ToDto(_apps)
            };
            json = JsonSerializer.Serialize(dto);
            _dirty = false;
        }
        try
        {
            File.WriteAllText(_path, json);
        }
        catch { /* never throw on the harvest path; retried on next label */ }
    }

    /// <summary>Clear all learned reputation data. Shipped priors are not user data and survive.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _domains.Clear();
            _apps.Clear();
            _dirty = true;
        }
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    /// <summary>
    /// Load the shipped distraction prior table (TSV: key, kind=domain|app, productive-rate prior).
    /// Missing or malformed file → empty priors → behavior identical to no-priors (neutral 0.5).
    /// </summary>
    internal static (Dictionary<string, double> Domains, Dictionary<string, double> Apps) LoadPriors(string tsvPath)
    {
        var domains = new Dictionary<string, double>(StringComparer.Ordinal);
        var apps = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(tsvPath))
            {
                foreach (string line in File.ReadLines(tsvPath))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length != 3 || parts[0].Length == 0) continue;
                    if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double prior)) continue;
                    prior = Math.Clamp(prior, 0.0, 1.0);
                    if (parts[1] == "domain") domains[parts[0]] = prior;
                    else if (parts[1] == "app") apps[parts[0]] = prior;
                }
            }
        }
        catch { /* unreadable → empty priors → neutral behavior */ }
        return (domains, apps);
    }

    private double SmoothedRate(Dictionary<string, LabelCounts> counts,
                                IReadOnlyDictionary<string, double> priors, string key)
    {
        lock (_lock)
        {
            var c = counts.GetValueOrDefault(key);
            double prior = priors.GetValueOrDefault(key, NeutralPrior);
            return (c.Productive + PriorStrength * prior) / (c.Productive + c.Unproductive + PriorStrength);
        }
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
