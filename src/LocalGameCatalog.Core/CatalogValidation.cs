using System;
using System.Collections.Generic;

namespace LocalGameCatalog.Core
{
    public sealed class CatalogValidationException : Exception
    {
        public CatalogValidationException(string message)
            : base(message)
        {
        }
    }

    public static class CatalogValidation
    {
        public static void EnsureValid(CatalogGame game)
        {
            if (game is null)
            {
                throw new ArgumentNullException(nameof(game));
            }

            if (game.SchemaVersion != CatalogSchema.CurrentVersion)
            {
                throw new CatalogValidationException("Unsupported schemaVersion.");
            }

            if (!IsSource(game.Source))
            {
                throw new CatalogValidationException("Source must be a lowercase identifier.");
            }

            if (!IsSourceId(game.SourceId))
            {
                throw new CatalogValidationException("SourceId is empty or contains a separator.");
            }

            var expectedId = CatalogIds.Create(game.Source, game.SourceId);
            if (!string.Equals(game.Id, expectedId, StringComparison.Ordinal))
            {
                throw new CatalogValidationException("Id does not match source and sourceId.");
            }

            if (string.IsNullOrWhiteSpace(game.Name))
            {
                throw new CatalogValidationException("Name is required.");
            }

            if (!IsHttpUrl(game.ProductUrl))
            {
                throw new CatalogValidationException("ProductUrl must be an absolute http(s) URL.");
            }

            if (!CatalogPaths.IsSafeRelativeCover(game.CoverImage))
            {
                throw new CatalogValidationException("CoverImage must be empty or a safe relative path.");
            }

            if (game.ReleaseDate != null && game.ReleaseDate.Length > 0 && !IsReleaseDate(game.ReleaseDate))
            {
                throw new CatalogValidationException("ReleaseDate must be yyyy-MM-dd.");
            }
        }

        public static void EnsureUniqueIds(IEnumerable<CatalogGame> games)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var game in games)
            {
                EnsureValid(game);
                if (!seen.Add(game.Id))
                {
                    throw new CatalogValidationException("Duplicate catalog id: " + game.Id);
                }
            }
        }

        private static bool IsSource(string? source)
        {
            if (source == null || source.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < source.Length; i++)
            {
                var ch = source[i];
                var ok = i == 0
                    ? ch >= 'a' && ch <= 'z'
                    : (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9');
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSourceId(string? sourceId)
        {
            if (sourceId == null || sourceId.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < sourceId.Length; i++)
            {
                var ch = sourceId[i];
                if (ch == ':' || ch == '/' || ch == '\\' || char.IsWhiteSpace(ch))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsHttpUrl(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        private static bool IsReleaseDate(string value)
        {
            return value.Length == 10 &&
                char.IsDigit(value[0]) &&
                char.IsDigit(value[1]) &&
                char.IsDigit(value[2]) &&
                char.IsDigit(value[3]) &&
                value[4] == '-' &&
                char.IsDigit(value[5]) &&
                char.IsDigit(value[6]) &&
                value[7] == '-' &&
                char.IsDigit(value[8]) &&
                char.IsDigit(value[9]);
        }
    }
}
