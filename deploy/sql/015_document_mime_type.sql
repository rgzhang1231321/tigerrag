-- 015_document_mime_type.sql
-- 为 document_record 增加 MimeType 列；Worker 启动恢复扫描 Pending 文档时，
-- 重新入队前可读取 MimeType 决定走哪个解析器。
ALTER TABLE document_record
    ADD COLUMN IF NOT EXISTS "MimeType" VARCHAR(64) NULL;
