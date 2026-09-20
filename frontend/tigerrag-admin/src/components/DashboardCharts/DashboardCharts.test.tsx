import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { DashboardMetrics } from '../../features/statistics/statisticsApi'
import { DashboardCharts } from './DashboardCharts'

const fullData: DashboardMetrics = {
  KnowledgeBaseCount: 3,
  DocumentCount: 10,
  IndexedDocumentCount: 8,
  ProcessingDocumentCount: 1,
  FailedDocumentCount: 1,
  UserCount: 5,
  ConversationCount: 20,
  MessageCount: 80,
  TotalTokens: 1234567,
  RecentWeekDocuments: [
    { Date: '2026-09-13', Count: 2 },
    { Date: '2026-09-14', Count: 5 },
    { Date: '2026-09-15', Count: 8 },
  ],
  DocumentsByKb: [
    { KnowledgeBaseId: 'kb-1', KnowledgeBaseName: '产品文档', DocumentCount: 6 },
    { KnowledgeBaseId: 'kb-2', KnowledgeBaseName: '技术手册', DocumentCount: 4 },
  ],
  MessagesPerDay: [
    { Date: '2026-09-13', Count: 12 },
    { Date: '2026-09-14', Count: 25 },
  ],
}

describe('DashboardCharts', () => {
  it('renders section heading', () => {
    render(<DashboardCharts data={fullData} />)
    expect(screen.getByText('数据趋势')).toBeInTheDocument()
  })

  it('renders empty state when no trend data', () => {
    render(<DashboardCharts data={{ ...fullData, RecentWeekDocuments: [], DocumentsByKb: [] }} />)
    expect(screen.getByText('暂无趋势数据')).toBeInTheDocument()
  })
})
