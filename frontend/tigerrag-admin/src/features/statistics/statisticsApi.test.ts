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
    expect(result.TotalTokens).toBe(1234567)
    expect(result.RecentWeekDocuments).toHaveLength(2)
    expect(result.DocumentsByKb).toHaveLength(2)
    expect(result.MessagesPerDay).toHaveLength(2)
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
        UploadTrend: [{ Date: '2026-09-01', Count: 5 }],
        StatusBreakdown: [{ Status: 'Indexed', Count: 10 }],
        ByKb: [{ KnowledgeBaseId: 'kb-1', KnowledgeBaseName: 'KB1', DocumentCount: 10 }],
        Failures: [],
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
