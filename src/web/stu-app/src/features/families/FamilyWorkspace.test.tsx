import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { FamilyWorkspace } from '../../../../stu-shared/src/families/FamilyWorkspace'
import { InteractionProvider, type Session } from '@stu/shared'

const maps = vi.hoisted(() => [] as { fitBounds: ReturnType<typeof vi.fn>; getSource: (id: string) => unknown; emit: (event: string, payload: unknown) => void }[])
vi.mock('maplibre-gl', () => {
  class Map {
    sources = new globalThis.Map<string, { setData: ReturnType<typeof vi.fn> }>()
    events = new globalThis.Map<string, (payload: unknown) => void>()
    fitBounds = vi.fn(); easeTo = vi.fn(); addControl = vi.fn(); remove = vi.fn(); addLayer = vi.fn()
    constructor() { maps.push(this) }
    addSource(id: string, value: unknown) { this.sources.set(id, { ...value as object, setData: vi.fn() }) }
    getSource(id: string) { return this.sources.get(id) }
    loaded() { return true }
    on(event: string, callback: (payload: unknown) => void) { this.events.set(event, callback); if (event === 'load') queueMicrotask(() => callback({})); return this }
    off(event: string) { this.events.delete(event); return this }
    once(event: string, callback: (payload: unknown) => void) { return this.on(event, callback) }
    emit(event: string, payload: unknown) { this.events.get(event)?.(payload) }
  }
  class Marker { setLngLat() { return this } addTo() { return this } on() { return this } remove() {} }
  return { Map, Marker, NavigationControl: class {}, setWorkerUrl: vi.fn() }
})

const boundary = { type: 'Polygon', coordinates: [[[-40,-18],[-39,-18],[-39,-17],[-40,-17],[-40,-18]]] }
const session: Session = { id: 'manager', userName: 'manager', displayName: 'Gerente', mustChangePassword: false, healthUnit: { id: 'unit', name: 'UBS Sintética', code: 'UBS' }, roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }], permissions: ['families.view', 'families.manage', 'properties.view', 'properties.manage', 'visits.view', 'visits.manage'] }
const property = { id: 'p1', street: 'Rua das Acácias', houseNumber: '10', concurrencyToken: 'pv1', situation: 'Occupied', startedAtUtc: '2026-09-20T12:00:00Z' }
const family = { id: 'f1', number: '001', responsibleName: 'Ana-María D’Ávila', healthUnitId: 'unit', concurrencyToken: 'fv1', archivedAtUtc: null as string | null, state: 'unlinked', coverageStatus: 'noProperty', lastVisitAtUtc: null, currentProperty: null as typeof property | null }
let record = { ...family }
let failLink = false
let fetchMock: ReturnType<typeof vi.fn>
beforeEach(() => {
  maps.length = 0; record = { ...family }; failLink = false
  fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input), url = new URL(path, 'https://test.local')
    if (init?.method === 'POST' && path === '/api/properties') return Promise.resolve(Response.json({ id: 'p1' }, { status: 201 }))
    if (path === '/api/properties/p1') return Promise.resolve(Response.json(property))
    if (url.pathname === '/api/properties/reference-data') return Promise.resolve(Response.json({ selectedHealthUnitId: 'unit', microregions: [{ id: 'micro', code: 'MR', name: 'Área sintética', boundary }], tags: [], coverageRules: [] }))
    if (url.pathname === '/api/properties/address-suggestion') return Promise.resolve(Response.json({ found: false }))
    if (path === '/api/auth/csrf') return Promise.resolve(Response.json({ token: 'csrf' }))
    if (init?.method === 'POST' && path === '/api/families/f1/property') {
      if (failLink) return Promise.resolve(Response.json({ detail: 'O imóvel já possui outra família.' }, { status: 409 }))
      record = { ...record, currentProperty: property, state: 'linked', coverageStatus: 'neverVisited', concurrencyToken: 'fv2' }
      return Promise.resolve(Response.json({ concurrencyToken: 'fv2' }))
    }
    if (init?.method === 'POST' && path === '/api/families/f1/unlink') { record = { ...record, currentProperty: null, state: 'unlinked', coverageStatus: 'noProperty' }; return Promise.resolve(Response.json({ concurrencyToken: 'fv3' })) }
    if (init?.method === 'POST' && path === '/api/families') {
      const body = JSON.parse(String(init.body)) as { number: string; responsibleName: string }
      record = { ...record, ...body }; return Promise.resolve(Response.json({ id: 'f1' }, { status: 201 }))
    }
    if (init?.method === 'PUT' && path === '/api/families/f1') return Promise.resolve(Response.json({ detail: 'Recarregue a ficha: outro usuário alterou a família.' }, { status: 409 }))
    if (url.pathname === '/api/families/available-properties') return Promise.resolve(Response.json({ items: [property], total: 1, page: 1, pageSize: 25 }))
    if (url.pathname === '/api/families') return Promise.resolve(Response.json({ items: [record], total: 1, page: 1, pageSize: 25 }))
    if (path === '/api/families/f1') return Promise.resolve(Response.json(record))
    if (path === '/api/families/f1/history') return Promise.resolve(Response.json({ links: [], versions: [] }))
    if (path === '/api/families/f1/visits') return Promise.resolve(Response.json([]))
    return Promise.resolve(Response.json({}, { status: 404 }))
  })
  vi.stubGlobal('fetch', fetchMock)
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
function show(current = session) { render(<InteractionProvider><FamilyWorkspace session={current} /></InteractionProvider>) }
async function select() { fireEvent.click(await screen.findByRole('button', { name: /Família 001/ })); await screen.findByRole('heading', { name: 'Família 001' }) }

