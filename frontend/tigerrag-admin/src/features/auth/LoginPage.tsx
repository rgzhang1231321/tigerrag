import { LockOutlined, UserOutlined } from '@ant-design/icons'
import { Alert, Button, Form, Input } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { login } from './authApi'
import type { AuthSession } from './authStore'

interface LoginValues {
  userName: string
  password: string
}

export default function LoginPage({ onAuthenticated }: { onAuthenticated: (session: AuthSession) => void }) {
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function submit(values: LoginValues) {
    setSubmitting(true)
    setError(null)
    try {
      onAuthenticated(await login(values.userName, values.password))
      navigate('/', { replace: true })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '登录失败')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="login-page">
      <section className="login-brand" aria-label="TigerRAG 管理平台">
        <div className="brand-mark">TR</div>
        <h1>TigerRAG</h1>
        <p>企业知识库管理平台</p>
      </section>
      <section className="login-panel">
        <div className="login-form-wrap">
          <h2>登录</h2>
          <p className="login-caption">使用管理员分配的账号访问系统</p>
          {error && <Alert className="login-alert" type="error" message={error} showIcon />}
          <Form<LoginValues> layout="vertical" requiredMark={false} onFinish={submit}>
            <Form.Item label="用户名" name="userName" rules={[{ required: true, message: '请输入用户名' }]}>
              <Input prefix={<UserOutlined />} autoComplete="username" size="large" />
            </Form.Item>
            <Form.Item label="密码" name="password" rules={[{ required: true, message: '请输入密码' }]}>
              <Input.Password prefix={<LockOutlined />} autoComplete="current-password" size="large" />
            </Form.Item>
            <Button type="primary" htmlType="submit" size="large" block loading={submitting}>
              登录
            </Button>
          </Form>
        </div>
      </section>
    </main>
  )
}
