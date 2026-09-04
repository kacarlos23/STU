export type HealthUnitItem = {
  id: string
  code: string
  name: string
  createdAtUtc: string
  updatedAtUtc: string | null
  archivedAtUtc: string | null
  activeUsers: number
}

export type RoleItem = {
  id: string
  name: string
  displayName: string
  description: string | null
  isSystem: boolean
  archivedAtUtc: string | null
  permissions: string[]
}

export type PermissionItem = {
  value: string
  displayName: string
  category: string
}

export type UserItem = {
  id: string
  userName: string
  displayName: string
  healthUnitId: string | null
  healthUnit: Pick<HealthUnitItem, 'id' | 'code' | 'name' | 'archivedAtUtc'> | null
  role: Pick<RoleItem, 'id' | 'name' | 'displayName' | 'isSystem' | 'archivedAtUtc'> | null
  mustChangePassword: boolean
  lockoutEnd: string | null
  archivedAtUtc: string | null
}

export type AuditItem = {
  id: string
  occurredAtUtc: string
  actorUserName: string
  action: string
  entityType: string
  entityId: string
  summary: string
  beforeJson: string | null
  afterJson: string | null
  ipAddress: string | null
}

export type BackupSettingsItem = {
  enabled: boolean
  dayOfWeek: 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'
  localHour: number
  retentionCount: number
  timeZoneId: string
  updatedAtUtc: string | null
}

export type BackupRunItem = {
  id: string
  trigger: 'Manual' | 'Scheduled'
  status: 'Queued' | 'Running' | 'Completed' | 'Failed'
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  fileName: string | null
  sizeBytes: number | null
  sha256: string | null
  errorSummary: string | null
  filePrunedAtUtc: string | null
  attemptCount: number
  canDownload: boolean
}

export type MonitoringStatusItem = {
  checkedAtUtc: string
  overallStatus: 'Healthy' | 'Warning' | 'Critical'
  checks: {
    id: string
    label: string
    status: 'Healthy' | 'Warning' | 'Critical'
    detail: string
    lastObservedAtUtc: string | null
  }[]
  alerts: {
    code: string
    severity: 'Warning' | 'Critical'
    title: string
    detail: string
  }[]
}

export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number }

export class AdminApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

async function readError(response: Response): Promise<string> {
  try {
    const problem = await response.json() as { detail?: string; title?: string; errors?: Record<string, string[]> }
    return Object.values(problem.errors ?? {}).flat()[0] ?? problem.detail ?? problem.title ?? 'Não foi possível concluir a operação.'
  } catch {
    return 'Não foi possível concluir a operação.'
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: { Accept: 'application/json', ...init?.headers },
  })
  if (!response.ok) throw new AdminApiError(await readError(response), response.status)
  if (response.status === 204) return undefined as T
  return await response.json() as T
}

async function mutate<T>(method: 'POST' | 'PUT', path: string, body?: unknown): Promise<T> {
  const { token } = await request<{ token: string }>('/api/auth/csrf')
  return await request<T>(path, {
    method,
    headers: {
      'X-STU-CSRF': token,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
}

export const adminApi = {
  healthUnits: (includeArchived = true) => request<HealthUnitItem[]>(`/api/admin/health-units?includeArchived=${includeArchived}`),
  createHealthUnit: (body: { code: string; name: string }) => mutate<HealthUnitItem>('POST', '/api/admin/health-units', body),
  updateHealthUnit: (id: string, body: { code: string; name: string }) => mutate<void>('PUT', `/api/admin/health-units/${id}`, body),
  archiveHealthUnit: (id: string) => mutate<void>('POST', `/api/admin/health-units/${id}/archive`),
  restoreHealthUnit: (id: string) => mutate<void>('POST', `/api/admin/health-units/${id}/restore`),

  users: (query = '') => request<Paged<UserItem>>(`/api/admin/users?pageSize=100&includeArchived=true&query=${encodeURIComponent(query)}`),
  createUser: (body: { userName: string; displayName: string; healthUnitId: string | null; roleName: string }) =>
    mutate<{ userId: string; userName: string; temporaryPassword: string }>('POST', '/api/admin/users', body),
  updateUser: (id: string, body: { displayName: string; healthUnitId: string | null; roleName: string }) =>
    mutate<void>('PUT', `/api/admin/users/${id}`, body),
  resetUserPassword: (id: string) => mutate<{ userId: string; temporaryPassword: string }>('POST', `/api/admin/users/${id}/reset-password`),
  archiveUser: (id: string) => mutate<void>('POST', `/api/admin/users/${id}/archive`),
  restoreUser: (id: string) => mutate<void>('POST', `/api/admin/users/${id}/restore`),

  roles: (includeArchived = true) => request<RoleItem[]>(`/api/admin/roles?includeArchived=${includeArchived}`),
  permissions: () => request<PermissionItem[]>('/api/admin/permissions'),
  createRole: (body: { displayName: string; description: string; permissions: string[] }) => mutate<{ id: string; name: string }>('POST', '/api/admin/roles', body),
  updateRole: (id: string, body: { displayName: string; description: string; permissions: string[] }) => mutate<void>('PUT', `/api/admin/roles/${id}`, body),
  archiveRole: (id: string) => mutate<void>('POST', `/api/admin/roles/${id}/archive`),
  restoreRole: (id: string) => mutate<void>('POST', `/api/admin/roles/${id}/restore`),

  audit: (entityType = '') => request<Paged<AuditItem>>(`/api/admin/audit?pageSize=100&entityType=${encodeURIComponent(entityType)}`),

  backupSettings: () => request<BackupSettingsItem>('/api/admin/backups/settings'),
  updateBackupSettings: (body: Pick<BackupSettingsItem, 'enabled' | 'dayOfWeek' | 'localHour' | 'retentionCount'>) =>
    mutate<BackupSettingsItem>('PUT', '/api/admin/backups/settings', body),
  backupRuns: () => request<BackupRunItem[]>('/api/admin/backups/runs'),
  requestBackup: () => mutate<Pick<BackupRunItem, 'id' | 'trigger' | 'status' | 'requestedAtUtc'>>('POST', '/api/admin/backups/runs'),

  monitoringStatus: () => request<MonitoringStatusItem>('/api/admin/monitoring/status'),
}
