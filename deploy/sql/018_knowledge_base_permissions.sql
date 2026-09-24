-- KB 级 ACL 表，镜像 document_permission_record。
-- PK = (KnowledgeBaseId, PrincipalType, PrincipalId)。
-- PrincipalType: User | Role。
-- 禁止外键：级联删除由业务代码在事务内完成。

CREATE TABLE IF NOT EXISTS "knowledge_base_permission_record" (
    "KnowledgeBaseId" uuid NOT NULL,
    "PrincipalType" character varying(16) NOT NULL,
    "PrincipalId" uuid NOT NULL,
    CONSTRAINT "PK_knowledge_base_permission_record"
        PRIMARY KEY ("KnowledgeBaseId", "PrincipalType", "PrincipalId")
);

CREATE INDEX IF NOT EXISTS "IX_knowledge_base_permission_record_PrincipalType_PrincipalId"
    ON "knowledge_base_permission_record" ("PrincipalType", "PrincipalId");
