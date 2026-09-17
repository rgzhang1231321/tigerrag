import { QueryClient } from '@tanstack/react-query'
import { ApiError } from './http'

/// <summary>
/// 构造全局 QueryClient：staleTime 30 秒；401/403 不重试，避免对未授权端点反复请求。
/// 只在根组合点实例化一次，避免在不同子树之间共享过期缓存。
/// </summary>
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        gcTime: 5 * 60_000,
        // 401/403 与无 httpStatus 的业务错误一律不重试；其它网络错误最多重试 1 次。
        retry: (failureCount, error) => {
          if (error instanceof ApiError) {
            if (error.code === 40100 || error.code === 40300) {
              return false
            }
            return error.httpStatus === undefined && failureCount < 1
          }
          return failureCount < 1
        },
        refetchOnWindowFocus: false,
      },
      mutations: {
        retry: false,
      },
    },
  })
}