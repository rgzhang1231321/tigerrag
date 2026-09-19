-- TigerRAG JWT 访问令牌按用户撤权机制（合约说明，无 DDL 变更）。
--
-- AspNetUsers.SecurityStamp 列自 001_initial_schema.sql:24 起即存在，
-- 本脚本不修改结构，只固化"按用户撤权"的运行合约，供运维与审计对照。
--
-- 合约要点：
-- 1. SecurityStamp 是"按用户撤权纪元"——任何敏感动作（角色分配、密码重置、
--    锁定、删除）必须在同一事务内轮换该值。轮换路径见 UserRoleService 与 AuthService。
-- 2. JWT 访问令牌必须写入 security_stamp 声明；缺失即视为旧版 token 失效。
-- 3. JWT 校验（ApiComposition.OnTokenValidated）每次都把 claim 中的 stamp
--    与 Redis L1 缓存（auth:user:{guid}:stamp，TTL = Jwt:AccessTokenMinutes*60 +
--    Jwt:ClockSkewSeconds 秒）比对；缓存 miss 回退到 DB 重新写回。
-- 4. Identity 的 ChangePasswordAsync 已自动轮换 SecurityStamp；自管写入
--    （SetInitialPasswordAsync / ResetPasswordAsync / SetLockoutAsync /
--    AssignRolesAsync / DeleteAsync）必须显式调用 userManager.UpdateSecurityStampAsync
--    或清空 Redis 缓存，否则旧 token 在最长 AccessTokenMinutes 内继续可用。
-- 5. 用户删除须先调用 IAuthRevocationCache.InvalidateAsync 清空缓存，
--    否则 401 路径要等缓存 TTL 自然到期。

BEGIN;
COMMIT;