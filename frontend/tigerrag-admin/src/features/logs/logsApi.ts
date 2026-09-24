import { http } from '../../app/http'

export interface ApiLogEntry {
  id: number
  timestamp: string
  level: string
  requestId: string
  sourceContext: string
  requestPath: string
  message: string
  exception: string | null
  elapsedMs: number
}

export interface ApiLogQueryResult {
  entries: ApiLogEntry[]
  total: number
}

export interface ApiLogQueryRequest {
  from?: string | null
  to?: string | null
  level?: string | null
  requestId?: string | null
  keyword?: string | null
  page: number
  pageSize: number
}

/// <summary>查询日志列表。</summary>
export async function listApiLogs(request: ApiLogQueryRequest): Promise<ApiLogQueryResult> {
  return http<ApiLogQueryResult>('/api/logs/list', { method: 'POST', body: request })
}

export interface AccessLogEntry {
  id: number
  timestamp: string
  requestId: string
  userId: string | null
  userName: string | null
  httpMethod: string
  requestPath: string
  queryString: string | null
  action: string | null
  requestBody: string | null
  responseBody: string | null
  statusCode: number
  elapsedMs: number
  ip: string | null
}

export interface AccessLogQueryResult {
  entries: AccessLogEntry[]
  total: number
}

export interface AccessLogQueryRequest {
  from?: string | null
  to?: string | null
  userName?: string | null
  pathKeyword?: string | null
  statusCode?: number | null
  requestId?: string | null
  page: number
  pageSize: number
}

/// <summary>查询访问日志列表。</summary>
export async function listAccessLogs(request: AccessLogQueryRequest): Promise<AccessLogQueryResult> {
  return http<AccessLogQueryResult>('/api/logs/access-list', { method: 'POST', body: request })
}
