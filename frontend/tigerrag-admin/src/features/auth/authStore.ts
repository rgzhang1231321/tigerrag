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
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  user: null,
  setSession: (session) => set({ accessToken: session.accessToken, user: session.user }),
  clear: () => set({ accessToken: null, user: null }),
}))
