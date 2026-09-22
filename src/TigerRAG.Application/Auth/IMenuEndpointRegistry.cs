namespace TigerRAG.Application.Auth;

/// <summary>启动时扫描所有 [MenuEndpoint] action 得到的注册表；授权 filter 与角色授权 UI 共同消费。</summary>
public interface IMenuEndpointRegistry
{
    IReadOnlyList<MenuEndpointDescriptor> All { get; }

    MenuEndpointDescriptor? Find(string endpointKey);
}