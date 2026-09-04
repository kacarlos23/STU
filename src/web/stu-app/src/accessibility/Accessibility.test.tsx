import axe from 'axe-core'
import { cleanup, render, screen } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Session } from '@stu/shared'
import { App } from '../app/App'
import { Dashboard } from '../features/dashboard/Dashboard'

const wcagTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']

describe('WCAG 2.2 AA — portal operacional', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('não encontra violações automatizadas na tela de login', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', {
      status: 401,
      headers: { 'Content-Type': 'application/json' },
    })))

    const { container } = render(<App />)
    await screen.findByRole('heading', { name: 'Entre na sua conta' })
    await expectAccessible(container)
  })

  it.each([
    ['Agente de saúde', 'HealthAgent'],
    ['Recepcionista', 'Receptionist'],
    ['Médico', 'Doctor'],
    ['Gerente', 'HealthUnitManager'],
  ])('não encontra violações automatizadas na visão geral de %s', async (displayName, roleName) => {
    mockAuthenticatedRequests()
    const session = operationalSession(roleName, displayName)
    const { container } = render(<Dashboard logout={vi.fn()} session={session} />)

    await screen.findByRole('heading', { name: `Bom trabalho, ${displayName.split(' ')[0]}.` })
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

function mockAuthenticatedRequests() {
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
    const path = String(input)
    if (path === '/api/dashboard/summary') return Promise.resolve(jsonResponse({
      healthUnitName: 'UBS Piloto', activeProperties: 3000, visitsThisMonth: 120,
      coverageAlerts: 8, unassignedMicroregions: 0, stage: 'Operação ativa',
    }))
    if (path.startsWith('/api/notifications')) return Promise.resolve(jsonResponse({ items: [], unreadCount: 0 }))
    return Promise.resolve(new Response('{}', { status: 404 }))
  }))
}

function operationalSession(roleName: string, displayName: string): Session {
  return {
    id: `${roleName}-id`, userName: roleName.toLowerCase(), displayName,
    mustChangePassword: false,
    healthUnit: { id: 'unit-id', code: 'UBS-PILOTO', name: 'UBS Piloto' },
    roles: [{ name: roleName, displayName }], permissions: [],
  }
}

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function formatViolations(violations: axe.Result[]) {
  return violations.map((violation) => `${violation.id}: ${violation.help}\n${violation.nodes.map((node) => node.target.join(' ')).join('\n')}`).join('\n\n')
}
