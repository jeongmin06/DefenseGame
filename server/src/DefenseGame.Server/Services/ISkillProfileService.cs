using DefenseGame.Server.Contracts;

namespace DefenseGame.Server.Services;

public interface ISkillProfileService
{
    Task<SkillProfileHandleResult> GetAsync(string userId, CancellationToken cancellationToken);
    Task<SkillProfileHandleResult> SaveAsync(
        string userId,
        SaveSkillProfileRequest request,
        CancellationToken cancellationToken);
}
