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

function jsonResponse(body: unknown): Response {
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
    isSystem: true,
    userCount: 1,
    menuCount: 0,
    menuNames: [],
  },
  {
    name: 'Auditor',
    isSystem: false,
    userCount: 2,
    menuCount: 1,
    menuNames: ['审计日志'],
  },
  {
    name: 'KbManager',
    isSystem: false,
    userCount: 0,
    menuCount: 2,
    menuNames: ['知识库', '问答工作台'],
  },
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

  it('lists roles with reference counts and associated menu names', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/list') return Promise.resolve(jsonResponse(envelope(seed)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('Admin')).toBeInTheDocument())
    expect(screen.getByText('Auditor')).toBeInTheDocument()
    expect(screen.getByText('KbManager')).toBeInTheDocument()
    // 用户引用数
    expect(screen.getByText(/用户 2/)).toBeInTheDocument()
    expect(screen.getByText(/菜单 1/)).toBeInTheDocument()
    // 关联菜单渲染为 Tag
    expect(screen.getByText('审计日志')).toBeInTheDocument()
    expect(screen.getByText('知识库')).toBeInTheDocument()
    expect(screen.getByText('问答工作台')).toBeInTheDocument()
  })

  it('opens modal for create when 新建角色 clicked and submits valid name', async () => {
    let live = seed.slice()
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const body = init?.body ? JSON.parse(String(init.body)) : null
      if (url === '/api/roles/list') {
        return Promise.resolve(jsonResponse(envelope(live)))
      }
      if (url === '/api/roles' && init?.method === 'POST') {
        live = [
          ...live,
          {
            name: body.name,
            isSystem: false,
            userCount: 0,
            menuCount: 0,
            menuNames: [],
          },
        ]
        return Promise.resolve(
          jsonResponse(
            envelope({
              name: body.name,
              isSystem: false,
              userCount: 0,
              menuCount: 0,
              menuNames: [],
            }),
          ),
        )
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('Admin')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: '新建角色' }))
    const input = await screen.findByPlaceholderText('如 CustomRole')
    fireEvent.change(input, { target: { value: 'NewRole' } })
    fireEvent.click(await screen.findByTestId('role-modal-ok'))

    await waitFor(() => expect(screen.getByText('NewRole')).toBeInTheDocument())
  })

  it('opens modal pre-filled with current name on 编辑 and posts rename', async () => {
    let live = seed.slice()
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const body = init?.body ? JSON.parse(String(init.body)) : null
      if (url === '/api/roles/list') {
        return Promise.resolve(jsonResponse(envelope(live)))
      }
      if (url.endsWith('/rename') && init?.method === 'POST') {
        live = live.map((role) =>
          role.name === 'Auditor'
            ? {
                ...role,
                name: body.name,
              }
            : role,
        )
        return Promise.resolve(
          jsonResponse(
            envelope({
              name: body.name,
              isSystem: false,
              userCount: 0,
              menuCount: 0,
              menuNames: [],
            }),
          ),
        )
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('Auditor')).toBeInTheDocument())

    const auditorRow = screen.getByText('Auditor').closest('tr')!
    fireEvent.click(auditorRow.querySelector('button')!)

    const input = await screen.findByDisplayValue('Auditor')
    fireEvent.change(input, { target: { value: 'AuditLead' } })
    fireEvent.click(await screen.findByTestId('role-modal-ok'))

    await waitFor(() => expect(screen.getByText('AuditLead')).toBeInTheDocument())
    expect(screen.queryByText('Auditor')).not.toBeInTheDocument()
  })

  it('disables edit/delete on system role but exposes delete for non-system', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/list') return Promise.resolve(jsonResponse(envelope(seed)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('Admin')).toBeInTheDocument())

    const adminRow = screen.getByText('Admin').closest('tr')!
    const adminEdit = Array.from(adminRow.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('编辑'),
    )
    expect(adminEdit?.disabled).toBe(true)

    const kbRow = screen.getByText('KbManager').closest('tr')!
    const kbEdit = Array.from(kbRow.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('编辑'),
    )
    const kbDelete = Array.from(kbRow.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('删除'),
    )
    expect(kbEdit?.disabled).toBeFalsy()
    expect(kbDelete?.disabled).toBeFalsy()
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

    fireEvent.click(screen.getByRole('button', { name: '新建角色' }))
    const input = await screen.findByPlaceholderText('如 CustomRole')
    fireEvent.change(input, { target: { value: 'NewRole' } })
    fireEvent.click(await screen.findByTestId('role-modal-ok'))

    await waitFor(() =>
      expect(screen.getAllByText('角色名必须以大写字母开头').length).toBeGreaterThan(0),
    )
  })
})