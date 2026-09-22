import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import { collectDescendantKeys, RoleListCard } from './RoleListCard'
import type { TreeNodeData } from './RoleListCard'

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
    userCount: 1,
    menuCount: 0,
    menuNames: [],
  },
  {
    name: 'Auditor',
    userCount: 2,
    menuCount: 1,
    menuNames: ['审计日志'],
  },
  {
    name: 'KbManager',
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
            userCount: 0,
            menuCount: 0,
            menuNames: [],
          },
        ]
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

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('Admin')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: '新建角色' }))
    const input = await screen.findByPlaceholderText('如 CustomRole')
    fireEvent.change(input, { target: { value: 'NewRole' } })
    fireEvent.click(await screen.findByTestId('role-modal-ok'))

    await waitFor(() => expect(screen.getByText('NewRole')).toBeInTheDocument())
  })

  it('opens modal with role name as Tag and saves menu visibility', async () => {
    let live = seed.slice()
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const body = init?.body ? JSON.parse(String(init.body)) : null
      if (url === '/api/roles/list') {
        return Promise.resolve(jsonResponse(envelope(live)))
      }
      if (url === '/api/menu-configs/list' && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(envelope([
          { id: 'm1', key: 'audit', label: '审计日志', roles: ['Auditor'], parentId: null, sortOrder: 1, isEnabled: true },
        ])))
      }
      if (url.startsWith('/api/menu-configs/') && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(envelope({ id: body?.id, roles: body?.data?.roles })))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('Auditor')).toBeInTheDocument())

    const auditorRow = screen.getByText('Auditor').closest('tr')!
    fireEvent.click(auditorRow.querySelector('button')!)

    // 编辑模式下角色名以 Tag 展示，不可直接编辑。
    const nameTags = await screen.findAllByText('Auditor')
    expect(nameTags.some((el) => el.closest('.ant-tag'))).toBe(true)

    // 直接保存（未修改菜单可见性）。
    fireEvent.click(await screen.findByTestId('role-modal-ok'))
    await waitFor(() => expect(screen.queryByText('保存')).not.toBeInTheDocument())
  })

  it('exposes edit/delete on every role including Admin', async () => {
    // 所有角色（含 Admin）平等可编辑/可删。
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
    expect(adminEdit?.disabled).toBeFalsy()

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

  it('syncMenuVisibility only modifies menus whose checked state changed', async () => {
    // 菜单：A（全员可见）、B（全员可见）、C（仅 Admin 可见）。
    // 编辑 Viewer 角色，初始可见 A、B（全员可见），C 不可见。
    // 不修改任何勾选 → 不应发起任何菜单更新请求。
    const menuConfigs = [
      { id: 'm1', key: 'A', label: '菜单A', roles: [], parentId: null, sortOrder: 1, isEnabled: true },
      { id: 'm2', key: 'B', label: '菜单B', roles: [], parentId: null, sortOrder: 2, isEnabled: true },
      { id: 'm3', key: 'C', label: '菜单C', roles: ['Admin'], parentId: null, sortOrder: 3, isEnabled: true },
    ]

    const updateCalls: Array<{ id: string; roles: string[] }> = []
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const body = init?.body ? JSON.parse(String(init.body)) : null
      if (url === '/api/roles/list') {
        return Promise.resolve(jsonResponse(envelope([
          { name: 'Admin', userCount: 1, menuCount: 1, menuNames: ['菜单C'] },
          { name: 'Viewer', userCount: 0, menuCount: 2, menuNames: ['菜单A', '菜单B'] },
        ])))
      }
      if (url === '/api/menu-configs/list' && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(envelope(menuConfigs)))
      }
      if (url.startsWith('/api/menu-configs/') && init?.method === 'POST') {
        updateCalls.push({ id: url.split('/')[2], roles: body?.data?.roles ?? body?.roles })
        return Promise.resolve(jsonResponse(envelope({ id: url.split('/')[2], roles: body?.data?.roles ?? body?.roles })))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleListCard />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('Viewer')).toBeInTheDocument())

    const viewerRow = screen.getByText('Viewer').closest('tr')!
    fireEvent.click(viewerRow.querySelector('button')!)

    // 等待编辑弹窗加载
    await screen.findByText('基本信息')

    // 直接保存（未修改任何菜单可见性）
    fireEvent.click(await screen.findByTestId('role-modal-ok'))

    // 等待保存完成
    await waitFor(() => expect(screen.queryByText('保存')).not.toBeInTheDocument())

    // 验证：没有发起任何菜单更新请求（A、B 全员可见保持不变，C 仅 Admin 保持不变）
    expect(updateCalls.length).toBe(0)
  })

  it('collectDescendantKeys returns all descendant keys of a parent node', () => {
    const tree: TreeNodeData[] = [
      { key: 'p1', title: '父菜单', children: [
        { key: 'c1', title: '子菜单1', children: [
          { key: 'g1', title: '孙菜单1' },
        ]},
        { key: 'c2', title: '子菜单2' },
      ]},
      { key: 'other', title: '其他' },
    ]

    const result = collectDescendantKeys(tree, 'p1')
    expect(result).toContain('c1')
    expect(result).toContain('c2')
    expect(result).toContain('g1')
    expect(result).not.toContain('p1')
    expect(result).not.toContain('other')
  })

  it('collectDescendantKeys returns empty for leaf node', () => {
    const tree: TreeNodeData[] = [
      { key: 'p1', title: '父菜单', children: [
        { key: 'c1', title: '子菜单1' },
      ]},
    ]

    expect(collectDescendantKeys(tree, 'c1')).toEqual([])
  })
})