using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

// The app speaks a fixed, credential-free protocol. Twitch secrets and tokens
// exist only in the separately deployable app coordinator, never in this client.
public sealed class IgdbGameVideoProvider(HttpClient client, Uri? service = null, bool allowTestLoopback = false) : IGameVideoProvider
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { MaxDepth = 24 };
    public async Task<IgdbResult> QueryAsync(GameVideoQuery query, CancellationToken cancellation)
    {
        if (!IgdbServiceEndpoint.Valid(service, allowTestLoopback)) throw new GameVideoException("unconfigured");
        if (!AniListValidation.Text(query.Title, 200, false) || query.Id is <= 0 || query.SteamId is not null && !GameVideos.ValidId(query.SteamId)) throw new InvalidDataException("游戏影像查询无效。");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation); stop.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(service!, "v1/game-videos/query")) { Content = JsonContent.Create(query, options: Options) };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) throw new GameVideoException("invalid_response");
            if (response.Content.Headers.ContentLength is > 262144) throw new GameVideoException("invalid_response");
            await using var stream = await response.Content.ReadAsStreamAsync(stop.Token).ConfigureAwait(false);
            using var data = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, stop.Token).ConfigureAwait(false)) > 0)
            { if (data.Length + count > 262144) throw new GameVideoException("invalid_response"); data.Write(buffer, 0, count); }
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode == HttpStatusCode.TooManyRequests ? "rate_limited" : "upstream";
                try { using var error = JsonDocument.Parse(data.ToArray()); if (error.RootElement.ValueKind == JsonValueKind.Object && error.RootElement.TryGetProperty("error", out var field) && field.ValueKind == JsonValueKind.String && field.GetString() is "unconfigured" or "authentication" or "timeout" or "rate_limited") code = field.GetString()!; } catch (JsonException) { }
                throw new GameVideoException(code);
            }
            var result = JsonSerializer.Deserialize<IgdbResult>(data.ToArray(), Options);
            if (!GameVideos.Valid(result)) throw new GameVideoException("invalid_response");
            return result!;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new GameVideoException("timeout"); }
        catch (HttpRequestException) { throw new GameVideoException("upstream"); }
        catch (JsonException) { throw new GameVideoException("invalid_response"); }
    }
}
