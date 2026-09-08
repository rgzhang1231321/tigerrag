namespace TigerRAG.Infrastructure.Identity;

public sealed class RefreshTokenOptions
{
    public int LifetimeDays { get; set; } = 7;
}
