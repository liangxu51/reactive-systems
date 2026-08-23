using System.Text;
using OrderService.Api.Auth;
using Xunit;

namespace OrderService.Api.Tests.Auth;

/// <summary>
/// Focused unit tests for <see cref="BasicAuthenticationHandler.TryParseBasicCredentials"/>
/// - the one piece of Task 4's auth code with parsing logic worth testing in
/// isolation from the full ASP.NET Core authentication pipeline (which would
/// need an in-memory test server / WebApplicationFactory to exercise the
/// handler's HandleAuthenticateAsync end-to-end).
/// </summary>
public class BasicAuthenticationHandlerTests
{
    private static string EncodeBasic(string username, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

    [Fact]
    public void TryParseBasicCredentials_ValidHeader_ReturnsUsernameAndPassword()
    {
        var header = EncodeBasic("admin", "s3cret");

        var result = BasicAuthenticationHandler.TryParseBasicCredentials(header, out var username, out var password);

        Assert.True(result);
        Assert.Equal("admin", username);
        Assert.Equal("s3cret", password);
    }

    [Fact]
    public void TryParseBasicCredentials_PasswordContainingColon_SplitsOnFirstColonOnly()
    {
        var header = EncodeBasic("admin", "pass:with:colons");

        var result = BasicAuthenticationHandler.TryParseBasicCredentials(header, out var username, out var password);

        Assert.True(result);
        Assert.Equal("admin", username);
        Assert.Equal("pass:with:colons", password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer sometoken")]
    [InlineData("Basic")]
    public void TryParseBasicCredentials_MissingOrWrongScheme_ReturnsFalse(string? header)
    {
        var result = BasicAuthenticationHandler.TryParseBasicCredentials(header, out var username, out var password);

        Assert.False(result);
        Assert.Equal(string.Empty, username);
        Assert.Equal(string.Empty, password);
    }

    [Fact]
    public void TryParseBasicCredentials_InvalidBase64_ReturnsFalse()
    {
        var result = BasicAuthenticationHandler.TryParseBasicCredentials("Basic not-valid-base64!!!", out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryParseBasicCredentials_DecodedValueWithoutColon_ReturnsFalse()
    {
        var header = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("no-colon-here"));

        var result = BasicAuthenticationHandler.TryParseBasicCredentials(header, out _, out _);

        Assert.False(result);
    }
}
