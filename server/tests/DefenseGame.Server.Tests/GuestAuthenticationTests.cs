using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DefenseGame.Server.Authentication;
using DefenseGame.Server.Contracts;
using DefenseGame.Server.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace DefenseGame.Server.Tests;

public class GuestAuthenticationTests
{
    private const string SigningKey = "defensegame-test-signing-key-32-characters-minimum";

    [Fact]
    public void RequiredAuthentication_WithoutSigningKey_IsRejected()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Required"] = "true"
            })
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => GuestAuthenticationOptions.FromConfiguration(configuration));

        Assert.Contains("SigningKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IssuedToken_ExpiresAndRejectsTampering()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero));
        var options = new GuestAuthenticationOptions(true, SigningKey, 1);
        var service = new GuestTokenService(options, clock);

        GuestTokenIssueResult issued = service.Issue("guest_test");

        Assert.Equal(GuestTokenValidationStatus.Valid, service.Validate(issued.AccessToken).Status);

        string tampered = issued.AccessToken[..^1] + (issued.AccessToken[^1] == 'A' ? "B" : "A");
        Assert.Equal(GuestTokenValidationStatus.Invalid, service.Validate(tampered).Status);

        clock.UtcNow = clock.UtcNow.AddHours(2);
        Assert.Equal(GuestTokenValidationStatus.Expired, service.Validate(issued.AccessToken).Status);
    }

    [Fact]
    public async Task GuestLogin_IssuesTokenThatCanAccessMatchingPlayer()
    {
        await using var app = new AuthenticatedWebApplicationFactory();
        var client = app.CreateClient();

        HttpResponseMessage login = await client.PostAsync("/v1/auth/guest", content: null);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        GuestLoginResponse payload = (await login.Content.ReadFromJsonAsync<GuestLoginResponse>())!;
        Assert.StartsWith("guest_", payload.PlayerId, StringComparison.Ordinal);
        Assert.NotEmpty(payload.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/progress/{payload.PlayerId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);
        HttpResponseMessage progress = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, progress.StatusCode);
        Assert.Equal(payload.PlayerId, (await progress.Content.ReadFromJsonAsync<ProgressResponse>())!.UserId);
    }

    [Fact]
    public async Task ProtectedPlayerApi_WithoutToken_ReturnsUnauthorized()
    {
        await using var app = new AuthenticatedWebApplicationFactory();
        var client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/v1/progress/guest_missing");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("missing_access_token", payload!["errorCode"]);
    }

    [Fact]
    public async Task ProtectedPlayerApi_WithDifferentPlayer_ReturnsForbidden()
    {
        await using var app = new AuthenticatedWebApplicationFactory();
        var client = app.CreateClient();
        GuestLoginResponse login = await LoginAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/progress/guest_other");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("authenticated_user_mismatch", payload!["errorCode"]);
    }

    [Fact]
    public async Task ProtectedWriteApi_WithDifferentBodyPlayer_ReturnsForbidden()
    {
        await using var app = new AuthenticatedWebApplicationFactory();
        var client = app.CreateClient();
        GuestLoginResponse login = await LoginAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/squads/stage_01")
        {
            Content = JsonContent.Create(new SaveStageSquadRequest(
                "guest_other",
                ["starter_archer_a"],
                0))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoints_DoNotRequireToken()
    {
        await using var app = new AuthenticatedWebApplicationFactory();
        var client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<GuestLoginResponse> LoginAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsync("/v1/auth/guest", content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GuestLoginResponse>())!;
    }

    private sealed class AuthenticatedWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempStorePath = Path.Combine(
            Path.GetTempPath(),
            "defensegame-server-auth-tests",
            Guid.NewGuid().ToString("N"),
            "progress.json");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ProgressStore:FilePath"] = _tempStorePath,
                    ["Authentication:Required"] = "true",
                    ["Authentication:SigningKey"] = SigningKey,
                    ["Authentication:TokenLifetimeHours"] = "1"
                });
            });
        }
    }
}
