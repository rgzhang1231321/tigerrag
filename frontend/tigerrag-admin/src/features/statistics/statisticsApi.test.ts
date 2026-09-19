import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchDashboardMetrics } from './statisticsApi'
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

  it('POST /api/statistics/dashboard and unwraps data', async () => {
    const payload = {
      knowledgeBaseCount: 3,
      documentCount: 10,
      indexedDocumentCount: 8,
      processingDocumentCount: 1,
      failedDocumentCount: 1,
      userCount: 5,
      conversationCount: 20,
      messageCount: 80,
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
  })
})

function jsonResponse(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    json: async () => body,
  } as Response
}
