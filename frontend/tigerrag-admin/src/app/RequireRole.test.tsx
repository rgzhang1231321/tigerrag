import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '../features/auth/authStore'
import { RequireRole } from './RequireRole'

function renderWithRouter(ui: React.ReactNode, initialPath = '/') {
  return render(<MemoryRouter initialEntries={[initialPath]}>{ui}</MemoryRouter>)
}

describe('RequireRole', () => {
  beforeEach(() => {
    useAuthStore.getState().clear()
  })

  afterEach(() => {
    useAuthStore.getState().clear()
  })

  it('renders children when the user has the required role', () => {
    useAuthStore.getState().setSession({
      accessToken: 'token',
      expiresAt: '2026-09-17T00:00:00Z',
      user: { id: 'u1', userName: 'admin', roles: ['Admin'] },
    })

    renderWithRouter(
      <RequireRole roles={['Admin']}>
        <div>secret-content</div>
      </RequireRole>,
    )

    expect(screen.getByText('secret-content')).toBeInTheDocument()
    expect(screen.queryByText('403')).not.toBeInTheDocument()
  })

  it('renders children when the user has the required permission', () => {
    useAuthStore.getState().setSession({
      accessToken: 'token',
      expiresAt: '2026-09-17T00:00:00Z',
      user: { id: 'u2', userName: 'auditor', roles: ['Auditor'] },
    })

    renderWithRouter(
      <RequireRole permission="audit.read">
        <div>audit-only</div>
      </RequireRole>,
    )

    expect(screen.getByText('audit-only')).toBeInTheDocument()
  })

  it('blocks access when the user lacks the required role', () => {
    useAuthStore.getState().setSession({
      accessToken: 'token',
      expiresAt: '2026-09-17T00:00:00Z',
      user: { id: 'u3', userName: 'viewer', roles: ['Viewer'] },
    })

    renderWithRouter(
      <RequireRole roles={['Admin']}>
        <div>secret-content</div>
      </RequireRole>,
    )

    expect(screen.queryByText('secret-content')).not.toBeInTheDocument()
    expect(screen.getByText('403')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '返回系统概览' })).toBeInTheDocument()
  })

  it('blocks access when the user lacks the required permission', () => {
    useAuthStore.getState().setSession({
      accessToken: 'token',
      expiresAt: '2026-09-17T00:00:00Z',
      user: { id: 'u4', userName: 'viewer', roles: ['Viewer'] },
    })

    renderWithRouter(
      <RequireRole permission="users.manage">
        <div>admin-only</div>
      </RequireRole>,
    )

    expect(screen.queryByText('admin-only')).not.toBeInTheDocument()
    expect(screen.getByText('403')).toBeInTheDocument()
  })

  it('blocks access when no user is signed in', () => {
    renderWithRouter(
      <RequireRole roles={['Admin']}>
        <div>secret-content</div>
      </RequireRole>,
    )

    expect(screen.queryByText('secret-content')).not.toBeInTheDocument()
    expect(screen.getByText('403')).toBeInTheDocument()
  })

  it('passes when either role or permission is satisfied', () => {
    useAuthStore.getState().setSession({
      accessToken: 'token',
      expiresAt: '2026-09-17T00:00:00Z',
      user: { id: 'u5', userName: 'editor', roles: ['Editor'] },
    })

    renderWithRouter(
      <RequireRole roles={['Admin']} permission="documents.manage">
        <div>editor-area</div>
      </RequireRole>,
    )

    expect(screen.getByText('editor-area')).toBeInTheDocument()
  })
})