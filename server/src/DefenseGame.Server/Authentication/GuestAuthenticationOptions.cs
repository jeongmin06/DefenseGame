namespace DefenseGame.Server.Authentication;

public sealed record GuestAuthenticationOptions(
    bool Required,
    string? SigningKey,
    int TokenLifetimeHours)
{
    public const int DefaultTokenLifetimeHours = 720;
    public const int MinimumSigningKeyLength = 32;

    public bool CanIssueTokens => !string.IsNullOrWhiteSpace(SigningKey);

    public static GuestAuthenticationOptions FromConfiguration(IConfiguration configuration)
    {
        string? requiredValue = configuration["Authentication:Required"];
        if (!string.IsNullOrWhiteSpace(requiredValue) && !bool.TryParse(requiredValue, out _))
            throw new InvalidOperationException("Authentication:Required must be true or false.");

        bool required = bool.TryParse(requiredValue, out bool parsedRequired) && parsedRequired;
        string? signingKey = configuration["Authentication:SigningKey"];
        if (!string.IsNullOrEmpty(signingKey) &&
            (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < MinimumSigningKeyLength))
        {
            throw new InvalidOperationException(
                $"Authentication:SigningKey must be at least {MinimumSigningKeyLength} characters.");
        }

        string? lifetimeValue = configuration["Authentication:TokenLifetimeHours"];
        int lifetimeHours = DefaultTokenLifetimeHours;
        if (!string.IsNullOrWhiteSpace(lifetimeValue) &&
            (!int.TryParse(lifetimeValue, out lifetimeHours) || lifetimeHours <= 0))
        {
            throw new InvalidOperationException("Authentication:TokenLifetimeHours must be a positive integer.");
        }

        if (required && string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                "Authentication:SigningKey is required when Authentication:Required is true.");
        }

        return new GuestAuthenticationOptions(required, signingKey, lifetimeHours);
    }
}
