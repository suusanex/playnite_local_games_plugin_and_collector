using DlsiteCollector;
using Xunit;

namespace LocalGameCatalog.Tests;

public class CollectorTests
{
    [Theory]
    [InlineData(" rj01234567 ", "RJ01234567")]
    [InlineData("VJ00000012", "VJ00000012")]
    public void NormalizesPrefixCaseOnly(string raw, string expected)
    {
        Assert.True(DlsiteIds.TryNormalize(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NOID-1")]
    [InlineData("RJ")]
    [InlineData("RJ0123-45")]
    public void RejectsNonProductIds(string raw)
    {
        Assert.False(DlsiteIds.TryNormalize(raw, out _));
    }

    [Fact]
    public void CsvReadsKeyColumnAndQuotedCommas()
    {
        var text = "title,key\r\n\"名前,カンマ\",rj01041722\r\n声,VJ1\r\n";
        var rows = CsvTable.Parse(text);
        Assert.Equal(new[] { "title", "key" }, rows[0]);
        Assert.Equal("名前,カンマ", rows[1][0]);
        Assert.Equal("rj01041722", rows[1][1]);
    }

    [Fact]
    public void ParserReadsPublicProductShape()
    {
        const string json = """
            [{
              "workno": "RJ01041722",
              "work_name": "サンプル",
              "maker_name": "Circle",
              "site_id": "home",
              "regist_date": "2023-03-23 16:00:00",
              "image_main": { "url": "//img.example.test/cover.jpg", "extension": "jpg" }
            }]
            """;

        Assert.True(DlsiteProductParser.TryParse(json, "RJ01041722", "maniax", out var product));
        Assert.NotNull(product);
        Assert.Equal("サンプル", product!.Name);
        Assert.Equal("Circle", product.MakerName);
        Assert.Equal("2023-03-23", product.ReleaseDate);
        Assert.Equal("https://img.example.test/cover.jpg", product.ImageUrl);
        Assert.Equal("https://www.dlsite.com/home/work/=/product_id/RJ01041722.html", product.ProductUrl);
    }

    [Fact]
    public async Task RerunKeepsStableIdAndPreservesFailedRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "collector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "input.csv");
        var output = Path.Combine(root, "games.jsonl");
        await File.WriteAllTextAsync(input, "key,title\nRJ01234567,ignored\nNOID-1,ignored\n");
        var fetcher = new FakeFetcher();

        var first = await CollectorApp.RunAsync(new[] { "--input", input, "--output", output }, fetcher, TextWriter.Null, new StringWriter(), CancellationToken.None);
        var firstText = await File.ReadAllTextAsync(output);

        await File.WriteAllTextAsync(input, "key\nRJ01234567\nRJ07654321\n");
        fetcher.Fail.Add("RJ07654321");
        var second = await CollectorApp.RunAsync(new[] { "--input", input, "--output", output }, fetcher, TextWriter.Null, new StringWriter(), CancellationToken.None);
        var secondText = await File.ReadAllTextAsync(output);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Contains("\"id\":\"dlsite:RJ01234567\"", firstText, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"dlsite:RJ01234567\"", secondText, StringComparison.Ordinal);
        Assert.Contains("media/dlsite/RJ01234567/cover.jpg", secondText, StringComparison.Ordinal);
        Assert.DoesNotContain("RJ07654321", secondText, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "media", "dlsite", "RJ01234567", "cover.jpg")));
    }

    [Fact]
    public async Task DuplicateKeyStopsWithoutReplacingCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "collector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "input.csv");
        var output = Path.Combine(root, "games.jsonl");
        await File.WriteAllTextAsync(output, "{\"schemaVersion\":1}\n");
        await File.WriteAllTextAsync(input, "key\nRJ01234567\nrj01234567\n");
        var stderr = new StringWriter();

        var code = await CollectorApp.RunAsync(new[] { "--input", input, "--output", output }, new FakeFetcher(), TextWriter.Null, stderr, CancellationToken.None);

        Assert.Equal(2, code);
        Assert.Contains("duplicate", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{\"schemaVersion\":1}\n", await File.ReadAllTextAsync(output));
    }

    private sealed class FakeFetcher : IDlsiteFetcher
    {
        public HashSet<string> Fail { get; } = new(StringComparer.Ordinal);

        public Task<DlsiteFetchResult> FetchAsync(string productId, CancellationToken cancellationToken)
        {
            if (Fail.Contains(productId))
            {
                return Task.FromResult(new DlsiteFetchResult { Error = "simulated failure" });
            }

            var product = new DlsiteProduct(
                productId,
                "Title " + productId,
                "Maker",
                "home",
                "2020-01-01",
                null,
                "jpg",
                "https://www.dlsite.com/home/work/=/product_id/" + productId + ".html");
            return Task.FromResult(new DlsiteFetchResult
            {
                Product = product,
                CoverBytes = new byte[] { 1, 2, 3 },
                CoverExtension = "jpg"
            });
        }
    }
}
