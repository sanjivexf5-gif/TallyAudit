using System;
using TallyAuditAssistant.Core.Common;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TallyEndpointNormalizationTests
{
    [Fact]
    public void Normalize_Localhost_ReturnsCorrectTuple()
    {
        var (host, port, scheme) = TallyEndpointNormalization.Normalize("localhost");
        Assert.Equal("localhost", host);
        Assert.Equal(9000, port);
        Assert.Equal("http", scheme);

        var (hostUpper, portUpper, _) = TallyEndpointNormalization.Normalize("LOCALHOST");
        Assert.Equal("localhost", hostUpper);
        Assert.Equal(9000, portUpper);
    }

    [Fact]
    public void Normalize_127_0_0_1_ReturnsCorrectTuple()
    {
        var (host, port, scheme) = TallyEndpointNormalization.Normalize("127.0.0.1");
        Assert.Equal("127.0.0.1", host);
        Assert.Equal(9000, port);
        Assert.Equal("http", scheme);
    }

    [Fact]
    public void Normalize_HttpLocalhost_ReturnsCorrectTuple()
    {
        var (host1, port1, scheme1) = TallyEndpointNormalization.Normalize("http://localhost");
        Assert.Equal("localhost", host1);
        Assert.Equal(9000, port1);
        Assert.Equal("http", scheme1);

        var (host2, port2, scheme2) = TallyEndpointNormalization.Normalize("http://localhost/");
        Assert.Equal("localhost", host2);
        Assert.Equal(9000, port2);
        Assert.Equal("http", scheme2);
    }

    [Fact]
    public void Normalize_Http127_0_0_1_ReturnsCorrectTuple()
    {
        var (host1, port1, scheme1) = TallyEndpointNormalization.Normalize("http://127.0.0.1");
        Assert.Equal("127.0.0.1", host1);
        Assert.Equal(9000, port1);
        Assert.Equal("http", scheme1);

        var (host2, port2, scheme2) = TallyEndpointNormalization.Normalize("http://127.0.0.1/");
        Assert.Equal("127.0.0.1", host2);
        Assert.Equal(9000, port2);
        Assert.Equal("http", scheme2);
    }

    [Fact]
    public void Normalize_LocalhostWithPort_ReturnsHostAndPort()
    {
        var (host1, port1, scheme1) = TallyEndpointNormalization.Normalize("localhost:9000");
        Assert.Equal("localhost", host1);
        Assert.Equal(9000, port1);
        Assert.Equal("http", scheme1);

        var (host2, port2, scheme2) = TallyEndpointNormalization.Normalize("http://localhost:9000");
        Assert.Equal("localhost", host2);
        Assert.Equal(9000, port2);
        Assert.Equal("http", scheme2);

        var (host3, port3, scheme3) = TallyEndpointNormalization.Normalize("localhost:9008");
        Assert.Equal("localhost", host3);
        Assert.Equal(9008, port3);
        Assert.Equal("http", scheme3);
    }

    [Fact]
    public void Normalize_127_0_0_1WithPort_ReturnsHostAndPort()
    {
        var (host1, port1, scheme1) = TallyEndpointNormalization.Normalize("127.0.0.1:9000");
        Assert.Equal("127.0.0.1", host1);
        Assert.Equal(9000, port1);
        Assert.Equal("http", scheme1);

        var (host2, port2, scheme2) = TallyEndpointNormalization.Normalize("http://127.0.0.1:9000");
        Assert.Equal("127.0.0.1", host2);
        Assert.Equal(9000, port2);
        Assert.Equal("http", scheme2);

        var (host3, port3, scheme3) = TallyEndpointNormalization.Normalize("127.0.0.1:9005");
        Assert.Equal("127.0.0.1", host3);
        Assert.Equal(9005, port3);
        Assert.Equal("http", scheme3);
    }

    [Fact]
    public void Normalize_PreservesExplicitlySuppliedPort()
    {
        var (host, port, _) = TallyEndpointNormalization.Normalize("localhost", defaultPort: 9005);
        Assert.Equal("localhost", host);
        Assert.Equal(9005, port);

        var (host2, port2, _) = TallyEndpointNormalization.Normalize("127.0.0.1", defaultPort: 9002);
        Assert.Equal("127.0.0.1", host2);
        Assert.Equal(9002, port2);

        // String port overrides fallback port
        var (host3, port3, _) = TallyEndpointNormalization.Normalize("localhost:9010", defaultPort: 9002);
        Assert.Equal("localhost", host3);
        Assert.Equal(9010, port3);
    }

    [Theory]
    [InlineData("http://http://localhost:9000")]
    [InlineData("http://")]
    [InlineData("http:///")]
    [InlineData("://localhost")]
    [InlineData("localhost:abc")]
    [InlineData("localhost:0")]
    [InlineData("localhost:-1")]
    [InlineData("localhost:99999")]
    [InlineData("localhost:9000:9000")]
    [InlineData("http://localhost:9000/api")]
    [InlineData("invalid..host")]
    [InlineData("http://user:pass@localhost:9000")]
    public void Normalize_MalformedEndpoints_ThrowsArgumentException(string malformedInput)
    {
        Assert.Throws<ArgumentException>(() => TallyEndpointNormalization.Normalize(malformedInput));
    }

    [Theory]
    [InlineData("http://http://localhost:9000")]
    [InlineData("http://")]
    [InlineData("localhost:abc")]
    public void TryNormalize_MalformedEndpoints_ReturnsFalse(string malformedInput)
    {
        var success = TallyEndpointNormalization.TryNormalize(malformedInput, out var host, out var port, out _);
        Assert.False(success);
        Assert.Empty(host);
        Assert.Equal(0, port);
    }

    [Fact]
    public void ToUrl_FormatsCorrectUrl()
    {
        var url = TallyEndpointNormalization.ToUrl("localhost", 9000);
        Assert.Equal("http://localhost:9000", url);

        var urlCustom = TallyEndpointNormalization.ToUrl("127.0.0.1", 9005, "https");
        Assert.Equal("https://127.0.0.1:9005", urlCustom);
    }
}
