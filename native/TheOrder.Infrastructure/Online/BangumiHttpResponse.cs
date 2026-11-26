using System.Globalization;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

internal static class BangumiHttpResponse
{
    public static TimeSpan? ReadRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var value = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - now);
        if (value is null && response.Headers.TryGetValues("Retry-After", out var values)
            && double.TryParse(values.FirstOrDefault(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds >= 0) value = TimeSpan.FromSeconds(Math.Min(seconds, 86400));
        return value is null ? null : TimeSpan.FromSeconds(Math.Clamp(value.Value.TotalSeconds, 0, 86400));
    }

    public static async Task<byte[]> ReadBodyAsync(HttpContent content, int maximumBytes,
        Func<BangumiRelationsException> tooLarge, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > maximumBytes) throw tooLarge();
        using var input = await content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            int available = maximumBytes + 1 - (int)output.Length;
            int count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, available)), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (count == 0) return output.ToArray();
            if (output.Length + count > maximumBytes) throw tooLarge();
            output.Write(buffer, 0, count);
        }
    }
}
