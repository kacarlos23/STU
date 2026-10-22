import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const session = { id: 'account-test', userName: 'gerente', displayName: 'Gerente de teste', mustChangePassword: false, healthUnit: { id: 'unit', code: 'TESTE', name: 'UBS de teste' }, roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }], permissions: ['families.view', 'properties.view'] }
const initial = { displayName: 'Gerente de teste', userName: 'gerente', avatarUrl: null, appearance: 'light', passwordResetRequestedAtUtc: null, concurrencyToken: 'v1' }
function json(body: unknown, status = 200) { return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }) }
function setup(failSave = false) {
  let profile = { ...initial }
  const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input)
    if (path === '/api/auth/me') return Promise.resolve(json(session))
    if (path.startsWith('/api/notifications')) return Promise.resolve(json({ items: [], unreadCount: 0 }))
    if (path === '/api/dashboard/summary') return Promise.resolve(json({ healthUnitName: 'UBS de teste', coverage: { covered: 0, overdue: 0, neverVisited: 0, notConfigured: 0 }, microregions: [] }))
    if (path === '/api/auth/csrf') return Promise.resolve(json({ token: 'csrf-test' }))
    if (path === '/api/auth/profile/password-reset-request') return Promise.resolve(json({ ...profile, passwordResetRequestedAtUtc: '2026-10-22T15:00:00Z', concurrencyToken: 'requested' }))
    if (path === '/api/auth/profile' && init?.method === 'PUT') {
      if (failSave) return Promise.resolve(json({ detail: 'Perfil alterado em outra sessão.' }, 409))
      const body = JSON.parse(String(init.body)); profile = { ...profile, displayName: body.displayName, appearance: body.appearance, concurrencyToken: 'v2' }
      return Promise.resolve(json(profile))
    }
    if (path === '/api/auth/profile') return Promise.resolve(json(profile))
    return Promise.resolve(json({}, 404))
  })
  vi.stubGlobal('fetch', fetchMock); render(<App />); return fetchMock
}

describe('Configurações da conta e navegação', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); Object.defineProperty(navigator, 'onLine', { configurable: true, value: true }) })

  it('abre pela lateral e pelo menu, protege alterações e atualiza o nome sem aplicar o modo escuro', async () => {
    const fetchMock = setup()
    expect(await screen.findByRole('heading', { name: 'Visão geral', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('form', { name: 'Busca geral' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Menu do usuário' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Configurações' }))
    expect(await screen.findByRole('heading', { name: 'Meu perfil' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Claro' })).toBeChecked()
    fireEvent.change(screen.getByRole('textbox', { name: 'Nome completo' }), { target: { value: 'Ana de teste' } })
    fireEvent.click(screen.getByRole('radio', { name: 'Escuro' }))
    fireEvent.click(screen.getByRole('button', { name: 'Ir para a visão geral' }))
    const dialog = await screen.findByRole('alertdialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Continuar aqui' }))
    expect(screen.getByRole('textbox', { name: 'Nome completo' })).toHaveValue('Ana de teste')
    fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
    expect(await screen.findByText('Configurações salvas.')).toBeInTheDocument()
    expect(screen.getByText('Ana de teste')).toBeInTheDocument()
    const sent = fetchMock.mock.calls.find(([url, init]) => url === '/api/auth/profile' && init?.method === 'PUT')
    expect(JSON.parse(String(sent?.[1]?.body))).toMatchObject({ displayName: 'Ana de teste', appearance: 'dark', concurrencyToken: 'v1' })
    expect(document.documentElement.dataset.theme).toBeUndefined()
    fireEvent.click(screen.getByRole('button', { name: 'Ir para a visão geral' }))
    expect(await screen.findByRole('heading', { name: 'Visão geral', level: 1 })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Configurações' }))
    expect(await screen.findByRole('radio', { name: 'Escuro' })).toBeChecked()
  })

  it('envia o pedido de senha uma vez e informa o estado pendente', async () => {
    const fetchMock = setup()
    fireEvent.click(await screen.findByRole('button', { name: 'Configurações' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Solicitar redefinição de senha' }))
    expect(await screen.findByRole('button', { name: 'Pedido enviado' })).toBeDisabled()
    expect(screen.getByText(/Aguardando atendimento/)).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/auth/profile/password-reset-request')).toHaveLength(1)
  })

  it('mantém o nome preenchido quando a gravação tem conflito e rejeita arquivos incompatíveis', async () => {
    setup(true)
    fireEvent.click(await screen.findByRole('button', { name: 'Configurações' }))
    await screen.findByRole('textbox', { name: 'Nome completo' })
    fireEvent.change(screen.getByLabelText('Foto de perfil'), { target: { files: [new File(['<svg/>'], 'foto.svg', { type: 'image/svg+xml' })] } })
    expect(await screen.findByText(/Escolha uma foto PNG, JPG ou WebP/)).toBeInTheDocument()
    fireEvent.change(screen.getByRole('textbox', { name: 'Nome completo' }), { target: { value: 'Nome preservado' } })
    fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
    expect(await screen.findByText('Perfil alterado em outra sessão.')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Nome completo' })).toHaveValue('Nome preservado')
    fireEvent.click(screen.getByRole('button', { name: 'Recarregar configurações' }))
    expect(await screen.findByRole('alertdialog')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Continuar aqui' }))
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: false }); fireEvent(window, new Event('offline'))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Salvar alterações' })).toBeDisabled())
  })
})
