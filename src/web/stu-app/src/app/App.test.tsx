import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

describe('Acesso ao STU', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); Object.defineProperty(window.navigator, 'onLine', { configurable: true, value: true }) })

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

  it('unifica a navegação em Imóveis e mantém o histórico e o registro de visitas', async () => {
    const property = { id: 'property-id', healthUnitId: 'unit-id', microregionId: 'micro-id', street: 'Rua de teste', houseNumber: '12', familyNumber: '34', postalCode: null, complement: null, geometry: { type: 'Point', coordinates: [-39.7419, -17.5394] }, registrationStatus: 'Active', situation: 'Occupied', concurrencyToken: 'property-version', archivedAtUtc: null, lastVisitAtUtc: null, coverageStatus: 'neverVisited', tags: [] }
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse(managerSession()))
      if (path === '/api/dashboard/summary') return Promise.resolve(jsonResponse({ healthUnitName: 'UBS Piloto' }))
      if (path.startsWith('/api/properties?')) return Promise.resolve(jsonResponse({ items: [property], total: 1 }))
      if (path.startsWith('/api/properties/reference-data?')) return Promise.resolve(jsonResponse({ selectedHealthUnitId: 'unit-id', microregions: [{ id: 'micro-id', code: 'MR01', name: 'Área de teste', assignedAgentId: null, boundary: null }], tags: [], coverageRules: [] }))
      if (path === '/api/properties/property-id/visits') return Promise.resolve(jsonResponse([{ id: 'visit-id', visitedAtUtc: '2026-09-08T12:00:00Z', type: 'Routine', outcome: 'Completed', observedSituation: 'Occupied', accessDifficulty: false, note: 'Acesso liberado', archivedAtUtc: null, concurrencyToken: 'visit-version', agentName: 'Agente de teste' }]))
      if (path === '/api/properties/property-id/versions') return Promise.resolve(jsonResponse([]))
      if (path.startsWith('/api/notifications')) return Promise.resolve(jsonResponse({ items: [], unreadCount: 0 }))
      return Promise.resolve(new Response('{}', { status: 404 }))
    }))

    render(<App />)
    const nav = await screen.findByRole('navigation', { name: 'Navegação principal' })
    expect(within(nav).getAllByRole('button', { name: 'Imóveis' })).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Visitas' })).not.toBeInTheDocument()
    fireEvent.click(within(nav).getByRole('button', { name: 'Imóveis' }))
    expect(await screen.findByText(/Acesso liberado/)).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Imóveis' })).toHaveFocus()
    expect(screen.queryByRole('button', { name: 'Visitas' })).not.toBeInTheDocument()
    expect(within(nav).getByRole('button', { name: 'Imóveis' })).toHaveAttribute('aria-current', 'page')

    fireEvent.click(screen.getByRole('button', { name: /Registrar visita/ }))
    expect(screen.getByRole('dialog', { name: 'Registrar visita operacional' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Fechar registro de visita' }))
    fireEvent.click(screen.getByRole('button', { name: 'Configurar' }))
    expect(screen.getByRole('heading', { name: 'Cobertura e rótulos' })).toBeInTheDocument()
    fireEvent.click(within(screen.getByRole('main')).getByRole('button', { name: 'Imóveis' }))
    expect(screen.getByRole('heading', { name: 'Visitas registradas' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Registrar visita/ })).toBeEnabled()
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
    expect(screen.getAllByRole('button', { name: /Cadastrar imóvel/ }).every(button => button.hasAttribute('disabled'))).toBe(true)
  })

  it('exibe tendências reais e bloqueia cadastro quando o navegador está offline', async () => {
    Object.defineProperty(window.navigator, 'onLine', { configurable: true, value: false })
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse(managerSession()))
      if (path === '/api/dashboard/summary') return Promise.resolve(jsonResponse({ healthUnitName: 'UBS Piloto', activeProperties: 12, activeFamilyIdentifiers: 10, visitsThisMonth: 6, visitsPreviousMonth: 4, visitsChangePercent: 50, coverageAlerts: 3, unassignedMicroregions: 1, coverage: { covered: 9, overdue: 2, neverVisited: 1, notConfigured: 0 }, microregions: [], stage: 'Operação ativa' }))
      if (path.startsWith('/api/notifications')) return Promise.resolve(jsonResponse({ items: [], unreadCount: 0 }))
      return Promise.resolve(new Response('{}', { status: 404 }))
    }))

    render(<App />)
    expect(await screen.findByText('+50% em relação ao mês anterior')).toBeInTheDocument()
    expect(screen.getByText(/Você está sem conexão/)).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Cadastrar imóvel/ }).every(button => button.hasAttribute('disabled'))).toBe(true)
    expect(screen.getByText('10 identificações familiares')).toBeInTheDocument()
  })
})

function managerSession() {
  return { id: 'manager-id', userName: 'gerente', displayName: 'Gerente da UBS', mustChangePassword: false, healthUnit: { id: 'unit-id', code: 'UBS-PILOTO', name: 'UBS Piloto' }, roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }], permissions: ['map.view', 'territory.manage', 'properties.view', 'properties.manage', 'visits.view', 'visits.manage', 'health_unit.users.manage'] }
}

function jsonResponse(body: unknown) { return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }) }
