-- TigerRAG 通用操作审计表。
-- 记录谁、何时、对什么资源、做了什么操作。无外键，ActorId 存 Guid。
-- 与业务操作同事务同步写入，保证审计与业务状态一致。

CREATE TABLE IF NOT EXISTS "operation_audit_record" (
    "Id" bigserial NOT NULL,
    "ActorId" uuid NOT NULL,
    "ActorName" character varying(64) NOT NULL,
    "Action" character varying(64) NOT NULL,
    "TargetType" character varying(64) NOT NULL,
    "TargetId" character varying(128) NOT NULL,
    "Summary" character varying(500) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_operation_audit_record" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_operation_audit_record_CreatedAt"
    ON "operation_audit_record" ("CreatedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_operation_audit_record_ActorId"
    ON "operation_audit_record" ("ActorId");
CREATE INDEX IF NOT EXISTS "IX_operation_audit_record_Action"
    ON "operation_audit_record" ("Action");
