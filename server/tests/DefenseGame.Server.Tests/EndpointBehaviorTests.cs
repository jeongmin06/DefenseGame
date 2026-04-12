using System.Net;
using System.Net.Http.Json;
using DefenseGame.Server.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace DefenseGame.Server.Tests;

public class EndpointBehaviorTests
{
    [Fact]
    public async Task Health_ReturnsOk()
    {
        await using var app = new TestWebApplicationFactory();
        var client = app.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StageClear_ThenGetProgress_ReturnsExpectedPayload()
    {
        await using var app = new TestWebApplicationFactory();
        var client = app.CreateClient();

        var clearResponse = await client.PostAsJsonAsync(
            "/v1/stage-clear",
            new StageClearRequest("user-1", StageId: 1, WasVictory: true));

        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        var clearPayload = await clearResponse.Content.ReadFromJsonAsync<StageClearResponse>();
        Assert.NotNull(clearPayload);
        Assert.Equal("user-1", clearPayload.UserId);
        Assert.True(clearPayload.ProgressUpdated);
        Assert.Equal(1, clearPayload.HighestClearedStageId);
        Assert.Equal(2, clearPayload.NextUnlockedStageId);

        var progressResponse = await client.GetAsync("/v1/progress/user-1");
        Assert.Equal(HttpStatusCode.OK, progressResponse.StatusCode);

        var progressPayload = await progressResponse.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(progressPayload);
        Assert.Equal("user-1", progressPayload.UserId);
        Assert.Equal(1, progressPayload.HighestClearedStageId);
        Assert.Equal(2, progressPayload.NextUnlockedStageId);
        Assert.Equal(new[] { 1 }, progressPayload.ClearedStageIds);
    }

    [Fact]
    public async Task StageClear_SkipStage_ReturnsBadRequest()
    {
        await using var app = new TestWebApplicationFactory();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/stage-clear",
            new StageClearRequest("user-1", StageId: 3, WasVictory: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetProgress_BlankUserId_ReturnsBadRequest()
    {
        await using var app = new TestWebApplicationFactory();
        var client = app.CreateClient();

        var response = await client.GetAsync("/v1/progress/%20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempStorePath = Path.Combine(
            Path.GetTempPath(),
            "defensegame-server-tests",
            Guid.NewGuid().ToString("N"),
            "progress.json");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ProgressStore:FilePath"] = _tempStorePath
                });
            });
        }
    }
}
