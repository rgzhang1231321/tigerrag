import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Breadcrumb } from './Breadcrumb'

describe('Breadcrumb', () => {
  it('renders only home for the dashboard route', () => {
    renderInRouter('/')
    expect(screen.getByText('首页')).toBeInTheDocument()
    expect(screen.queryByText('用户管理')).not.toBeInTheDocument()
  })

  it('renders home + module for known routes', () => {
    renderInRouter('/users')
    expect(screen.getByText('首页')).toBeInTheDocument()
    expect(screen.getByText('用户管理')).toBeInTheDocument()
  })

  it('renders home only for unknown routes', () => {
    renderInRouter('/some-unknown-route')
    expect(screen.getByText('首页')).toBeInTheDocument()
    expect(screen.queryByText('用户管理')).not.toBeInTheDocument()
  })
})

function renderInRouter(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Breadcrumb />
    </MemoryRouter>,
  )
}
