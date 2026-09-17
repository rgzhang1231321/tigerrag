import { describe, expect, it } from 'vitest'
import type { AuthUser } from './authStore'
import {
  ALL_ROLES,
  can,
  hasAnyRole,
  hasRole,
  listPermissions,
} from './permissions'

function user(roles: string[]): AuthUser {
  return { id: 'user-id', userName: 'tester', roles }
}

describe('permissions', () => {
  describe('hasRole', () => {
    it('grants when the role is present', () => {
      expect(hasRole(user(['Admin']), 'Admin')).toBe(true)
    })

    it('denies when the role is absent', () => {
      expect(hasRole(user(['Viewer']), 'Admin')).toBe(false)
    })

    it('denies when the user is null', () => {
      expect(hasRole(null, 'Admin')).toBe(false)
    })
  })

  describe('hasAnyRole', () => {
    it('grants when any of the required roles is present', () => {
      expect(hasAnyRole(user(['Viewer']), ['Admin', 'Viewer'])).toBe(true)
    })

    it('denies when none of the required roles are present', () => {
      expect(hasAnyRole(user(['Viewer']), ['Admin', 'KbManager'])).toBe(false)
    })

    it('denies when the user is null', () => {
      expect(hasAnyRole(null, ['Admin'])).toBe(false)
    })
  })

  describe('can', () => {
    it('Admin has users.manage', () => {
      expect(can(user(['Admin']), 'users.manage')).toBe(true)
    })

    it('KbManager does not have users.manage', () => {
      expect(can(user(['KbManager']), 'users.manage')).toBe(false)
    })

    it('Editor has documents.manage but not users.manage', () => {
      expect(can(user(['Editor']), 'documents.manage')).toBe(true)
      expect(can(user(['Editor']), 'users.manage')).toBe(false)
    })

    it('Viewer only has chat.use', () => {
      expect(can(user(['Viewer']), 'chat.use')).toBe(true)
      expect(can(user(['Viewer']), 'documents.manage')).toBe(false)
      expect(can(user(['Viewer']), 'audit.read')).toBe(false)
    })

    it('Auditor only has audit.read', () => {
      expect(can(user(['Auditor']), 'audit.read')).toBe(true)
      expect(can(user(['Auditor']), 'users.manage')).toBe(false)
      expect(can(user(['Auditor']), 'chat.use')).toBe(false)
    })

    it('null user is denied', () => {
      expect(can(null, 'chat.use')).toBe(false)
    })

    it('unknown role names are silently ignored', () => {
      expect(can(user(['NonExistent']), 'chat.use')).toBe(false)
    })
  })

  describe('listPermissions', () => {
    it('returns the full set for Admin', () => {
      expect(listPermissions(user(['Admin']))).toEqual(
        new Set([
          'users.manage',
          'knowledge-bases.manage',
          'documents.manage',
          'chat.use',
          'audit.read',
        ]),
      )
    })

    it('returns an empty set for null user', () => {
      expect(listPermissions(null).size).toBe(0)
    })
  })

  describe('ALL_ROLES', () => {
    it('contains every system role in alphabetical order', () => {
      expect(ALL_ROLES).toEqual(['Admin', 'Auditor', 'Editor', 'KbManager', 'Viewer'])
    })
  })

  // 镜像后端 src/TigerRAG.Application/Security/RolePermissionMap.cs。
  // 任一项失败都意味着前后端权限模型漂移，必须先停下同步再发布。
  describe('mirrors backend RolePermissionMap', () => {
    it.each([
      [
        'Admin',
        [
          'users.manage',
          'knowledge-bases.manage',
          'documents.manage',
          'chat.use',
          'audit.read',
        ],
      ],
      ['KbManager', ['knowledge-bases.manage', 'documents.manage', 'chat.use']],
      ['Editor', ['documents.manage', 'chat.use']],
      ['Viewer', ['chat.use']],
      ['Auditor', ['audit.read']],
    ])('%s → %j', (role, expected) => {
      expect(listPermissions(user([role]))).toEqual(new Set(expected))
    })
  })
})