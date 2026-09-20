import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchDashboardMetrics, fetchReport } from './statisticsApi'
import { useAuthStore } from '../auth/authStore'

const session = {
  accessToken: 'access-token',
  expiresAt: '2099-01-01T00:00:00Z',
  user: { id: 'user-id', userName: 'admin', roles: ['Admin'] },
}

describe('fetchDashboardMetrics', () => {
  beforeEach(() => {
    useAuthStore.getState().clear()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => vi.unstubAllGlobals())

  it('POST /api/statistics/dashboard and unwraps data with all 11 fields', async () => {
    const payload = {
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
    vi.mocked(fetch).mockResolvedValue(jsonResponse({
      requestId: 'rid',
      code: 0,
      value: 'Success',
      flag: true,
      message: 'success',
      data: payload,
      hasNextPage: false,
      total: 0,
    }))

    useAuthStore.getState().setSession(session)

    const result = await fetchDashboardMetrics()

    expect(fetch).toHaveBeenCalledWith('/api/statistics/dashboard', expect.objectContaining({
      method: 'POST',
      credentials: 'include',
    }))
    expect(result).toEqual(payload)
    expect(result.totalTokens).toBe(1234567)
    expect(result.recentWeekDocuments).toHaveLength(2)
    expect(result.documentsByKb).toHaveLength(2)
    expect(result.messagesPerDay).toHaveLength(2)
  })
})

describe('fetchReport', () => {
  beforeEach(() => {
    useAuthStore.getState().clear()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => vi.unstubAllGlobals())

  it('POST /api/statistics/reports and unwraps documents report', async () => {
    const payload = {
      type: 1,
      data: {
        uploadTrend: [{ date: '2026-09-01', count: 5 }],
        statusBreakdown: [{ status: 'Indexed', count: 10 }],
        byKb: [{ knowledgeBaseId: 'kb-1', knowledgeBaseName: 'KB1', documentCount: 10 }],
        failures: [],
      },
    }
    vi.mocked(fetch).mockResolvedValue(jsonResponse({
      requestId: 'rid',
      code: 0,
      value: 'Success',
      flag: true,
      message: 'success',
      data: payload,
      hasNextPage: false,
      total: 0,
    }))

    useAuthStore.getState().setSession(session)

    const result = await fetchReport({
      reportType: 'documents',
      dateRange: { start: '2026-09-01', end: '2026-09-20' },
    })

    expect(fetch).toHaveBeenCalledWith('/api/statistics/reports', expect.objectContaining({
      method: 'POST',
      credentials: 'include',
      body: JSON.stringify({
        reportType: 'documents',
        dateRange: { start: '2026-09-01', end: '2026-09-20' },
      }),
    }))
    expect(result).toEqual(payload)
  })
})

function jsonResponse(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as Response
}
