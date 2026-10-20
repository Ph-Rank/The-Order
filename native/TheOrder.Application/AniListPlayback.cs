using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace TheOrder.Application;

// AniList's STREAMING link is evidence of a licensed platform, not a claim
// that the title is currently available in every country. No title-derived URLs.
public sealed record AniListWatchLink(int Id, string Site, string Url, string? Language = null, string? Notes = null);
public sealed record AniListTrailer(string Site, string Id)
{
    public bool CanEmbed => Site == "youtube";
    public string Url => Site == "youtube" ? "https://www.youtube.com/watch?v=" + Id : "https://www.dailymotion.com/video/" + Id;
}
public static class AniListPlayback
{
    public const string AppOrigin = "https://dev.local.kungalmanager";
    public const string DocumentUrl = AppOrigin + "/pv/index.html";
    public static bool SafeExternalUrl(string? value) => WatchLinks.SafeUrl(value);
    public static bool ValidWatchLink(AniListWatchLink? link) => link is not null && link.Id > 0 && AniListValidation.Text(link.Site, 100, false)
        && SafeExternalUrl(link.Url) && AniListValidation.OptionalText(link.Language, 100) && AniListValidation.OptionalText(link.Notes, 500);
    public static bool ValidTrailer(AniListTrailer? trailer) => trailer is not null && trailer.Id is not null && trailer.Site switch
    {
        "youtube" => trailer.Id.Length == 11 && trailer.Id.All(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-'),
        "dailymotion" => trailer.Id.Length is >= 6 and <= 16 && trailer.Id.All(char.IsAsciiLetterOrDigit),
        _ => false
    };
    public static string PlayerError(int code) => code switch
    {
        100 => "原站已移除此预告片，或视频暂未公开。",
        101 or 150 => "原站禁止此视频在应用内播放，可在原站观看。",
        153 => "原站未接受播放器身份，请重试或在原站观看。",
        _ => "预告片暂时无法播放，请重试或在原站观看。"
    };
    public static string PlayerDocument(AniListTrailer trailer)
    {
        if (!ValidTrailer(trailer) || !trailer.CanEmbed) throw new InvalidDataException("此预告片不支持应用内播放。");
        // Only a validated video ID enters this fixed document. Remote metadata
        // never supplies HTML/JavaScript. No autoplay, download or proxying video.
        var script = $$"""
        let player;
        const report=(kind,value)=>window.chrome.webview.postMessage({kind,value});
        window.onYouTubeIframeAPIReady=()=>{player=new YT.Player('player',{
          host:'https://www.youtube-nocookie.com',videoId:'{{trailer.Id}}',
          playerVars:{autoplay:0,controls:1,fs:1,hl:'zh-CN',playsinline:1,origin:'{{AppOrigin}}',widget_referrer:'{{AppOrigin}}'},
          events:{onReady:()=>report('ready',0),onStateChange:e=>report('state',e.data),onError:e=>report('error',e.data)}
        });};
        window.pausePv=()=>{if(player&&player.pauseVideo)player.pauseVideo();};
        """;
        // HTML parsing normalizes CRLF/CR to LF before checking inline CSP hashes.
        // Git checkout/build newline policy must not disable the player.
        script = script.ReplaceLineEndings("\n");
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        return $$"""
        <!doctype html><html><head><meta charset="utf-8"><meta name="referrer" content="strict-origin-when-cross-origin">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'sha256-{{hash}}' https://www.youtube.com; frame-src https://www.youtube-nocookie.com; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
        <meta name="viewport" content="width=device-width,initial-scale=1"><title>PV · YouTube</title>
        <style>html,body{margin:0;width:100%;height:100%;background:transparent;overflow:hidden}
        #player{display:block;margin:0;border:0;width:100%;height:100%;background:#111;border-radius:10px;clip-path:inset(0 round 10px)}</style></head>
        <body><div id="player"></div><script>{{script}}</script><script src="https://www.youtube.com/iframe_api"></script></body></html>
        """;
    }
}
