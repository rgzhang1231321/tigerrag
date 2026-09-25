import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../auth/authStore'
import { LogsPage } from './LogsPage'

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

/// 消息行种子（kind='message'，访问维度字段全空）。
const seedEntries = [
  {
    id: 1,
    timestamp: '2026-09-01T12:00:00Z',
    level: 'Error',
    requestId: 'req-1',
    sourceContext: 'Test',
    requestPath: 'GET /api/test',
    message: 'Something failed',
    exception: 'System.Exception: timeout',
    elapsedMs: 150,
    kind: 'message',
    userName: null,
    action: null,
    statusCode: null,
    requestBody: null,
    responseBody: null,
  },
  {
    id: 2,
    timestamp: '2026-09-01T11:00:00Z',
    level: 'Warning',
    requestId: 'req-2',
    sourceContext: 'Test',
    requestPath: 'POST /api/users',
    message: 'Retry attempt',
    exception: null,
    elapsedMs: 50,
    kind: 'message',
    userName: null,
    action: null,
    statusCode: null,
    requestBody: null,
    responseBody: null,
  },
]

/// 访问行种子（kind='access'：合并路径含方法、用户名、action、状态码、脱敏请求体、失败响应体）。
const seedAccessEntries = [
  {
    id: 101,
    timestamp: '2026-09-01T12:00:00Z',
    level: 'Information',
    requestId: 'acc-1',
    sourceContext: null,
    requestPath: 'POST /api/knowledge-bases/list',
    message: 'POST /api/knowledge-bases/list 200 42ms',
    exception: null,
    elapsedMs: 42,
    kind: 'access',
    userName: 'bob',
    action: 'Kb.List',
    statusCode: 200,
    requestBody: '{"page":1,"passwordHash":"***"}',
    responseBody: '[40100] 未授权访问',
  },
  {
    id: 102,
    timestamp: '2026-09-01T11:00:00Z',
    level: 'Information',
    requestId: 'acc-2',
    sourceContext: null,
    requestPath: 'POST /api/no-such',
    message: 'POST /api/no-such 404 3ms',
    exception: null,
    elapsedMs: 3,
    kind: 'access',
    userName: null,
    action: null,
    statusCode: 404,
    requestBody: null,
    responseBody: '[40400] 资源不存在',
  },
]

/// 拦截唯一日志端点 /api/logs/list，按请求体 kind 分支返回两类行；
/// captured 记录每次请求体，供断言两 Tab 的 kind 判别。
function mockLogsFetch(
  captured: Record<string, unknown>[],
  options: { messageEntries?: typeof seedEntries; accessEntries?: typeof seedAccessEntries } = {},
) {
  const messageEntries = options.messageEntries ?? seedEntries
  const accessEntries = options.accessEntries ?? seedAccessEntries
  vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    if (url !== '/api/logs/list') {
      throw new Error(`Unexpected fetch: ${url}`)
    }
    const body = init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : {}
    captured.push(body)
    const entries = body.kind === 'access' ? accessEntries : messageEntries
    return Promise.resolve(jsonResponse(envelope({ entries, total: entries.length })))
  }) as never)
}

describe('LogsPage', () => {
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

  it('lists message entries and sends kind message on the error tab', async () => {
    const captured: Record<string, unknown>[] = []
    mockLogsFetch(captured)

    render(<LogsPage />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('req-1')).toBeInTheDocument())
    expect(screen.getByText('req-2')).toBeInTheDocument()
    expect(screen.getByText('GET /api/test')).toBeInTheDocument()
    expect(screen.getByText('POST /api/users')).toBeInTheDocument()
    // 错误日志 Tab 显式声明 kind='message'，单端点单数据源。
    expect(captured[0]).toMatchObject({ kind: 'message' })
  })

  it('filters by keyword', async () => {
    const captured: Record<string, unknown>[] = []
    mockLogsFetch(captured, { messageEntries: [seedEntries[0]] })

    render(<LogsPage />, { wrapper: wrap(makeClient()) })
    await waitFor(() => expect(screen.getByText('req-1')).toBeInTheDocument())

    fireEvent.change(screen.getByPlaceholderText('按关键词过滤'), {
      target: { value: 'timeout' },
    })
    fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }))

    await waitFor(() => {
      expect(captured.at(-1)).toMatchObject({ keyword: 'timeout', kind: 'message' })
    })
  })

  it('shows empty state when no logs', async () => {
    const captured: Record<string, unknown>[] = []
    mockLogsFetch(captured, { messageEntries: [] })

    render(<LogsPage />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('暂无日志')).toBeInTheDocument())
  })

  it('renders access rows from the same endpoint with kind access', async () => {
    const captured: Record<string, unknown>[] = []
    mockLogsFetch(captured)

    render(<LogsPage />, { wrapper: wrap(makeClient()) })
    // 默认 Tab 是错误日志：先渲染错误日志行。
    await waitFor(() => expect(screen.getByText('req-1')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('tab', { name: /访问日志/ }))

    // 访问日志走同一端点，仅靠 kind='access' 区分。
    await waitFor(() =>
      expect(captured.some((body) => body.kind === 'access')).toBe(true),
    )
    await waitFor(() => expect(screen.getByText('bob')).toBeInTheDocument())
    // 合并路径（方法+路径）直接展示。
    expect(screen.getByText('POST /api/knowledge-bases/list')).toBeInTheDocument()
    expect(screen.getByText('Kb.List')).toBeInTheDocument()
    expect(screen.getByText('acc-1')).toBeInTheDocument()
    // 业务失败（200 + 响应体）显示红色 Tag，404 显示橙色 Tag。
    expect(screen.getByText('200 失败')).toBeInTheDocument()
    expect(screen.getByText('404')).toBeInTheDocument()
  })

  it('shows request body modal from the access tab', async () => {
    const captured: Record<string, unknown>[] = []
    mockLogsFetch(captured, { messageEntries: [] })

    render(<LogsPage />, { wrapper: wrap(makeClient()) })
    fireEvent.click(screen.getByRole('tab', { name: /访问日志/ }))

    await waitFor(() => expect(screen.getByText('bob')).toBeInTheDocument())

    // 点"查看"打开请求参数 Modal，显示脱敏后的完整请求体。
    fireEvent.click(screen.getAllByRole('button', { name: /查看/ })[0])
    await waitFor(() =>
      expect(screen.getByText(/passwordHash/)).toBeInTheDocument(),
    )
  })
})
