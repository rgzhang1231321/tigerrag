import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { md5 } from 'js-md5'
import { changePassword, login } from './authApi'
import { useAuthStore } from './authStore'

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
      if (url === '/api/auth/salt') {
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

describe('authApi.changePassword', () => {
  beforeEach(() => {
    useAuthStore.getState().setSession({
      accessToken: 'access-token',
      expiresAt: '2026-09-17T00:00:00Z',
      user: { id: 'u1', userName: 'admin', roles: ['Admin'] },
    })
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    useAuthStore.getState().clear()
  })

  it('submits MD5(current + salt) and MD5(new + salt) as the two hashes', async () => {
    const salt = '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
    const current = 'OldPass-1234'
    const next = 'NewPass-5678'
    const expectedCurrentHash = md5(current + salt).toLowerCase()
    const expectedNewHash = md5(next + salt).toLowerCase()

    let changeBody: { currentPasswordHash: string; newPasswordHash: string } | null = null
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/auth/salt') {
        return Promise.resolve(jsonResponse(envelope({ salt })))
      }
      if (url === '/api/auth/change-password') {
        changeBody = JSON.parse(String(init?.body)) as {
          currentPasswordHash: string
          newPasswordHash: string
        }
        return Promise.resolve(jsonResponse(envelope(null)))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    await changePassword(current, next)

    expect(changeBody).toEqual({ currentPasswordHash: expectedCurrentHash, newPasswordHash: expectedNewHash })
  })

  it('throws when the server reports the current password is wrong', async () => {
    vi.mocked(fetch).mockImplementation(((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url === '/api/auth/salt') {
        return Promise.resolve(jsonResponse(envelope({ salt: 'salt-value' })))
      }
      if (url === '/api/auth/change-password') {
        return Promise.resolve(jsonResponse(envelope(null, false, '当前密码错误或新密码不符合要求')))
      }
      throw new Error(`Unexpected fetch: ${url}`)
    }) as never)

    await expect(changePassword('wrong', 'NewPass-5678')).rejects.toThrow('当前密码错误或新密码不符合要求')
  })
})

function jsonResponse(body: unknown) {
  return { ok: true, status: 200, json: async () => body, text: async () => JSON.stringify(body) } as Response
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