import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../../app/App'
import { InteractionProvider } from '@stu/shared'
import { TerritoryComparison, comparisonBounds } from '../../../../stu-shared/src/territory/TerritoryComparison'
import type { TerritoryGeometry } from '../../../../stu-shared/src/territory/osmImport'

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

const boundary: TerritoryGeometry = { type: 'Polygon', coordinates: [[[-40,-18],[-39,-18],[-39,-17],[-40,-17],[-40,-18]]] }
const records = [
  { id: 'p1', houseNumber: '12', familyNumber: '34', registrationStatus: 'Active', archivedAtUtc: null },
  { id: 'p2', houseNumber: '14', familyNumber: '36', registrationStatus: 'Active', archivedAtUtc: null },
  { id: 'p3', houseNumber: '16', familyNumber: '38', registrationStatus: 'Draft', archivedAtUtc: null },
  { id: 'p4', houseNumber: '18', familyNumber: '40', registrationStatus: 'Active', archivedAtUtc: '2026-09-01' },
].map(item => ({ ...item, healthUnitId: 'unit', microregionId: 'micro', street: 'Rua de teste', geometry: { type: 'Point', coordinates: [-39.5,-17.5] }, situation: 'Occupied', concurrencyToken: 'version', tags: [], lastVisitAtUtc: null, coverageStatus: 'neverVisited' }))
let fetchMock: ReturnType<typeof vi.fn>
let addressStatus = 200
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } })
beforeEach(() => {
  maps.length = 0
  addressStatus = 200
  fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input), url = new URL(path, 'https://test.local')
    if (path === '/api/auth/me') return Promise.resolve(response({ id: 'manager', userName: 'manager', displayName: 'Gerente', mustChangePassword: false, healthUnit: { id: 'unit', name: 'UBS', code: 'UBS' }, roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }], permissions: ['properties.view','properties.manage','visits.view','visits.manage','territory.manage'] }))
    if (path === '/api/dashboard/summary') return Promise.resolve(response({ healthUnitName: 'UBS' }))
    if (path.startsWith('/api/notifications')) return Promise.resolve(response({ items: [], unreadCount: 0 }))
    if (path === '/api/auth/csrf') return Promise.resolve(response({ token: 'test-token' }))
    if (url.pathname === '/api/properties/address-suggestion') return Promise.resolve(response({ found: true, street: 'Rua sugerida', postalCode: '45990-000' }, addressStatus))
    if (init?.method === 'POST' && path.endsWith('/visits')) return Promise.resolve(response({ detail: 'Observação inválida para a visita.' }, 400))
    if (url.pathname === '/api/properties/reference-data') return Promise.resolve(response({ selectedHealthUnitId: 'unit', microregions: [{ id: 'micro', name: 'Área de teste', code: 'MR', boundary }], tags: [], coverageRules: [] }))
    if (url.pathname === '/api/properties') {
      const state = url.searchParams.get('recordState')
      const items = records.filter(item => state === 'all' || (state === 'archived' ? !!item.archivedAtUtc : !item.archivedAtUtc && item.registrationStatus === (state === 'draft' ? 'Draft' : 'Active')))
      return Promise.resolve(response({ items, total: items.length, coverageSummary: { total: 2, overdue: 1, neverVisited: 1, covered: 0, notConfigured: 0 } }))
    }
    if (/\/api\/properties\/p\d\/(visits|versions)$/.test(url.pathname) || url.pathname === '/api/property-settings/tags') return Promise.resolve(response([]))
    return Promise.resolve(response({}, 404))
  })
  vi.stubGlobal('fetch', fetchMock)
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

async function openProperties() {
  render(<App />)
  const nav = await screen.findByRole('navigation', { name: 'Navegação principal' })
  fireEvent.click(within(nav).getByRole('button', { name: 'Cobertura' }))
  await screen.findByRole('heading', { name: 'Rua de teste, 12' })
  return nav
}

