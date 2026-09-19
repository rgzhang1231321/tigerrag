import { useQuery } from '@tanstack/react-query'
import { fetchDashboardMetrics } from './statisticsApi'

export function queryDashboardMetricsKeys() {
  return ['statistics', 'dashboard'] as const
}

/// <summary>Dashboard 指标 React Query hook；30s 缓存避免每次进入首页都打接口。</summary>
export function useDashboardMetrics() {
  return useQuery({
    queryKey: queryDashboardMetricsKeys(),
    queryFn: fetchDashboardMetrics,
    staleTime: 30_000,
  })
}
