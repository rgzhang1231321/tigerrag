import { Button, Result } from 'antd'
import { Component, type ReactNode } from 'react'

interface Props {
  children: ReactNode
}

interface State {
  error: Error | null
}

/// <summary>
/// 顶层错误边界：单个子组件抛错时不让整棵 React 树卸载，
/// 否则用户连"导航切几次"后看到的就是空白页（"页面死了"）。
/// </summary>
export class ErrorBoundary extends Component<Props, State> {
  override state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  override componentDidCatch(error: Error): void {
    // 仅输出到 console.error；不上报服务，避免引入未授权依赖。
    console.error('[ErrorBoundary] 捕获到未处理渲染错误:', error)
  }

  private handleReload = () => {
    this.setState({ error: null })
  }

  override render(): ReactNode {
    if (this.state.error !== null) {
      return (
        <Result
          status="error"
          title="页面渲染出错"
          subTitle={this.state.error.message}
          extra={
            <Button type="primary" onClick={this.handleReload}>
              重试
            </Button>
          }
        />
      )
    }
    return this.props.children
  }
}