import http from './http'

export interface ServerDto {
  id: number
  agentId: string
  name: string
  hostname?: string
  dhcpVersion?: string
  agentVersion?: string
  online: boolean
  lastSeenUtc?: string
  siteId?: number | null
  siteName?: string | null
}

export interface Site {
  id: number
  name: string
  serverCount: number
}

export interface AlertConfig {
  enabled: boolean
  webhookUrl: string
  threshold: number
  intervalMinutes: number
}

export interface Scope {
  scopeId: string
  name: string
  description?: string
  subnetMask: string
  startRange: string
  endRange: string
  state: string
  leaseDuration?: string
}

export interface ScopeStats {
  scopeId: string
  inUse: number
  free: number
  percentageInUse: number
  reserved: number
}

export interface Lease {
  ipAddress: string
  scopeId?: string
  clientId?: string
  hostName?: string
  addressState?: string
  leaseExpiryTime?: string
}

export interface Reservation {
  ipAddress: string
  scopeId?: string
  clientId?: string
  name?: string
  description?: string
  type?: string
}

export interface OptionValue {
  optionId: number
  name?: string
  value: string[]
}

export interface LeaseLog {
  id: number
  agentId: number
  serverName: string
  scopeId: string
  scopeName?: string
  ipAddress: string
  clientId?: string
  hostName?: string
  seenAtUtc: string
}

export interface LeaseLogPage {
  total: number
  items: LeaseLog[]
}

export interface UserDto {
  id: number
  username: string
  role: string
  enabled: boolean
  createdAt: string
}

export interface LoginResponse {
  token: string
  username: string
  role: string
  expiresAt: string
}

export const api = {
  // 鉴权
  login: (username: string, password: string) =>
    http.post<LoginResponse>('/auth/login', { username, password }),
  me: () => http.get<{ username: string; role: string }>('/auth/me'),
  changePassword: (oldPassword: string, newPassword: string) =>
    http.post<{ message: string }>('/auth/change-password', { oldPassword, newPassword }),

  // 用户管理(仅 Admin)
  users: () => http.get<UserDto[]>('/users'),
  createUser: (username: string, password: string, role: string) =>
    http.post<UserDto>('/users', { username, password, role }),
  updateUser: (id: number, body: { role?: string; enabled?: boolean; password?: string }) =>
    http.put<UserDto>(`/users/${id}`, body),
  deleteUser: (id: number) => http.delete(`/users/${id}`),

  // Agent 安装信息
  agentSetupInfo: () =>
    http.get<{
      downloadUrl: string
      platformUrl: string
      autoPlatformUrl: string
      platformUrlConfigured: boolean
      enrollmentSecret: string
      enrollmentSecretConfigured: boolean
      installCommand: string
    }>('/agent/setup-info'),
  // 设置「接入服务器」使用的平台对外地址;url 传空字符串表示恢复自动识别
  setPlatformUrl: (url: string) =>
    http.put<{ message: string; url?: string }>('/agent/platform-url', { url }),
  // 设置 Agent 连接密钥;secret 传空字符串表示恢复服务器默认密钥
  setEnrollmentSecret: (secret: string) =>
    http.put<{ message: string }>('/agent/enrollment-secret', { secret }),

  // 工区
  sites: () => http.get<Site[]>('/sites'),
  createSite: (name: string) => http.post<Site>('/sites', { name }),
  renameSite: (id: number, name: string) => http.put<Site>(`/sites/${id}`, { name }),
  deleteSite: (id: number) => http.delete(`/sites/${id}`),

  // 服务器(Agent)
  servers: () => http.get<ServerDto[]>('/servers'),
  renameServer: (id: number, name: string) => http.put(`/servers/${id}`, { name }),
  deleteServer: (id: number) => http.delete(`/servers/${id}`),
  assignServerSite: (id: number, siteId: number | null) =>
    http.put<ServerDto>(`/servers/${id}/site`, { siteId }),
  setServerCallbackUrl: (id: number, url: string) =>
    http.put<{ message: string; url: string }>(`/servers/${id}/callback-url`, { url }),
  health: (id: number) => http.post(`/servers/${id}/health`),

  // 作用域
  scopes: (id: number) => http.get<Scope[]>(`/servers/${id}/scopes`),
  allScopeStats: (id: number) => http.get<ScopeStats[]>(`/servers/${id}/scope-statistics`),
  scopeStats: (id: number, scopeId: string) =>
    http.get<ScopeStats>(`/servers/${id}/scopes/${scopeId}/statistics`),
  createScope: (id: number, body: any) => http.post(`/servers/${id}/scopes`, body),
  updateScope: (id: number, scopeId: string, body: any) =>
    http.put(`/servers/${id}/scopes/${scopeId}`, body),
  deleteScope: (id: number, scopeId: string) => http.delete(`/servers/${id}/scopes/${scopeId}`),

  // 租约
  leases: (id: number, scopeId: string) =>
    http.get<Lease[]>(`/servers/${id}/scopes/${scopeId}/leases`),

  // 保留
  reservations: (id: number, scopeId: string) =>
    http.get<Reservation[]>(`/servers/${id}/scopes/${scopeId}/reservations`),
  addReservation: (id: number, scopeId: string, body: any) =>
    http.post(`/servers/${id}/scopes/${scopeId}/reservations`, body),
  removeReservation: (id: number, scopeId: string, ip: string) =>
    http.delete(`/servers/${id}/scopes/${scopeId}/reservations`, { params: { ip } }),

  // 选项
  options: (id: number, scopeId?: string) =>
    http.get<OptionValue[]>(`/servers/${id}/options`, { params: scopeId ? { scopeId } : {} }),
  setOption: (id: number, body: any) => http.put(`/servers/${id}/options`, body),
  removeOption: (id: number, optionId: number, scopeId?: string) =>
    http.delete(`/servers/${id}/options/${optionId}`, { params: scopeId ? { scopeId } : {} }),

  // 租约日志
  leaseLogs: (params: { q?: string; serverId?: number; page?: number; pageSize?: number }) =>
    http.get<LeaseLogPage>('/lease-logs', { params }),
  clearLeaseLogs: () => http.delete<{ message: string; deleted: number; retainDays: number }>('/lease-logs'),

  // 告警
  alertConfig: () => http.get<AlertConfig>('/alerts/config'),
  saveAlertConfig: (body: AlertConfig) => http.put<AlertConfig>('/alerts/config', body),
  testAlert: (webhookUrl?: string) => http.post<{ message: string }>('/alerts/test', { webhookUrl }),
}
