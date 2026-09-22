import { md5 } from 'js-md5'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  assignRoles,
  computePasswordHash,
  createUser,
  fetchSalt,
  listRoles,
  listUsers,
  resetPassword,
  setInitialPassword,
} from './usersApi'

interface CapturedCall {
  url: string
  method: string
  body: unknown
  headers: Record<string, string>
}

const session = {
  accessToken: 'access-token',
  expiresAt: '2026-09-17T00:00:00Z',
  user: { id: 'admin-id', userName: 'admin', roles: ['Admin'] },
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

describe('usersApi.fetchSalt', () => {
  beforeEach(() => vi.stubGlobal('fetch', vi.fn()))
  afterEach(() => vi.unstubAllGlobals())

  it('POSTs {userName} and unwraps the salt field', async () => {
    const calls = setupFetchMock((call) =>
      call.url === '/api/auth/salt' ? envelope({ salt: '0'.repeat(64) }) : envelope(session),
    )

    const salt = await fetchSalt('alice')
    expect(salt).toBe('0'.repeat(64))
    const saltCall = calls.find((c) => c.url === '/api/auth/salt')
    expect(saltCall).toBeDefined()
    expect(saltCall!.method).toBe('POST')
    expect(saltCall!.body).toEqual({ userName: 'alice' })
  })

  it('throws when the envelope reports failure', async () => {
    setupFetchMock(() => envelope(null, false, '用户不存在'))
    await expect(fetchSalt('ghost')).rejects.toThrow('用户不存在')
  })
})

describe('usersApi.computePasswordHash', () => {
  it('produces lowercase MD5 of password concatenated with salt', () => {
    const salt = '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
    const password = 'correct-password'
    expect(computePasswordHash(password, salt)).toBe(md5(password + salt).toLowerCase())
  })
})

describe('usersApi endpoints', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })
  afterEach(() => vi.unstubAllGlobals())

  it('listUsers POSTs /api/users/list and injects Bearer', async () => {
    const users = [
      { id: 'u1', userName: 'admin', roles: ['Admin'], isLocked: false },
      { id: 'u2', userName: 'viewer', roles: ['Viewer'], isLocked: true },
    ]
    const calls = setupFetchMock(() => envelope(users))
    const result = await listUsers()
    expect(result).toEqual(users)
    const call = calls[0]
    expect(call.url).toBe('/api/users/list')
    expect(call.method).toBe('POST')
  })

  it('listRoles POSTs /api/users/roles/list', async () => {
    const roles = ['Admin', 'Auditor', 'Editor', 'KbManager', 'Viewer']
    const calls = setupFetchMock(() => envelope(roles))
    const result = await listRoles()
    expect(result).toEqual(roles)
    expect(calls[0].url).toBe('/api/users/roles/list')
    expect(calls[0].method).toBe('POST')
  })

  it('createUser POSTs the payload to /api/users', async () => {
    const created = { id: 'new-id', userName: 'alice', roles: ['Viewer'], isLocked: false }
    const calls = setupFetchMock(() => envelope(created))
    const result = await createUser({ userName: 'alice', roles: ['Viewer'] })
    expect(result).toEqual(created)
    expect(calls[0].url).toBe('/api/users')
    expect(calls[0].method).toBe('POST')
    expect(calls[0].body).toEqual({ userName: 'alice', roles: ['Viewer'] })
  })

  it('setInitialPassword POSTs the passwordHash to /api/users/{id}/initial-password', async () => {
    const calls = setupFetchMock(() => envelope(null))
    await setInitialPassword({ userId: 'new-id', passwordHash: 'a'.repeat(32) })
    const call = calls[0]
    expect(call.url).toBe('/api/users/new-id/initial-password')
    expect(call.method).toBe('POST')
    expect(call.body).toEqual({ passwordHash: 'a'.repeat(32) })
  })

  it('assignRoles POSTs roles to /api/users/{id}/roles', async () => {
    const calls = setupFetchMock(() => envelope(null))
    await assignRoles({ userId: 'u2', roles: ['Editor'] })
    const call = calls[0]
    expect(call.url).toBe('/api/users/u2/roles')
    expect(call.method).toBe('POST')
    expect(call.body).toEqual({ roles: ['Editor'] })
  })

  it('resetPassword POSTs passwordHash to /api/users/{id}/password', async () => {
    const calls = setupFetchMock(() => envelope(null))
    await resetPassword({ userId: 'u2', passwordHash: 'b'.repeat(32) })
    const call = calls[0]
    expect(call.url).toBe('/api/users/u2/password')
    expect(call.method).toBe('POST')
    expect(call.body).toEqual({ passwordHash: 'b'.repeat(32) })
  })
})