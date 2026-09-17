import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

describe('Acesso à administração global do STU', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('exibe o login administrativo quando não há uma sessão ativa', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', {
      status: 401,
      headers: { 'Content-Type': 'application/json' },
    })))

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Entre na sua conta' })).toBeInTheDocument()
    expect(screen.getByText('Administração global')).toBeInTheDocument()
    expect(screen.getByLabelText('Usuário')).toBeInTheDocument()
  })

  it('permite navegar para o cadastro de UBS após autenticar', async () => {
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse({
        id: 'admin-id', userName: 'admin.global', displayName: 'Administrador', mustChangePassword: false,
        healthUnit: null, roles: [{ name: 'GlobalAdministrator', displayName: 'Administrador global' }], permissions: ['*'],
      }))
      if (path === '/api/admin/overview') return Promise.resolve(jsonResponse({ activeHealthUnits: 1, activeUsers: 2, pendingPasswordChanges: 0, activeRoles: 5 }))
      if (path.startsWith('/api/admin/health-units')) return Promise.resolve(jsonResponse([]))
      return Promise.resolve(new Response('{}', { status: 404 }))
    }))

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Visão geral' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Imóveis' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Imóveis e visitas' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^Abrir imóveis/ })).toBeInTheDocument()
    fireEvent.click(screen.getAllByRole('button', { name: /UBS e territórios/ })[0])
    expect(await screen.findByRole('heading', { name: 'UBS cadastradas' })).toBeInTheDocument()
    const newUnit = screen.getByRole('button', { name: /Nova UBS/ })
    newUnit.focus()
    fireEvent.click(newUnit)
    expect(screen.getByRole('heading', { name: 'Cadastrar UBS' })).toBeInTheDocument()
    expect(screen.getByLabelText('Código da UBS')).toBeInTheDocument()
    const close = screen.getByRole('button', { name: 'Fechar janela' })
    await waitFor(() => expect(close).toHaveFocus())
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(newUnit).toHaveFocus()
  })

  it('abre a área de backups com rotina semanal e execução manual', async () => {
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse({
        id: 'admin-id', userName: 'admin.global', displayName: 'Administrador', mustChangePassword: false,
        healthUnit: null, roles: [{ name: 'GlobalAdministrator', displayName: 'Administrador global' }], permissions: ['*'],
      }))
      if (path === '/api/admin/overview') return Promise.resolve(jsonResponse({ activeHealthUnits: 1, activeUsers: 2, pendingPasswordChanges: 0, activeRoles: 5 }))
      if (path === '/api/admin/backups/settings') return Promise.resolve(jsonResponse({ enabled: true, dayOfWeek: 'Sunday', localHour: 0, retentionCount: 8, timeZoneId: 'America/Bahia', updatedAtUtc: null }))
      if (path === '/api/admin/backups/runs') return Promise.resolve(jsonResponse([]))
      return Promise.resolve(new Response('{}', { status: 404 }))
    }))

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Visão geral' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Backups' }))
    expect(await screen.findByRole('heading', { level: 2, name: 'Backups' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Fazer backup agora/ })).toBeEnabled()
    expect(screen.getByText('Rotina semanal')).toBeInTheDocument()
    expect(screen.getByText('Nenhum backup solicitado.')).toBeInTheDocument()
  })

  it('exibe componentes e alertas no monitoramento global', async () => {
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse({
        id: 'admin-id', userName: 'admin.global', displayName: 'Administrador', mustChangePassword: false,
        healthUnit: null, roles: [{ name: 'GlobalAdministrator', displayName: 'Administrador global' }], permissions: ['*'],
      }))
      if (path === '/api/admin/overview') return Promise.resolve(jsonResponse({ activeHealthUnits: 1, activeUsers: 2, pendingPasswordChanges: 0, activeRoles: 5 }))
      if (path === '/api/admin/monitoring/status') return Promise.resolve(jsonResponse({
        checkedAtUtc: '2026-08-24T14:00:00Z', overallStatus: 'Warning',
        checks: [
          { id: 'database', label: 'Banco de dados', status: 'Healthy', detail: 'Conexão disponível.', lastObservedAtUtc: '2026-08-24T14:00:00Z' },
          { id: 'operations', label: 'Importações e exportações', status: 'Warning', detail: 'Existem trabalhos que exigem atenção.', lastObservedAtUtc: '2026-08-24T14:00:00Z' },
        ],
        alerts: [{ code: 'operations-failed', severity: 'Warning', title: 'Falhas operacionais recentes', detail: 'Um trabalho falhou.' }],
      }))
      return Promise.resolve(new Response('{}', { status: 404 }))
    }))

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Visão geral' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Monitoramento' }))
    expect(await screen.findByRole('heading', { level: 2, name: 'Monitoramento' })).toBeInTheDocument()
    expect(screen.getByText('Banco de dados')).toBeInTheDocument()
    expect(screen.getByText('Falhas operacionais recentes')).toBeInTheDocument()
    expect(screen.getAllByText('Atenção')).toHaveLength(3)
  })

  it('consolida as pendências da pré-implantação sem ativar dados automaticamente', async () => {
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse({
        id: 'admin-id', userName: 'admin.global', displayName: 'Administrador', mustChangePassword: false,
        healthUnit: null, roles: [{ name: 'GlobalAdministrator', displayName: 'Administrador global' }], permissions: ['*'],
      }))
      if (path === '/api/admin/overview') return Promise.resolve(jsonResponse({ activeHealthUnits: 1, activeUsers: 2, pendingPasswordChanges: 0, activeRoles: 5 }))
      if (path.startsWith('/api/admin/health-units')) return Promise.resolve(jsonResponse([{ id: 'unit-id', code: 'UBS-PILOTO', name: 'UBS Piloto', archivedAtUtc: null }]))
      if (path.startsWith('/api/onboarding/readiness')) return Promise.resolve(jsonResponse({
        generatedAtUtc: '2026-08-31T12:00:00Z', snapshotHash: 'abc',
        healthUnit: { id: 'unit-id', code: 'UBS-PILOTO', name: 'UBS Piloto' },
        counts: { neighborhoods: 2, microregions: 0, properties: 0, activeUsers: 1, temporaryPasswords: 0, healthAgents: 0, receptionists: 0, doctors: 0, managers: 1 },
        importSummary: { beforeLatestImport: 0, latestImported: 0, currentTotal: 0, pendingImports: 0 },
        backup: { scheduleEnabled: true, runId: null, completedAtUtc: null, sha256: null, sizeBytes: null, recent: false },
        readyForApproval: false,
        checks: [
          { id: 'neighborhood-count', label: 'Três bairros vinculados', status: 'blocked', detail: '2 de 3 bairros vinculados.' },
          { id: 'pending-imports', label: 'Sem importação pendente', status: 'passed', detail: '0 importações pendentes.' },
        ],
        latestApproval: null,
      }))
      if (path.startsWith('/api/notifications')) return Promise.resolve(jsonResponse({ items: [], unreadCount: 0 }))
      return Promise.resolve(new Response('{}', { status: 404 }))
    }))

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Visão geral' })).toBeInTheDocument()
    fireEvent.click(screen.getAllByRole('button', { name: 'Pré-implantação' })[0])
    expect(await screen.findByRole('heading', { level: 2, name: 'Pré-implantação da UBS' })).toBeInTheDocument()
    expect(await screen.findByText('Existem pendências antes da aprovação')).toBeInTheDocument()
    expect(screen.getByText('Três bairros vinculados')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Aprovar pré-implantação' })).toBeDisabled()
  })

  it('exibe o portão final, os guias por função e mantém a liberação bloqueada', async () => {
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const path = String(input)
      if (path === '/api/auth/me') return Promise.resolve(jsonResponse({
        id: 'admin-id', userName: 'admin.global', displayName: 'Administrador', mustChangePassword: false,
        healthUnit: null, roles: [{ name: 'GlobalAdministrator', displayName: 'Administrador global' }], permissions: ['*'],
      }))
      if (path === '/api/admin/overview') return Promise.resolve(jsonResponse({ activeHealthUnits: 1, activeUsers: 2, pendingPasswordChanges: 0, activeRoles: 5 }))
      if (path.startsWith('/api/admin/health-units')) return Promise.resolve(jsonResponse([{ id: 'unit-id', code: 'UBS-PILOTO', name: 'UBS Piloto', archivedAtUtc: null }]))
      if (path.startsWith('/api/pilot-release/readiness')) return Promise.resolve(jsonResponse({
        generatedAtUtc: '2026-08-31T15:00:00Z', releaseSnapshotHash: 'release-hash', onboardingSnapshotHash: 'onboarding-hash',
        healthUnit: { id: 'unit-id', code: 'UBS-PILOTO', name: 'UBS Piloto' }, readyForDecision: false, released: false,
        checks: [
          { id: 'capacity', label: 'Capacidade e desempenho', status: 'passed', detail: 'Ensaio aprovado.' },
          { id: 'onboarding', label: 'Pré-implantação aprovada e atual', status: 'blocked', detail: 'Conclua a pré-implantação.' },
        ],
        evidence: [{ label: 'Capacidade', document: 'PHASE2_CAPACITY_REPORT_2026-08-28.md' }], latestDecision: null,
      }))
      if (path.startsWith('/api/notifications')) return Promise.resolve(jsonResponse({ items: [], unreadCount: 0 }))
      return Promise.resolve(new Response('{}', { status: 404 }))
    }))

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Visão geral' })).toBeInTheDocument()
    fireEvent.click(screen.getAllByRole('button', { name: 'Liberação do piloto' })[0])
    expect(await screen.findByRole('heading', { level: 2, name: 'Liberação do piloto' })).toBeInTheDocument()
    expect(await screen.findByText('Liberação bloqueada por pendências')).toBeInTheDocument()
    expect(screen.getByText('Pré-implantação aprovada e atual')).toBeInTheDocument()
    expect(screen.getByText('Agente de saúde')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Registrar liberação' })).toBeDisabled()
  })
})

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
