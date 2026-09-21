import { useQuery } from '@tanstack/react-query'
import { listAuditLogs, type AuditQueryRequest } from './auditApi'

export function queryAuditLogsKeys() {
  return ['audit-logs'] as const
}

/// <summary>查询审计日志列表。</summary>
export function useAuditLogs(request: AuditQueryRequest) {
  return useQuery({
    queryKey: [
      ...queryAuditLogsKeys(),
      request.from,
      request.to,
      request.actorId,
      request.action,
      request.keyword,
      request.page,
      request.pageSize,
    ],
    queryFn: () => listAuditLogs(request),
    staleTime: 30_000,
  })
}
