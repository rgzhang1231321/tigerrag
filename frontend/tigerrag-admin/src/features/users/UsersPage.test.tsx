import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import { UsersPage } from './UsersPage'

function renderWithClient() {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0 },
      mutations: { retry: false },
    },
  })
  return render(
    <QueryClientProvider client={client}>
      <UsersPage />
    </QueryClientProvider>,
  )
}

function jsonResponse(body: unknown) {
  return { ok: true, status: 200, json: async () => body } as Response
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

const users = [
  { id: 'u1', userName: 'admin', roles: ['Admin'], isLocked: false },
  { id: 'u2', userName: 'alice', roles: ['Editor', 'Viewer'], isLocked: false },
]

const roles = ['Admin', 'Auditor', 'Editor', 'KbManager', 'Viewer']

describe('UsersPage', () => {
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

  it('lists users with role tags after the queries finish', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/users/list') return Promise.resolve(jsonResponse(envelope(users)))
      if (url === '/api/users/roles/list') return Promise.resolve(jsonResponse(envelope(roles)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderWithClient()

    expect(await screen.findByText('admin')).toBeInTheDocument()
    expect(await screen.findByText('alice')).toBeInTheDocument()
    expect(screen.getAllByText('Admin').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Editor').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Viewer').length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: '新建用户' })).toBeInTheDocument()
  })

  it('filters rows by the search input', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/users/list') return Promise.resolve(jsonResponse(envelope(users)))
      if (url === '/api/users/roles/list') return Promise.resolve(jsonResponse(envelope(roles)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderWithClient()

    await waitFor(() => {
      expect(screen.getByText('admin')).toBeInTheDocument()
    })

    const search = screen.getByPlaceholderText('按用户名过滤')
    search.focus()
    // 用 native input setter 触发 React 的 onChange
    const nativeSetter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype,
      'value',
    )?.set
    nativeSetter?.call(search, 'ali')
    search.dispatchEvent(new Event('input', { bubbles: true }))

    await waitFor(() => {
      expect(screen.queryByText('admin')).not.toBeInTheDocument()
    })
    expect(screen.getByText('alice')).toBeInTheDocument()
  })
})