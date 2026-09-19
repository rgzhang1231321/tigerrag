-- 新增报表查看权限：Admin 与 Auditor 可看 /reports 页面。
-- 角色 → 权限映射走 role_permission_record，与 application 启动期策略注册对齐。

BEGIN;

INSERT INTO "system_permission_record" ("Code", "Description") VALUES
    ('reports.view', '查看报表')
ON CONFLICT DO NOTHING;

INSERT INTO "role_permission_record" ("RoleName", "PermissionCode") VALUES
    ('Admin',   'reports.view'),
    ('Auditor', 'reports.view')
ON CONFLICT DO NOTHING;

COMMIT;
