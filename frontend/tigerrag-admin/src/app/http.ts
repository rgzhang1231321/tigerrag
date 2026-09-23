import { message } from 'antd'
import { refreshSession } from '../features/auth/authApi'
import { useAuthStore, type AuthSession } from '../features/auth/authStore'

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
  // FormData 由浏览器自动设置 Content-Type（含 boundary）；不可手动 JSON.stringify。
  if (body !== undefined && !(body instanceof FormData)) {
    requestInit.body = JSON.stringify(body)
  } else if (body instanceof FormData) {
    requestInit.body = body
  }

  try {
    return await execute<T>(path, requestInit, retryOnAuth)
  } catch (error) {
    if (error instanceof ApiError) {
      showError(error)
      if (error.code === 40100 && retryOnAuth) {
        // 刷新失败：清空登录态触发 App.tsx 的 if (!user) → LoginPage。
        // 40100 是 refresh token 无效或 cookie 缺失的明确信号，无静默重试空间。
        useAuthStore.getState().clear()
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
  // FormData：Content-Type 必须由浏览器补 boundary，强制覆盖避免 JSON 默认头干扰。
  if (body instanceof FormData) {
    delete headers['Content-Type']
  } else if (body !== undefined && headers['Content-Type'] === undefined) {
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

/// <summary>刷新单例：并发 refresh 复用同一 Promise，避免后端 atomic rotate 撤销旧 token 后第二次 refresh 拿旧 cookie 失败。</summary>
let refreshInFlight: Promise<AuthSession | null> | null = null

/// <summary>并发调用共享同一刷新请求；返回 null 表示刷新失败。</summary>
export async function refreshSessionShared(): Promise<AuthSession | null> {
  if (refreshInFlight !== null) {
    return refreshInFlight
  }

  refreshInFlight = (async () => {
    try {
      return await refreshSession()
    } catch {
      return null
    } finally {
      // 完成后清空单例：下一次 401 触发新一次 refresh。
      refreshInFlight = null
    }
  })()

  return refreshInFlight
}

async function tryRefresh(): Promise<boolean> {
  const session = await refreshSessionShared()
  if (session === null) {
    return false
  }
  useAuthStore.getState().setSession(session)
  return true
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
  roleEndpoints: (role: string) => ['role-endpoints', role] as const,
  knowledgeBases: ['knowledge-bases'] as const,
  documents: (kbId: string | null) => ['documents', kbId ?? 'all'] as const,
}