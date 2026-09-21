import { message } from 'antd'
import { refreshSession } from '../features/auth/authApi'
import { useAuthStore } from '../features/auth/authStore'

/// <summary>业务错误：把 ApiResponse 信封 flag=false 抛成 Error；httpStatus 仅在传输层失败时填入。</summary>
export class ApiError extends Error {
  constructor(
    readonly code: number,
    message: string,
    readonly httpStatus?: number,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

/// <summary>全局错误弹框：接口失败时统一以 antd message 提示；401 登出静默处理不弹。</summary>
function showError(error: ApiError) {
  if (error.code === 40100) return
  message.error(error.message)
}

export interface HttpOptions extends Omit<RequestInit, 'body'> {
  /// <summary>请求体；非空时自动 JSON.stringify 并设置 Content-Type。</summary>
  body?: unknown
  /// <summary>设为 false 可关闭 401 静默刷新重试；用于 login/refresh 自身避免自循环。</summary>
  retryOnAuth?: boolean
}

interface ApiEnvelope<T> {
  requestId: string
  code: number
  value: string
  flag: boolean
  message: string
  data: T | null
  hasNextPage: boolean
  total: number
}

/// <summary>
/// 统一的 fetch 出口：所有非 auth 调用必须走这里。
/// - 自动注入 Authorization: Bearer <accessToken>（来自 Zustand，内存中的令牌不持久化）
/// - 始终 credentials: 'include'，让 refresh cookie 跟随请求
/// - 解开 ApiResponse 信封：flag=false 时抛 ApiError；flag=true 时返回 data
/// - 401 时静默调用 refreshSession 一次再重试；刷新失败则清空登录态
/// </summary>
export async function http<T>(path: string, options: HttpOptions = {}): Promise<T> {
  const { body, retryOnAuth = true, headers, ...init } = options
  const token = useAuthStore.getState().accessToken

  const requestInit: RequestInit = {
    ...init,
    credentials: 'include',
    headers: buildHeaders(token, headers, body),
  }
  if (body !== undefined) {
    requestInit.body = JSON.stringify(body)
  }

  try {
    return await execute<T>(path, requestInit, retryOnAuth)
  } catch (error) {
    if (error instanceof ApiError) {
      showError(error)
      if (error.code === 40100 && retryOnAuth) {
        // 刷新失败：仅清空 access token，保留 user 让 UI 留在原页。
        // 下次 API 请求会读到 accessToken === null，发送时不带 Authorization 头，
        // 后端返回 401 → execute 内部再次尝试刷新。反复失败也不会踢出登录页，
        // 避免一次暂时性刷新失败（网络抖动等）就把用户踢回登录页。
        // sessionStorage 缓存同时保留：若整个页面被刷新，仍可由 restoreSessionFromCache 恢复。
        useAuthStore.getState().clearMemoryOnly()
      }
    }
    throw error
  }
}

function buildHeaders(
  token: string | null,
  extra: HeadersInit | undefined,
  body: unknown,
): HeadersInit {
  const headers: Record<string, string> = {}
  if (extra !== undefined) {
    const entries =
      extra instanceof Headers
        ? extra.entries()
        : Array.isArray(extra)
          ? (extra as [string, string][])
          : Object.entries(extra as Record<string, string>)
    for (const [key, value] of entries) {
      headers[key] = value
    }
  }
  if (token !== null) {
    headers['Authorization'] = `Bearer ${token}`
  }
  if (body !== undefined && headers['Content-Type'] === undefined) {
    headers['Content-Type'] = 'application/json'
  }
  return headers
}

async function execute<T>(
  path: string,
  init: RequestInit,
  retryOnAuth: boolean,
): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, init)
  } catch {
    // connection refused / network down
    throw new ApiError(50000, '服务暂不可用，请检查网络或稍后重试')
  }
  const envelope = (await parseEnvelope(response)) as ApiEnvelope<T>
  if (envelope.flag) {
    return envelope.data as T
  }

  // 401 静默刷新一次：先调 refresh，再原请求重发一次。
  if (envelope.code === 40100 && retryOnAuth) {
    const refreshed = await tryRefresh()
    if (refreshed) {
      // 用最新的 token 重发：重新读 store，确保拿到刷新后的值。
      const retriedInit: RequestInit = { ...init }
      const currentToken = useAuthStore.getState().accessToken
      const mergedHeaders: Record<string, string> = { ...(init.headers as Record<string, string>) }
      if (currentToken !== null) {
        mergedHeaders['Authorization'] = `Bearer ${currentToken}`
      }
      retriedInit.headers = mergedHeaders
      const retried = await fetch(path, retriedInit)
      const retriedEnvelope = (await parseEnvelope(retried)) as ApiEnvelope<T>
      if (retriedEnvelope.flag) {
        return retriedEnvelope.data as T
      }
      throw new ApiError(retriedEnvelope.code, retriedEnvelope.message)
    }
  }

  throw new ApiError(envelope.code, envelope.message)
}

async function tryRefresh(): Promise<boolean> {
  try {
    const session = await refreshSession()
    useAuthStore.getState().setSession(session)
    return true
  } catch {
    return false
  }
}

async function parseEnvelope(response: Response): Promise<ApiEnvelope<unknown>> {
  // 网络层或协议层失败：返回友好提示，替代 JSON.parse TypeError 原始堆栈
  if (!response.ok) {
    throw new ApiError(50000, `服务暂不可用（HTTP ${response.status}）`, response.status)
  }

  // 2xx 但 body 为空或非 JSON：后端未启动或代理层异常
  const text = await response.text()
  if (!text || text.trim().length === 0) {
    throw new ApiError(50000, '服务暂不可用：响应内容为空', response.status)
  }

  try {
    return JSON.parse(text) as ApiEnvelope<unknown>
  } catch {
    // 非 JSON 响应：后端未启动/代理层异常
    throw new ApiError(50000, '服务暂不可用或响应格式异常', response.status)
  }
}

/// <summary>React Query 缓存键工厂；调用方按业务模块命名空间扩展。</summary>
export const queryKeys = {
  users: ['users'] as const,
  roles: ['roles'] as const,
}