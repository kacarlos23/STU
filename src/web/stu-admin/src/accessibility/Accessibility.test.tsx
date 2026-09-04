import axe from 'axe-core'
import { cleanup, render, screen } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Session } from '@stu/shared'
import { App } from '../app/App'
import { AdminDashboard } from '../features/dashboard/AdminDashboard'

const wcagTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']

describe('WCAG 2.2 AA — administração global', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('não encontra violações automatizadas no login administrativo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', {
      status: 401,
      headers: { 'Content-Type': 'application/json' },
    })))

    const { container } = render(<App />)
    await screen.findByRole('heading', { name: 'Entre na sua conta' })
    await expectAccessible(container)
  })

  it('não encontra violações automatizadas na visão geral global', async () => {
    mockAdminRequests()
    const { container } = render(<AdminDashboard logout={vi.fn()} session={adminSession} />)

    await screen.findByRole('heading', { name: 'Visão geral' })
    await expectAccessible(container)
  })
})

async function expectAccessible(container: HTMLElement) {
  const result = await axe.run(container, {
    runOnly: { type: 'tag', values: wcagTags },
    rules: { 'color-contrast': { enabled: false } },
  })
  expect(result.violations, formatViolations(result.violations)).toEqual([])
}

function mockAdminRequests() {
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
    const path = String(input)
    if (path === '/api/admin/overview') return Promise.resolve(jsonResponse({
      activeHealthUnits: 1, activeUsers: 100, pendingPasswordChanges: 0, activeRoles: 5,
    }))
    if (path.startsWith('/api/notifications')) return Promise.resolve(jsonResponse({ items: [], unreadCount: 0 }))
    return Promise.resolve(new Response('{}', { status: 404 }))
  }))
}

const adminSession: Session = {
  id: 'admin-id', userName: 'admin.global', displayName: 'Administrador global', mustChangePassword: false,
  healthUnit: null,
  roles: [{ name: 'GlobalAdministrator', displayName: 'Administrador global' }], permissions: ['*'],
}

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function formatViolations(violations: axe.Result[]) {
  return violations.map((violation) => `${violation.id}: ${violation.help}\n${violation.nodes.map((node) => node.target.join(' ')).join('\n')}`).join('\n\n')
}
