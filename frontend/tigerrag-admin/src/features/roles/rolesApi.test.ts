import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../app/http'
import { createRole, deleteRole, listRolesAll, updateRole } from './rolesApi'

interface CapturedCall {
  url: string
  method: string
  body: unknown
  headers: Record<string, string>
}

function jsonResponse(body: unknown) {
  return {
    ok: true,
    status: 200,
    json: async () => body,
    text: async () => JSON.stringify(body),
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

function setupFetchMock(handler: (call: CapturedCall) => unknown) {
  const calls: CapturedCall[] = []
  vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const headers: Record<string, string> = {}
    if (init?.headers instanceof Headers) {
      init.headers.forEach((value, key) => {
        headers[key] = value
      })
    } else if (Array.isArray(init?.headers)) {
      for (const [key, value] of init.headers as [string, string][]) {
        headers[key] = value
      }
    } else if (init?.headers) {
      Object.assign(headers, init.headers as Record<string, string>)
    }
    const call: CapturedCall = {
      url,
      method: init?.method ?? 'GET',
      body: init?.body ? JSON.parse(String(init.body)) : null,
      headers,
    }
    calls.push(call)
    return Promise.resolve(jsonResponse(handler(call)))
  }) as never)
  return calls
}

describe('rolesApi', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })
  afterEach(() => vi.unstubAllGlobals())

  it('listRolesAll POSTs /api/roles/list and unwraps role list with reference counts', async () => {
    const payload = [
      { name: 'Admin', userCount: 1, menuCount: 0, menuNames: [] },
      {
        name: 'Viewer',
        userCount: 3,
        menuCount: 2,
        menuNames: ['问答工作台', '知识库'],
      },
    ]
    const calls = setupFetchMock(() => envelope(payload))
    const result = await listRolesAll()
    expect(result).toEqual(payload)
    expect(calls[0].url).toBe('/api/roles/list')
    expect(calls[0].method).toBe('POST')
  })

  it('createRole POSTs {name} to /api/roles', async () => {
    const created = {
      name: 'CustomRole',
      userCount: 0,
      menuCount: 0,
      menuNames: [],
    }
    const calls = setupFetchMock(() => envelope(created))
    const result = await createRole({ name: 'CustomRole' })
    expect(result).toEqual(created)
    expect(calls[0].url).toBe('/api/roles')
    expect(calls[0].method).toBe('POST')
    expect(calls[0].body).toEqual({ name: 'CustomRole' })
  })

  it('updateRole POSTs {name} to /api/roles/{name}/rename', async () => {
    const updated = {
      name: 'RenamedRole',
      userCount: 1,
      menuCount: 0,
      menuNames: [],
    }
    const calls = setupFetchMock(() => envelope(updated))
    const result = await updateRole('CustomRole', { name: 'RenamedRole' })
    expect(result).toEqual(updated)
    expect(calls[0].url).toBe('/api/roles/CustomRole/rename')
    expect(calls[0].method).toBe('POST')
    expect(calls[0].body).toEqual({ name: 'RenamedRole' })
  })

  it('updateRole URL-encodes original role name', async () => {
    const calls = setupFetchMock(() =>
      envelope({
        name: 'NewRole',
        userCount: 0,
        menuCount: 0,
        menuNames: [],
      }),
    )
    await updateRole('Custom Role', { name: 'NewRole' })
    expect(calls[0].url).toBe('/api/roles/Custom%20Role/rename')
  })

  it('deleteRole POSTs to /api/roles/{name}/delete with URL encoding', async () => {
    const calls = setupFetchMock(() => envelope(null))
    await deleteRole('CustomRole')
    const call = calls[0]
    expect(call.url).toBe('/api/roles/CustomRole/delete')
    expect(call.method).toBe('POST')
    expect(call.body).toBeNull()
  })

  it('createRole propagates server validation message via ApiError', async () => {
    setupFetchMock(() => envelope(null, false, '角色 Admin 已存在。'))
    await expect(createRole({ name: 'Admin' })).rejects.toThrow('角色 Admin 已存在')
  })

  it('deleteRole propagates not-found message via ApiError', async () => {
    setupFetchMock(() => envelope(null, false, '角色 Ghost 不存在'))
    await expect(deleteRole('Ghost')).rejects.toBeInstanceOf(ApiError)
  })

  it('updateRole propagates conflict message via ApiError', async () => {
    setupFetchMock(() => envelope(null, false, '角色 Other 已存在'))
    await expect(updateRole('Custom', { name: 'Other' })).rejects.toThrow('已存在')
  })
})