using System.Net;
using System.Net.Http.Headers;
using BeastScreen.Testing;
using Xunit;

namespace BeastScreen.Tests;

public sealed class StreamFixtureTests
{
    [Fact] public async Task FixtureReturnsRealByteRangesAndBoundsFailures()
    {
        using var directory = new TemporaryDirectory(); var bytes = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(directory.File("direct.mkv"), bytes);
        await using var server = new LoopbackMediaServer(directory.Path); using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Url("direct.mkv")); request.Headers.Range = new RangeHeaderValue(10, 19);
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode); Assert.Equal(bytes[10..20], await response.Content.ReadAsByteArrayAsync());
        server.FailNext("/recover", 1); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(server.Url("recover"))).StatusCode);
        Assert.Equal(bytes, await client.GetByteArrayAsync(server.Url("recover"))); Assert.Equal(2, server.RequestCount("/recover"));
    }
    [Fact] public async Task FixtureRejectsOutOfBoundsRangesAndEncodedEscapingPaths()
    {
        using var directory = new TemporaryDirectory(); await File.WriteAllBytesAsync(directory.File("direct.mkv"), [1, 2, 3]);
        await using var server = new LoopbackMediaServer(directory.Path); using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Url("direct.mkv")); request.Headers.Range = new RangeHeaderValue(100, 200);
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        using var escaped = await client.GetAsync(server.Url("%2e%2e%2foutside.txt")); Assert.Equal(HttpStatusCode.NotFound, escaped.StatusCode);
    }
}
