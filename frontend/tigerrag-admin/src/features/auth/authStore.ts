import { create } from 'zustand'

export interface AuthUser {
  id: string
  userName: string
  roles: string[]
}

export interface AuthSession {
  accessToken: string
  expiresAt: string
  user: AuthUser
}

interface AuthState {
  accessToken: string | null
  user: AuthUser | null
  setSession: (session: AuthSession) => void
  clear: () => void
  /// <summary>仅清空内存态，保留 sessionStorage 缓存；用于刷新失败时保留缓存供下次恢复。</summary>
  clearMemoryOnly: () => void
}

const SESSION_KEY = 'tigerrag.session'

/// <summary>从 sessionStorage 读取缓存的会话；sessionStorage 在 F5 刷新后保留，关闭标签页后清除。</summary>
function readCachedSession(): AuthSession | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY)
    if (raw === null) return null
    return JSON.parse(raw) as AuthSession
  } catch {
    return null
  }
}

function writeCachedSession(session: AuthSession): void {
  try {
    sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
  } catch {
    // sessionStorage 不可用（隐私模式等）：降级为纯内存，刷新会掉。
  }
}

function clearCachedSession(): void {
  try {
    sessionStorage.removeItem(SESSION_KEY)
  } catch {
    // ignore
  }
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  user: null,
  setSession: (session) => {
    writeCachedSession(session)
    set({ accessToken: session.accessToken, user: session.user })
  },
  clear: () => {
    clearCachedSession()
    set({ accessToken: null, user: null })
  },
  clearMemoryOnly: () => set({ accessToken: null, user: null }),
}))

/// <summary>页面加载时调用：从 sessionStorage 恢复会话（如果有），避免 F5 后因 cookie 未发送导致闪跳登录页。</summary>
export function restoreSessionFromCache(): AuthSession | null {
  const cached = readCachedSession()
  if (cached === null) return null
  useAuthStore.getState().setSession(cached)
  return cached
}
