import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import { useCreateRole, useDeleteRole, useRoles, useUpdateRole } from './useRoles'

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
  return { ok: true, status: 200, json: async () => body, text: async () => JSON.stringify(body) } as Response
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
  {
    name: 'Admin',
    userCount: 1,
    menuCount: 0,
    menuNames: [],
  },
  {
    name: 'CustomRole',
    userCount: 0,
    menuCount: 0,
    menuNames: [],
  },
]

describe('useRoles', () => {
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

  it('shares the result and the cache key across two hook instances', async () => {
    let rolesCallCount = 0
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/list') {
        rolesCallCount++
        return Promise.resolve(jsonResponse(envelope(seed)))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    const client = makeClient()
    const first = renderHook(() => useRoles(), { wrapper: wrap(client) })
    await waitFor(() => expect(first.result.current.data).toEqual(seed))
    expect(rolesCallCount).toBeGreaterThanOrEqual(1)

    const second = renderHook(() => useRoles(), { wrapper: wrap(client) })
    await waitFor(() => expect(second.result.current.data).toEqual(seed))
    expect(second.result.current.data?.map((r) => r.name)).toEqual(['Admin', 'CustomRole'])
    expect(first.result.current.data).toBe(second.result.current.data)
  })

  it('useCreateRole invalidates the roles cache after mutation', async () => {
    let rolesCallCount = 0
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const body = init?.body ? JSON.parse(String(init.body)) : null
      if (url === '/api/roles/list') {
        rolesCallCount++
        return Promise.resolve(jsonResponse(envelope(seed)))
      }
      if (url === '/api/roles' && init?.method === 'POST') {
        return Promise.resolve(
          jsonResponse(
            envelope({
              name: body.name,
              userCount: 0,
              menuCount: 0,
              menuNames: [],
            }),
          ),
        )
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    const client = makeClient()
    const { result } = renderHook(
      () => ({ roles: useRoles(), create: useCreateRole() }),
      { wrapper: wrap(client) },
    )
    await waitFor(() => expect(result.current.roles.data).toEqual(seed))
    expect(rolesCallCount).toBe(1)

    await act(async () => {
      await result.current.create.mutateAsync({ name: 'NewRole' })
    })
    await waitFor(() => expect(rolesCallCount).toBeGreaterThan(1))
  })

  it('useDeleteRole invalidates the roles cache after mutation', async () => {
    let rolesCallCount = 0
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/list') {
        rolesCallCount++
        return Promise.resolve(jsonResponse(envelope(seed)))
      }
      if (url.endsWith('/delete') && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(envelope(null)))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    const client = makeClient()
    const { result } = renderHook(
      () => ({ roles: useRoles(), remove: useDeleteRole() }),
      { wrapper: wrap(client) },
    )
    await waitFor(() => expect(result.current.roles.data).toEqual(seed))
    expect(rolesCallCount).toBe(1)

    await act(async () => {
      await result.current.remove.mutateAsync('CustomRole')
    })
    await waitFor(() => expect(rolesCallCount).toBeGreaterThan(1))
  })

  it('useUpdateRole invalidates the roles cache after rename', async () => {
    let rolesCallCount = 0
    let renameSeen: { originalName: string; body: unknown } | null = null
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const body = init?.body ? JSON.parse(String(init.body)) : null
      if (url === '/api/roles/list') {
        rolesCallCount++
        return Promise.resolve(jsonResponse(envelope(seed)))
      }
      if (url.endsWith('/rename') && init?.method === 'POST') {
        renameSeen = {
          originalName: decodeURIComponent(url.split('/').slice(-2, -1)[0]!),
          body,
        }
        return Promise.resolve(
          jsonResponse(
            envelope({
              name: body.name,
              userCount: 0,
              menuCount: 0,
              menuNames: [],
            }),
          ),
        )
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    const client = makeClient()
    const { result } = renderHook(
      () => ({ roles: useRoles(), update: useUpdateRole() }),
      { wrapper: wrap(client) },
    )
    await waitFor(() => expect(result.current.roles.data).toEqual(seed))
    expect(rolesCallCount).toBe(1)

    await act(async () => {
      await result.current.update.mutateAsync({
        originalName: 'CustomRole',
        name: 'RenamedRole',
      })
    })

    expect(renameSeen).toEqual({ originalName: 'CustomRole', body: { name: 'RenamedRole' } })
    await waitFor(() => expect(rolesCallCount).toBeGreaterThan(1))
  })
})