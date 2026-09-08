import type { AuthSession } from './authStore'

export async function login(userName: string, password: string): Promise<AuthSession> {
  return request('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ userName, password }),
  })
}

export async function refreshSession(): Promise<AuthSession> {
  return request('/api/auth/refresh', { method: 'POST' })
}

export async function logout(): Promise<void> {
  const response = await fetch('/api/auth/logout', {
    method: 'POST',
    credentials: 'include',
  })
  const envelope = (await response.json()) as ApiEnvelope<null>
  ensureSuccess(envelope, '退出登录失败')
}

async function request(path: string, init: RequestInit): Promise<AuthSession> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...init.headers },
  })
  if (!response.ok) {
    throw new Error('认证服务暂不可用')
  }

  const envelope = (await response.json()) as ApiEnvelope<AuthSession>
  ensureSuccess(envelope, envelope.message || '认证请求失败')
  return envelope.data
}

interface ApiEnvelope<T> {
  code: number
  message: string
  data: T
}

function ensureSuccess<T>(envelope: ApiEnvelope<T>, fallback: string): void {
  if (envelope.code !== 0) {
    throw new Error(envelope.message || fallback)
  }
}
