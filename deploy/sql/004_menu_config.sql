-- TigerRAG 菜单配置表。导航菜单由前端从本表动态读取，可在管理页增删改。
-- ParentId 为 NULL 表示顶级菜单；非 NULL 表示子菜单（如系统设置下的用户管理、角色管理）。
-- Roles 为 JSONB 角色名数组：空数组表示所有人可见；Admin 始终可见（前端 bypass）。
-- 注意：不使用 ON DELETE CASCADE，级联删除由业务代码显式处理（MenuConfigDal.DeleteSubtreeAsync），
-- 以保持 EF 与 SQL 建库行为一致，并让业务层完全控制删除边界。

BEGIN;

CREATE TABLE IF NOT EXISTS "menu_config_record" (
    "Id" uuid NOT NULL,
    "Key" character varying(100) NOT NULL,
    "Label" character varying(100) NOT NULL,
    "Icon" character varying(100),
    "Roles" jsonb NOT NULL DEFAULT '[]'::jsonb,
    "ParentId" uuid,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsEnabled" boolean NOT NULL DEFAULT true,
    CONSTRAINT "PK_menu_config_record" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_menu_config_record_ParentId"
        FOREIGN KEY ("ParentId") REFERENCES "menu_config_record" ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_menu_config_record_Key" ON "menu_config_record" ("Key");
CREATE INDEX IF NOT EXISTS "IX_menu_config_record_ParentId" ON "menu_config_record" ("ParentId");
CREATE INDEX IF NOT EXISTS "IX_menu_config_record_SortOrder" ON "menu_config_record" ("SortOrder");

-- 种子数据：与前端原硬编码导航一致。
-- 先插入父级 "系统设置"，再插入子级（需要 ParentId）。
INSERT INTO "menu_config_record" ("Id", "Key", "Label", "Icon", "Roles", "ParentId", "SortOrder", "IsEnabled")
VALUES
    ('a1a1a1a1-0000-0000-0000-000000000001', '/', '概览', 'DashboardOutlined', '[]'::jsonb, NULL, 10, true),
    ('a1a1a1a1-0000-0000-0000-000000000002', '/knowledge-bases', '知识库', 'BookOutlined', '["Admin","KbManager"]'::jsonb, NULL, 20, true),
    ('a1a1a1a1-0000-0000-0000-000000000003', '/documents', '文档管理', 'FileTextOutlined', '["Admin","KbManager","Editor"]'::jsonb, NULL, 30, true),
    ('a1a1a1a1-0000-0000-0000-000000000004', '/chat', '问答工作台', 'MessageOutlined', '["Admin","KbManager","Editor","Viewer"]'::jsonb, NULL, 40, true),
    ('a1a1a1a1-0000-0000-0000-000000000005', 'system-settings', '系统设置', 'SettingOutlined', '[]'::jsonb, NULL, 50, true),
    ('a1a1a1a1-0000-0000-0000-000000000006', '/users', '用户管理', 'TeamOutlined', '["Admin"]'::jsonb, 'a1a1a1a1-0000-0000-0000-000000000005', 51, true),
    ('a1a1a1a1-0000-0000-0000-000000000007', '/roles', '角色管理', 'SafetyOutlined', '["Admin"]'::jsonb, 'a1a1a1a1-0000-0000-0000-000000000005', 52, true),
    ('a1a1a1a1-0000-0000-0000-000000000008', '/menu-configs', '菜单管理', 'MenuOutlined', '["Admin"]'::jsonb, 'a1a1a1a1-0000-0000-0000-000000000005', 53, true),
    ('a1a1a1a1-0000-0000-0000-000000000009', '/audit', '审计日志', 'AuditOutlined', '["Admin","Auditor"]'::jsonb, NULL, 60, true)
ON CONFLICT DO NOTHING;

COMMIT;
