namespace TheOrder.Presentation;

public sealed record StatisticsChartScale(double Minimum, double Maximum, double Step, double Divisor, string Unit)
{
    public static StatisticsChartScale For(IEnumerable<long> values)
    {
        var seconds = values.Select(x => (double)x).ToArray();
        double low = Math.Min(0, seconds.DefaultIfEmpty(0).Min()), high = Math.Max(0, seconds.DefaultIfEmpty(0).Max());
        var largest = Math.Max(Math.Abs(low), high);
        double divisor = largest < 60 ? 1 : largest < 3600 ? 60 : 3600;
        string unit = divisor == 1 ? "秒" : divisor == 60 ? "分" : "时";
        if (largest == 0) return new(0, 3600, 1200, 3600, "时");
        double rough = (high - low) / divisor / 4;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double step = new[] { 1d, 2, 2.5, 5, 10 }.First(x => x * magnitude >= rough) * magnitude * divisor;
        return new(Math.Floor(low / step) * step, Math.Ceiling(high / step) * step, step, divisor, unit);
    }
}
