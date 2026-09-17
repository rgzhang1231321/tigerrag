import { act, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../features/auth/authStore'
import { App } from './App'

const session = {
  accessToken: 'access-token',
  expiresAt: '2026-09-07T13:00:00Z',
  user: { id: 'user-id', userName: 'admin', roles: ['Admin'] },
}

describe('TigerRAG authentication shell', () => {
  beforeEach(() => {
    useAuthStore.getState().clear()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => vi.unstubAllGlobals())

  it('restores the session from the refresh cookie', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(envelope(session)))
    await renderApp()

    expect(await screen.findByRole('heading', { name: 'TigerRAG' })).toBeInTheDocument()
    expect(screen.getByText('admin')).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith('/api/auth/refresh', expect.objectContaining({ credentials: 'include' }))
  })

  it('logs in and keeps the access token in memory', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse(envelope(null, false, '未授权'), true))
      .mockResolvedValueOnce(jsonResponse(envelope({ salt: '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef' })))
      .mockResolvedValueOnce(jsonResponse(envelope(session)))
    await renderApp()

    await screen.findByLabelText('用户名')
    await act(async () => {
      fireEvent.change(screen.getByLabelText('用户名'), { target: { value: 'admin' } })
      fireEvent.change(screen.getByLabelText('密码'), { target: { value: 'correct-password' } })
      fireEvent.click(screen.getByRole('button', { name: /登\s*录/ }))
      await Promise.resolve()
    })

    expect(await screen.findByRole('heading', { name: '系统概览' })).toBeInTheDocument()
    expect(useAuthStore.getState().accessToken).toBe('access-token')
  })

  it('logs out and returns to the login page', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse(envelope(session)))
      .mockResolvedValueOnce(jsonResponse(envelope(null)))
    await renderApp()
    await act(async () => {
      fireEvent.click(await screen.findByRole('button', { name: '退出登录' }))
      await Promise.resolve()
    })

    expect(await screen.findByRole('button', { name: /登\s*录/ })).toBeInTheDocument()
    expect(useAuthStore.getState().accessToken).toBeNull()
  })
})

async function renderApp() {
  await act(async () => {
    render(
      <MemoryRouter>
        <App />
      </MemoryRouter>,
    )
    await Promise.resolve()
  })
}

function jsonResponse(body: unknown, ok = true, status = 200) {
  return { ok, status, json: async () => body } as Response
}

function envelope<T>(data: T, flag = true, message = 'success') {
  return {
    requestId: '',
    code: 0,
    value: flag ? 'Success' : 'Unauthorized',
    flag,
    message,
    data,
    hasNextPage: false,
    total: 0,
  }
}
