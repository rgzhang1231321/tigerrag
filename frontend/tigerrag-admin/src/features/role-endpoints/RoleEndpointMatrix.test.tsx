import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { createRef } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RoleEndpointMatrix, type RoleEndpointMatrixHandle } from './RoleEndpointMatrix'

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
    code: flag ? 0 : 40000,
    value: flag ? 'Success' : 'Validation',
    flag,
    message,
    data,
    hasNextPage: false,
    total: 0,
  }
}

const sampleMatrix = {
  menus: [
    {
      menuKey: 'documents',
      endpoints: [
        { endpointKey: 'documents.list', description: '列出文档', httpMethod: 'POST', path: 'list', granted: true },
        { endpointKey: 'documents.upload', description: '上传文档', httpMethod: 'POST', path: 'upload', granted: false },
        { endpointKey: 'documents.delete', description: '删除文档', httpMethod: 'POST', path: '{id}/delete', granted: false },
      ],
    },
    {
      menuKey: 'knowledgeBases',
      endpoints: [
        { endpointKey: 'knowledgeBases.list', description: '列出知识库', httpMethod: 'POST', path: 'list', granted: false },
      ],
    },
  ],
}

function makeFetch(matrix: unknown) {
  return ((input: RequestInfo | URL, _init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    if (url === '/api/roles/Viewer/grants') {
      return Promise.resolve(jsonResponse(envelope(matrix)))
    }
    throw new Error(`Unexpected fetch: ${url}`)
  }) as never
}

