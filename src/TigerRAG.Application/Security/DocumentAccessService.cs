namespace TigerRAG.Application.Security;

public sealed class DocumentAccessService(IDocumentAccessDal documentAccess)
{
    public async Task<DocumentAccessScope> GetScopeAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        if (roles.Contains(SystemRoles.Admin, StringComparer.Ordinal))
        {
            return new DocumentAccessScope(true, []);
        }

        var documentIds = await documentAccess.GetAccessibleDocumentIdsAsync(
            userId,
            roles,
            cancellationToken);
        return new DocumentAccessScope(false, documentIds);
    }

    public Task ReplacePermissionsAsync(
        Guid documentId,
        Guid actorId,
        bool isAdmin,
        IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var invalidRole = roles.FirstOrDefault(role => !SystemRoles.All.Contains(role));
        if (invalidRole is not null)
        {
            throw new ArgumentException($"Unknown system role: {invalidRole}", nameof(roles));
        }

        return documentAccess.ReplacePermissionsAsync(
            documentId,
            actorId,
            isAdmin,
            userIds.Distinct().ToArray(),
            roles.Distinct(StringComparer.Ordinal).OrderBy(role => role, StringComparer.Ordinal).ToArray(),
            cancellationToken);
    }
}
