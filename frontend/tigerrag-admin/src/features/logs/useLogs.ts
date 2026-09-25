import { useQuery } from '@tanstack/react-query'
import { listApiLogs, type ApiLogQueryRequest } from './logsApi'

export function queryLogsKeys() {
  return ['api-logs'] as const
}

/// <summary>查询日志列表：消息日志与访问日志共用同一 hook，kind 与各 Tab 筛选字段全部参与缓存键。</summary>
export function useApiLogs(request: ApiLogQueryRequest) {
  return useQuery({
    queryKey: [
      ...queryLogsKeys(),
      request.kind,
      request.requestId,
      request.keyword,
      request.level,
      request.userName,
      request.pathKeyword,
      request.statusCode,
      request.page,
      request.pageSize,
    ],
    queryFn: () => listApiLogs(request),
    staleTime: 30_000,
  })
}
