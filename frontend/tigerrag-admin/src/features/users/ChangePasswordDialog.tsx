import { Alert, Button, Form, Input, Modal } from 'antd'
import { useState } from 'react'
import { changePassword } from '../auth/authApi'

interface ChangePasswordDialogProps {
  open: boolean
  onCancel: () => void
  onChanged: () => void
}

/// <summary>当前用户自助改密：原密码 + 新密码 + 确认。成功后引导重新登录。</summary>
export function ChangePasswordDialog({ open, onCancel, onChanged }: ChangePasswordDialogProps) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  function reset() {
    setCurrentPassword('')
    setNewPassword('')
    setConfirm('')
    setError(null)
  }

  function handleCancel() {
    if (submitting) return
    reset()
    onCancel()
  }

  async function handleSubmit() {
    setError(null)
    if (currentPassword.length === 0) {
      setError('请填写当前密码')
      return
    }
    if (newPassword.length < 8) {
      setError('新密码至少 8 位')
      return
    }
    if (newPassword !== confirm) {
      setError('两次输入的新密码不一致')
      return
    }
    setSubmitting(true)
    try {
      await changePassword(currentPassword, newPassword)
      reset()
      onChanged()
    } catch (mutationError) {
      setError(mutationError instanceof Error ? mutationError.message : '修改失败')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title="修改密码"
      open={open}
      onCancel={handleCancel}
      destroyOnHidden
      footer={null}
      maskClosable={false}
    >
      {error !== null && <Alert type="error" message={error} showIcon className="users-alert" />}
      <Form layout="vertical" className="users-form">
        <Form.Item label="当前密码" required>
          <Input.Password
            value={currentPassword}
            onChange={(event) => setCurrentPassword(event.target.value)}
          />
        </Form.Item>
        <Form.Item label="新密码" required>
          <Input.Password
            value={newPassword}
            onChange={(event) => setNewPassword(event.target.value)}
          />
        </Form.Item>
        <Form.Item label="确认新密码" required>
          <Input.Password
            value={confirm}
            onChange={(event) => setConfirm(event.target.value)}
          />
        </Form.Item>
      </Form>
      <div className="users-form-actions">
        <Button onClick={handleCancel} disabled={submitting}>
          取消
        </Button>
        <Button type="primary" loading={submitting} onClick={handleSubmit}>
          提交
        </Button>
      </div>
    </Modal>
  )
}
