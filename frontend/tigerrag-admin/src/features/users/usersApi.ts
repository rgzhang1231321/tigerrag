import { md5 } from 'js-md5'
import { http } from '../../app/http'

export interface UserDto {
  id: string
  userName: string
  roles: string[]
  isLocked: boolean
}

interface ApiEnvelope<T> {
  flag: boolean
  code: number
  message: string
  data: T | null
}

/// <summary>取指定用户名的服务端 salt。独立走 fetch，刻意避开 http() 的 401 重试回路，避免自循环。</summary>
export async function fetchSalt(userName: string): Promise<string> {
  const response = await fetch('/api/auth/salt', {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userName }),
  })
  const envelope = (await response.json()) as ApiEnvelope<{ salt: string } | null>
  if (!envelope.flag || !envelope.data) {
    throw new Error(envelope.message || '无法获取 salt')
  }
  return envelope.data.salt
}

/// <summary>客户端按 MD5(password+salt) 计算传输层哈希，与服务端密码格式校验对齐。</summary>
export function computePasswordHash(password: string, salt: string): string {
  return md5(password + salt).toLowerCase()
}

/// <summary>列出全部用户及其角色。仅 users.manage 受权。</summary>
export function listUsers(): Promise<UserDto[]> {
  return http<UserDto[]>('/api/users/list', { method: 'POST' })
}

/// <summary>获取系统固定角色列表（按字母序）。仅 users.manage 受权。</summary>
export function listRoles(): Promise<string[]> {
  return http<string[]>('/api/users/roles/list', { method: 'POST' })
}

/// <summary>创建用户并分配初始角色；不设密码。返回的 DTO 不含 salt。</summary>
export function createUser(input: { userName: string; roles: string[] }): Promise<UserDto> {
  return http<UserDto>('/api/users', { method: 'POST', body: input })
}

/// <summary>为新用户设置初始密码。客户端需先调 fetchSalt 再 MD5 哈希。</summary>
export function setInitialPassword(input: { userId: string; passwordHash: string }): Promise<null> {
  return http<null>(`/api/users/${input.userId}/initial-password`, {
    method: 'POST',
    body: { passwordHash: input.passwordHash },
  })
}

/// <summary>替换指定用户的角色集合。</summary>
export function assignRoles(input: { userId: string; roles: string[] }): Promise<null> {
  return http<null>(`/api/users/${input.userId}/roles`, {
    method: 'POST',
    body: { roles: input.roles },
  })
}

/// <summary>为指定用户重置密码。会撤销该用户全部刷新会话，迫使其重新登录。</summary>
export function resetPassword(input: { userId: string; passwordHash: string }): Promise<null> {
  return http<null>(`/api/users/${input.userId}/password`, {
    method: 'POST',
    body: { passwordHash: input.passwordHash },
  })
}

/// <summary>删除指定用户。自保护：后端拒绝删除当前登录的自身账号。</summary>
export function deleteUser(userId: string): Promise<null> {
  return http<null>(`/api/users/${userId}/delete`, { method: 'POST' })
}

/// <summary>设置用户锁口：locked=true 锁定并撤销全部会话；false 解锁。自保护：不能锁定自身。</summary>
export function setUserLockout(input: { userId: string; locked: boolean }): Promise<null> {
  return http<null>(`/api/users/${input.userId}/lock`, {
    method: 'POST',
    body: { locked: input.locked },
  })
}