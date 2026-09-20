import { useQuery } from '@tanstack/react-query'
import { fetchDashboardMetrics, fetchReport } from './statisticsApi'
import type { ReportRequest } from './statisticsApi'

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

export function queryReportKeys(request: ReportRequest) {
  return ['statistics', 'report', request.reportType, request.dateRange.start, request.dateRange.end] as const
}

/// <summary>报表数据 React Query hook；5s 缓存，按报表类型和日期范围缓存。</summary>
export function useReport(request: ReportRequest) {
  return useQuery({
    queryKey: queryReportKeys(request),
    queryFn: () => fetchReport(request),
    staleTime: 5_000,
  })
}
