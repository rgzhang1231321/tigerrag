-- TigerRAG 菜单配置表。导航菜单由前端从本表动态读取，可在管理页增删改。
-- ParentId 为 NULL 表示顶级菜单；非 NULL 表示子菜单（如系统设置下的用户管理、角色管理）。

BEGIN;

CREATE TABLE IF NOT EXISTS "menu_config_record" (
    "Id" uuid NOT NULL,
    "Key" character varying(100) NOT NULL,
    "Label" character varying(100) NOT NULL,
    "Icon" character varying(100),
    "Permission" character varying(100),
    "ParentId" uuid,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsEnabled" boolean NOT NULL DEFAULT true,
    CONSTRAINT "PK_menu_config_record" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_menu_config_record_ParentId"
        FOREIGN KEY ("ParentId") REFERENCES "menu_config_record" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_menu_config_record_Key" ON "menu_config_record" ("Key");
CREATE INDEX IF NOT EXISTS "IX_menu_config_record_ParentId" ON "menu_config_record" ("ParentId");
CREATE INDEX IF NOT EXISTS "IX_menu_config_record_SortOrder" ON "menu_config_record" ("SortOrder");

-- 种子数据：与前端原硬编码导航一致。
-- 先插入父级 "系统设置"，再插入子级（需要 ParentId）。
INSERT INTO "menu_config_record" ("Id", "Key", "Label", "Icon", "Permission", "ParentId", "SortOrder", "IsEnabled")
VALUES
    ('a1a1a1a1-0000-0000-0000-000000000001', '/', '概览', 'DashboardOutlined', NULL, NULL, 10, true),
    ('a1a1a1a1-0000-0000-0000-000000000002', '/knowledge-bases', '知识库', 'BookOutlined', 'knowledge-bases.manage', NULL, 20, true),
    ('a1a1a1a1-0000-0000-0000-000000000003', '/documents', '文档管理', 'FileTextOutlined', 'documents.manage', NULL, 30, true),
    ('a1a1a1a1-0000-0000-0000-000000000004', '/chat', '问答工作台', 'MessageOutlined', 'chat.use', NULL, 40, true),
    ('a1a1a1a1-0000-0000-0000-000000000005', 'system-settings', '系统设置', 'SettingOutlined', NULL, NULL, 50, true),
    ('a1a1a1a1-0000-0000-0000-000000000006', '/users', '用户管理', 'TeamOutlined', 'users.manage', 'a1a1a1a1-0000-0000-0000-000000000005', 51, true),
    ('a1a1a1a1-0000-0000-0000-000000000007', '/roles', '角色管理', 'SafetyOutlined', NULL, 'a1a1a1a1-0000-0000-0000-000000000005', 52, true),
    ('a1a1a1a1-0000-0000-0000-000000000008', '/menu-configs', '菜单管理', 'MenuOutlined', 'users.manage', 'a1a1a1a1-0000-0000-0000-000000000005', 53, true),
    ('a1a1a1a1-0000-0000-0000-000000000009', '/audit', '审计日志', 'AuditOutlined', 'audit.read', NULL, 60, true)
ON CONFLICT DO NOTHING;

COMMIT;
