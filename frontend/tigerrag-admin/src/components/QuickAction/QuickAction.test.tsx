import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { QuickAction } from './QuickAction'
import { useNavigate } from 'react-router-dom'

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>()
  return { ...actual, useNavigate: vi.fn() }
})

describe('QuickAction', () => {
  it('renders three action buttons', () => {
    render(<QuickAction />)
    expect(screen.getByText('新建知识库')).toBeInTheDocument()
    expect(screen.getByText('上传文档')).toBeInTheDocument()
    expect(screen.getByText('新建用户')).toBeInTheDocument()
  })

  it('navigates to target route on click', () => {
    const navigate = vi.fn()
    vi.mocked(useNavigate).mockReturnValue(navigate)

    render(<QuickAction />)
    screen.getByText('新建知识库').click()
    expect(navigate).toHaveBeenCalledWith('/knowledge-bases')
  })
})
