using DefenseGame.Server.Domain;
using DefenseGame.Server.Stores;

namespace DefenseGame.Server.Tests.TestDoubles;

internal sealed class InMemoryProgressStore : IProgressStore
{
    private readonly Dictionary<string, UserProgress> _users = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _nowProvider;

    public InMemoryProgressStore(Func<DateTimeOffset> nowProvider)
    {
        _nowProvider = nowProvider;
    }

    public Task<UserProgress> GetOrCreateAsync(string userId, CancellationToken cancellationToken)
    {
        if (!_users.TryGetValue(userId, out var progress))
        {
            progress = UserProgress.New(userId, _nowProvider());
            _users[userId] = progress;
        }

        return Task.FromResult(progress.Clone());
    }

    public Task<UserProgress> UpsertAsync(UserProgress progress, CancellationToken cancellationToken)
    {
        _users[progress.UserId] = progress.Clone();
        return Task.FromResult(progress.Clone());
    }
}
