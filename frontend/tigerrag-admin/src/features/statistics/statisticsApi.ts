import { http } from '../../app/http'

/// <summary>Dashboard 聚合指标响应；字段命名与后端 DashboardMetricsResponse 对齐。</summary>
export interface DashboardMetrics {
  knowledgeBaseCount: number
  documentCount: number
  indexedDocumentCount: number
  processingDocumentCount: number
  failedDocumentCount: number
  userCount: number
  conversationCount: number
  messageCount: number
}

/// <summary>取 Dashboard 指标；走统一 http，自动注入 access token。</summary>
export async function fetchDashboardMetrics(): Promise<DashboardMetrics> {
  return http<DashboardMetrics>('/api/statistics/dashboard', { method: 'POST' })
}
