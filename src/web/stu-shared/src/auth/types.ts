export type Portal = 'main' | 'admin'

export type Role = {
  name: string
  displayName: string
}

export type HealthUnit = {
  id: string
  code: string
  name: string
}

export type Session = {
  id: string
  userName: string
  displayName: string
  mustChangePassword: boolean
  healthUnit: HealthUnit | null
  roles: Role[]
  permissions: string[]
}

export type AuthenticatedContext = {
  session: Session
  logout: () => Promise<void>
}

export type ProblemDetails = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

