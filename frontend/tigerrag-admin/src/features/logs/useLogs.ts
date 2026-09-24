import { useQuery } from '@tanstack/react-query'
import { listAccessLogs, listApiLogs, type AccessLogQueryRequest, type ApiLogQueryRequest } from './logsApi'

export function queryLogsKeys() {
  return ['api-logs'] as const
}

/// <summary>查询日志列表。</summary>
export function useApiLogs(request: ApiLogQueryRequest) {
  return useQuery({
    queryKey: [...queryLogsKeys(), request.requestId, request.keyword, request.level, request.page, request.pageSize],
    queryFn: () => listApiLogs(request),
    staleTime: 30_000,
  })
}

export function queryAccessLogsKeys() {
  return ['api-access-logs'] as const
}

/// <summary>查询访问日志列表。</summary>
export function useAccessLogs(request: AccessLogQueryRequest) {
  return useQuery({
    queryKey: [
      ...queryAccessLogsKeys(),
      request.userName,
      request.pathKeyword,
      request.statusCode,
      request.requestId,
      request.page,
      request.pageSize,
    ],
    queryFn: () => listAccessLogs(request),
    staleTime: 30_000,
  })
}
