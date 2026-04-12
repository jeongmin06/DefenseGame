using DefenseGame.Server.Domain;

namespace DefenseGame.Server.Stores;

public interface IProgressStore
{
    Task<UserProgress> GetOrCreateAsync(string userId, CancellationToken cancellationToken);
    Task<UserProgress> UpsertAsync(UserProgress progress, CancellationToken cancellationToken);
}
