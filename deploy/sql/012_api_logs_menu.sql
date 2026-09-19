-- 012_api_logs_menu.sql
-- 在"系统设置"下新增"日志管理"菜单项，仅 Admin 可见。
INSERT INTO "menu_config_record" ("Id", "Key", "Label", "Icon", "Roles", "ParentId", "SortOrder", "IsEnabled")
VALUES
    ('a1a1a1a1-0000-0000-0000-000000000010', '/logs', '日志管理', 'FileTextOutlined', 'Admin', 'a1a1a1a1-0000-0000-0000-000000000005', 54, true)
ON CONFLICT DO NOTHING;
