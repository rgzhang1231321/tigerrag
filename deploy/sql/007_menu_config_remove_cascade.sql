-- 迁移：移除 menu_config_record 的 ON DELETE CASCADE，改为由业务代码处理级联删除。
-- 适用于已从 004_menu_config.sql（带 CASCADE）建库的实例。

BEGIN;

ALTER TABLE "menu_config_record"
    DROP CONSTRAINT IF EXISTS "FK_menu_config_record_ParentId";

ALTER TABLE "menu_config_record"
    ADD CONSTRAINT "FK_menu_config_record_ParentId"
        FOREIGN KEY ("ParentId") REFERENCES "menu_config_record" ("Id");

COMMIT;
