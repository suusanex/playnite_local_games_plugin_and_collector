using System;
using System.Globalization;
using System.Text.Json;

namespace DlsiteCollector;

internal sealed class DlsiteProduct
{
    public DlsiteProduct(
        string workNo,
        string name,
        string? makerName,
        string? siteId,
        string? releaseDate,
        string? imageUrl,
        string? imageExtension,
        string productUrl)
    {
        WorkNo = workNo;
        Name = name;
        MakerName = makerName;
        SiteId = siteId;
        ReleaseDate = releaseDate;
        ImageUrl = imageUrl;
        ImageExtension = imageExtension;
        ProductUrl = productUrl;
    }

    public string WorkNo { get; }

    public string Name { get; }

    public string? MakerName { get; }

    public string? SiteId { get; }

    public string? ReleaseDate { get; }

    public string? ImageUrl { get; }

    public string? ImageExtension { get; }

    public string ProductUrl { get; }
}

internal static class DlsiteProductParser
{
    public static bool TryParse(string json, string requestedId, string fallbackSiteId, out DlsiteProduct? product)
    {
        product = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return false;
        }

        var item = document.RootElement[0];
        var workNo = ReadString(item, "workno");
        var name = ReadString(item, "work_name");
        if (string.IsNullOrWhiteSpace(workNo) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!string.Equals(workNo, requestedId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var siteId = ReadString(item, "site_id");
        if (string.IsNullOrWhiteSpace(siteId))
        {
            siteId = fallbackSiteId;
        }

        string? imageUrl = null;
        string? imageExtension = null;
        if (item.TryGetProperty("image_main", out var image) && image.ValueKind == JsonValueKind.Object)
        {
            imageUrl = AbsoluteUrl(ReadString(image, "url"));
            imageExtension = ReadString(image, "extension");
        }

        var releaseDate = NormalizeDate(ReadString(item, "regist_date"));
        var maker = ReadString(item, "maker_name");
        product = new DlsiteProduct(
            requestedId,
            name.Trim(),
            string.IsNullOrWhiteSpace(maker) ? null : maker.Trim(),
            siteId,
            releaseDate,
            imageUrl,
            string.IsNullOrWhiteSpace(imageExtension) ? null : imageExtension.Trim().TrimStart('.'),
            "https://www.dlsite.com/" + siteId + "/work/=/product_id/" + requestedId + ".html");
        return true;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? AbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        return url;
    }

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 10)
        {
            return null;
        }

        var date = value.Substring(0, 10);
        return DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? date
            : null;
    }
}
