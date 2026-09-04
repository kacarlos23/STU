import type { Portal, ProblemDetails, Session } from './types'

export class ApiError extends Error {
  readonly status: number
  readonly validationErrors: Record<string, string[]>

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? 'Não foi possível concluir a operação.')
    this.name = 'ApiError'
    this.status = status
    this.validationErrors = problem.errors ?? {}
  }
}

async function parseProblem(response: Response): Promise<ProblemDetails> {
  try {
    return await response.json() as ProblemDetails
  } catch {
    return { detail: 'O servidor não conseguiu concluir a solicitação.' }
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...init?.headers,
    },
  })

  if (!response.ok) {
    throw new ApiError(response.status, await parseProblem(response))
  }

  if (response.status === 204) {
    return undefined as T
  }

  return await response.json() as T
}

async function getCsrfToken(): Promise<string> {
  const response = await request<{ token: string }>('/api/auth/csrf')
  return response.token
}

async function post<T>(path: string, body?: unknown): Promise<T> {
  const token = await getCsrfToken()
  return await request<T>(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-STU-CSRF': token,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
}

export async function getCurrentSession(): Promise<Session | null> {
  try {
    return await request<Session>('/api/auth/me')
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null
    }
    throw error
  }
}

export async function login(
  userName: string,
  password: string,
  portal: Portal,
): Promise<Session> {
  return await post<Session>('/api/auth/login', { userName, password, portal })
}

export async function changePassword(
  currentPassword: string,
  newPassword: string,
  confirmPassword: string,
): Promise<Session> {
  return await post<Session>('/api/auth/change-password', {
    currentPassword,
    newPassword,
    confirmPassword,
  })
}

export async function logout(): Promise<void> {
  await post<void>('/api/auth/logout')
}

