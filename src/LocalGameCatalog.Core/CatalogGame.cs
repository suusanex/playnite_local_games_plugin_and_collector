using System;

namespace LocalGameCatalog.Core
{
    /// <summary>
    /// 共通カタログの1作品。DLsite 固有の取得仕様は持たない。
    /// </summary>
    public sealed class CatalogGame
    {
        public int SchemaVersion { get; set; } = CatalogSchema.CurrentVersion;

        public string Source { get; set; } = "";

        public string SourceId { get; set; } = "";

        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string ProductUrl { get; set; } = "";

        public string CoverImage { get; set; } = "";

        public string? Developer { get; set; }

        public string? ReleaseDate { get; set; }
    }

    public static class CatalogSchema
    {
        public const int CurrentVersion = 1;
    }

    public static class CatalogSources
    {
        public static string DisplayName(string source)
        {
            if (string.Equals(source, "dlsite", StringComparison.Ordinal))
            {
                return "DLsite";
            }

            return source ?? "";
        }
    }

    public static class CatalogIds
    {
        public static string Create(string source, string sourceId)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (sourceId is null)
            {
                throw new ArgumentNullException(nameof(sourceId));
            }

            return source.ToLowerInvariant() + ":" + sourceId;
        }
    }
}
