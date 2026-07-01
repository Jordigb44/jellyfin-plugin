using System.Globalization;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.M3UExport.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.M3UExport.Controllers;

[ApiController]
[Route("M3UExport")]
public partial class M3UExportController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILiveTvManager _liveTvManager;
    private readonly ILogger<M3UExportController> _logger;

    public M3UExportController(
        ILibraryManager libraryManager,
        ILiveTvManager liveTvManager,
        ILogger<M3UExportController> logger)
    {
        _libraryManager = libraryManager;
        _liveTvManager = liveTvManager;
        _logger = logger;
    }

    private PluginConfiguration Config => Plugin.Instance!.Configuration;

    [AllowAnonymous]
    [HttpGet("movies.m3u")]
    public IActionResult GetMoviesM3U([FromQuery] Guid? userId, [FromQuery] string? token)
    {
        if (!ValidateToken(token)) return Unauthorized(new { error = "API Key inv\u00e1lida" });
        return M3UFileResult(ExportMovies(userId));
    }

    [AllowAnonymous]
    [HttpGet("series.m3u")]
    public IActionResult GetSeriesM3U([FromQuery] Guid? userId, [FromQuery] string? token)
    {
        if (!ValidateToken(token)) return Unauthorized(new { error = "API Key inv\u00e1lida" });
        return M3UFileResult(ExportSeries(userId));
    }

    [AllowAnonymous]
    [HttpGet("livetv.m3u")]
    public IActionResult GetLiveTVM3U([FromQuery] Guid? userId, [FromQuery] string? token)
    {
        if (!ValidateToken(token)) return Unauthorized(new { error = "API Key inv\u00e1lida" });
        return M3UFileResult(ExportLiveTV(userId));
    }

    [AllowAnonymous]
    [HttpGet("collections.m3u")]
    public IActionResult GetCollectionsM3U([FromQuery] Guid? userId, [FromQuery] string? token)
    {
        if (!ValidateToken(token)) return Unauthorized(new { error = "API Key inv\u00e1lida" });
        return M3UFileResult(ExportCollections(userId));
    }

    [AllowAnonymous]
    [HttpGet("all.m3u")]
    public async Task<IActionResult> GetAllM3U([FromQuery] Guid? userId, [FromQuery] string? token, CancellationToken ct)
    {
        if (!ValidateToken(token)) return Unauthorized(new { error = "API Key inv\u00e1lida" });

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        if (Config.IncludeMovies)
            sb.Append(StripHeader(ExportMovies(userId)));

        if (Config.IncludeSeries)
            sb.Append(StripHeader(ExportSeries(userId)));

        if (Config.IncludeLiveTV)
            sb.Append(StripHeader(ExportLiveTV(userId)));

        if (Config.IncludeCollections)
            sb.Append(StripHeader(ExportCollections(userId)));

        return M3UFileResult(sb.ToString());
    }

    [Authorize]
    [HttpGet("export")]
    public async Task<IActionResult> ExportToFile(CancellationToken ct)
    {
        var outputDir = Config.OutputDirectory;
        if (string.IsNullOrEmpty(outputDir))
            return BadRequest(new { error = "OutputDirectory no configurado" });

        try
        {
            Directory.CreateDirectory(outputDir);
            var prefix = Config.FileNamePrefix;

            var tasks = new List<(string FileName, string Content)>();

            if (Config.IncludeMovies)
                tasks.Add(($"{prefix}_movies.m3u", ExportMovies(null)));

            if (Config.IncludeSeries)
                tasks.Add(($"{prefix}_series.m3u", ExportSeries(null)));

            if (Config.IncludeLiveTV)
                tasks.Add(($"{prefix}_livetv.m3u", ExportLiveTV(null)));

            if (Config.IncludeCollections)
                tasks.Add(($"{prefix}_collections.m3u", ExportCollections(null)));

            tasks.Add(($"{prefix}_all.m3u", await GetAllM3UString(null, ct)));

            foreach (var (fileName, content) in tasks)
            {
                var path = Path.Combine(outputDir, fileName);
                await System.IO.File.WriteAllTextAsync(path, content, Encoding.UTF8, ct);
            }

            return Ok(new { message = "Exportación completada", outputDir });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to file");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "ok",
            plugin = "M3UExport",
            version = Plugin.Instance?.Version.ToString() ?? "unknown"
        });
    }

    private async Task<string> GetAllM3UString(Guid? userId, CancellationToken ct)
    {
        var result = await GetAllM3U(userId, null, ct);
        if (result is FileContentResult file)
            return Encoding.UTF8.GetString(file.FileContents);
        return string.Empty;
    }

    private string ExportMovies(Guid? userId)
    {
        var items = _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            IsVirtualItem = false,
        }).Items;
        return BuildM3U(items, "Movies", "movie");
    }

    private string ExportSeries(Guid? userId)
    {
        if (!Config.ExportEpisodesSeparately)
        {
            var items = _libraryManager.GetItemsResult(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Series],
                Recursive = true,
                IsVirtualItem = false,
            }).Items;
            return BuildM3U(items, "Series", "series");
        }

        var episodes = _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true,
            IsVirtualItem = false,
        }).Items;

        if (Config.MaxEpisodesPerSeries > 0)
        {
            episodes = episodes
                .OfType<Episode>()
                .GroupBy(e => e.SeriesId)
                .SelectMany(g => g.Take(Config.MaxEpisodesPerSeries))
                .Cast<BaseItem>()
                .ToList();
        }

        return BuildM3U(episodes, "TV Shows", "episode");
    }

    private string ExportLiveTV(Guid? userId)
    {
        var query = new LiveTvChannelQuery { UserId = userId ?? Guid.Empty };
        var result = _liveTvManager.GetInternalChannels(query, new DtoOptions(), default);

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (var channel in result.Items)
        {
            var name = channel.Name ?? "Unknown";
            sb.Append("#EXTINF:-1");
            sb.Append(CultureInfo.InvariantCulture, $" tvg-id=\"live-{channel.Id}\"");
            sb.Append(CultureInfo.InvariantCulture, $" tvg-name=\"{Sanitize(name)}\"");
            sb.Append(" tvg-type=\"live\"");
            if (Config.UseGroupTitle)
                sb.Append(" group-title=\"Live TV\"");
            if (Config.IncludeMetadata)
            {
                var serverUrl = Config.ServerUrl?.TrimEnd('/');
                if (!string.IsNullOrEmpty(serverUrl))
                    sb.Append(CultureInfo.InvariantCulture, $" tvg-logo=\"{serverUrl}/LiveTv/Channel/{channel.Id}/Images/Primary\"");
            }
            sb.Append(CultureInfo.InvariantCulture, $",{Sanitize(name)}");
            sb.AppendLine();

            var url = Config.StreamType switch
            {
                M3UStreamType.FilePath => channel.Path ?? string.Empty,
                _ => BuildStreamUrl($"LiveTv/LiveStreamFiles/{channel.Id}/stream.ts")
            };
            sb.AppendLine(url);
        }

        return sb.ToString();
    }

    private string ExportCollections(Guid? userId)
    {
        var items = _libraryManager.GetItemsResult(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Recursive = true,
        }).Items;
        return BuildM3U(items, "Collections", "collection");
    }

    private string BuildM3U(IReadOnlyList<BaseItem> items, string groupTitle, string type)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        if (!Config.GroupByLibrary)
        {
            foreach (var item in items)
                AppendEntry(sb, item, groupTitle, type);
            return sb.ToString();
        }

        foreach (var group in items.GroupBy(GetLibraryName))
        {
            if (Config.UseGroupTitle)
                sb.AppendLine(CultureInfo.InvariantCulture, $"#EXTINF:-1 group-title=\"{Sanitize(group.Key)}\",--- {Sanitize(group.Key)} ---");

            foreach (var item in group)
                AppendEntry(sb, item, group.Key, type);
        }

        return sb.ToString();
    }

    private void AppendEntry(StringBuilder sb, BaseItem item, string group, string type)
    {
        var name = BuildDisplayName(item);
        sb.Append("#EXTINF:-1");
        sb.Append(CultureInfo.InvariantCulture, $" tvg-id=\"{Sanitize(type)}-{item.Id}\"");
        sb.Append(CultureInfo.InvariantCulture, $" tvg-name=\"{Sanitize(name)}\"");
        sb.Append(CultureInfo.InvariantCulture, $" tvg-type=\"{Sanitize(type)}\"");
        if (Config.UseGroupTitle && !string.IsNullOrEmpty(group))
            sb.Append(CultureInfo.InvariantCulture, $" group-title=\"{Sanitize(group)}\"");
        if (Config.IncludeMetadata)
        {
            if (item.ProductionYear.HasValue)
                sb.Append(CultureInfo.InvariantCulture, $" tvg-year=\"{item.ProductionYear.Value}\"");
            if (!string.IsNullOrEmpty(item.OfficialRating))
                sb.Append(CultureInfo.InvariantCulture, $" tvg-rating=\"{Sanitize(item.OfficialRating)}\"");
            var serverUrl = Config.ServerUrl?.TrimEnd('/');
            if (!string.IsNullOrEmpty(serverUrl))
                sb.Append(CultureInfo.InvariantCulture, $" tvg-logo=\"{serverUrl}/Items/{item.Id}/Images/Primary\"");
        }

        sb.Append(CultureInfo.InvariantCulture, $",{Sanitize(name)}");
        sb.AppendLine();

        if (Config.IncludeMetadata && !string.IsNullOrEmpty(item.Overview))
            sb.AppendLine(CultureInfo.InvariantCulture, $"#EXTDESC:{Sanitize(item.Overview)}");

        var url = Config.StreamType switch
        {
            M3UStreamType.FilePath => item.Path ?? string.Empty,
            M3UStreamType.Transcoded => BuildStreamUrl($"Videos/{item.Id}/stream.mkv"),
            _ => BuildStreamUrl($"Videos/{item.Id}/stream?static=true")
        };
        sb.AppendLine(url);
    }

    private string BuildStreamUrl(string relativePath)
    {
        if (string.IsNullOrEmpty(Config.ServerUrl))
            return string.Empty;

        var baseUrl = Config.ServerUrl.TrimEnd('/');
        var url = $"{baseUrl}/{relativePath}";

        if (!string.IsNullOrEmpty(Config.ApiKey) && Config.AppendUserToken)
            url += $"{(url.Contains('?') ? '&' : '?')}api_key={Config.ApiKey}";

        return url;
    }

    private static string BuildDisplayName(BaseItem item)
    {
        if (item is Episode ep)
        {
            var sn = ep.SeriesName ?? "Unknown";
            var en = ep.Name ?? "Unknown";
            if (ep.ParentIndexNumber.HasValue && ep.IndexNumber.HasValue)
                return $"{sn} S{ep.ParentIndexNumber.Value:D2}E{ep.IndexNumber.Value:D2} - {en}";
            if (ep.IndexNumber.HasValue)
                return $"{sn} E{ep.IndexNumber.Value:D2} - {en}";
            return $"{sn} - {en}";
        }

        if (item is Movie movie)
            return movie.ProductionYear.HasValue ? $"{movie.Name} ({movie.ProductionYear.Value})" : (movie.Name ?? "Unknown");

        if (item is Series series)
            return series.ProductionYear.HasValue ? $"{series.Name} ({series.ProductionYear.Value})" : (series.Name ?? "Unknown");

        return item.Name ?? "Unknown";
    }

    private static string GetLibraryName(BaseItem item)
    {
        var parent = item.GetParents().FirstOrDefault();
        return parent?.Name ?? "General";
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return "Unknown";
        return UnsafeCharsRegex().Replace(input, "_");
    }

    private static string StripHeader(string m3u)
    {
        if (m3u.Length > 8 && m3u.StartsWith("#EXTM3U", StringComparison.Ordinal))
            return m3u[8..];
        return m3u;
    }

    private FileContentResult M3UFileResult(string content)
    {
        return File(Encoding.UTF8.GetBytes(content), MediaTypeNames.Text.Plain, "playlist.m3u");
    }

    private bool ValidateToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return true;
        return token == Config.ApiKey;
    }

    [GeneratedRegex("[<>\"'&|\\\\/:*?\\x00-\\x1f]")]
    private static partial Regex UnsafeCharsRegex();
}
