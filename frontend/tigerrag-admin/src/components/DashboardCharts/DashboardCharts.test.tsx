import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { DashboardMetrics } from '../../features/statistics/statisticsApi'
import { DashboardCharts } from './DashboardCharts'

const fullData: DashboardMetrics = {
  knowledgeBaseCount: 3,
  documentCount: 10,
  indexedDocumentCount: 8,
  processingDocumentCount: 1,
  failedDocumentCount: 1,
  userCount: 5,
  conversationCount: 20,
  messageCount: 80,
  totalTokens: 1234567,
  recentWeekDocuments: [
    { date: '2026-09-13', count: 2 },
    { date: '2026-09-14', count: 5 },
    { date: '2026-09-15', count: 8 },
  ],
  documentsByKb: [
    { knowledgeBaseId: 'kb-1', knowledgeBaseName: '产品文档', documentCount: 6 },
    { knowledgeBaseId: 'kb-2', knowledgeBaseName: '技术手册', documentCount: 4 },
  ],
  messagesPerDay: [
    { date: '2026-09-13', count: 12 },
    { date: '2026-09-14', count: 25 },
  ],
}

describe('DashboardCharts', () => {
  it('renders section heading', () => {
    render(<DashboardCharts data={fullData} />)
    expect(screen.getByText('数据趋势')).toBeInTheDocument()
  })

  it('renders empty state when no trend data', () => {
    render(<DashboardCharts data={{ ...fullData, recentWeekDocuments: [], documentsByKb: [] }} />)
    expect(screen.getByText('暂无趋势数据')).toBeInTheDocument()
  })
})