describe('Cadastro e acompanhamento de famílias', () => {
  it('cadastra somente número e responsável, sem exigir imóvel', async () => {
    show(); fireEvent.click(screen.getByRole('button', { name: 'Cadastrar família' }))
    const dialog = screen.getByRole('dialog', { name: 'Cadastrar família' })
    expect(within(dialog).getAllByRole('textbox')).toHaveLength(2)
    expect(within(dialog).queryByLabelText(/Imóvel/)).not.toBeInTheDocument()
    fireEvent.change(within(dialog).getByLabelText('Número da família'), { target: { value: '002' } })
    fireEvent.change(within(dialog).getByLabelText('Nome do responsável'), { target: { value: 'João de Souza' } })
    fireEvent.submit(dialog)
    expect(await screen.findByRole('heading', { name: 'Família 002' })).toBeInTheDocument()
    const request = fetchMock.mock.calls.find(([path, init]) => path === '/api/families' && init?.method === 'POST')
    expect(JSON.parse(String(request?.[1]?.body))).toMatchObject({ number: '002', responsibleName: 'João de Souza', healthUnitId: 'unit' })
    expect(screen.queryByRole('button', { name: 'Registrar visita' })).not.toBeInTheDocument()
  })

  it('busca por responsável e envia os filtros e paginação ao servidor', async () => {
    show(); fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar família' }), { target: { value: 'D’Ávila' } })
    fireEvent.change(screen.getByLabelText('Situação'), { target: { value: 'unlinked' } })
    await waitFor(() => expect(fetchMock.mock.calls.some(([path]) => { const url = new URL(String(path), 'https://test.local'); return url.searchParams.get('query') === 'D’Ávila' && url.searchParams.get('state') === 'unlinked' && url.searchParams.get('page') === '1' })).toBe(true))
    expect(screen.getByRole('button', { name: 'Anterior' })).toBeDisabled()
  })

  it('vincula um imóvel existente com as versões da família e do imóvel', async () => {
    show(); await select(); fireEvent.click(screen.getByRole('button', { name: 'Vincular imóvel' }))
    const dialog = screen.getByRole('dialog', { name: 'Vincular imóvel' })
    fireEvent.click(await within(dialog).findByRole('button', { name: /Rua das Acácias, nº 10/ }))
    expect(await screen.findByRole('button', { name: 'Alterar imóvel' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Encerrar vínculo' })).toHaveClass('family-unlink-button')
    const request = fetchMock.mock.calls.find(([path, init]) => path === '/api/families/f1/property' && init?.method === 'POST')
    expect(JSON.parse(String(request?.[1]?.body))).toEqual({ propertyId: 'p1', expectedVersion: 'fv1', expectedPropertyVersion: 'pv1' })
    expect(screen.getByRole('button', { name: 'Registrar visita' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Arquivar família' })).toBeDisabled()
  })

  it('cadastra um imóvel sem número familiar e retorna à família para concluir o vínculo', async () => {
    show(); await select(); fireEvent.click(screen.getByRole('button', { name: 'Vincular imóvel' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Cadastrar novo imóvel' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cadastro do imóvel' })
    expect(within(dialog).queryByLabelText('Número da família')).not.toBeInTheDocument()
    fireEvent.change(within(dialog).getByLabelText('Logradouro'), { target: { value: 'Rua das Acácias' } })
    fireEvent.change(within(dialog).getByLabelText('Número da casa'), { target: { value: '10' } })
    await act(async () => maps.at(-1)!.emit('click', { lngLat: { lng: -39.5, lat: -17.5 } }))
    fireEvent.submit(dialog)
    expect(await screen.findByRole('button', { name: 'Alterar imóvel' })).toBeInTheDocument()
    const propertyRequest = fetchMock.mock.calls.find(([path, init]) => path === '/api/properties' && init?.method === 'POST')
    expect(JSON.parse(String(propertyRequest?.[1]?.body))).not.toHaveProperty('familyNumber')
    expect(fetchMock.mock.calls.some(([path, init]) => path === '/api/families/f1/property' && init?.method === 'POST')).toBe(true)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('explica o impacto da mudança e mantém a residência quando houver conflito', async () => {
    record = { ...record, currentProperty: { ...property, id: 'old', street: 'Rua anterior' }, state: 'linked' }; failLink = true
    show(); await select(); fireEvent.click(screen.getByRole('button', { name: 'Alterar imóvel' }))
    fireEvent.click(await within(screen.getByRole('dialog', { name: 'Alterar imóvel' })).findByRole('button', { name: /Rua das Acácias, nº 10/ }))
    const impact = screen.getByRole('alertdialog', { name: 'Confirmar mudança de imóvel?' })
    expect(within(impact).getByText(/As visitas antigas manterão o endereço/)).toBeInTheDocument()
    fireEvent.click(within(impact).getByRole('button', { name: 'Confirmar mudança' }))
    await waitFor(() => expect(within(screen.getByRole('dialog', { name: 'Alterar imóvel' })).getByRole('alert')).toHaveTextContent('O imóvel já possui outra família.'))
    expect(within(screen.getByRole('article', { name: 'Ficha da família' })).getByText('Rua anterior, nº 10')).toBeInTheDocument()
  })

  it('preserva campos editados após concorrência e protege saída por teclado', async () => {
    show(); await select(); fireEvent.click(screen.getByRole('button', { name: 'Editar família' }))
    const dialog = screen.getByRole('dialog', { name: 'Editar família' })
    fireEvent.change(within(dialog).getByLabelText('Nome do responsável'), { target: { value: 'Nome alterado' } }); fireEvent.submit(dialog)
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Recarregue a ficha')
    expect(within(dialog).getByLabelText('Nome do responsável')).toHaveValue('Nome alterado')
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(await screen.findByRole('alertdialog', { name: 'Descartar alterações?' })).toBeInTheDocument()
  })

  it('limita ações para perfil de consulta e apresenta família arquivada', async () => {
    record = { ...record, state: 'archived', archivedAtUtc: '2026-09-20T12:00:00Z', coverageStatus: 'archived' }
    show({ ...session, permissions: ['families.view'] }); await select()
    expect(screen.queryByRole('button', { name: 'Cadastrar família' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Editar família' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Vincular imóvel' })).not.toBeInTheDocument()
    expect(screen.getAllByText('Arquivada').length).toBeGreaterThan(0)
    expect(fetchMock.mock.calls.some(([path]) => String(path).endsWith('/visits'))).toBe(false)
  })
})
