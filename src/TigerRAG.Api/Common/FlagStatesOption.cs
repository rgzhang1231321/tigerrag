namespace TigerRAG.Api.Common;

/// <summary>
/// 业务码枚举；数值与早期 ApiErrorCodes 保持一致，便于老前端按数值兼容。
/// </summary>
public enum FlagStatesOption
{
    Success = 0,
    Validation = 40000,
    Unauthorized = 40100,
    Forbidden = 40300,
    NotFound = 40400,
    Conflict = 40900,
    InternalServerError = 50000,
    NotImplemented = 50100
}