using NpgsqlTypes;

namespace TigerRAG.Infrastructure.Persistence.Entities;

public sealed class DocumentChunkRecord
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int Position { get; set; }
    public int? PageNumber { get; set; }
    public string? Title { get; set; }
    public required string Content { get; set; }
    public NpgsqlTsVector? SearchVector { get; set; }
}
