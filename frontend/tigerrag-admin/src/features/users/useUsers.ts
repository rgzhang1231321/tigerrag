import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { refreshSession } from '../auth/authApi'
import { useAuthStore } from '../auth/authStore'
import { queryKeys } from '../../app/http'
import { listRolesAll } from '../roles/rolesApi'
import type { RoleDto } from '../roles/rolesApi'
import {
  assignRoles,
  computePasswordHash,
  createUser,
  deleteUser,
  fetchSalt,
  listUsers,
  resetPassword,
  setInitialPassword,
  setUserLockout,
} from './usersApi'
import type { UserDto } from './usersApi'

/// <summary>列出全部用户（带角色）。缓存键 queryKeys.users。</summary>
export function useUsers() {
  return useQuery({
    queryKey: queryKeys.users,
    queryFn: listUsers,
  })
}

/// <summary>
/// 列出全部角色名（按字母序）。后端返回 RoleDto 列表，前端只取 name 字段。
/// 缓存键 queryKeys.roles 与 useRoles 共享，通过 select 在视图层转为 string[]，避免重写缓存把 useRoles 的 RoleDto[] 数据抹掉。
/// 角色 CRUD 后 useRoles 的 onSuccess 会 invalidateQueries 该键，本 hook 自动联动刷新。
/// </summary>
export function useUserRoles() {
  return useQuery<RoleDto[], Error, string[]>({
    queryKey: queryKeys.roles,
    queryFn: listRolesAll,
    select: (roles) => roles.map((role) => role.name).sort((a, b) => a.localeCompare(b)),
  })
}

/// <summary>
/// 创建用户并设置初始密码：自动拼装 salt+MD5，把两步合并为一个 mutation。
/// 成功时让列表缓存失效以触发重拉。
/// </summary>
export function useCreateUserWithPassword() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { userName: string; roles: string[]; password: string }) => {
      const created = await createUser({ userName: input.userName, roles: input.roles })
      const salt = await fetchSalt(created.userName)
      const passwordHash = computePasswordHash(input.password, salt)
      await setInitialPassword({ userId: created.id, passwordHash })
      return created
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.users })
    },
  })
}

/// <summary>为指定用户重置密码：自动取 salt+MD5。成功后让列表失效。</summary>
export function useResetPassword() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { user: UserDto; password: string }) => {
      const salt = await fetchSalt(input.user.userName)
      const passwordHash = computePasswordHash(input.password, salt)
      await resetPassword({ userId: input.user.id, passwordHash })
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.users })
    },
  })
}

/// <summary>
/// 替换用户的角色集合。若变更影响当前登录用户，触发刷新让内存里的角色与权限 claim 同步。
/// </summary>
export function useAssignRoles(currentUserId: string | undefined) {
  const queryClient = useQueryClient()
  const setSession = useAuthStore((state) => state.setSession)
  return useMutation({
    mutationFn: async (input: { user: UserDto; roles: string[] }) => {
      await assignRoles({ userId: input.user.id, roles: input.roles })
    },
    onSuccess: async (_void, variables) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.users })
      if (currentUserId !== undefined && variables.user.id === currentUserId) {
        const session = await refreshSession()
        setSession(session)
      }
    },
  })
}

/// <summary>删除用户。成功后让列表缓存失效。</summary>
export function useDeleteUser() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (user: UserDto) => {
      await deleteUser(user.id)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.users })
    },
  })
}

/// <summary>设置用户锁口。成功后让列表缓存失效。</summary>
export function useSetUserLockout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { user: UserDto; locked: boolean }) => {
      await setUserLockout({ userId: input.user.id, locked: input.locked })
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.users })
    },
  })
}