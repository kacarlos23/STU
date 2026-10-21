import axe from 'axe-core'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InteractionProvider, type Session } from '@stu/shared'
import { RegistrationWorkspace } from './RegistrationWorkspace'
import type { Data } from './types'

const session: Session = { id: 'manager', userName: 'manager', displayName: 'Gerente', mustChangePassword: false,
  healthUnit: { id: 'unit', code: 'UBS', name: 'UBS de teste' }, roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }],
  permissions: ['properties.view', 'families.view', 'territory.manage', 'properties.manage', 'families.manage', 'health_unit.users.manage'] }
let data: Data
let fetchMock: ReturnType<typeof vi.fn>
let rejectWrite: boolean
const json = (body: unknown, status = 200) => Response.json(body, { status })

beforeEach(() => {
  rejectWrite = false
  data = { neighborhoods: [{ id: 'bairro', name: 'Bairro cadastrado' }],
    regions: [{ id: 'region', code: 'MR01', name: 'Área cadastrada', neighborhoodId: 'bairro', neighborhood: 'Bairro cadastrado', concurrencyToken: 'region-version' }],
    agents: [{ id: 'agent', name: 'Agente cadastrado', userName: 'agent' }, { id: 'other', name: 'Outro agente', userName: 'other' }],
    assignments: [{ id: 'link', regionId: 'region', agentId: 'agent', concurrencyToken: 'link-version' }], addresses: [], families: [] }
  fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input)
    if (path === '/api/registration/units') return json([session.healthUnit])
    if (path.startsWith('/api/registration?')) return json(data)
    if (path === '/api/auth/csrf') return json({ token: 'csrf-token' })
    if (rejectWrite) return json({ detail: 'Outro usuário alterou o cadastro. Atualize a consulta.' }, 409)
    const body = JSON.parse(String(init?.body ?? '{}'))
    if (path.endsWith('/regions/region/agents')) {
      data.assignments.push({ id: 'server-link', regionId: 'region', agentId: body.agentId, concurrencyToken: 'saved-version' })
      return json({ id: 'server-link', userName: 'other' }, 201)
    }
    if (path === '/api/registration/addresses') {
      data.families.push({ id: 'server-family', number: body.familyNumber, responsibleName: body.familyResponsibleName })
      data.addresses.push({ id: 'server-address', ...body, neighborhood: 'Bairro cadastrado', familyId: 'server-family' })
      return json({ id: 'server-address', familyId: 'server-family' }, 201)
    }
    if (path.endsWith('/assignments/link/unlink')) {
      data.assignments = data.assignments.filter(a => a.id !== 'link')
      return json({ id: 'link' })
    }
    return json({}, 404)
  })
  vi.stubGlobal('fetch', fetchMock)
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

async function open(activeSession = session) {
  const result = render(<InteractionProvider><RegistrationWorkspace session={activeSession} /></InteractionProvider>)
  fireEvent.click(await screen.findByRole('button', { name: 'Abrir Área cadastrada' }))
  return result
}

describe('Cadastro territorial conectado à API', () => {
  it('salva o vínculo com CSRF e versão, recarrega os dados e permanece na equipe', async () => {
    await open()
    fireEvent.click(screen.getByRole('button', { name: 'Vincular agente' }))
    fireEvent.change(screen.getByLabelText('Agente da UBS *'), { target: { value: 'other' } })
    fireEvent.click(screen.getByRole('button', { name: 'Salvar' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByRole('heading', { name: 'Agentes da Área cadastrada' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Consultar endereços de Outro agente' })).toBeInTheDocument()
    const write = fetchMock.mock.calls.find(([url, init]) => String(url).endsWith('/regions/region/agents') && init?.method === 'POST')!
    expect(write[1]).toMatchObject({ credentials: 'include', headers: { 'X-STU-CSRF': 'csrf-token' } })
    expect(JSON.parse(String(write[1]?.body))).toMatchObject({ agentId: 'other', expectedVersion: 'region-version' })
    expect(fetchMock.mock.calls.filter(([url]) => String(url).startsWith('/api/registration?'))).toHaveLength(2)
  })

  it('persiste endereço e família juntos, herdando bairro e agente sem coordenadas', async () => {
    await open()
    fireEvent.click(screen.getByRole('button', { name: 'Consultar endereços de Agente cadastrado' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cadastrar endereço' }))
    expect(screen.getByLabelText('Bairro *')).toHaveValue('Bairro cadastrado')
    fireEvent.change(screen.getByLabelText('Logradouro *'), { target: { value: 'Rua nova' } })
    fireEvent.change(screen.getByLabelText('Número *'), { target: { value: '42' } })
    fireEvent.click(screen.getByRole('button', { name: 'Continuar para família' }))
    fireEvent.change(screen.getByLabelText('Número da família *'), { target: { value: '012' } })
    fireEvent.change(screen.getByLabelText('Responsável pela família *'), { target: { value: 'Ana Souza' } })
    fireEvent.click(screen.getByRole('button', { name: 'Conferir cadastro' }))
    fireEvent.click(screen.getByRole('button', { name: 'Salvar endereço e família' }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Família 012' })).toBeInTheDocument()
    const write = fetchMock.mock.calls.find(([url, init]) => url === '/api/registration/addresses' && init?.method === 'POST')!
    const body = JSON.parse(String(write[1]?.body))
    expect(body).toMatchObject({ assignmentId: 'link', neighborhoodId: 'bairro', street: 'Rua nova', houseNumber: '42', familyNumber: '012', familyResponsibleName: 'Ana Souza', expectedAssignmentVersion: 'link-version' })
    expect(body).not.toHaveProperty('geometry')
  })

  it('mantém formulário e seleção quando o servidor rejeita versão desatualizada', async () => {
    rejectWrite = true
    await open()
    fireEvent.click(screen.getByRole('button', { name: 'Vincular agente' }))
    fireEvent.change(screen.getByLabelText('Agente da UBS *'), { target: { value: 'other' } })
    fireEvent.click(screen.getByRole('button', { name: 'Salvar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Outro usuário alterou o cadastro')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.getByLabelText('Agente da UBS *')).toHaveValue('other')
    expect(screen.queryByRole('button', { name: 'Consultar endereços de Outro agente' })).not.toBeInTheDocument()
  })

  it('transfere também endereços arquivados antes de encerrar o vínculo', async () => {
    data.assignments[0].addressCount = 2
    data.assignments.push({ id: 'recipient', regionId: 'region', agentId: 'other', concurrencyToken: 'recipient-version' })
    await open()
    fireEvent.click(screen.getByRole('button', { name: 'Desvincular Agente cadastrado' }))
    fireEvent.change(screen.getByLabelText('Novo agente responsável *'), { target: { value: 'recipient' } })
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Desvincular' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const write = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/assignments/link/unlink'))!
    expect(JSON.parse(String(write[1]?.body))).toMatchObject({ recipientId: 'recipient', expectedVersion: 'link-version', expectedRegionVersion: 'region-version', expectedRecipientVersion: 'recipient-version' })
  })

  it('permite consulta sem exibir ações de gestão e mantém acessibilidade das etapas', async () => {
    const { container } = await open({ ...session, permissions: ['properties.view', 'families.view'] })
    expect(screen.queryByRole('button', { name: 'Vincular agente' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Desvincular/ })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Consultar endereços de Agente cadastrado' }))
    expect(screen.queryByRole('button', { name: 'Cadastrar endereço' })).not.toBeInTheDocument()
    const result = await axe.run(container, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'] }, rules: { 'color-contrast': { enabled: false } } })
    expect(result.violations).toEqual([])
  })
})
