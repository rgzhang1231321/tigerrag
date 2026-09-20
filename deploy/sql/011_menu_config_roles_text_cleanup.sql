-- 011_menu_config_roles_text_cleanup.sql
-- 纠正 010 脚本的转换遗漏：JSONB 文本表示中的双引号和空格未清理。
-- 例如 "Admin", "KbManager" → "Admin,KbManager"

UPDATE menu_config_record
SET "Roles" = trim(both ',' from replace(replace(replace(replace("Roles", '"', ''), '[', ''), ']', ''), ' ', ''))
WHERE "Roles" <> '';
