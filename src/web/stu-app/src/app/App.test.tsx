import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
    const navigation = screen.getByRole('navigation', { name: 'Navegação principal' })
    expect(within(navigation).getByRole('button', { name: 'Território' })).toBeInTheDocument()
    expect(within(navigation).queryByText('Operação')).not.toBeInTheDocument()
    expect(screen.getByText(/Território,\s*mais saúde\s*para todos\./)).toBeInTheDocument()
    expect(screen.getByText('UBS-PILOTO · Município de Teixeira de Freitas')).toBeInTheDocument()
    expect(screen.getByRole('searchbox', { name: 'Buscar família por número ou responsável' })).toBeInTheDocument()
    const profileMenu = screen.getByLabelText('Menu do usuário')
    fireEvent.click(profileMenu)
    expect(screen.getByRole('menuitem', { name: 'Sair do STU' })).toBeInTheDocument()
    fireEvent.pointerDown(document.body)
    expect(screen.queryByRole('menuitem', { name: 'Sair do STU' })).not.toBeInTheDocument()
    fireEvent.click(profileMenu)
    expect(screen.getByRole('menuitem', { name: 'Sair do STU' })).toBeInTheDocument()
    fireEvent.click(profileMenu)
    expect(screen.queryByRole('menuitem', { name: 'Sair do STU' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Servidores' }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Equipe da UBS' })).toHaveFocus()
    expect(screen.queryByRole('form', { name: 'Busca geral' })).not.toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 2, name: 'Servidores cadastrados' })).toBeInTheDocument()
    expect(screen.getByLabelText('Situação')).toHaveValue('all')
    expect(screen.getByLabelText('Função')).toHaveValue('all')
    expect(await screen.findAllByText('Gerente da UBS')).not.toHaveLength(0)
    expect(await screen.findByText('Conta protegida')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Buscar servidor'), { target: { value: 'conta inexistente' } })
    expect(screen.getByText('Nenhum servidor encontrado')).toBeInTheDocument()
    fireEvent.click(within(screen.getByRole('region', { name: 'Buscar e filtrar equipe' })).getByRole('button', { name: 'Limpar filtros' }))
    expect(screen.getByText('Conta protegida')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Adicionar servidor/ }))
    const dialog = screen.getByRole('dialog')
    expect(dialog).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Usuário de acesso')).toBeInTheDocument()
    expect(within(dialog).getByRole('option', { name: 'Agente de saúde' })).toBeInTheDocument()
  })

  it('unifica imóveis e cobertura mantendo cadastro, histórico e visitas', async () => {
    const property = { id: 'property-id', healthUnitId: 'unit-id', microregionId: 'micro-id', street: 'Rua de teste', houseNumber: '12', familyNumber: '34', familyId: 'family-id', familyConcurrencyToken: 'family-version', postalCode: null, complement: null, geometry: { type: 'Point', coordinates: [-39.7419, -17.5394] }, registrationStatus: 'Active', situation: 'Occupied', concurrencyToken: 'property-version', archivedAtUtc: null, lastVisitAtUtc: null, coverageStatus: 'neverVisited', tags: [] }
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
    expect(within(nav).getAllByRole('button', { name: 'Cobertura' })).toHaveLength(1)
    expect(within(nav).queryByRole('button', { name: 'Imóveis' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Visitas' })).not.toBeInTheDocument()
    fireEvent.click(within(nav).getByRole('button', { name: 'Cobertura' }))
    fireEvent.change(await screen.findByLabelText('Buscar imóvel'), { target: { value: 'Rua de teste' } })
    expect(await screen.findByText(/Acesso liberado/)).toBeInTheDocument()
    expect(screen.getByDisplayValue('Rua de teste')).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Busca geral' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Cobertura' })).toHaveFocus()
    expect(screen.queryByRole('button', { name: 'Visitas' })).not.toBeInTheDocument()
    expect(within(nav).getByRole('button', { name: 'Cobertura' })).toHaveAttribute('aria-current', 'page')

    fireEvent.click(screen.getByRole('button', { name: /Registrar visita/ }))
    expect(screen.getByRole('dialog', { name: 'Registrar visita operacional' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Fechar registro de visita' }))
    fireEvent.click(screen.getByRole('button', { name: 'Configurar' }))
    expect(screen.getByRole('heading', { name: 'Cobertura e rótulos' })).toBeInTheDocument()
    fireEvent.click(within(screen.getByRole('main')).getByRole('button', { name: 'Cobertura' }))
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
    fireEvent.click(screen.getByRole('button', { name: 'Cobertura' }))
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
    expect(screen.getAllByRole('button', { name: /Cadastrar família/ }).every(button => button.hasAttribute('disabled'))).toBe(true)
    expect(screen.getByRole('button', { name: /Famílias cadastradas: 10/ })).toBeInTheDocument()
  })

  it('abre a cobertura já filtrada ao selecionar um indicador contextual', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      const url = new URL(path, 'https://test.local')
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse(managerSession()))
      if (path === '/api/dashboard/summary') return Promise.resolve(jsonResponse({ healthUnitName: 'UBS Piloto', activeProperties: 4, activeFamilyIdentifiers: 4, visitsThisMonth: 6, visitsPreviousMonth: 4, visitsChangePercent: 50, coverageAlerts: 2, unassignedMicroregions: 1, coverage: { covered: 2, overdue: 1, neverVisited: 1, notConfigured: 0 }, microregions: [{ id: 'm1' }, { id: 'm2' }], stage: 'Operação ativa' }))
      if (url.pathname === '/api/properties') return Promise.resolve(jsonResponse({ items: [], total: 0, coverageSummary: { total: 4, covered: 2, overdue: 1, neverVisited: 1, notConfigured: 0 } }))
      if (url.pathname === '/api/properties/reference-data') return Promise.resolve(jsonResponse({ selectedHealthUnitId: 'unit-id', microregions: [{ id: 'm1', code: 'MR01', name: 'Área um', boundary: null }], tags: [], coverageRules: [] }))
      if (path.startsWith('/api/notifications')) return Promise.resolve(jsonResponse({ items: [], unreadCount: 0 }))
      return Promise.resolve(new Response('{}', { status: 404 }))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: /Alertas de cobertura: 2/ }))
    await screen.findByRole('heading', { name: 'Imóveis e cobertura' })
    expect(screen.getByLabelText('Filtrar cobertura')).toHaveValue('pending')
    await waitFor(() => expect(fetchMock.mock.calls.some(([path]) => String(path).includes('coverage=pending'))).toBe(true))
  })
})

function managerSession() {
  return { id: 'manager-id', userName: 'gerente', displayName: 'Gerente da UBS', mustChangePassword: false, healthUnit: { id: 'unit-id', code: 'UBS-PILOTO', name: 'UBS Piloto' }, roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }], permissions: ['map.view', 'territory.manage', 'families.view','families.manage','properties.view', 'properties.manage', 'visits.view', 'visits.manage', 'health_unit.users.manage'] }
}

function jsonResponse(body: unknown) { return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }) }
