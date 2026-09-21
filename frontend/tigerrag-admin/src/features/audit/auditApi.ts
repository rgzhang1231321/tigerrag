import { http } from '../../app/http'

export interface AuditEntry {
  id: number
  createdAt: string
  actorId: string
  actorName: string
  action: string
  targetType: string
  targetId: string
  summary: string
}

export interface AuditQueryResult {
  entries: AuditEntry[]
  total: number
}

export interface AuditQueryRequest {
  from?: string | null
  to?: string | null
  actorId?: string | null
  action?: string | null
  keyword?: string | null
  page: number
  pageSize: number
}

/// <summary>查询审计日志列表。</summary>
export async function listAuditLogs(request: AuditQueryRequest): Promise<AuditQueryResult> {
  return http<AuditQueryResult>('/api/audit-logs/list', { method: 'POST', body: request })
}
