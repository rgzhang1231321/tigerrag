-- TigerRAG PostgreSQL 增量脚本：refresh_token_record 绑定用户 SecurityStamp。
-- 与 EF Core 解耦，由维护人员手工执行。
--
-- 背景：refresh token 之前不感知用户的"按用户撤权纪元"（Identity SecurityStamp），
-- 改密/锁定/角色变更后既有 refresh token 仍可轮换出新 AccessToken，等于绕过撤权。
-- 005_security_stamp_cache.sql 已为 AccessToken 路径接入该机制，本脚本把同一机制接到 refresh token 维度。
--
-- 合约要点：
-- 1. CreateAsync 写入新记录时快照用户当时的 SecurityStamp；
-- 2. RotateAsync 拿用户最新 stamp 与记录的 stamp 比对，不一致或用户被锁即拒轮换；
-- 3. stamp 跨敏感动作（ChangePassword / UpdateSecurityStamp / SetLockout / Delete）的轮换由 Identity 与
--    IUserSecurityStampRotator 负责，refresh token DAL 只读不写。
-- 4. 历史遗留记录（SecurityStamp IS NULL）在严格匹配下会被拒绝；部署后用户需重登一次换取新 token。
-- 5. 不加索引：本表的主键与 TokenHash 唯一索引已覆盖 RotateAsync 的查询路径，stamp 不参与检索。

BEGIN;

ALTER TABLE "refresh_token_record"
    ADD COLUMN IF NOT EXISTS "SecurityStamp" character varying(64);

COMMIT;