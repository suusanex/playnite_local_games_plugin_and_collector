using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LocalGameCatalog.Core;

namespace DlsiteCollector;

internal static class CollectorApp
{
    public static int Run(string[] args)
    {
        return RunAsync(args, new DlsiteHttpFetcher(), Console.Out, Console.Error, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static async Task<int> RunAsync(
        string[] args,
        IDlsiteFetcher fetcher,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        if (!TryParseArgs(args, out var input, out var output, out var usageError))
        {
            stderr.WriteLine("ERROR " + usageError);
            return 2;
        }

        CsvTable table;
        try
        {
            table = CsvTable.Read(input);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CollectorInputException)
        {
            stderr.WriteLine("ERROR " + exception.Message);
            return 2;
        }

        var keyIndex = IndexOf(table.Header, "key");
        if (keyIndex < 0)
        {
            stderr.WriteLine("ERROR CSV is missing the key column.");
            return 2;
        }

        var requestedIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rowFailures = new List<string>();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var raw = keyIndex < row.Count ? row[keyIndex] : "";
            if (!DlsiteIds.TryNormalize(raw, out var normalized))
            {
                rowFailures.Add("row " + (i + 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
                stderr.WriteLine("ERROR row " + (i + 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + " key is not a DLsite product id.");
                continue;
            }

            if (!seen.Add(normalized))
            {
                stderr.WriteLine("ERROR duplicate DLsite id " + normalized + ".");
                return 2;
            }

            requestedIds.Add(normalized);
        }

        IReadOnlyList<CatalogGame> previous = Array.Empty<CatalogGame>();
        if (File.Exists(output))
        {
            try
            {
                previous = CatalogJsonl.Read(output);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                stderr.WriteLine("ERROR existing catalog cannot be read: " + exception.Message);
                return 2;
            }
        }

        var succeeded = new Dictionary<string, CatalogGame>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var productId in requestedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DlsiteFetchResult result;
            try
            {
                result = await fetcher.FetchAsync(productId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                result = new DlsiteFetchResult { Error = exception.Message };
            }

            if (!result.Succeeded || result.Product is null)
            {
                failed.Add(CatalogIds.Create("dlsite", productId));
                stderr.WriteLine("ERROR " + productId + " " + (result.Error ?? "fetch failed"));
                continue;
            }

            try
            {
                succeeded[CatalogIds.Create("dlsite", productId)] = CreateRecord(output, result);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failed.Add(CatalogIds.Create("dlsite", productId));
                stderr.WriteLine("ERROR " + productId + " " + exception.Message);
            }
        }

        var requestedCatalogIds = requestedIds.Select(id => CatalogIds.Create("dlsite", id)).ToList();
        var merged = CatalogMerge.PreserveFailures(previous, succeeded, requestedCatalogIds, failed);
        try
        {
            CatalogJsonl.WriteAtomic(output, merged);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            stderr.WriteLine("ERROR catalog write failed: " + exception.Message);
            return 2;
        }

        var failureCount = failed.Count + rowFailures.Count;
        stdout.WriteLine("succeeded=" + succeeded.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " failed=" + failureCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return failureCount == 0 ? 0 : 1;
    }

    private static CatalogGame CreateRecord(string catalogPath, DlsiteFetchResult result)
    {
        var product = result.Product ?? throw new InvalidDataException("Product is missing.");
        var coverImage = "";
        if (result.CoverBytes is { Length: > 0 })
        {
            coverImage = CatalogPaths.CoverRelativePath("dlsite", product.WorkNo, result.CoverExtension ?? "jpg");
            var fullPath = CatalogPaths.ResolveCover(catalogPath, coverImage);
            WriteBytesAtomic(fullPath, result.CoverBytes);
            RemoveOtherCovers(fullPath);
        }

        return new CatalogGame
        {
            SchemaVersion = CatalogSchema.CurrentVersion,
            Source = "dlsite",
            SourceId = product.WorkNo,
            Id = CatalogIds.Create("dlsite", product.WorkNo),
            Name = product.Name,
            ProductUrl = product.ProductUrl,
            CoverImage = coverImage,
            Developer = product.MakerName,
            ReleaseDate = product.ReleaseDate
        };
    }

    private static void WriteBytesAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        if (File.Exists(path))
        {
            File.Replace(temporary, path, null);
        }
        else
        {
            File.Move(temporary, path);
        }
    }

    private static void RemoveOtherCovers(string writtenPath)
    {
        var directory = Path.GetDirectoryName(writtenPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var writtenName = Path.GetFileName(writtenPath);
        foreach (var path in Directory.GetFiles(directory, "cover.*"))
        {
            if (!string.Equals(Path.GetFileName(path), writtenName, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
            }
        }
    }

    private static bool TryParseArgs(string[] args, out string input, out string output, out string error)
    {
        input = "";
        output = "";
        error = "";
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--input" && i + 1 < args.Length)
            {
                input = args[++i];
                continue;
            }

            if (arg == "--output" && i + 1 < args.Length)
            {
                output = args[++i];
                continue;
            }

            error = "Usage: dotnet run --file src/DlsiteCollector/DlsiteCollector.cs -- --input <csv> --output <games.jsonl>";
            return false;
        }

        if (input.Length == 0 || output.Length == 0)
        {
            error = "Both --input and --output are required.";
            return false;
        }

        return true;
    }

    private static int IndexOf(IReadOnlyList<string> header, string name)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
