-- TigerRAG PostgreSQL 增量脚本：新建 api_log 表，用于集中存放应用运行日志。
-- 仅记录 Warning/Error/Critical 级别；条目由 TigerRagSqlLoggerProvider 异步批量写入。
-- 上线要求：生产尚未启用。

BEGIN;

CREATE TABLE IF NOT EXISTS api_log (
    id              BIGSERIAL PRIMARY KEY,
    "timestamp"     TIMESTAMPTZ NOT NULL,
    level           VARCHAR(16) NOT NULL,
    request_id      VARCHAR(64) NOT NULL,
    source_context  VARCHAR(500),
    request_path    VARCHAR(500),
    message         TEXT NOT NULL,
    exception       TEXT,
    elapsed_ms      INT
);

CREATE INDEX IF NOT EXISTS api_log_request_id_idx ON api_log (request_id);
CREATE INDEX IF NOT EXISTS api_log_timestamp_idx ON api_log ("timestamp" DESC);

COMMIT;