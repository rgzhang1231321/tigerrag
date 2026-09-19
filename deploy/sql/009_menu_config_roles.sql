-- 迁移：menu_config_record 的 Permission 列 → Roles JSONB 列。
-- 角色 → 菜单可见性映射（空数组 = 所有人可见；Admin 前端 bypass 始终可见）。

BEGIN;

ALTER TABLE "menu_config_record" DROP COLUMN "Permission";
ALTER TABLE "menu_config_record" ADD COLUMN "Roles" jsonb NOT NULL DEFAULT '[]'::jsonb;

-- 存量数据迁移：仅当仍存在旧权限列数据时执行（新部署直接由 004 种子写入，本段可空转）。
UPDATE "menu_config_record" SET "Roles" = '["Admin","KbManager"]'::jsonb WHERE "Key" = '/knowledge-bases';
UPDATE "menu_config_record" SET "Roles" = '["Admin","KbManager","Editor"]'::jsonb WHERE "Key" = '/documents';
UPDATE "menu_config_record" SET "Roles" = '["Admin","KbManager","Editor","Viewer"]'::jsonb WHERE "Key" = '/chat';
UPDATE "menu_config_record" SET "Roles" = '["Admin"]'::jsonb WHERE "Key" = '/users';
UPDATE "menu_config_record" SET "Roles" = '["Admin"]'::jsonb WHERE "Key" = '/roles';
UPDATE "menu_config_record" SET "Roles" = '["Admin"]'::jsonb WHERE "Key" = '/menu-configs';
UPDATE "menu_config_record" SET "Roles" = '["Admin","Auditor"]'::jsonb WHERE "Key" = '/audit';

COMMIT;
