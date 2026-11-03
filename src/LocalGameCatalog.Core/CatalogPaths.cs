using System;
using System.IO;

namespace LocalGameCatalog.Core
{
    public static class CatalogPaths
    {
        public static string CoverRelativePath(string source, string sourceId, string extension)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Source is required.", nameof(source));
            }

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException("Source id is required.", nameof(sourceId));
            }

            var normalizedExtension = (extension ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (normalizedExtension.Length == 0)
            {
                normalizedExtension = "jpg";
            }

            return "media/" + source.ToLowerInvariant() + "/" + sourceId + "/cover." + normalizedExtension;
        }

        public static bool IsSafeRelativeCover(string? relativePath)
        {
            if (relativePath == null)
            {
                return true;
            }

            var path = relativePath.Trim();
            if (path.Length == 0)
            {
                return true;
            }
            if (path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0)
            {
                return false;
            }

            if (Path.IsPathRooted(path))
            {
                return false;
            }

            var parts = path.Split('/');
            if (parts.Length == 0 || parts[0].Length == 0)
            {
                return false;
            }

            foreach (var part in parts)
            {
                if (part.Length == 0 || part == "." || part == "..")
                {
                    return false;
                }
            }

            return true;
        }

        public static string ResolveCover(string catalogFilePath, string relativePath)
        {
            if (!IsSafeRelativeCover(relativePath) || string.IsNullOrWhiteSpace(relativePath))
            {
                throw new InvalidDataException("Cover path is not a safe relative path.");
            }

            var catalogDirectory = Path.GetDirectoryName(Path.GetFullPath(catalogFilePath));
            if (string.IsNullOrEmpty(catalogDirectory))
            {
                throw new InvalidDataException("Catalog directory could not be resolved.");
            }

            var combined = Path.GetFullPath(Path.Combine(catalogDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var root = catalogDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Cover path escapes the catalog directory.");
            }

            return combined;
        }
    }
}
