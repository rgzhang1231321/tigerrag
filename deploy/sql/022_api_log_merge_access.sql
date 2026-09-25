-- 022_api_log_merge_access.sql
-- 访问日志并入 api_log 单表：新增访问维度列（kind 判别 + 用户/action/状态码/请求响应体），
-- 复用现有 request_path（存 "METHOD /path?query"）与 elapsed_ms 列。若存在 021 建的
-- api_access_log，则将存量数据迁入 api_log 后删表；并清理已下线端点的授权残留。
ALTER TABLE api_log ADD COLUMN IF NOT EXISTS kind          VARCHAR(16) NOT NULL DEFAULT 'message';
ALTER TABLE api_log ADD COLUMN IF NOT EXISTS user_name     VARCHAR(256);
ALTER TABLE api_log ADD COLUMN IF NOT EXISTS action        VARCHAR(500);
ALTER TABLE api_log ADD COLUMN IF NOT EXISTS status_code   INT;
ALTER TABLE api_log ADD COLUMN IF NOT EXISTS request_body  TEXT;
ALTER TABLE api_log ADD COLUMN IF NOT EXISTS response_body TEXT;
CREATE INDEX IF NOT EXISTS api_log_kind_timestamp_idx ON api_log (kind, "timestamp" DESC);
DO $$
BEGIN
    -- 021 未在多数环境执行过；对执行过的环境按列映射迁入（user_id/ip 按决策丢弃）。
    -- request_path 拼接 method+path+query 后可能超过 500，LEFT 截断保证不越界。
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'api_access_log') THEN
        INSERT INTO api_log ("timestamp", level, request_id, source_context, request_path, message, exception, elapsed_ms, kind, user_name, action, status_code, request_body, response_body)
        SELECT a."timestamp",
               'Information',
               a.request_id,
               NULL,
               LEFT(a.http_method || ' ' || a.request_path || COALESCE(a.query_string, ''), 500),
               a.http_method || ' ' || a.request_path || COALESCE(a.query_string, '') || ' ' || a.status_code || ' ' || a.elapsed_ms || 'ms',
               NULL,
               a.elapsed_ms,
               'access',
               a.user_name,
               a.action,
               a.status_code,
               a.request_body,
               a.response_body
        FROM api_access_log a;
        DROP TABLE api_access_log;
    END IF;
END $$;
-- access-list 端点已下线（访问日志改走 /api/logs/list），清理 Admin 引导灌入的授权残留。
DELETE FROM "role_endpoint_grant" WHERE "EndpointKey" = 'apiLogs.accessList';
