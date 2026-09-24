import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import { KnowledgeBasePage } from './KnowledgeBasePage'

function renderWithClient() {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0 },
      mutations: { retry: false },
    },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <KnowledgeBasePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
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

const kbs = [
  {
    id: 'kb1',
    name: '研发手册',
    description: '内部研发资料',
    ownerId: 'u1',
    ownerName: 'admin',
    documentCount: 12,
    createdAt: '2026-09-20T10:00:00Z',
  },
  {
    id: 'kb2',
    name: '客户案例',
    description: null,
    ownerId: 'u2',
    ownerName: 'alice',
    documentCount: 0,
    createdAt: '2026-09-21T11:00:00Z',
  },
]

describe('KnowledgeBasePage', () => {
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

  it('lists knowledge bases after the query resolves', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/knowledge-bases/list') return Promise.resolve(jsonResponse(envelope(kbs)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderWithClient()

    expect(await screen.findByText('研发手册')).toBeInTheDocument()
    expect(screen.getByText('客户案例')).toBeInTheDocument()
    expect(screen.getByText('内部研发资料')).toBeInTheDocument()
    expect(screen.getByText('admin')).toBeInTheDocument()
    expect(screen.getByText('alice')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '新建知识库' })).toBeInTheDocument()
  })

  it('filters rows by the search input', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/knowledge-bases/list') return Promise.resolve(jsonResponse(envelope(kbs)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderWithClient()

    await waitFor(() => {
      expect(screen.getByText('研发手册')).toBeInTheDocument()
    })

    const searchInput = screen.getByPlaceholderText('按名称过滤')
    const nativeSetter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype,
      'value',
    )?.set
    nativeSetter?.call(searchInput, '案例')
    searchInput.dispatchEvent(new Event('input', { bubbles: true }))

    await waitFor(() => {
      expect(screen.queryByText('研发手册')).not.toBeInTheDocument()
    })
    expect(screen.getByText('客户案例')).toBeInTheDocument()
  })

  it('renders the create dialog when clicking the new button', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/knowledge-bases/list') return Promise.resolve(jsonResponse(envelope(kbs)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderWithClient()
    await waitFor(() => {
      expect(screen.getByText('研发手册')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByRole('button', { name: '新建知识库' }))
    expect(await screen.findByText('新建知识库', { selector: '.ant-modal-title' })).toBeInTheDocument()
  })

  it('shows empty state when no knowledge bases exist', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/knowledge-bases/list') return Promise.resolve(jsonResponse(envelope([])))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderWithClient()

    expect(await screen.findByText('还没有知识库')).toBeInTheDocument()
    expect(screen.getByText('创建第一个知识库，开始管理文档')).toBeInTheDocument()
  })
})
