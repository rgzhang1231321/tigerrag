import { http } from '../../app/http'

/// 日志条目（api_log 单表）：kind='message' 为消息日志（访问维度字段为 null），
/// kind='access' 为每请求访问日志（sourceContext/exception 为 null）。
export interface ApiLogEntry {
  id: number
  timestamp: string
  level: string
  requestId: string
  sourceContext: string | null
  requestPath: string
  message: string
  exception: string | null
  elapsedMs: number
  kind: string
  userName: string | null
  action: string | null
  statusCode: number | null
  requestBody: string | null
  responseBody: string | null
}

export interface ApiLogQueryResult {
  entries: ApiLogEntry[]
  total: number
}

/// 查询请求：错误日志 Tab 用 level/keyword + kind='message'；
/// 访问日志 Tab 用 userName/pathKeyword/statusCode + kind='access'。
export interface ApiLogQueryRequest {
  from?: string | null
  to?: string | null
  level?: string | null
  requestId?: string | null
  keyword?: string | null
  kind?: string | null
  userName?: string | null
  pathKeyword?: string | null
  statusCode?: number | null
  page: number
  pageSize: number
}

/// <summary>查询日志列表（消息日志与访问日志共用唯一端点，kind 判别）。</summary>
export async function listApiLogs(request: ApiLogQueryRequest): Promise<ApiLogQueryResult> {
  return http<ApiLogQueryResult>('/api/logs/list', { method: 'POST', body: request })
}
