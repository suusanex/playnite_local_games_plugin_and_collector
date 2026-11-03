using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DlsiteCollector;

internal interface IDlsiteFetcher
{
    Task<DlsiteFetchResult> FetchAsync(string productId, CancellationToken cancellationToken);
}

internal sealed class DlsiteFetchResult
{
    public DlsiteProduct? Product { get; init; }

    public byte[]? CoverBytes { get; init; }

    public string? CoverExtension { get; init; }

    public string? Error { get; init; }

    public bool Succeeded => Product is not null && Error is null;
}

internal sealed class DlsiteHttpFetcher : IDlsiteFetcher
{
    private static readonly string[] Sites = ["maniax", "home", "pro", "books", "app", "girls", "bl"];

    private readonly HttpClient http;

    public DlsiteHttpFetcher()
        : this(CreateClient())
    {
    }

    public DlsiteHttpFetcher(HttpClient http)
    {
        this.http = http;
    }

    public async Task<DlsiteFetchResult> FetchAsync(string productId, CancellationToken cancellationToken)
    {
        string? lastError = null;
        foreach (var site in Sites)
        {
            var result = await TrySiteAsync(site, productId, announce: false, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded || result.Product is not null)
            {
                return result;
            }

            lastError = result.Error;
        }

        var announced = await TrySiteAsync("maniax", productId, announce: true, cancellationToken).ConfigureAwait(false);
        if (announced.Succeeded || announced.Product is not null)
        {
            return announced;
        }

        return new DlsiteFetchResult { Error = announced.Error ?? lastError ?? "Product was not found." };
    }

    private async Task<DlsiteFetchResult> TrySiteAsync(string site, string productId, bool announce, CancellationToken cancellationToken)
    {
        var url = "https://www.dlsite.com/" + site + "/api/=/product.json?workno=" + Uri.EscapeDataString(productId) + "&locale=ja_JP";
        if (announce)
        {
            url += "&is_ana=1";
        }

        string? body;
        try
        {
            body = await GetStringWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new DlsiteFetchResult { Error = exception.Message };
        }

        if (body is null)
        {
            return new DlsiteFetchResult { Error = "Product was not found." };
        }

        if (!DlsiteProductParser.TryParse(body, productId, site, out var product) || product is null)
        {
            return new DlsiteFetchResult { Error = "Product was not found." };
        }

        if (string.IsNullOrWhiteSpace(product.ImageUrl))
        {
            return new DlsiteFetchResult { Product = product };
        }

        try
        {
            var bytes = await GetBytesWithRetryAsync(product.ImageUrl, cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return new DlsiteFetchResult { Product = product, Error = "Cover image was empty." };
            }

            return new DlsiteFetchResult
            {
                Product = product,
                CoverBytes = bytes,
                CoverExtension = string.IsNullOrWhiteSpace(product.ImageExtension) ? ExtensionFromUrl(product.ImageUrl) : product.ImageExtension
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new DlsiteFetchResult { Product = product, Error = "Cover download failed: " + exception.Message };
        }
    }

    private async Task<string?> GetStringWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        var bytes = await GetBytesWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]?> GetBytesWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode == 404)
                {
                    return null;
                }

                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (attempt == 3)
                    {
                        throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException("Request failed.");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalGameCatalogCollector/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json,image/*;q=0.8,*/*;q=0.5");
        return client;
    }

    private static string ExtensionFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "jpg";
        }

        var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
        return extension.Length == 0 ? "jpg" : extension;
    }
}
