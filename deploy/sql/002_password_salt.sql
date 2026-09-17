-- TigerRAG PostgreSQL 增量脚本：新增用户密码 salt 列（用于客户端 MD5 传输层哈希）。
-- 与 EF Core 解耦，由维护人员手工执行。
-- 上线要求：生产尚未启用，无需迁移旧用户；DEFAULT '' 兼容老账号（无 salt 的用户将无法登录）。

BEGIN;

ALTER TABLE "AspNetUsers"
    ADD COLUMN IF NOT EXISTS "PasswordSalt" character varying(64) NOT NULL DEFAULT '';

COMMIT;