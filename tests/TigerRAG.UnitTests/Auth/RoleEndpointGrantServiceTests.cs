using TigerRAG.Application.Auth;
using TigerRAG.Application.OperationAudit;
using TigerRAG.Application.Shared;
using TigerRAG.Application.Users;

namespace TigerRAG.UnitTests.Auth;

/// <summary>角色-Endpoint 授权 service 行为：菜单级批量授权/撤销、单 endpoint 切换、矩阵查询。所有角色（含 Admin）平等可配置。</summary>
public sealed class RoleEndpointGrantServiceTests
{
    private static readonly MenuEndpointDescriptor[] SampleEndpoints =
    [
        new("documents", "documents.list", "列出文档", "POST", "list"),
        new("documents", "documents.upload", "上传文档", "POST", "upload"),
        new("documents", "documents.delete", "删除文档", "POST", "{id}/delete"),
        new("knowledgeBases", "knowledgeBases.list", "列出知识库", "POST", "list"),
    ];

    private static global::TigerRAG.Application.Auth.RoleEndpointGrantService BuildService(
        IRoleEndpointGrantStore? store = null,
        IOperationAuditWriter? audit = null,
        IUnitOfWork? unitOfWork = null)
    {
        return new global::TigerRAG.Application.Auth.RoleEndpointGrantService(
            store ?? new RecordingGrantStore(),
            audit ?? new RecordingAuditWriter(),
            unitOfWork ?? new RecordingUnitOfWork());
    }

