import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import { RoleListCard } from './RoleListCard'

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

const seed = [
  { name: 'Admin', isSystem: true },
  { name: 'Auditor', isSystem: true },
  { name: 'CustomRole', isSystem: false },
]

describe('RoleListCard', () => {
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

  it('lists all roles with a 系统 tag on reserved ones', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/list') return Promise.resolve(jsonResponse(envelope(seed)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('Admin')).toBeInTheDocument())
    expect(screen.getByText('Auditor')).toBeInTheDocument()
    expect(screen.getByText('CustomRole')).toBeInTheDocument()
    expect(screen.getAllByText('系统').length).toBeGreaterThanOrEqual(2)
  })

  it('creates a custom role when the user submits a valid name', async () => {
    let live = seed.slice()
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const body = init?.body ? JSON.parse(String(init.body)) : null
      if (url === '/api/roles/list') {
        return Promise.resolve(jsonResponse(envelope(live)))
      }
      if (url === '/api/roles' && init?.method === 'POST') {
        live = [...live, { name: body.name, isSystem: false }]
        return Promise.resolve(jsonResponse(envelope({ name: body.name, isSystem: false })))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('Admin')).toBeInTheDocument())

    const input = screen.getByPlaceholderText('如 CustomRole')
    fireEvent.change(input, { target: { value: 'NewRole' } })
    fireEvent.click(screen.getByRole('button', { name: '新建角色' }))

    await waitFor(() => expect(screen.getByText('NewRole')).toBeInTheDocument())
  })

  it('disables delete on system roles and wires Popconfirm for custom ones', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/list') return Promise.resolve(jsonResponse(envelope(seed)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('Admin')).toBeInTheDocument())

    const adminRow = screen.getByText('Admin').closest('tr')
    expect(adminRow).not.toBeNull()
    const adminDelete = adminRow!.querySelector('button[disabled]')
    expect(adminDelete).not.toBeNull()

    const customRow = screen.getByText('CustomRole').closest('tr')
    expect(customRow).not.toBeNull()
    const customDelete = customRow!.querySelector('button:not([disabled])')
    expect(customDelete).not.toBeNull()
  })

  it('surfaces server validation errors as a message', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/list') {
        return Promise.resolve(jsonResponse(envelope(seed)))
      }
      if (url === '/api/roles' && init?.method === 'POST') {
        return Promise.resolve(
          jsonResponse(envelope(null, false, '角色名必须以大写字母开头')),
        )
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('Admin')).toBeInTheDocument())

    fireEvent.change(screen.getByPlaceholderText('如 CustomRole'), {
      target: { value: 'bad-role' },
    })
    fireEvent.click(screen.getByRole('button', { name: '新建角色' }))

    await waitFor(() =>
      expect(screen.getByText('角色名必须以大写字母开头')).toBeInTheDocument(),
    )
  })
})