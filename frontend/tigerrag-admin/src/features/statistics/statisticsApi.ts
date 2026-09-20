import { http } from '../../app/http'

export interface DailyCount {
  Date: string
  Count: number
}

export interface KbDocumentCount {
  KnowledgeBaseId: string
  KnowledgeBaseName: string
  DocumentCount: number
}

/// <summary>Dashboard 聚合指标响应；字段命名与后端 DashboardMetricsResponse 对齐。</summary>
export interface DashboardMetrics {
  KnowledgeBaseCount: number
  DocumentCount: number
  IndexedDocumentCount: number
  ProcessingDocumentCount: number
  FailedDocumentCount: number
  UserCount: number
  ConversationCount: number
  MessageCount: number
  TotalTokens: number
  RecentWeekDocuments: DailyCount[]
  DocumentsByKb: KbDocumentCount[]
  MessagesPerDay: DailyCount[]
}

/// <summary>取 Dashboard 指标；走统一 http，自动注入 access token。</summary>
export async function fetchDashboardMetrics(): Promise<DashboardMetrics> {
  return http<DashboardMetrics>('/api/statistics/dashboard', { method: 'POST' })
}

// ── 报表类型 ──────────────────────────────────────────────

export type ReportType = 'documents' | 'users' | 'conversations' | 'system'

export interface DateRange {
  start: string
  end: string
}

export interface ReportRequest {
  reportType: ReportType
  dateRange: DateRange
}

export interface StatusCount {
  Status: string
  Count: number
}

export interface FailureItem {
  DocumentId: string
  DocumentTitle: string
  Reason: string
  FailedAt: string
}

export interface RoleCount {
  Role: string
  Count: number
}

export interface DocumentsReportData {
  UploadTrend: DailyCount[]
  StatusBreakdown: StatusCount[]
  ByKb: KbDocumentCount[]
  Failures: FailureItem[]
}

export interface UsersReportData {
  NewUserTrend: DailyCount[]
  ActiveUserTrend: DailyCount[]
  RoleDistribution: RoleCount[]
}

export interface ConversationsReportData {
  ConversationTrend: DailyCount[]
  MessageTrend: DailyCount[]
  AvgMessagesPerConversation: number
  TokenTrend: DailyCount[]
}

export interface SystemReportData {
  IndexingSuccessRate: number
  FailureRate: number
  ApiCallTrend: DailyCount[]
  AvgProcessTimeSeconds: number
}

export interface ReportDataWrapper {
  type: number
  data:
    | DocumentsReportData
    | UsersReportData
    | ConversationsReportData
    | SystemReportData
}

const REPORT_TYPE_MAP: Record<ReportType, number> = {
  documents: 1,
  users: 2,
  conversations: 3,
  system: 4,
}

function toApiRequest(request: ReportRequest) {
  return { reportType: REPORT_TYPE_MAP[request.reportType], dateRange: request.dateRange }
}

/// <summary>获取报表数据。</summary>
export async function fetchReport(request: ReportRequest): Promise<ReportDataWrapper> {
  return http<ReportDataWrapper>('/api/statistics/reports', {
    method: 'POST',
    body: toApiRequest(request),
  })
}

/// <summary>导出报表为 CSV 文件。</summary>
export async function exportReport(request: ReportRequest): Promise<Blob> {
  const apiBody = toApiRequest(request)
  const response = await fetch('/api/statistics/reports/export', {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${useAuthStore.getState().accessToken ?? ''}`,
    },
    body: JSON.stringify(apiBody),
  })
  if (!response.ok) {
    throw new Error(`导出失败：HTTP ${response.status}`)
  }
  return response.blob()
}

// 延迟导入避免循环依赖
import { useAuthStore } from '../auth/authStore'
