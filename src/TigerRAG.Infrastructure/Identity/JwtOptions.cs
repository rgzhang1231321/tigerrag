namespace TigerRAG.Infrastructure.Identity;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "TigerRAG";
    public string Audience { get; set; } = "TigerRAG";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
}
