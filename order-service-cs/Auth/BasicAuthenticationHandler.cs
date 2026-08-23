using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderService.Api.Auth;

/// <summary>
/// Options bag for <see cref="BasicAuthenticationHandler"/>. No settings of
/// its own today - <see cref="AuthenticationSchemeOptions"/> is the minimum
/// shape ASP.NET Core's <c>AddScheme</c> requires - but kept as a distinct
/// type (rather than reusing the base class directly) so scheme-specific
/// options have somewhere to go later without changing the scheme's type
/// signature.
/// </summary>
public sealed class BasicAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// HTTP Basic authentication handler, registered in Program.cs as the
/// application's *only* (default) authentication scheme. Mirrors
/// order-service (Java)'s SecurityConfig
/// (<c>.authorizeExchange(exchanges -&gt; exchanges.anyExchange().authenticated())
/// .httpBasic(Customizer.withDefaults())</c>): every request must present a
/// valid <c>Authorization: Basic ...</c> header, checked against a single
/// configured username/password - there is no per-user store, matching the
/// Java reference's single Spring Security "user" identity.
///
/// Credentials come from configuration keys <c>ApiAuth:Username</c> /
/// <c>ApiAuth:Password</c> (environment variable equivalents, via ASP.NET
/// Core's default configuration binder: <c>ApiAuth__Username</c> /
/// <c>ApiAuth__Password</c> - set by Task 6/7's Helm chart and
/// docker-compose respectively). Unlike the Java reference (which falls
/// back to Spring Boot auto-generating and logging a random password when
/// none is configured), a missing configuration value here fails every
/// request closed (401) rather than falling back to a guessable or
/// undocumented default - there is no safe default credential to fall back
/// to for a service reachable over the network.
/// </summary>
public sealed class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
{
    public const string SchemeName = "Basic";

    private readonly IConfiguration _configuration;

    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header."));
        }

        var header = headerValues.ToString();
        if (!TryParseBasicCredentials(header, out var username, out var password))
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed Basic Authorization header."));
        }

        var expectedUsername = _configuration["ApiAuth:Username"];
        var expectedPassword = _configuration["ApiAuth:Password"];

        if (string.IsNullOrEmpty(expectedUsername) || string.IsNullOrEmpty(expectedPassword))
        {
            // No configured identity to authenticate against - fail closed
            // rather than accept-anything. Task 5's appsettings/docker-compose
            // env and Task 6's Helm values are expected to always supply
            // both keys in every environment this runs in.
            Logger.LogError("ApiAuth:Username/ApiAuth:Password are not configured - rejecting all requests.");
            return Task.FromResult(AuthenticateResult.Fail("Server has no configured credentials."));
        }

        if (!FixedTimeEquals(username, expectedUsername) || !FixedTimeEquals(password, expectedPassword))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid username or password."));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, username) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.Append("WWW-Authenticate", "Basic realm=\"order-service-cs\"");
        return base.HandleChallengeAsync(properties);
    }

    /// <summary>
    /// Parses a raw <c>Authorization</c> header value as RFC 7617 Basic
    /// credentials (<c>Basic &lt;base64(username:password)&gt;</c>).
    /// Factored out as its own testable helper rather than inlined in
    /// <see cref="HandleAuthenticateAsync"/> - see the task report for why
    /// this is the one piece of Task 4's auth code worth a focused unit
    /// test.
    /// </summary>
    public static bool TryParseBasicCredentials(string? authorizationHeader, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        if (string.IsNullOrEmpty(authorizationHeader) ||
            !authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encoded = authorizationHeader["Basic ".Length..].Trim();

        byte[] decodedBytes;
        try
        {
            decodedBytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        var decoded = Encoding.UTF8.GetString(decodedBytes);
        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
        {
            return false;
        }

        username = decoded[..separatorIndex];
        password = decoded[(separatorIndex + 1)..];
        return true;
    }

    /// <summary>
    /// Constant-time string comparison so a failed auth attempt can't be
    /// used to learn how many leading characters of the configured
    /// username/password it got right via response-timing side channel.
    /// </summary>
    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
