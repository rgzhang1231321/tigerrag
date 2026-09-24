using TigerRAG.Domain.Documents;

namespace TigerRAG.UnitTests.Documents;

public sealed class DocumentTests
{
    [Fact]
    public void CompleteIndexing_WhenProcessing_MarksDocumentAsIndexed()
    {
        var document = Document.Create(Guid.NewGuid(), "handbook.pdf", "documents/handbook.pdf", 2048);

        document.StartProcessing();
        document.CompleteIndexing(chunkCount: 12);

        Assert.Equal(DocumentStatus.Indexed, document.Status);
        Assert.Equal(12, document.ChunkCount);
        Assert.Null(document.FailureReason);
    }

    [Fact]
    public void FailIndexing_WhenProcessing_RecordsFailureReason()
    {
        var document = Document.Create(Guid.NewGuid(), "handbook.pdf", "documents/handbook.pdf", 2048);

        document.StartProcessing();
        document.FailIndexing("Embedding service unavailable");

        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal("Embedding service unavailable", document.FailureReason);
    }

    [Fact]
    public void CompleteIndexing_WhenPending_RejectsInvalidTransition()
    {
        var document = Document.Create(Guid.NewGuid(), "handbook.pdf", "documents/handbook.pdf", 2048);

        var error = Assert.Throws<InvalidOperationException>(() => document.CompleteIndexing(1));

        Assert.Contains("Processing", error.Message);
    }
}