describe('Melhorias operacionais', () => {
  it('busca o endereço ao clicar e preenche somente logradouro e CEP vazios', async () => {
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: 'Cadastrar imóvel' }))
    const editor = await screen.findByRole('dialog', { name: 'Imóvel e número de família' })
    await waitFor(() => expect(maps.length).toBeGreaterThan(0))
    await act(async () => { maps.at(-1)!.emit('click', { lngLat: { lng: -39.5, lat: -17.5 } }) })
    await waitFor(() => expect(within(editor).getByLabelText('Logradouro')).toHaveValue('Rua sugerida'), { timeout: 3000 })
    expect(within(editor).getByLabelText('CEP')).toHaveValue('45990-000')
    expect(within(editor).getByLabelText('Número da casa')).toHaveValue('')
    expect(within(editor).getByLabelText('Número da família')).toHaveValue('')
    expect(fetchMock.mock.calls.some(([path]) => String(path).includes('microregionId=micro&latitude=-17.5&longitude=-39.5'))).toBe(true)
  })

  it('preserva endereço manual e exige confirmação para substituí-lo pela sugestão', async () => {
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: 'Cadastrar imóvel' }))
    const editor = await screen.findByRole('dialog', { name: 'Imóvel e número de família' })
    await waitFor(() => expect(maps.length).toBeGreaterThan(0))
    await act(async () => { maps.at(-1)!.emit('click', { lngLat: { lng: -39.5, lat: -17.5 } }) })
    fireEvent.change(within(editor).getByLabelText('Logradouro'), { target: { value: 'Rua digitada' } })
    fireEvent.change(within(editor).getByLabelText('CEP'), { target: { value: '45990-111' } })
    fireEvent.click(await screen.findByRole('button', { name: 'Usar endereço sugerido' }, { timeout: 3000 }))
    expect(within(editor).getByLabelText('Logradouro')).toHaveValue('Rua digitada')
    expect(within(editor).getByLabelText('CEP')).toHaveValue('45990-111')
    fireEvent.click(await screen.findByRole('button', { name: 'Continuar aqui' }))
    expect(within(editor).getByLabelText('Logradouro')).toHaveValue('Rua digitada')
    fireEvent.click(screen.getByRole('button', { name: 'Usar endereço sugerido' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Usar sugestão' }))
    await waitFor(() => expect(within(editor).getByLabelText('Logradouro')).toHaveValue('Rua sugerida'))
  })

  it('mantém o cadastro manual disponível quando a consulta falha', async () => {
    addressStatus = 503
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: 'Cadastrar imóvel' }))
    const editor = await screen.findByRole('dialog', { name: 'Imóvel e número de família' })
    await waitFor(() => expect(maps.length).toBeGreaterThan(0))
    await act(async () => { maps.at(-1)!.emit('click', { lngLat: { lng: -39.5, lat: -17.5 } }) })
    expect(await screen.findByText('Não foi possível consultar o endereço. Você pode preencher manualmente.', {}, { timeout: 3000 })).toBeInTheDocument()
    fireEvent.change(within(editor).getByLabelText('Logradouro'), { target: { value: 'Rua manual' } })
    expect(within(editor).getByLabelText('Logradouro')).toHaveValue('Rua manual')
    expect(screen.getByRole('button', { name: 'Consultar endereço novamente' })).toBeEnabled()
  })

  it('separa ativos, rascunhos e arquivados com filtros enviados ao servidor', async () => {
    await openProperties()
    expect(screen.getByLabelText('Situação do cadastro')).toHaveValue('active')
    fireEvent.change(screen.getByLabelText('Situação do cadastro'), { target: { value: 'draft' } })
    await screen.findByRole('heading', { name: 'Rua de teste, 16' })
    fireEvent.change(screen.getByLabelText('Situação do cadastro'), { target: { value: 'archived' } })
    await screen.findByRole('heading', { name: 'Rua de teste, 18' })
    expect(screen.queryByRole('button', { name: 'Registrar visita' })).not.toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([path]) => String(path).includes('recordState=archived'))).toBe(true)
  })

  it('restaura busca, filtros e imóvel selecionado ao voltar da visão geral', async () => {
    const nav = await openProperties()
    fireEvent.change(screen.getByLabelText('Buscar imóvel'), { target: { value: 'Rua' } })
    fireEvent.change(screen.getByLabelText('Filtrar microrregião'), { target: { value: 'micro' } })
    await waitFor(() => expect(fetchMock.mock.calls.some(([path]) => String(path).includes('query=Rua'))).toBe(true))
    fireEvent.click(within(screen.getByLabelText('Imóveis encontrados')).getByRole('button', { name: /Rua de teste, 14/ }))
    fireEvent.click(within(nav).getByRole('button', { name: 'Visão geral' }))
    fireEvent.click(within(nav).getByRole('button', { name: 'Cobertura' }))
    await screen.findByRole('heading', { name: 'Rua de teste, 14' })
    expect(screen.getByLabelText('Buscar imóvel')).toHaveValue('Rua')
    expect(screen.getByLabelText('Filtrar microrregião')).toHaveValue('micro')
  })

  it('abre o cadastro pelo atalho e protege o formulário ao fechar ou sair', async () => {
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: 'Cadastrar imóvel' }))
    const editor = await screen.findByRole('dialog', { name: 'Imóvel e número de família' })
    fireEvent.change(within(editor).getByLabelText('Logradouro'), { target: { value: 'Rua nova' } })
    const unload = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(unload)
    expect(unload.defaultPrevented).toBe(true)
    fireEvent.keyDown(document, { key: 'Escape' })
    await screen.findByRole('alertdialog', { name: 'Descartar alterações?' })
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(editor).toBeInTheDocument()
    expect(within(editor).getByLabelText('Logradouro')).toHaveValue('Rua nova')
    fireEvent.click(screen.getByRole('button', { name: 'Fechar cadastro de imóvel' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Descartar e sair' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const cleanUnload = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(cleanUnload)
    expect(cleanUnload.defaultPrevented).toBe(false)
  })

  it('unifica imóveis e cobertura mantendo resumo e filtros operacionais', async () => {
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: 'Cobertura' }))
    await screen.findByRole('heading', { name: 'Imóveis e cobertura' })
    expect(screen.getByLabelText('Filtrar cobertura')).toHaveValue('')
    expect(screen.getByLabelText('Situação do cadastro')).toHaveValue('active')
    expect(screen.getByRole('button', { name: /Cadastrar imóvel/ })).toBeInTheDocument()
    const summary = screen.getByRole('region', { name: 'Resumo da cobertura' })
    await waitFor(() => expect(within(summary).getByRole('button', { name: '2 Precisam de atenção' })).toBeInTheDocument())
    fireEvent.click(within(summary).getByRole('button', { name: '2 Precisam de atenção' }))
    await waitFor(() => expect(fetchMock.mock.calls.some(([path]) => String(path).includes('coverage=pending'))).toBe(true))
  })

  it('mantém o rascunho da visita após erro e pede confirmação ao navegar', async () => {
    const nav = await openProperties()
    fireEvent.click(screen.getByRole('button', { name: 'Registrar visita' }))
    const dialog = screen.getByRole('dialog', { name: 'Registrar visita operacional' })
    fireEvent.change(within(dialog).getByLabelText('Observação curta e opcional'), { target: { value: 'Minha anotação' } })
    fireEvent.submit(dialog)
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Observação inválida')
    expect(within(dialog).getByLabelText('Observação curta e opcional')).toHaveValue('Minha anotação')
    fireEvent.click(within(nav).getByRole('button', { name: 'Visão geral' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Continuar aqui' }))
    expect(dialog).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Fechar registro de visita' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Descartar e sair' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('compara os limites na mesma extensão sem modificar a geometria salva', async () => {
    const after: TerritoryGeometry = { type: 'Polygon', coordinates: [[[-40,-18],[-39.5,-18],[-39.5,-17],[-40,-17],[-40,-18]]] }
    const original = JSON.stringify(boundary)
    render(<InteractionProvider><TerritoryComparison before={boundary} after={after} validated onClose={vi.fn()} /></InteractionProvider>)
    await act(async () => {})
    expect(screen.getByRole('region', { name: 'Limite atual salvo' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Limite proposto' })).toBeInTheDocument()
    expect(comparisonBounds(boundary, after)).toEqual([[-40,-18],[-39,-17]])
    expect(maps).toHaveLength(2)
    expect(maps[0].fitBounds.mock.lastCall).toEqual(maps[1].fitBounds.mock.lastCall)
    expect(JSON.stringify(boundary)).toBe(original)
  })
})
