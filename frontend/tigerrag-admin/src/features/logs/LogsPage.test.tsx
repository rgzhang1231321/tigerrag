import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import { LogsPage } from './LogsPage'

function wrap(client: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
}

function makeClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0 },
      mutations: { retry: false },
    },
  })
}

function jsonResponse(body: unknown) {
  const text = JSON.stringify(body)
  return {
    ok: true,
    status: 200,
    text: async () => text,
    json: async () => body,
  } as Response
}

function envelope<T>(data: T, flag = true, message = 'success') {
  return {
    requestId: '',
    code: flag ? 0 : 40400,
    value: flag ? 'Success' : 'NotFound',
    flag,
    message,
    data,
    hasNextPage: false,
    total: 0,
  }
}

const seedEntries = [
  {
    id: 1,
    timestamp: '2026-09-01T12:00:00Z',
    level: 'Error',
    requestId: 'req-1',
    sourceContext: 'Test',
    requestPath: 'GET /api/test',
    message: 'Something failed',
    exception: 'System.Exception: timeout',
    elapsedMs: 150,
  },
  {
    id: 2,
    timestamp: '2026-09-01T11:00:00Z',
    level: 'Warning',
    requestId: 'req-2',
    sourceContext: 'Test',
    requestPath: 'POST /api/users',
    message: 'Retry attempt',
    exception: null,
    elapsedMs: 50,
  },
]

describe('LogsPage', () => {
  beforeEach(() => {
    useAuthStore.getState().setSession({
      accessToken: 'token',
      expiresAt: '2026-09-17T00:00:00Z',
      user: { id: 'u1', userName: 'admin', roles: ['Admin'] },
    })
    vi.stubGlobal('fetch', vi.fn())
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    useAuthStore.getState().clear()
  })

  it('lists log entries with level and request path', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/logs/list') {
        return Promise.resolve(
          jsonResponse(
            envelope({
              entries: seedEntries,
              total: 2,
            }),
          ),
        )
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<LogsPage />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('req-1')).toBeInTheDocument())
    expect(screen.getByText('req-2')).toBeInTheDocument()
    expect(screen.getByText('GET /api/test')).toBeInTheDocument()
    expect(screen.getByText('POST /api/users')).toBeInTheDocument()
  })

  it('filters by keyword', async () => {
    let capturedBody: Record<string, unknown> | null = null
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/logs/list') {
        capturedBody = init?.body ? JSON.parse(String(init.body)) : null
        return Promise.resolve(
          jsonResponse(
            envelope({
              entries: [seedEntries[0]],
              total: 1,
            }),
          ),
        )
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<LogsPage />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('req-1')).toBeInTheDocument())

    fireEvent.change(screen.getByPlaceholderText('按关键词过滤'), {
      target: { value: 'timeout' },
    })
    fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }))

    await waitFor(() => {
      expect(capturedBody).toMatchObject({ keyword: 'timeout' })
    })
  })

  it('shows empty state when no logs', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/logs/list') {
        return Promise.resolve(
          jsonResponse(
            envelope({
              entries: [],
              total: 0,
            }),
          ),
        )
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<LogsPage />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('暂无日志')).toBeInTheDocument())
  })
})
