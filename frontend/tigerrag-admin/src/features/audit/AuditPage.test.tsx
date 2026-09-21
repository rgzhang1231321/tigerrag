import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import { AuditPage } from './AuditPage'

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
    createdAt: '2026-09-01T12:00:00Z',
    actorId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
    actorName: 'admin',
    action: 'user.create',
    targetType: 'user',
    targetId: 'user-1',
    summary: '创建用户：testuser',
  },
  {
    id: 2,
    createdAt: '2026-09-01T11:00:00Z',
    actorId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
    actorName: 'admin',
    action: 'role.delete',
    targetType: 'role',
    targetId: 'old-role',
    summary: '删除角色：old-role',
  },
]

describe('AuditPage', () => {
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

  it('lists audit entries with action labels', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/audit-logs/list') {
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

    render(<AuditPage />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('创建用户：testuser')).toBeInTheDocument())
    expect(screen.getByText('删除角色：old-role')).toBeInTheDocument()
    expect(screen.getAllByText('admin').length).toBeGreaterThanOrEqual(1)
  })

  it('filters by keyword', async () => {
    let capturedBody: Record<string, unknown> | null = null
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/audit-logs/list') {
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

    render(<AuditPage />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('创建用户：testuser')).toBeInTheDocument())

    fireEvent.change(screen.getByPlaceholderText('关键词'), {
      target: { value: 'testuser' },
    })
    fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }))

    await waitFor(() => {
      expect(capturedBody).toMatchObject({ keyword: 'testuser' })
    })
  })

  it('shows empty state when no audit records', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/audit-logs/list') {
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

    render(<AuditPage />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('暂无审计记录')).toBeInTheDocument())
  })
})
