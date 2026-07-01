using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.M3UExport.Configuration;

public enum M3UStreamType
{
    Direct,
    Transcoded,
    FilePath
}

public class PluginConfiguration : BasePluginConfiguration
{
    public string ServerUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string OutputDirectory { get; set; } = string.Empty;

    public M3UStreamType StreamType { get; set; } = M3UStreamType.Direct;

    public bool IncludeMovies { get; set; } = true;

    public bool IncludeSeries { get; set; } = true;

    public bool IncludeLiveTV { get; set; } = true;

    public bool IncludeCollections { get; set; } = true;

    public bool GroupByLibrary { get; set; } = true;

    public bool UseGroupTitle { get; set; } = true;

    public bool ExportEpisodesSeparately { get; set; } = true;

    public int MaxEpisodesPerSeries { get; set; } = 0;

    public bool IncludeMetadata { get; set; } = true;

    public string FileNamePrefix { get; set; } = "jellyfin_export";

    public bool EnableScheduledExport { get; set; } = false;

    public int ScheduledExportIntervalHours { get; set; } = 24;

    public bool AppendUserToken { get; set; } = true;
}
