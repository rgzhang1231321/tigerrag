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
  keyword?: string | null
  page: number
  pageSize: number
}

/// <summary>查询日志列表。</summary>
export async function listApiLogs(request: ApiLogQueryRequest): Promise<ApiLogQueryResult> {
  return http<ApiLogQueryResult>('/api/logs/list', { method: 'POST', body: request })
}