describe('RoleEndpointMatrix', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders menu groups with endpoint rows and granted counts', async () => {
    vi.mocked(fetch).mockImplementation(makeFetch(sampleMatrix))

    render(<RoleEndpointMatrix role="Viewer" />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('documents')).toBeInTheDocument())
    expect(screen.getByText(/已授权 1\/3/)).toBeInTheDocument()
    expect(screen.getByText('列出文档')).toBeInTheDocument()
    expect(screen.getByText('上传文档')).toBeInTheDocument()
    expect(screen.getByText(/已授权 0\/1/)).toBeInTheDocument()
  })

  it('clicking single checkbox only marks dirty, no backend call yet', async () => {
    vi.mocked(fetch).mockImplementation(makeFetch(sampleMatrix))

    const ref = createRef<RoleEndpointMatrixHandle>()
    render(<RoleEndpointMatrix ref={ref} role="Viewer" />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('上传文档')).toBeInTheDocument())

    const uploadRow = screen.getByText('上传文档').closest('.role-endpoint-row')!
    const checkbox = uploadRow.querySelector('input[type="checkbox"]')!
    fireEvent.click(checkbox)

    // dirty：ref.isDirty 变 true + 不应发起后端调用
    await waitFor(() => expect(ref.current?.isDirty).toBe(true))
    expect(screen.getByText(/已授权 2\/3/)).toBeInTheDocument()
    expect(vi.mocked(fetch)).not.toHaveBeenCalledWith(
      expect.stringContaining('/endpoints/toggle'),
      expect.anything(),
    )
  })

  it('clicking menu checkbox marks every endpoint in that menu as pending', async () => {
    vi.mocked(fetch).mockImplementation(makeFetch(sampleMatrix))

    const ref = createRef<RoleEndpointMatrixHandle>()
    render(<RoleEndpointMatrix ref={ref} role="Viewer" />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText(/已授权 0\/1/)).toBeInTheDocument())

    // 点击 knowledgeBases 菜单的 checkbox → pending 1 个；总计变化
    const kbCheckbox = screen.getByText('knowledgeBases').closest('.role-endpoint-menu-header')!
      .querySelector('input[type="checkbox"]')!
    fireEvent.click(kbCheckbox)

    await waitFor(() => expect(ref.current?.isDirty).toBe(true))
    await waitFor(() => expect(screen.getByText('总计 2/4')).toBeInTheDocument())
  })

  it('submit sends single batch request and clears dirty state on success', async () => {
    const batchCalls: unknown[] = []
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/Viewer/grants') {
        return Promise.resolve(jsonResponse(envelope(sampleMatrix)))
      }
      if (url === '/api/roles/Viewer/grants/batch' && init?.method === 'POST') {
        batchCalls.push(init.body)
        return Promise.resolve(jsonResponse(envelope(1)))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    const ref = createRef<RoleEndpointMatrixHandle>()
    render(<RoleEndpointMatrix ref={ref} role="Viewer" />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('上传文档')).toBeInTheDocument())

    // 勾选一个 → dirty
    const uploadRow = screen.getByText('上传文档').closest('.role-endpoint-row')!
    fireEvent.click(uploadRow.querySelector('input[type="checkbox"]')!)

    // 通过 ref.submit() 提交
    await act(async () => {
      await ref.current?.submit()
    })

    await waitFor(() => expect(batchCalls).toHaveLength(1))
    expect(vi.mocked(fetch)).not.toHaveBeenCalledWith(
      expect.stringContaining('/endpoints/toggle'),
      expect.anything(),
    )
    expect(vi.mocked(fetch)).not.toHaveBeenCalledWith(
      expect.stringContaining('/menus/documents/grant'),
      expect.anything(),
    )

    // dirty 已清：ref.isDirty 回到 false
    await waitFor(() => expect(ref.current?.isDirty).toBe(false))
  })

  it('submit failure keeps dirty state and surfaces error', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/Viewer/grants') {
        return Promise.resolve(jsonResponse(envelope(sampleMatrix)))
      }
      if (url === '/api/roles/Viewer/grants/batch' && init?.method === 'POST') {
        return Promise.resolve({
          ok: false,
          status: 500,
          json: async () => ({}),
          text: async () => '保存失败',
        } as Response)
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    const ref = createRef<RoleEndpointMatrixHandle>()
    render(<RoleEndpointMatrix ref={ref} role="Viewer" />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('上传文档')).toBeInTheDocument())

    const uploadRow = screen.getByText('上传文档').closest('.role-endpoint-row')!
    fireEvent.click(uploadRow.querySelector('input[type="checkbox"]')!)

    await act(async () => {
      await ref.current?.submit()
    })

    // 保存失败：dirty 仍保持
    await waitFor(() => expect(ref.current?.isDirty).toBe(true))
  })

  it('renders toolbar with grant-all/revoke-all and total summary', async () => {
    vi.mocked(fetch).mockImplementation(makeFetch(sampleMatrix))

    render(<RoleEndpointMatrix role="Viewer" />, { wrapper: wrap(makeClient()) })

    expect(await screen.findByTestId('role-endpoint-toolbar')).toBeInTheDocument()
    expect(screen.getByTestId('role-endpoint-grant-all')).toBeInTheDocument()
    expect(screen.getByTestId('role-endpoint-revoke-all')).toBeInTheDocument()
    expect(screen.getByText('总计 1/4')).toBeInTheDocument()
  })

  it('grant-all populates every endpoint in pending (submit via ref)', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/Viewer/grants') {
        return Promise.resolve(jsonResponse(envelope(sampleMatrix)))
      }
      if (url === '/api/roles/Viewer/grants/batch' && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(envelope(4)))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    const ref = createRef<RoleEndpointMatrixHandle>()
    render(<RoleEndpointMatrix ref={ref} role="Viewer" />, { wrapper: wrap(makeClient()) })

    fireEvent.click(await screen.findByTestId('role-endpoint-grant-all'))

    // pending 进入 dirty：总计变为 4/4
    await waitFor(() => expect(screen.getByText('总计 4/4')).toBeInTheDocument())
    expect(ref.current?.isDirty).toBe(true)

    // 通过 ref.submit() 提交
    await act(async () => {
      await ref.current?.submit()
    })

    await waitFor(() => expect(ref.current?.isDirty).toBe(false))
  })

  it('grant-all can be clicked repeatedly (never disabled)', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/Viewer/grants') {
        return Promise.resolve(jsonResponse(envelope(sampleMatrix)))
      }
      if (url === '/api/roles/Viewer/grants/batch' && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(envelope(4)))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleEndpointMatrix role="Viewer" />, { wrapper: wrap(makeClient()) })

    const grantAll = await screen.findByTestId('role-endpoint-grant-all')

    // 第一次点击
    fireEvent.click(grantAll)
    await waitFor(() => expect(screen.getByText('总计 4/4')).toBeInTheDocument())

    // 第二次点击仍可用（不 disabled）
    expect(grantAll).not.toBeDisabled()
    fireEvent.click(grantAll)
    await waitFor(() => expect(screen.getByText('总计 4/4')).toBeInTheDocument())
  })

  it('revoke-all marks every endpoint as pending=false (submit via ref)', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/Viewer/grants') {
        return Promise.resolve(jsonResponse(envelope(sampleMatrix)))
      }
      if (url === '/api/roles/Viewer/grants/batch' && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(envelope(1)))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    const ref = createRef<RoleEndpointMatrixHandle>()
    render(<RoleEndpointMatrix ref={ref} role="Viewer" />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('总计 1/4')).toBeInTheDocument())

    fireEvent.click(await screen.findByTestId('role-endpoint-revoke-all'))

    // pending 进入 dirty：总计变为 0/4
    await waitFor(() => expect(screen.getByText('总计 0/4')).toBeInTheDocument())
    expect(ref.current?.isDirty).toBe(true)

    // 通过 ref.submit() 提交
    await act(async () => {
      await ref.current?.submit()
    })

    await waitFor(() => expect(ref.current?.isDirty).toBe(false))
  })

  it('revoke-all can be clicked repeatedly (never disabled)', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/roles/Viewer/grants') {
        return Promise.resolve(jsonResponse(envelope(sampleMatrix)))
      }
      if (url === '/api/roles/Viewer/grants/batch' && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(envelope(4)))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    render(<RoleEndpointMatrix role="Viewer" />, { wrapper: wrap(makeClient()) })

    const revokeAll = await screen.findByTestId('role-endpoint-revoke-all')

    // 第一次点击
    fireEvent.click(revokeAll)
    await waitFor(() => expect(screen.getByText('总计 0/4')).toBeInTheDocument())

    // 第二次点击仍可用（不 disabled）
    expect(revokeAll).not.toBeDisabled()
    fireEvent.click(revokeAll)
    await waitFor(() => expect(screen.getByText('总计 0/4')).toBeInTheDocument())
  })

  it('toggleSingle flips already-granted endpoint to revoked', async () => {
    vi.mocked(fetch).mockImplementation(makeFetch(sampleMatrix))

    const ref = createRef<RoleEndpointMatrixHandle>()
    render(<RoleEndpointMatrix ref={ref} role="Viewer" />, { wrapper: wrap(makeClient()) })

    await waitFor(() => expect(screen.getByText('列出文档')).toBeInTheDocument())

    // documents.list 初始 granted=true
    const listRow = screen.getByText('列出文档').closest('.role-endpoint-row')!
    const checkbox = listRow.querySelector('input[type="checkbox"]')!
    expect(checkbox).toBeChecked()

    // 点击后应翻转为未授权
    fireEvent.click(checkbox)
    await waitFor(() => expect(checkbox).not.toBeChecked())
    await waitFor(() => expect(screen.getByText(/已授权 0\/3/)).toBeInTheDocument())
    expect(ref.current?.isDirty).toBe(true)
  })
})
