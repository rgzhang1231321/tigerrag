import { Button, Checkbox, Space, Spin, message } from 'antd'
import { forwardRef, useImperativeHandle, useMemo, useState } from 'react'
import { useApplyBatch, useRoleEndpoints } from './useRoleEndpoints'
import type {
  BatchEndpointChange,
  EndpointGrantViewDto,
  RoleEndpointMatrixDto,
} from './roleEndpointApi'

export interface RoleEndpointMatrixHandle {
  submit: () => Promise<void>
  isDirty: boolean
}

interface RoleEndpointMatrixProps {
  role: string
}

// 当前 endpoint 在考虑 pending 后的最终授权态。
function effectiveGranted(
  ep: EndpointGrantViewDto,
  pendingMap: Record<string, BatchEndpointChange>,
): boolean {
  return pendingMap[ep.endpointKey]?.granted ?? ep.granted
}

/// 角色-Endpoint 授权矩阵：按菜单分组展示 endpoint 授权状态，所有勾选只更新本地 pending；点击 Modal 底部"保存"统一提交一次批量接口。
export const RoleEndpointMatrix = forwardRef<RoleEndpointMatrixHandle, RoleEndpointMatrixProps>(
  function RoleEndpointMatrix({ role }, ref) {
  const { data: matrix, isPending, refetch } = useRoleEndpoints(role)
  const applyBatch = useApplyBatch(role)
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})
  // pending 按 endpointKey 索引：值为该 endpoint 的目标态（含 menuKey）。
  const [pending, setPending] = useState<Record<string, BatchEndpointChange>>({})

  const flatEndpoints = useMemo(() => {
    if (!matrix) return [] as EndpointGrantViewDto[]
    return matrix.menus.flatMap((menu) => menu.endpoints)
  }, [matrix])

  const summary = useMemo(() => {
    let granted = 0
    let total = 0
    for (const ep of flatEndpoints) {
      total += 1
      if (effectiveGranted(ep, pending)) granted += 1
    }
    return { granted, total }
  }, [flatEndpoints, pending])

  const isDirty = Object.keys(pending).length > 0

  useImperativeHandle(ref, () => ({
    submit,
    isDirty,
  }), [submit, isDirty])

  if (isPending) {
    return <Spin size="small" />
  }

  if (!matrix || matrix.menus.length === 0) {
    return <span style={{ color: 'rgba(0, 0, 0, 0.45)' }}>暂无可配置的接口</span>
  }

  const safeMatrix = matrix

  function toggleSingle(endpointKey: string, menuKey: string, current: boolean) {
    setPending((prev) => {
      const next = { ...prev }
      // 无论当前是否已授权，点击即翻转目标态；已授权 → 目标为撤销，未授权 → 目标为授予。
      next[endpointKey] = { menuKey, endpointKey, granted: !current }
      return next
    })
  }

  function toggleMenu(menu: RoleEndpointMatrixDto['menus'][0]) {
    const allGranted = menu.endpoints.every((ep) => effectiveGranted(ep, pending))
    setPending((prev) => {
      const next = { ...prev }
      for (const ep of menu.endpoints) {
        next[ep.endpointKey] = { menuKey: menu.menuKey, endpointKey: ep.endpointKey, granted: !allGranted }
      }
      return next
    })
  }

  function grantAll() {
    const menus = safeMatrix.menus
    setPending((prev) => {
      const next = { ...prev }
      for (const ep of flatEndpoints) {
        next[ep.endpointKey] = {
          menuKey: menus.find((m) => m.endpoints.some((e) => e.endpointKey === ep.endpointKey))!.menuKey,
          endpointKey: ep.endpointKey,
          granted: true,
        }
      }
      return next
    })
  }

  function revokeAll() {
    const menus = safeMatrix.menus
    setPending((prev) => {
      const next = { ...prev }
      for (const ep of flatEndpoints) {
        // 全不选 = 把每个 endpoint 标记为撤销目标（granted=false）。后端 batch 会把 false 项对应的现有授权行删除。
        next[ep.endpointKey] = {
          menuKey: menus.find((m) => m.endpoints.some((e) => e.endpointKey === ep.endpointKey))!.menuKey,
          endpointKey: ep.endpointKey,
          granted: false,
        }
      }
      return next
    })
  }

  async function submit() {
    // 提交全集：所有 endpoint 的最终授权态（含未变更的），后端按全集做替换式 diff。
    const endpoints: BatchEndpointChange[] = flatEndpoints.map((ep) => ({
      menuKey: matrix!.menus.find((m) => m.endpoints.some((e) => e.endpointKey === ep.endpointKey))!.menuKey,
      endpointKey: ep.endpointKey,
      granted: effectiveGranted(ep, pending),
    }))
    if (endpoints.length === 0) return
    try {
      const affected = await applyBatch.mutateAsync({ endpoints })
      message.success(`已保存 ${affected} 项变更`)
      setPending({})
      await refetch()
    } catch (error) {
      message.error(error instanceof Error ? error.message : '保存失败')
    }
  }

  return (
    <div className="role-endpoint-matrix">
      <Space style={{ marginBottom: 12 }} data-testid="role-endpoint-toolbar">
        <Button
          type="primary"
          size="small"
          onClick={grantAll}
          data-testid="role-endpoint-grant-all"
        >
          全选
        </Button>
        <Button
          size="small"
          onClick={revokeAll}
          data-testid="role-endpoint-revoke-all"
        >
          全不选
        </Button>
        <span style={{ color: 'rgba(0, 0, 0, 0.45)', fontSize: 12 }}>
          总计 {summary.granted}/{summary.total}
        </span>
      </Space>
      {matrix.menus.map((menu) => {
        const grantedCount = menu.endpoints.filter((ep) => effectiveGranted(ep, pending)).length
        const totalCount = menu.endpoints.length
        const allGrantedMenu = grantedCount === totalCount
        const isExpanded = expanded[menu.menuKey] !== false // 默认展开

        return (
          <div key={menu.menuKey} className="role-endpoint-menu-group" style={{ marginBottom: 16 }}>
            <div
              className="role-endpoint-menu-header"
              style={{ display: 'flex', alignItems: 'center', cursor: 'pointer', padding: '4px 0' }}
              onClick={() => setExpanded((prev) => ({ ...prev, [menu.menuKey]: !isExpanded }))}
            >
              <Checkbox
                checked={allGrantedMenu}
                indeterminate={grantedCount > 0 && !allGrantedMenu}
                onClick={(e) => e.stopPropagation()}
                onChange={() => toggleMenu(menu)}
              />
              <span style={{ marginLeft: 8, fontWeight: 500 }}>
                {menu.menuKey}
                <span style={{ marginLeft: 8, color: 'rgba(0, 0, 0, 0.45)', fontWeight: 400, fontSize: 12 }}>
                  已授权 {grantedCount}/{totalCount}
                </span>
              </span>
              <span style={{ marginLeft: 8, color: 'rgba(0, 0, 0, 0.3)', fontSize: 12 }}>
                {isExpanded ? '▼' : '▶'}
              </span>
            </div>
            {isExpanded && (
              <div className="role-endpoint-list" style={{ paddingLeft: 24, marginTop: 4 }}>
                {menu.endpoints.map((ep) => {
                  const granted = effectiveGranted(ep, pending)
                  return (
                    <div
                      key={ep.endpointKey}
                      className="role-endpoint-row"
                      style={{ display: 'flex', alignItems: 'center', padding: '2px 0' }}
                    >
                      <Checkbox
                        checked={granted}
                        onChange={() => toggleSingle(ep.endpointKey, menu.menuKey, granted)}
                      />
                      <span style={{ marginLeft: 8, fontFamily: 'monospace', fontSize: 12 }}>
                        {ep.httpMethod} {ep.path}
                      </span>
                      <span style={{ marginLeft: 12 }}>{ep.description}</span>
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
})