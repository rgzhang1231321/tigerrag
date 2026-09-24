-- 021_api_access_log.sql
-- 新建 api_access_log 表：记录每个 /api 请求一条访问日志（用户、方法、路径、action、
-- 脱敏后的请求参数、失败响应、真实状态码、耗时、IP）。与 api_log（消息日志）分离，
-- 两表通过 request_id 关联排错。写入由 AccessLogBuffer 异步批量完成。
CREATE TABLE IF NOT EXISTS api_access_log (
    id            BIGSERIAL PRIMARY KEY,
    "timestamp"   TIMESTAMPTZ NOT NULL,
    request_id    VARCHAR(64) NOT NULL,
    user_id       UUID,
    user_name     VARCHAR(256),
    http_method   VARCHAR(8) NOT NULL,
    request_path  VARCHAR(500) NOT NULL,
    query_string  TEXT,
    action        VARCHAR(500),
    request_body  TEXT,
    response_body TEXT,
    status_code   INT NOT NULL,
    elapsed_ms    INT NOT NULL,
    ip            VARCHAR(64)
);

-- 访问日志默认查询：时间倒序分页。
CREATE INDEX IF NOT EXISTS api_access_log_timestamp_idx ON api_access_log ("timestamp" DESC);
-- 按 requestId 关联 api_log 排错。
CREATE INDEX IF NOT EXISTS api_access_log_request_id_idx ON api_access_log (request_id);
-- 按用户追溯访问轨迹。
CREATE INDEX IF NOT EXISTS api_access_log_user_id_idx ON api_access_log (user_id);
