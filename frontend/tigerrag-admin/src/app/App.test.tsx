import { act, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
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
    vi.mocked(fetch).mockImplementation((url) => {
      if (url === '/api/auth/refresh') {
        return Promise.resolve(jsonResponse(envelope(session)))
      }
      if (url === '/api/menu-configs/tree') {
        return Promise.resolve(jsonResponse(envelope([])))
      }
      return Promise.resolve(jsonResponse(envelope(null, false, '未授权'), true))
    })
    await renderApp()

    expect(await screen.findByRole('heading', { name: 'TigerRAG' })).toBeInTheDocument()
    expect(screen.getByText('admin')).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith('/api/auth/refresh', expect.objectContaining({ credentials: 'include' }))
  })

  it('logs in and keeps the access token in memory', async () => {
    vi.mocked(fetch).mockImplementation((url) => {
      if (url === '/api/auth/refresh') {
        return Promise.resolve(jsonResponse(envelope(null, false, '未授权'), true))
      }
      if (url === '/api/auth/salt') {
        return Promise.resolve(jsonResponse(envelope({ salt: '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef' })))
      }
      if (url === '/api/auth/login') {
        return Promise.resolve(jsonResponse(envelope(session)))
      }
      if (url === '/api/menu-configs/tree') {
        return Promise.resolve(jsonResponse(envelope([])))
      }
      return Promise.resolve(jsonResponse(envelope(null, false, '未授权'), true))
    })
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

  it('logs out via the user dropdown and returns to the login page', async () => {
    vi.mocked(fetch).mockImplementation((url) => {
      if (url === '/api/auth/refresh') {
        return Promise.resolve(jsonResponse(envelope(session)))
      }
      if (url === '/api/menu-configs/tree') {
        return Promise.resolve(jsonResponse(envelope([])))
      }
      if (url === '/api/auth/logout') {
        return Promise.resolve(jsonResponse(envelope(null)))
      }
      return Promise.resolve(jsonResponse(envelope(null, false, '未授权'), true))
    })
    await renderApp()

    await screen.findByText('admin')
    await act(async () => {
      fireEvent.click(screen.getByText('admin'))
      await Promise.resolve()
    })

    await act(async () => {
      fireEvent.click(await screen.findByText('退出登录'))
      await Promise.resolve()
    })

    expect(await screen.findByRole('button', { name: /登\s*录/ })).toBeInTheDocument()
    expect(useAuthStore.getState().accessToken).toBeNull()
  })
})

async function renderApp() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  await act(async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <App />
        </MemoryRouter>
      </QueryClientProvider>,
    )
    await Promise.resolve()
  })
}

function jsonResponse(body: unknown, ok = true, status = 200) {
  return { ok, status, json: async () => body, text: async () => JSON.stringify(body) } as Response
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
