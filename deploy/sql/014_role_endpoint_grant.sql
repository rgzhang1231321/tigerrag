-- TigerRAG 角色-Endpoint 授权表。
-- 记录某个角色被授予了哪个 endpoint 的访问权。PK = (RoleName, EndpointKey)。
-- MenuKey 冗余存但建索引，供角色授权 UI 按菜单分组展示。
-- 无外键：RoleName 与 AspNetRoles.Name 的引用由业务代码维护（RoleAdminService 已校验存在性）。

CREATE TABLE IF NOT EXISTS "role_endpoint_grant" (
    "RoleName"    text        NOT NULL,
    "MenuKey"     text        NOT NULL,
    "EndpointKey" text        NOT NULL,
    "GrantedAt"   timestamp with time zone NOT NULL DEFAULT now(),
    "GrantedBy"   uuid        NOT NULL,
    CONSTRAINT "PK_role_endpoint_grant" PRIMARY KEY ("RoleName", "EndpointKey")
);

CREATE INDEX IF NOT EXISTS "IX_role_endpoint_grant_MenuKey"
    ON "role_endpoint_grant" ("MenuKey");

CREATE INDEX IF NOT EXISTS "IX_role_endpoint_grant_EndpointKey"
    ON "role_endpoint_grant" ("EndpointKey");