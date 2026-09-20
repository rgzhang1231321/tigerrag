-- 010_menu_config_roles_text.sql
-- 将 menu_config_record.Roles 列从 jsonb 改为 varchar(500)，逗号分隔存储角色名。
-- 空字符串表示所有人可见。历史 JSONB 数组 ["Admin","Editor"] 转为 "Admin,Editor"。
-- 注意：PostgreSQL JSONB 的文本表示含双引号和空格，如 ["Admin", "Editor"]，
-- 转换时需同时去掉方括号、双引号和空格，仅保留逗号分隔的角色名。

ALTER TABLE menu_config_record ALTER COLUMN "Roles" TYPE varchar(500) USING (
  trim(both ',' from replace(replace(replace(replace(
    "Roles"::text,
    '"', ''),
    '[', ''),
    ']', ''),
    ' ', ''))
);
ALTER TABLE menu_config_record ALTER COLUMN "Roles" SET NOT NULL;
ALTER TABLE menu_config_record ALTER COLUMN "Roles" SET DEFAULT '';