    private static ActorContext AdminActor => new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "admin");

    [Fact]
    public async Task GrantMenuAsync_AdminRole_GrantsSuccessfully()
    {
        // Admin 与其他角色平等，可配置授权。
        var store = new RecordingGrantStore();
        var service = BuildService(store);

        var affected = await service.GrantMenuAsync(AdminActor, "Admin", "documents", SampleEndpoints, CancellationToken.None);

        Assert.Equal(3, affected);
        Assert.Equal(3, store.Granted.Count);
    }

    [Fact]
    public async Task GrantMenuAsync_GrantsAllEndpointsInMenu()
    {
        var store = new RecordingGrantStore();
        var service = BuildService(store);

        var affected = await service.GrantMenuAsync(AdminActor, "Viewer", "documents", SampleEndpoints, CancellationToken.None);

        Assert.Equal(3, affected);   // knowledgeBases 不在 documents 菜单下
        Assert.Equal(3, store.Granted.Count);
        Assert.Contains(store.Granted, g => g.EndpointKey == "documents.list");
        Assert.Contains(store.Granted, g => g.EndpointKey == "documents.delete");
        Assert.DoesNotContain(store.Granted, g => g.EndpointKey == "knowledgeBases.list");
    }

    [Fact]
    public async Task RevokeMenuAsync_RevokesAllEndpointsInMenu()
    {
        var store = new RecordingGrantStore();
        store.SeedGrant("Viewer", "documents", "documents.list");
        store.SeedGrant("Viewer", "documents", "documents.upload");
        store.SeedGrant("Viewer", "documents", "documents.delete");
        var service = BuildService(store);

        await service.RevokeMenuAsync(AdminActor, "Viewer", "documents", CancellationToken.None);

        Assert.Equal(3, store.RevokedMenuCalls);
    }

    [Fact]
    public async Task ToggleEndpointAsync_Grant_CreatesGrant()
    {
        var store = new RecordingGrantStore();
        var service = BuildService(store);

        await service.ToggleEndpointAsync(AdminActor, "Viewer", "documents.list", "documents", true, CancellationToken.None);

        Assert.Single(store.Granted);
        Assert.Equal("documents.list", store.Granted[0].EndpointKey);
    }

    [Fact]
    public async Task ToggleEndpointAsync_Revoke_RemovesGrant()
    {
        var store = new RecordingGrantStore();
        var service = BuildService(store);

        await service.ToggleEndpointAsync(AdminActor, "Viewer", "documents.list", "documents", false, CancellationToken.None);

        Assert.Single(store.Revoked);
        Assert.Equal("documents.list", store.Revoked[0].EndpointKey);
    }

    [Fact]
    public async Task ListForRoleAsync_ReturnsMatrix()
    {
        var store = new RecordingGrantStore();
        store.SeedGrant("Viewer", "documents", "documents.list");
        var service = BuildService(store);

        var matrix = await service.ListForRoleAsync("Viewer", SampleEndpoints, CancellationToken.None);

        var documentsGroup = matrix.Menus.First(m => m.MenuKey == "documents");
        Assert.Equal(3, documentsGroup.Endpoints.Count);
        var listEp = documentsGroup.Endpoints.First(e => e.EndpointKey == "documents.list");
        Assert.True(listEp.Granted);
        var deleteEp = documentsGroup.Endpoints.First(e => e.EndpointKey == "documents.delete");
        Assert.False(deleteEp.Granted);
    }

    [Fact]
    public async Task ApplyBatchAsync_RebuildsGrantsInOneShot()
    {
        // 现状：documents.list 已授权、documents.upload 未授权；目标态交换两者。
        var store = new RecordingGrantStore();
        store.SeedGrant("Viewer", "documents", "documents.list");
        var service = BuildService(store);

        var desired = new[]
        {
            new BatchEndpointChange("documents", "documents.upload", true),
            new BatchEndpointChange("documents", "documents.list", false),
            new BatchEndpointChange("knowledgeBases", "knowledgeBases.list", true),
        };
        var affected = await service.ApplyBatchAsync(AdminActor, "Viewer", desired, CancellationToken.None);

        // documents.list 撤销 + documents.upload 授予 + knowledgeBases.list 授予 = 3。
        Assert.Equal(3, affected);
        Assert.Equal(1, store.Revoked.Count);
        Assert.Equal("documents.list", store.Revoked[0].EndpointKey);
        Assert.Equal(2, store.Granted.Count);
        Assert.Contains(store.Granted, g => g.EndpointKey == "documents.upload");
        Assert.Contains(store.Granted, g => g.EndpointKey == "knowledgeBases.list");
    }

    [Fact]
    public async Task ApplyBatchAsync_WhenNoDiff_ReturnsZeroAndNoAudit()
    {
        var store = new RecordingGrantStore();
        store.SeedGrant("Viewer", "documents", "documents.list");
        var audit = new RecordingAuditWriter();
        var service = BuildService(store, audit);

        // 目标态与现状一致 → 无变更 → 无审计。
        var desired = new[] { new BatchEndpointChange("documents", "documents.list", true) };
        var affected = await service.ApplyBatchAsync(AdminActor, "Viewer", desired, CancellationToken.None);

        Assert.Equal(0, affected);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task ApplyBatchAsync_WritesSingleAuditEntry()
    {
        var store = new RecordingGrantStore();
        store.SeedGrant("Viewer", "documents", "documents.list");
        var audit = new RecordingAuditWriter();
        var service = BuildService(store, audit);

        await service.ApplyBatchAsync(
            AdminActor,
            "Viewer",
            [new BatchEndpointChange("documents", "documents.list", false)],
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(OperationAuditActions.RoleEndpointApplyBatch, entry.Action);
    }

    // ── Recording stubs ──

    private sealed class RecordingGrantStore : IRoleEndpointGrantStore
    {
        public List<(string RoleName, string MenuKey, string EndpointKey)> Granted { get; } = [];
        public List<(string RoleName, string EndpointKey)> Revoked { get; } = [];
        public int RevokedMenuCalls { get; private set; }
        public List<(string RoleName, IReadOnlyList<BatchEndpointChange> Desired, Guid ActorId)> ApplyBatchCalls { get; } = [];
        private readonly List<RoleEndpointGrant> _grants = [];

        public void SeedGrant(string role, string menu, string endpoint) =>
            _grants.Add(new RoleEndpointGrant(role, menu, endpoint, DateTimeOffset.UtcNow, Guid.NewGuid()));

        public Task<IReadOnlyList<RoleEndpointGrant>> ListByRoleAsync(string roleName, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RoleEndpointGrant>>(
                _grants.Where(g => g.RoleName == roleName).ToList());

        public Task<bool> HasGrantAsync(IEnumerable<string> userRoles, string endpointKey, CancellationToken ct) =>
            Task.FromResult(_grants.Any(g => g.EndpointKey == endpointKey && userRoles.Contains(g.RoleName)));

        public Task GrantAsync(string roleName, string menuKey, string endpointKey, Guid actorId, CancellationToken ct)
        {
            Granted.Add((roleName, menuKey, endpointKey));
            _grants.Add(new RoleEndpointGrant(roleName, menuKey, endpointKey, DateTimeOffset.UtcNow, actorId));
            return Task.CompletedTask;
        }

        public Task RevokeAsync(string roleName, string endpointKey, CancellationToken ct)
        {
            Revoked.Add((roleName, endpointKey));
            _grants.RemoveAll(g => g.RoleName == roleName && g.EndpointKey == endpointKey);
            return Task.CompletedTask;
        }

        public Task<int> GrantAllInMenuAsync(string roleName, string menuKey, IReadOnlyCollection<MenuEndpointDescriptor> endpoints, Guid actorId, CancellationToken ct)
        {
            foreach (var ep in endpoints.Where(e => e.MenuKey == menuKey && !_grants.Any(g => g.RoleName == roleName && g.EndpointKey == e.EndpointKey)))
            {
                Granted.Add((roleName, menuKey, ep.EndpointKey));
                _grants.Add(new RoleEndpointGrant(roleName, menuKey, ep.EndpointKey, DateTimeOffset.UtcNow, actorId));
            }
            return Task.FromResult(endpoints.Count(e => e.MenuKey == menuKey));
        }

        public Task<int> RevokeAllInMenuAsync(string roleName, string menuKey, CancellationToken ct)
        {
            RevokedMenuCalls = _grants.RemoveAll(g => g.RoleName == roleName && g.MenuKey == menuKey);
            return Task.FromResult(RevokedMenuCalls);
        }

        public Task<int> ApplyBatchAsync(string roleName, IReadOnlyCollection<BatchEndpointChange> desiredEndpoints, Guid actorId, CancellationToken ct)
        {
            ApplyBatchCalls.Add((roleName, desiredEndpoints.ToArray(), actorId));
            var desired = desiredEndpoints.ToArray();
            var granted = new HashSet<(string MenuKey, string EndpointKey)>(
                desired.Where(c => c.Granted).Select(c => (c.MenuKey, c.EndpointKey)));
            var currentKeys = _grants.Where(g => g.RoleName == roleName).Select(g => (g.MenuKey, g.EndpointKey)).ToHashSet();
            var affected = 0;
            // revoke: current 中不在 desired granted 集合内的
            foreach (var g in _grants.Where(g => g.RoleName == roleName).ToList())
            {
                if (!granted.Contains((g.MenuKey, g.EndpointKey)))
                {
                    Revoked.Add((roleName, g.EndpointKey));
                    _grants.Remove(g);
                    affected++;
                }
            }
            // add: desired granted 中尚未存在的
            foreach (var change in desired.Where(c => c.Granted))
            {
                if (currentKeys.Contains((change.MenuKey, change.EndpointKey))) continue;
                Granted.Add((roleName, change.MenuKey, change.EndpointKey));
                _grants.Add(new RoleEndpointGrant(roleName, change.MenuKey, change.EndpointKey, DateTimeOffset.UtcNow, actorId));
                affected++;
            }
            return Task.FromResult(affected);
        }
    }

    private sealed class RecordingAuditWriter : IOperationAuditWriter
    {
        public List<OperationAuditEntry> Entries { get; } = [];
        public Task RecordAsync(OperationAuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int ExecuteCount { get; private set; }
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
        {
            ExecuteCount++;
            return operation(ct);
        }
    }
}