using System.Globalization;
using Jellyfin.Plugin.M3UExport.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.M3UExport;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    public override string Name => "M3U Export";

    public override string Description => "Exporta películas, series, TV en vivo y colecciones a listas M3U compatibles con cualquier reproductor IPTV.";

    public override string ConfigurationFileName => "Jellyfin.Plugin.M3UExport.xml";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "M3UExport",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.config.html", GetType().Namespace)
            }
        };
    }
}
