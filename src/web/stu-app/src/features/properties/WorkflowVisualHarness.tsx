import { createRoot } from 'react-dom/client'
import { Dashboard } from '../dashboard/Dashboard'
import { InteractionProvider, type Session } from '@stu/shared'
import { TerritoryComparison } from '../../../../stu-shared/src/territory/TerritoryComparison'
import '../../styles/global.css'
import '../../app/app.css'

// Local, synthetic, read-only harness; excluded from the production entry point.
if (!import.meta.env.DEV || !['localhost', '127.0.0.1'].includes(location.hostname)) throw new Error('Somente validação local')
const boundary = { type: 'Polygon' as const, coordinates: [[[-39.758,-17.54],[-39.752,-17.54],[-39.752,-17.535],[-39.758,-17.535],[-39.758,-17.54]]] }
const after = { type: 'Polygon' as const, coordinates: [[[-39.758,-17.54],[-39.754,-17.54],[-39.754,-17.535],[-39.758,-17.535],[-39.758,-17.54]]] }
const session: Session = { id: 'visual-user', userName: 'visual', displayName: 'Gerente de validação', mustChangePassword: false, healthUnit: { id: 'visual-unit', code: 'TESTE', name: 'UBS fictícia — validação visual' }, roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }], permissions: ['properties.view', 'properties.manage', 'visits.view', 'visits.manage', 'territory.manage'] }
const records = [
  { id: 'p1', houseNumber: '120', familyNumber: '001', registrationStatus: 'Active', archivedAtUtc: null, coverageStatus: 'overdue', lastVisitAtUtc: '2026-05-01T12:00:00Z' },
  { id: 'p2', houseNumber: '122', familyNumber: '002', registrationStatus: 'Active', archivedAtUtc: null, coverageStatus: 'neverVisited', lastVisitAtUtc: null },
  { id: 'p3', houseNumber: '124', familyNumber: '003', registrationStatus: 'Draft', archivedAtUtc: null, coverageStatus: 'neverVisited', lastVisitAtUtc: null },
  { id: 'p4', houseNumber: '126', familyNumber: '004', registrationStatus: 'Active', archivedAtUtc: '2026-09-01', coverageStatus: 'neverVisited', lastVisitAtUtc: null },
].map(item => ({ ...item, street: 'Rua de demonstração', microregionId: 'micro', healthUnitId: 'visual-unit', geometry: { type: 'Point', coordinates: [-39.755,-17.537] }, situation: 'Occupied', tags: [], concurrencyToken: 'visual' }))
const originalFetch = globalThis.fetch.bind(globalThis)
globalThis.fetch = async (input, init) => {
  const path = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
  if (!path.startsWith('/api/')) return originalFetch(input, init)
  if (init?.method && init.method !== 'GET') return Response.json({ detail: 'Demonstração local: nenhuma gravação é permitida.' }, { status: 403 })
  const url = new URL(path, location.origin)
  if (url.pathname === '/api/properties/address-suggestion') return Response.json({ found: true, street: 'Rua fictícia de validação', postalCode: '45990-000' })
  if (path.startsWith('/api/dashboard')) return Response.json({ healthUnitName: session.healthUnit?.name, activeProperties: 4, activeFamilyIdentifiers: 4, visitsThisMonth: 18, visitsPreviousMonth: 15, visitsChangePercent: 20, coverageAlerts: 2, unassignedMicroregions: 0, coverage: { covered: 2, overdue: 1, neverVisited: 1, notConfigured: 0 }, microregions: [{ id: 'micro', code: 'MR01', name: 'Microrregião de demonstração', color: '#2F6BBD', assigned: true, activeProperties: 4, covered: 2, alerts: 2 }], stage: '1 microrregião ativa no mapa' })
  if (path.startsWith('/api/notifications')) return Response.json({ items: [], unreadCount: 0 })
  if (url.pathname === '/api/territories/map') return Response.json({ type: 'FeatureCollection', features: [{ type: 'Feature', id: 'micro', geometry: boundary, properties: { entityType: 'microregion', id: 'micro', code: 'MR01', name: 'Microrregião de demonstração', color: '#2F6BBD', assignedAgentId: 'visual-user', neighborhoodIds: [] } }] })
  if (url.pathname === '/api/territories/microregions/archived') return Response.json({ type: 'FeatureCollection', features: [] })
  if (url.pathname === '/api/properties/map') return Response.json({ type: 'FeatureCollection', features: records.map(item => ({ type: 'Feature', id: item.id, geometry: item.geometry, properties: { entityType: 'property', houseNumber: item.houseNumber, familyNumber: item.familyNumber, coverageStatus: item.coverageStatus } })) })
  if (url.pathname === '/api/territories/reference-data') return Response.json({ selectedHealthUnitId: 'visual-unit', neighborhoods: [], agents: [{ id: 'visual-user', displayName: 'Agente de demonstração' }], healthUnits: [session.healthUnit] })
  if (url.pathname === '/api/properties/reference-data') return Response.json({ selectedHealthUnitId: 'visual-unit', microregions: [{ id: 'micro', name: 'Microrregião de demonstração', code: 'MR01', boundary }], tags: [], coverageRules: [] })
  if (url.pathname === '/api/properties') {
    const state = url.searchParams.get('recordState'), coverage = url.searchParams.get('coverage'), query = url.searchParams.get('query')?.toLowerCase()
    let items = records.filter(item => state === 'all' || (state === 'archived' ? !!item.archivedAtUtc : !item.archivedAtUtc && item.registrationStatus === (state === 'draft' ? 'Draft' : 'Active')))
    if (query) items = items.filter(item => `${item.street} ${item.houseNumber} ${item.familyNumber}`.toLowerCase().includes(query))
    const coverageSummary = { total: items.length, overdue: items.filter(item => item.coverageStatus === 'overdue').length, neverVisited: items.filter(item => item.coverageStatus === 'neverVisited').length, covered: 0, notConfigured: 0 }
    if (coverage) items = items.filter(item => coverage === 'pending' ? ['overdue','neverVisited'].includes(item.coverageStatus) : item.coverageStatus === coverage)
    return Response.json({ items, total: items.length, coverageSummary })
  }
  if (path.startsWith('/api/properties/p1/visits')) return Response.json([{ id: 'v1', visitedAtUtc: '2026-05-01T12:00:00Z', agentName: 'Agente de demonstração', type: 'Routine', outcome: 'Completed', observedSituation: 'Occupied', accessDifficulty: false, note: 'Registro fictício para validar a interface.', archivedAtUtc: null }])
  if (/\/api\/properties\/p\d\/(visits|versions)/.test(path) || path.startsWith('/api/property-settings/tags')) return Response.json([])
  return Response.json({}, { status: 404 })
}
const visualView = new URLSearchParams(location.search).get('view')
createRoot(document.getElementById('root')!).render(visualView === 'comparison'
  ? <InteractionProvider><TerritoryComparison before={boundary} after={after} validated onClose={() => location.assign('/workflow-visual-test.html')} /></InteractionProvider>
  : <Dashboard session={session} logout={async () => {}} />)
if (visualView === 'map') window.setTimeout(() => {
  const mapButton = [...document.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.includes('Mapa territorial'))
  mapButton?.click()
}, 250)
