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

    [Fact]
    public async Task SaveSquad_ThenGetSquad_PreservesServerState()
    {
        await using var app = new TestWebApplicationFactory();
        var client = app.CreateClient();
        var save = await client.PutAsJsonAsync("/v1/squads/stage_01",
            new SaveStageSquadRequest("player", ["starter_warrior_a", "starter_healer_a"], 0));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        StageSquadResponse payload = (await save.Content.ReadFromJsonAsync<StageSquadResponse>())!;
        Assert.Equal(1, payload.Revision);

        StageSquadResponse loaded = (await client.GetFromJsonAsync<StageSquadResponse>(
            "/v1/squads/player/stage_01"))!;
        Assert.Equal(["starter_warrior_a", "starter_healer_a"], loaded.CharacterIds);
        Assert.Equal(1, loaded.Revision);
    }

    [Fact]
    public async Task SaveSquad_WithStaleRevision_ReturnsConflict()
    {
        await using var app = new TestWebApplicationFactory();
        var client = app.CreateClient();
        await client.PutAsJsonAsync("/v1/squads/stage_01",
            new SaveStageSquadRequest("player", ["starter_archer_a"], 0));
        var conflict = await client.PutAsJsonAsync("/v1/squads/stage_01",
            new SaveStageSquadRequest("player", ["starter_archer_b"], 0));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task ConcurrentFirstClear_GrantsRewardExactlyOnce_ForNormalizedUser()
    {
        await using var app = new TestWebApplicationFactory();
        var client = app.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 40).Select(index =>
            client.PostAsJsonAsync("/v1/stage-clear",
                new StageClearRequest(index % 2 == 0 ? " concurrent-user " : "concurrent-user", 1, true))))
            .WaitAsync(TimeSpan.FromSeconds(10));
        var payloads = new List<StageClearResponse>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            payloads.Add((await response.Content.ReadFromJsonAsync<StageClearResponse>())!);
        }
        Assert.Single(payloads, payload => payload.ProgressUpdated);
        Assert.Single(payloads.SelectMany(payload => payload.GrantedRewards));
        var progress = await client.GetFromJsonAsync<ProgressResponse>("/v1/progress/concurrent-user");
        Assert.Equal(new[] { 1 }, progress!.ClearedStageIds);
    }

    [Fact]
    public async Task RepeatedHosts_StartAndStop_WithoutSharedRuntimeState()
    {
        for (var index = 0; index < 3; index++)
        {
            await using var app = new TestWebApplicationFactory();
            var client = app.CreateClient();
            var response = await client.PostAsJsonAsync("/v1/stage-clear", new StageClearRequest("user", 1, true))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True((await response.Content.ReadFromJsonAsync<StageClearResponse>())!.ProgressUpdated);
        }
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
