using System.Net;
using Microsoft.Extensions.Configuration;
using STU.Api.Properties;

namespace STU.IntegrationTests.Api;

public sealed class AddressLookupTests
{
    [Fact]
    public async Task ReturnsOnlyAddressFieldsAndCachesRepeatedCoordinates()
    {
        using var handler = new Handler("""{"address":{"country_code":"br","road":"Rua de teste","postcode":"45990-000","house_number":"99","suburb":"Outro bairro"}}""");
        using var service = Create(handler);
        var first = await service.ReverseAsync(-17.53, -39.74, default);
        Assert.Equal(new AddressSuggestion("Rua de teste", "45990-000"), first.Address);
        Assert.Equal(first, await service.ReverseAsync(-17.53, -39.74, default));
        Assert.Equal(1, handler.Calls);
        Assert.Contains("lat=-17.530000&lon=-39.740000", handler.LastUri);
        Assert.DoesNotContain("family", handler.LastUri);
        Assert.Equal("busy", (await service.ReverseAsync(-17.54, -39.75, default)).Error);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"address\":{\"country_code\":\"us\",\"road\":\"Street\"}}")]
    public async Task MissingOrForeignAddressesDoNotInventFields(string payload)
    {
        using var handler = new Handler(payload);
        using var service = Create(handler);
        Assert.Null((await service.ReverseAsync(-17.53, -39.74, default)).Address);
    }

    [Theory]
    [InlineData(429, "busy")]
    [InlineData(403, "busy")]
    [InlineData(500, "unavailable")]
    public async Task ProviderFailuresAllowManualFallback(int code, string error)
    {
        using var handler = new Handler("{}", (HttpStatusCode)code);
        using var service = Create(handler);
        Assert.Equal(error, (await service.ReverseAsync(-17.53, -39.74, default)).Error);
    }

    [Fact]
    public async Task DisabledLookupDoesNotCallProvider()
    {
        using var handler = new Handler("{}");
        using var service = new AddressLookup(new Factory(handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Geocoding:Enabled"] = "false" }).Build());
        Assert.Equal("disabled", (await service.ReverseAsync(-17.53, -39.74, default)).Error);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task MalformedResponseAllowsManualFallback()
    {
        using var handler = new Handler("not json");
        using var service = Create(handler);
        Assert.Equal("unavailable", (await service.ReverseAsync(-17.53, -39.74, default)).Error);
    }

    private static AddressLookup Create(Handler handler) => new(new Factory(handler), new ConfigurationBuilder().Build());
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Handler(string payload, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string LastUri { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(payload) });
        }
    }
}
