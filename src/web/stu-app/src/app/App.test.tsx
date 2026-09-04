import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

describe('Acesso ao STU', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals() })

  it('exibe o login quando não há uma sessão ativa', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', {
      status: 401,
      headers: { 'Content-Type': 'application/json' },
    })))

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Entre na sua conta' })).toBeInTheDocument()
    expect(screen.getByText('Sistema Territorial das UBS')).toBeInTheDocument()
    expect(screen.getByLabelText('Usuário')).toBeInTheDocument()
    expect(screen.getByLabelText('Senha')).toBeInTheDocument()
  })

  it('permite ao gerente consultar e iniciar o cadastro de servidores da própria UBS', async () => {
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse(managerSession()))
      if (path === '/api/dashboard/summary') return Promise.resolve(jsonResponse({ healthUnitName: 'UBS Piloto', activeProperties: 0, visitsThisMonth: 0, coverageAlerts: 0, unassignedMicroregions: 0, stage: 'Preparação' }))
      if (path.startsWith('/api/health-unit-users?')) return Promise.resolve(jsonResponse({ items: [{ id: 'manager-id', userName: 'gerente', displayName: 'Gerente da UBS', role: { id: 'role-manager', name: 'HealthUnitManager', displayName: 'Gerente', isSystem: true }, mustChangePassword: false, lockoutEnd: null, archivedAtUtc: null, manageable: false }], total: 1, page: 1, pageSize: 100 }))
      if (path === '/api/health-unit-users/reference-data') return Promise.resolve(jsonResponse({ healthUnit: { id: 'unit-id', code: 'UBS-PILOTO', name: 'UBS Piloto' }, roles: [{ id: 'agent-role', name: 'HealthAgent', displayName: 'Agente de saúde', description: 'Operação territorial', isSystem: true }] }))
      if (path.startsWith('/api/notifications')) return Promise.resolve(jsonResponse({ items: [], unreadCount: 0 }))
      return Promise.resolve(new Response('{}', { status: 404 }))
    }))

    render(<App />)
    expect(await screen.findByRole('heading', { name: /Bom trabalho/ })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Servidores' }))
    expect(await screen.findByRole('heading', { level: 2, name: 'Servidores cadastrados' })).toBeInTheDocument()
    expect(await screen.findAllByText('Gerente da UBS')).not.toHaveLength(0)
    expect(await screen.findByText('Gerência protegida')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Adicionar servidor/ }))
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.getByLabelText('Usuário de acesso')).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Agente de saúde' })).toBeInTheDocument()
  })

  it('explica a dependência territorial quando ainda não existe microrregião para o imóvel', async () => {
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse(managerSession()))
      if (path === '/api/dashboard/summary') return Promise.resolve(jsonResponse({ healthUnitName: 'UBS Piloto', activeProperties: 0, visitsThisMonth: 0, coverageAlerts: 0, unassignedMicroregions: 0, stage: 'Preparação' }))
      if (path.startsWith('/api/properties?')) return Promise.resolve(jsonResponse({ items: [], total: 0, page: 1, pageSize: 100 }))
      if (path.startsWith('/api/properties/reference-data?')) return Promise.resolve(jsonResponse({ selectedHealthUnitId: 'unit-id', microregions: [], tags: [], coverageRules: [] }))
      if (path.startsWith('/api/notifications')) return Promise.resolve(jsonResponse({ items: [], unreadCount: 0 }))
      return Promise.resolve(new Response('{}', { status: 404 }))
    }))

    render(<App />)
    expect(await screen.findByRole('heading', { name: /Bom trabalho/ })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Imóveis' }))
    expect(await screen.findByRole('heading', { level: 3, name: 'Cadastre uma microrregião antes do primeiro imóvel' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Ir para o mapa territorial/ })).toBeEnabled()
    expect(screen.getByRole('button', { name: /Cadastrar imóvel/ })).toBeDisabled()
  })
})

function managerSession() {
  return { id: 'manager-id', userName: 'gerente', displayName: 'Gerente da UBS', mustChangePassword: false, healthUnit: { id: 'unit-id', code: 'UBS-PILOTO', name: 'UBS Piloto' }, roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }], permissions: ['map.view', 'territory.manage', 'properties.view', 'properties.manage', 'visits.view', 'visits.manage', 'health_unit.users.manage'] }
}

function jsonResponse(body: unknown) { return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }) }
