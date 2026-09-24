-- 019_document_mime_type_widen_256.sql
-- 扩展上传支持：CSV/HTML/DOC/PPTX/XLSX/XLS/EML/MSG/图片/OpenDocument 等。
-- 当前 VARCHAR(128) 可容纳所有新 MIME，但为长期安全加宽到 VARCHAR(256)。
ALTER TABLE document_record
    ALTER COLUMN "MimeType" TYPE VARCHAR(256);
