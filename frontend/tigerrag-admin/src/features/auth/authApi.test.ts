import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { md5 } from 'js-md5'
import { login } from './authApi'

const session = {
  accessToken: 'access-token',
  expiresAt: '2026-09-07T13:00:00Z',
  user: { id: 'user-id', userName: 'admin', roles: ['Admin'] },
}

describe('authApi.login', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('fetches the per-user salt and submits MD5(password + salt) as passwordHash', async () => {
    const salt = '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
    const password = 'correct-password'
    const expectedHash = md5(password + salt).toLowerCase()

    let loginBody: { userName: string; passwordHash: string } | null = null
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url.startsWith('/api/auth/salt')) {
        return Promise.resolve(jsonResponse(envelope({ salt })))
      }
      if (url === '/api/auth/login') {
        loginBody = JSON.parse(String(init?.body)) as { userName: string; passwordHash: string }
        return Promise.resolve(jsonResponse(envelope(session)))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    const result = await login('admin', password)

    expect(result.accessToken).toBe('access-token')
    expect(loginBody).toEqual({ userName: 'admin', passwordHash: expectedHash })
  })

  it('surfaces a user-not-found error when the salt endpoint reports NotFound', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(envelope(null, false, '用户不存在')))

    await expect(login('ghost', 'whatever')).rejects.toThrow('用户不存在')
  })
})

function jsonResponse(body: unknown) {
  return { ok: true, status: 200, json: async () => body } as Response
}

function envelope<T>(data: T, flag = true, message = 'success') {
  return {
    requestId: '',
    code: 0,
    value: flag ? 'Success' : 'NotFound',
    flag,
    message,
    data,
    hasNextPage: false,
    total: 0,
  }
}