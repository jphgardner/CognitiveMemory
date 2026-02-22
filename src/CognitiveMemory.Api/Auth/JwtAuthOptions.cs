namespace CognitiveMemory.Api.Auth;

public sealed class JwtAuthOptions
{
    public string Issuer { get; set; } = "CognitiveMemory";
    public string Audience { get; set; } = "CognitiveMemory.Client";
    public string SigningKey { get; set; } = "change-me-to-a-long-random-secret-at-least-32-chars";
    public int AccessTokenMinutes { get; set; } = 480;
}
