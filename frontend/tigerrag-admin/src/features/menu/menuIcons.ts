import {
  AuditOutlined,
  BookOutlined,
  DashboardOutlined,
  FileTextOutlined,
  MenuOutlined,
  MessageOutlined,
  SafetyOutlined,
  SettingOutlined,
  TeamOutlined,
} from '@ant-design/icons'
import type { ComponentType } from 'react'

/// <summary>图标名 → Antd 图标组件映射。后端只存名称，前端按名渲染。</summary>
export const menuIconMap: Record<string, ComponentType> = {
  DashboardOutlined,
  BookOutlined,
  FileTextOutlined,
  MessageOutlined,
  SettingOutlined,
  TeamOutlined,
  AuditOutlined,
  SafetyOutlined,
  MenuOutlined,
}

export const AVAILABLE_ICONS = Object.keys(menuIconMap)
