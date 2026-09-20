import { md5 } from 'js-md5'
import type { AuthSession } from './authStore'
import { useAuthStore } from './authStore'

// 与后端 FlagStatesOption 数值一一对应；保持数字字面量以便老调用方按数值兼容。
const FlagStatesOption = {
  Success: 0,
  Validation: 40000,
  Unauthorized: 40100,
  Forbidden: 40300,
  NotFound: 40400,
  Conflict: 40900,
  InternalServerError: 50000,
  NotImplemented: 50100,
} as const

type FlagStatesOption = (typeof FlagStatesOption)[keyof typeof FlagStatesOption]

export { FlagStatesOption }

// 客户端密码处理：服务端先 GET /api/auth/salt 拿 salt，再以 MD5(password+salt) 提交 passwordHash。
// salt 由服务端在创建用户时随机生成并存储，永不下行到客户端以外的存储；MD5 仅作传输层哈希，存储仍走服务端的 PBKDF2。

interface LoginEnvelope {
  accessToken: string
  expiresAt: string
  user: {
    id: string
    userName: string
    roles: string[]
  }
}

export async function login(userName: string, password: string): Promise<AuthSession> {
  const salt = await fetchSalt(userName)
  const passwordHash = md5(password + salt).toLowerCase()
  return request('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ userName, passwordHash }),
  })
}

export async function refreshSession(): Promise<AuthSession> {
  return request('/api/auth/refresh', { method: 'POST' })
}

export async function logout(): Promise<void> {
  try {
    const response = await fetch('/api/auth/logout', {
      method: 'POST',
      credentials: 'include',
    })
    const envelope = (await response.json()) as ApiEnvelope<null>
    ensureSuccess(envelope, '退出登录失败')
  } catch {
    throw new Error('服务暂不可用，请检查网络或稍后重试')
  }
}

/// <summary>修改当前登录用户的密码。成功后服务端会删除 refresh cookie 并撤销全部会话，调用方应引导重新登录。</summary>
export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const userName = useAuthStore.getState().user?.userName
  if (userName === undefined) {
    throw new Error('未登录')
  }
  const salt = await fetchSalt(userName)
  const currentPasswordHash = md5(currentPassword + salt).toLowerCase()
  const newPasswordHash = md5(newPassword + salt).toLowerCase()
  try {
    const response = await fetch('/api/auth/change-password', {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${useAuthStore.getState().accessToken ?? ''}`,
      },
      body: JSON.stringify({ currentPasswordHash, newPasswordHash }),
    })
    const envelope = (await response.json()) as ApiEnvelope<null>
    ensureSuccess(envelope, '修改密码失败')
  } catch {
    throw new Error('服务暂不可用，请检查网络或稍后重试')
  }
}

async function fetchSalt(userName: string): Promise<string> {
  try {
    const response = await fetch('/api/auth/salt', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userName }),
    })
    if (!response.ok) {
      throw new Error(`服务暂不可用（HTTP ${response.status}）`)
    }
    const envelope = (await response.json()) as ApiEnvelope<{ salt: string } | null>
    if (!envelope.flag || !envelope.data) {
      throw new Error(envelope.message || '用户不存在')
    }

    return envelope.data.salt
  } catch (error) {
    if (error instanceof Error && (error.message.startsWith('服务暂不可用') || error.message === '用户不存在')) {
      throw error
    }
    throw new Error('服务暂不可用，请检查网络或稍后重试')
  }
}

async function request(path: string, init: RequestInit): Promise<AuthSession> {
  let response: Response
  try {
    response = await fetch(path, {
      ...init,
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', ...init.headers },
    })
  } catch {
    throw new Error('服务暂不可用，请检查网络或稍后重试')
  }
  if (!response.ok) {
    throw new Error('认证服务暂不可用')
  }

  const envelope = (await response.json()) as ApiEnvelope<LoginEnvelope>
  ensureSuccess(envelope, envelope.message || '认证请求失败')
  const data = envelope.data
  return {
    accessToken: data.accessToken,
    expiresAt: data.expiresAt,
    user: {
      id: data.user.id,
      userName: data.user.userName,
      roles: data.user.roles,
    },
  }
}

interface ApiEnvelope<T> {
  requestId: string
  code: FlagStatesOption
  value: string
  flag: boolean
  message: string
  data: T
  hasNextPage: boolean
  total: number
}

function ensureSuccess<T>(envelope: ApiEnvelope<T>, fallback: string): void {
  if (!envelope.flag) {
    throw new Error(envelope.message || fallback)
  }
}