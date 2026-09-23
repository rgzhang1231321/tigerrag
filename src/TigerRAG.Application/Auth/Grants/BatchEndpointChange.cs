namespace TigerRAG.Application.Auth;

/// <summary>单条目标授权变更（最终态）：true=授予、false=撤销；仅授予项会落库。</summary>
public sealed record BatchEndpointChange(
    string MenuKey,
    string EndpointKey,
    bool Granted);