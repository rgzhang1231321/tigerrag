-- 016_document_mime_type_widen.sql
-- document_record.MimeType 原 VARCHAR(64) 无法容纳 "application/vnd.openxmlformats-officedocument.wordprocessingml.document"（66 字符），
-- 导致 .docx 上传时报 22001 value too long。放宽到 VARCHAR(128)。
ALTER TABLE document_record
    ALTER COLUMN "MimeType" TYPE VARCHAR(128);
