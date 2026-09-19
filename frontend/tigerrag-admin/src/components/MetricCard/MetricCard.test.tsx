import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MetricCard } from './MetricCard'

describe('MetricCard', () => {
  it('renders label and value', () => {
    render(<MetricCard label="知识库" value={12} loading={false} />)
    expect(screen.getByText('知识库')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
  })

  it('renders loading skeleton when loading is true', () => {
    const { container } = render(<MetricCard label="知识库" value={0} loading={true} />)
    expect(screen.getByText('知识库')).toBeInTheDocument()
    expect(container.querySelector('.ant-skeleton')).not.toBeNull()
  })
})
