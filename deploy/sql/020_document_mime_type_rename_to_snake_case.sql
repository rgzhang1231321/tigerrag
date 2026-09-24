-- 020_document_mime_type_rename_to_snake_case.sql
-- 修复 42703 错误：历史脚本 015 使用了带引号的 "MimeType" 创建列，
-- 导致列名存储为大小写敏感的 MimeType，而 EF Core 未配置 HasColumnName，
-- 生成 SQL 时使用未加引号的 MimeType 并被 PostgreSQL 折叠为小写 mimeType，
-- 找不到列。此处重命名为小写的 mime_type 与项目数据库命名约定一致。
ALTER TABLE document_record
    RENAME COLUMN "MimeType" TO mime_type;
