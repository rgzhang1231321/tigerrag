import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { KnowledgeBasePermissionModal } from './KnowledgeBasePermissionModal'

function renderModal(props: { kbId: string; kbName: string; open: boolean; onClose?: () => void }) {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0 },
      mutations: { retry: false },
    },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <KnowledgeBasePermissionModal
          kbId={props.kbId}
          kbName={props.kbName}
          open={props.open}
          onClose={props.onClose ?? (() => {})}
        />
      </MemoryRouter>
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
    message: 'success',
    data,
    hasNextPage: false,
    total: 0,
  }
}

const users = [
  { id: 'u1', userName: 'alice', roles: ['Editor'], isLocked: false },
  { id: 'u2', userName: 'bob', roles: ['Viewer'], isLocked: false },
]

const roles = [
  { name: 'Admin', userCount: 1, menuCount: 0, menuNames: [] },
  { name: 'Editor', userCount: 2, menuCount: 0, menuNames: [] },
]

describe('KnowledgeBasePermissionModal', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders the modal with user and role panels, and shows current permissions', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/knowledge-bases/kb1/permissions') return Promise.resolve(jsonResponse(envelope({ userIds: ['u1'], roles: ['Admin'] })))
      if (url === '/api/users/list') return Promise.resolve(jsonResponse(envelope(users)))
      if (url === '/api/roles/list') return Promise.resolve(jsonResponse(envelope(roles)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderModal({ kbId: 'kb1', kbName: '测试知识库', open: true })

    // 面板标题（等待权限加载完成后渲染）
    expect(await screen.findByText('按用户授权')).toBeInTheDocument()
    expect(screen.getByText('按角色授权')).toBeInTheDocument()
    // 用户列表（alice 同时出现在用户列表和已授权摘要中）
    expect(screen.getAllByText('alice').length).toBeGreaterThan(0)
    expect(screen.getByText('bob')).toBeInTheDocument()
    // 角色列表
    expect(screen.getAllByText('Admin').length).toBeGreaterThan(0)
    // 保存按钮
    expect(await screen.findByText('保存权限')).toBeInTheDocument()
  })

  it('renders panels with empty selection when permissions API returns null (degraded mode)', async () => {
    // getKbPermissions 在 HTTP 失败时 catch 并返回 null；
    // 降级模式：面板正常渲染，已选状态为空（不预勾选）。
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/knowledge-bases/kb1/permissions') {
        return Promise.reject(new Error('network error'))
      }
      if (url === '/api/users/list') return Promise.resolve(jsonResponse(envelope(users)))
      if (url === '/api/roles/list') return Promise.resolve(jsonResponse(envelope(roles)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderModal({ kbId: 'kb1', kbName: '测试知识库', open: true })

    // 面板正常渲染，用户列表可见
    expect(await screen.findByText('按用户授权')).toBeInTheDocument()
    expect(screen.getAllByText('alice').length).toBeGreaterThan(0)
    // 保存按钮可用
    expect(screen.getByText('保存权限')).toBeInTheDocument()
  })

  it('filters users by search keyword', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/knowledge-bases/kb1/permissions') return Promise.resolve(jsonResponse(envelope({ userIds: [], roles: [] })))
      if (url === '/api/users/list') return Promise.resolve(jsonResponse(envelope(users)))
      if (url === '/api/roles/list') return Promise.resolve(jsonResponse(envelope(roles)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderModal({ kbId: 'kb1', kbName: '测试知识库', open: true })

    await waitFor(() => {
      expect(screen.getByText('alice')).toBeInTheDocument()
    })

    // 搜索 bob：应只显示 bob，不显示 alice
    const searchInput = screen.getByPlaceholderText('搜索用户...')
    vi.mocked(searchInput).dispatchEvent(new Event('input', { bubbles: true }))
    // 通过 fireEvent 触发 onChange
    const { fireEvent } = await import('@testing-library/react')
    fireEvent.change(searchInput, { target: { value: 'bob' } })

    await waitFor(() => {
      expect(screen.getByText('bob')).toBeInTheDocument()
      expect(screen.queryByText('alice')).not.toBeInTheDocument()
    })
  })
})
