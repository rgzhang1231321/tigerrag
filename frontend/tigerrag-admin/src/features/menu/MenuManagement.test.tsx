import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import {
  buildMenuTree,
  collectDescendantIds,
  MenuManagement,
} from './MenuManagement'

function renderWithClient() {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0 },
      mutations: { retry: false },
    },
  })
  return render(
    <QueryClientProvider client={client}>
      <MenuManagement />
    </QueryClientProvider>,
  )
}

function jsonResponse(body: unknown) {
  return { ok: true, status: 200, json: async () => body, text: async () => JSON.stringify(body) } as Response
}

function envelope<T>(data: T, flag = true) {
  return {
    requestId: '',
    code: flag ? 0 : 40400,
    value: flag ? 'Success' : 'NotFound',
    flag,
    message: flag ? 'success' : 'fail',
    data,
    hasNextPage: false,
    total: 0,
  }
}

const flatMenus = [
  {
    id: 'root-1',
    key: '/system',
    label: '系统设置',
    icon: 'SettingOutlined',
    roles: [],
    parentId: null,
    sortOrder: 10,
    isEnabled: true,
  },
  {
    id: 'child-1',
    key: '/users',
    label: '用户管理',
    icon: 'TeamOutlined',
    roles: ['Admin'],
    parentId: 'root-1',
    sortOrder: 11,
    isEnabled: true,
  },
  {
    id: 'child-2',
    key: '/roles',
    label: '角色管理',
    icon: 'SafetyOutlined',
    roles: ['Admin'],
    parentId: 'root-1',
    sortOrder: 12,
    isEnabled: true,
  },
]

function mockFetchList() {
  vi.mocked(fetch).mockImplementation((async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString()
    if (url === '/api/menu-configs/list') {
      return Promise.resolve(jsonResponse(envelope(flatMenus)))
    }
    throw new Error(`Unexpected fetch: ${url}`)
  }) as never)
}

describe('MenuManagement (render)', () => {
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

  it('renders the heading without crashing', () => {
    mockFetchList()
    renderWithClient()
    expect(screen.getByRole('heading', { name: '菜单管理' })).toBeInTheDocument()
  })

  it('renders the toolbar buttons', () => {
    mockFetchList()
    renderWithClient()
    expect(screen.getByRole('button', { name: '新建菜单项' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '展开全部' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '折叠全部' })).toBeInTheDocument()
  })

  it('renders the rows including nested children', async () => {
    mockFetchList()
    renderWithClient()
    expect(await screen.findByText('系统设置')).toBeInTheDocument()
    // 用户管理 / 角色管理 在表格行里至少各出现 1 次（label cell）
    await waitFor(() => {
      expect(screen.getAllByText('用户管理').length).toBeGreaterThan(0)
      expect(screen.getAllByText('角色管理').length).toBeGreaterThan(0)
    })
    expect(screen.getByText('顶级')).toBeInTheDocument()
    expect(screen.getAllByText('子级').length).toBe(2)
  })

  it('collapses all children when the collapse-all button is clicked', async () => {
    mockFetchList()
    renderWithClient()
    // 等到子级行出现：每个子节点都有一个 Tag，断言数量到位即可。
    await waitFor(() => {
      expect(screen.getAllByText('子级').length).toBe(2)
    })

    fireEvent.click(screen.getByRole('button', { name: '折叠全部' }))

    await waitFor(() => {
      expect(screen.queryAllByText('子级')).toHaveLength(0)
    })
    expect(screen.getByText('系统设置')).toBeInTheDocument()
    expect(screen.getByText('/system')).toBeInTheDocument()
  })
})

describe('buildMenuTree', () => {
  it('assembles children under their parent and orders siblings by sortOrder', () => {
    const tree = buildMenuTree(flatMenus)
    expect(tree).toHaveLength(1)
    const root = tree[0]!
    expect(root.id).toBe('root-1')
    expect(root.children.map((c) => c.id)).toEqual(['child-1', 'child-2'])
  })

  it('treats orphan nodes (missing parent) as roots', () => {
    const orphan = [
      {
        id: 'a',
        key: '/a',
        label: 'A',
        icon: null,
        roles: [],
        parentId: 'missing',
        sortOrder: 0,
        isEnabled: true,
      },
    ]
    const tree = buildMenuTree(orphan)
    expect(tree).toHaveLength(1)
    expect(tree[0]!.id).toBe('a')
  })
})

describe('collectDescendantIds', () => {
  const tree = buildMenuTree(flatMenus)

  it('includes self and all nested children when given a parent id', () => {
    const ids = collectDescendantIds(tree, 'root-1')
    expect(ids).toEqual(new Set(['root-1', 'child-1', 'child-2']))
  })

  it('returns just self when given a leaf id', () => {
    const ids = collectDescendantIds(tree, 'child-1')
    expect(ids).toEqual(new Set(['child-1']))
  })

  it('returns just the requested id when the node is not in the tree', () => {
    const ids = collectDescendantIds(tree, 'nope')
    expect(ids).toEqual(new Set(['nope']))
  })
})
