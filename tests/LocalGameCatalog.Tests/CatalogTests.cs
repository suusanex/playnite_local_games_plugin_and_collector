using LocalGameCatalog.Core;
using Xunit;

namespace LocalGameCatalog.Tests;

public class CatalogTests
{
    [Fact]
    public void StableIdUsesSourceAndSourceIdWithoutRewritingDigits()
    {
        Assert.Equal("dlsite:RJ01234567", CatalogIds.Create("dlsite", "RJ01234567"));
        Assert.Equal("dlsite:RJ01234567", CatalogIds.Create("DLSITE", "RJ01234567"));
        Assert.Equal("DLsite", CatalogSources.DisplayName("dlsite"));
    }

    [Fact]
    public void JsonlRoundTripPreservesTextAndOptionalFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "catalog-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var games = new[]
            {
                Game("RJ01234567", "線と\"改行\"\n", "https://example.test/rj", "media/dlsite/RJ01234567/cover.jpg", "サークル", "2024-01-02"),
                Game("VJ000001", "声", "https://example.test/vj", "", null, null)
            };

            CatalogJsonl.WriteAtomic(path, games);
            var loaded = CatalogJsonl.Read(path);

            Assert.Equal(2, loaded.Count);
            Assert.Equal("線と\"改行\"\n", loaded[0].Name);
            Assert.Equal("サークル", loaded[0].Developer);
            Assert.Equal("2024-01-02", loaded[0].ReleaseDate);
            Assert.Equal("", loaded[1].CoverImage);
            Assert.Null(loaded[1].Developer);
            Assert.Equal(games[0].Id, loaded[0].Id);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void RejectsUnsafeCoverAndMismatchedId()
    {
        var escaped = Game("RJ01234567", "名前", "https://example.test/rj", "media/../secret.jpg", null, null);
        Assert.Throws<CatalogValidationException>(() => CatalogValidation.EnsureValid(escaped));

        var mismatched = Game("RJ01234567", "名前", "https://example.test/rj", "media/dlsite/RJ01234567/cover.jpg", null, null);
        mismatched.Id = "dlsite:RJ00000000";
        Assert.Throws<CatalogValidationException>(() => CatalogValidation.EnsureValid(mismatched));
    }

    [Fact]
    public void ResolveCoverStaysUnderCatalogDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var catalog = Path.Combine(directory, "games.jsonl");
        var resolved = CatalogPaths.ResolveCover(catalog, "media/dlsite/RJ01234567/cover.jpg");
        Assert.StartsWith(directory, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(() => CatalogPaths.ResolveCover(catalog, "media/../../outside.jpg"));
    }

    [Fact]
    public void MergeKeepsFailedPreviousRecordAndDropsRemovedIds()
    {
        var kept = Game("RJ01234567", "古い", "https://example.test/old", "media/dlsite/RJ01234567/cover.jpg", null, null);
        var removed = Game("RJ00000001", "消える", "https://example.test/gone", "", null, null);
        var updated = Game("VJ000001", "新しい", "https://example.test/new", "media/dlsite/VJ000001/cover.jpg", null, null);
        var succeeded = new Dictionary<string, CatalogGame> { [updated.Id] = updated };
        var failed = new HashSet<string> { kept.Id };
        var requested = new[] { updated.Id, kept.Id };

        var merged = CatalogMerge.PreserveFailures(new[] { kept, removed }, succeeded, requested, failed);

        Assert.Equal(new[] { updated.Id, kept.Id }, merged.Select(game => game.Id).ToArray());
        Assert.Equal("古い", merged[1].Name);
        Assert.Equal("新しい", merged[0].Name);
    }

    private static CatalogGame Game(string sourceId, string name, string url, string cover, string? developer, string? releaseDate)
    {
        return new CatalogGame
        {
            SchemaVersion = 1,
            Source = "dlsite",
            SourceId = sourceId,
            Id = CatalogIds.Create("dlsite", sourceId),
            Name = name,
            ProductUrl = url,
            CoverImage = cover,
            Developer = developer,
            ReleaseDate = releaseDate
        };
    }
}
