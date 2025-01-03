namespace SlimFaas;

using Prometheus;
using System.Collections.Concurrent;

public class DynamicGaugeService
{
    private readonly ConcurrentDictionary<string, Gauge> _gauges = new();

    private Gauge GetOrCreateGauge(string name, string help)
    {
        return _gauges.GetOrAdd(name, _ => Metrics.CreateGauge(name, help));
    }

    public void SetGaugeValue(string name, double value, string help = "")
    {
        var gauge = GetOrCreateGauge(name, help);
        gauge.Set(value);
    }

    public double GetGaugeValue(string name, string help = "")
    {
        var gauge = GetOrCreateGauge(name, help);
        return gauge.Value;
    }

}
