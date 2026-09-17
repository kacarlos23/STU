using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace STU.Api.Properties;

public sealed record AddressSuggestion(string? Street, string? PostalCode);
public sealed record AddressLookupResult(AddressSuggestion? Address, string? Error = null);
public interface IAddressLookup
{
    Task<AddressLookupResult> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken);
}

// One instance per API process. Public Nominatim must not be used by multiple replicas
// without a shared limiter. Only public provider results are held in this bounded cache.
public sealed class AddressLookup(IHttpClientFactory clients, IConfiguration configuration) : IAddressLookup, IDisposable
{
    private static readonly string[] StreetKeys = ["road", "pedestrian", "residential", "living_street", "footway", "path"];
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 2000 });
    private DateTimeOffset nextRequest = DateTimeOffset.MinValue;

    public async Task<AddressLookupResult> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Geocoding:Enabled", true)) return new(null, "disabled");
        var endpoint = configuration["Geocoding:BaseUrl"] ?? "https://nominatim.openstreetmap.org/";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) || baseUri.Scheme != "https") return new(null, "unavailable");
        var key = FormattableString.Invariant($"{latitude:F6},{longitude:F6}");
        if (cache.TryGetValue<AddressLookupResult>(key, out var cached)) return cached!;
        if (!await gate.WaitAsync(0, cancellationToken)) return new(null, "busy");
        try
        {
            if (cache.TryGetValue<AddressLookupResult>(key, out cached)) return cached!;
            if (DateTimeOffset.UtcNow < nextRequest) return new(null, "busy");
            nextRequest = DateTimeOffset.UtcNow.AddSeconds(1.1);
            using var client = clients.CreateClient("Geocoding");
            var query = "reverse?format=jsonv2&addressdetails=1&layer=address&zoom=18&accept-language=pt-BR&lat="
                + latitude.ToString("F6", CultureInfo.InvariantCulture) + "&lon=" + longitude.ToString("F6", CultureInfo.InvariantCulture);
            using var response = await client.GetAsync(new Uri(baseUri, query), cancellationToken);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            {
                nextRequest = DateTimeOffset.UtcNow.AddMinutes(1);
                return new(null, "busy");
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return Remember(key, new(null));
            if (!response.IsSuccessStatusCode) return new(null, "unavailable");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
                return Remember(key, new(null));
            var country = Text(address, "country_code", 2);
            if (country != "br") return Remember(key, new(null));
            var street = StreetKeys
                .Select(name => Text(address, name, 180)).FirstOrDefault(value => value is not null);
            var postalCode = Text(address, "postcode", 16);
            return Remember(key, new(street is null && postalCode is null ? null : new(street, postalCode)));
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new(null, "unavailable");
        }
        finally { gate.Release(); }
    }

    private AddressLookupResult Remember(string key, AddressLookupResult result)
    {
        cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) });
        return result;
    }
    private static string? Text(JsonElement source, string name, int limit)
    {
        if (!source.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) || text.Length > limit ? null : text;
    }
    public void Dispose() { cache.Dispose(); gate.Dispose(); }
}
