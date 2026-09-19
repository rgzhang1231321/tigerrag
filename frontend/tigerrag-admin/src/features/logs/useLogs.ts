import { useQuery } from '@tanstack/react-query'
import { listApiLogs, type ApiLogQueryRequest } from './logsApi'

export function queryLogsKeys() {
  return ['api-logs'] as const
}

/// <summary>查询日志列表。</summary>
export function useApiLogs(request: ApiLogQueryRequest) {
  return useQuery({
    queryKey: [...queryLogsKeys(), request.keyword, request.level, request.page, request.pageSize],
    queryFn: () => listApiLogs(request),
    staleTime: 30_000,
  })
}
