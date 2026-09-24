import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import { DocumentPage } from './DocumentPage'

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
        <DocumentPage />
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
    documentCount: 2,
    createdAt: '2026-09-20T10:00:00Z',
  },
]

const docsPage = {
  items: [
    {
      id: 'd1',
      kbId: 'kb1',
      fileName: 'spec.md',
      status: 'Indexed',
      chunkCount: 14,
      fileSize: 102400,
      failureReason: null,
      createdBy: 'u1',
      createdAt: '2026-09-20T10:00:00Z',
      updatedAt: '2026-09-20T10:05:00Z',
    },
    {
      id: 'd2',
      kbId: 'kb1',
      fileName: 'design.pdf',
      status: 'Failed',
      chunkCount: 0,
      fileSize: 2048000,
      failureReason: 'parser timeout',
      createdBy: 'u1',
      createdAt: '2026-09-21T10:00:00Z',
      updatedAt: '2026-09-21T10:05:00Z',
    },
  ],
  page: 1,
  pageSize: 100,
  total: 2,
}

describe('DocumentPage', () => {
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

  it('renders the KB header and lists documents of the selected KB', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/knowledge-bases/list') return Promise.resolve(jsonResponse(envelope(kbs)))
      if (url === '/api/documents/list') return Promise.resolve(jsonResponse(envelope(docsPage)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderWithClient()

    expect(await screen.findByText('研发手册', { selector: '.detail-header-title' })).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.getByText('spec.md')).toBeInTheDocument()
    })
    expect(screen.getByText('design.pdf')).toBeInTheDocument()
    expect(screen.getByText('parser timeout')).toBeInTheDocument()
    expect(screen.getByText('14')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /上传文档/ })).toBeInTheDocument()
  })

  it('shows metric cards with correct counts', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/knowledge-bases/list') return Promise.resolve(jsonResponse(envelope(kbs)))
      if (url === '/api/documents/list') return Promise.resolve(jsonResponse(envelope(docsPage)))
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    renderWithClient()

    await waitFor(() => {
      expect(screen.getByText('总文档')).toBeInTheDocument()
    })
    expect(screen.getByText('已索引')).toBeInTheDocument()
    expect(screen.getByText('处理中')).toBeInTheDocument()
    expect(screen.getByText('失败')).toBeInTheDocument()
  })
})
