import { createRoot } from 'react-dom/client'
import { Dashboard } from '../dashboard/Dashboard'
import { InteractionProvider, type Session } from '@stu/shared'
import { TerritoryComparison } from '../../../../stu-shared/src/territory/TerritoryComparison'
import { exampleData } from '../registration-preview/data'
import '../../styles/global.css'
import '../../app/app.css'
import '../../styles/purple-theme.css'

// Local, synthetic, read-only harness; excluded from the production entry point.
if (!import.meta.env.DEV || !['localhost', '127.0.0.1'].includes(location.hostname)) throw new Error('Somente validação local')
const boundary = { type: 'Polygon' as const, coordinates: [[[-39.758,-17.54],[-39.752,-17.54],[-39.752,-17.535],[-39.758,-17.535],[-39.758,-17.54]]] }
const after = { type: 'Polygon' as const, coordinates: [[[-39.758,-17.54],[-39.754,-17.54],[-39.754,-17.535],[-39.758,-17.535],[-39.758,-17.54]]] }
const session: Session = { id: 'visual-user', userName: 'visual', displayName: 'Gerente de validação', mustChangePassword: false, healthUnit: { id: 'visual-unit', code: 'TESTE', name: 'UBS fictícia — validação visual' }, roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }], permissions: ['families.view', 'families.manage', 'properties.view', 'properties.manage', 'visits.view', 'visits.manage', 'territory.manage', 'health_unit.users.manage', 'reports.export'] }
const records = [
  { id: 'p1', houseNumber: '120', familyNumber: '001', registrationStatus: 'Active', archivedAtUtc: null, coverageStatus: 'overdue', lastVisitAtUtc: '2026-05-01T12:00:00Z' },
  { id: 'p2', houseNumber: '122', familyNumber: '002', registrationStatus: 'Active', archivedAtUtc: null, coverageStatus: 'neverVisited', lastVisitAtUtc: null },
  { id: 'p3', houseNumber: '124', familyNumber: '003', registrationStatus: 'Draft', archivedAtUtc: null, coverageStatus: 'neverVisited', lastVisitAtUtc: null },
  { id: 'p4', houseNumber: '126', familyNumber: '004', registrationStatus: 'Active', archivedAtUtc: '2026-09-01', coverageStatus: 'neverVisited', lastVisitAtUtc: null },
].map(item => ({ ...item, street: 'Rua de demonstração', microregionId: 'micro', healthUnitId: 'visual-unit', geometry: { type: 'Point', coordinates: [-39.755,-17.537] }, situation: 'Occupied', tags: [], concurrencyToken: 'visual' }))
const familyRecords = [
  { id: 'f1', number: '001', responsibleName: 'Ana-María D’Ávila', state: 'linked', currentProperty: { ...records[0], startedAtUtc: '2026-09-15T12:00:00Z' }, coverageStatus: 'covered', lastVisitAtUtc: '2026-09-10T12:00:00Z', archivedAtUtc: null },
  { id: 'f2', number: '002', responsibleName: 'João de Souza', state: 'unlinked', currentProperty: null, coverageStatus: 'noProperty', lastVisitAtUtc: null, archivedAtUtc: null },
  { id: 'f3', number: '003', responsibleName: 'Rosa dos Santos', state: 'archived', currentProperty: null, coverageStatus: 'archived', lastVisitAtUtc: null, archivedAtUtc: '2026-09-01T12:00:00Z' },
].map(item => ({ ...item, healthUnitId: 'visual-unit', concurrencyToken: 'visual' }))
const originalFetch = globalThis.fetch.bind(globalThis)
globalThis.fetch = async (input, init) => {
  const path = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
  if (!path.startsWith('/api/')) return originalFetch(input, init)
  if (init?.method && init.method !== 'GET') return Response.json({ detail: 'Demonstração local: nenhuma gravação é permitida.' }, { status: 403 })
  const url = new URL(path, location.origin)
  if (url.pathname === '/api/registration/units') return Response.json([session.healthUnit])
  if (url.pathname === '/api/registration') return Response.json(exampleData())
  if (url.pathname === '/api/families') {
    const state = url.searchParams.get('state'), query = url.searchParams.get('query')?.toLowerCase() ?? ''
    const items = familyRecords.filter(f => (state === 'all' || (state === 'active' ? !f.archivedAtUtc : f.state === state)) && `${f.number} ${f.responsibleName}`.toLowerCase().includes(query))
    return Response.json({ items, total: items.length, page: 1, pageSize: 25 })
  }
  if (url.pathname === '/api/families/available-properties') return Response.json({ items: [records[1]], total: 1, page: 1, pageSize: 25 })
  const family = familyRecords.find(f => url.pathname === `/api/families/${f.id}`)
  if (family) return Response.json(family)
  if (/\/api\/families\/f\d\/history/.test(path)) return Response.json({ links: path.includes('/f1/') ? [{ id: 'l1', propertyId: 'p1', street: 'Rua de demonstração', houseNumber: '120', startedAtUtc: '2026-09-15T12:00:00Z', endedAtUtc: null }, { id: 'l0', propertyId: 'p2', street: 'Rua das Acácias', houseNumber: '80', startedAtUtc: '2026-06-01T12:00:00Z', endedAtUtc: '2026-09-15T12:00:00Z' }] : [], versions: [{ id: 'fv1', versionNumber: 1, number: '001', responsibleName: 'Ana-María D’Ávila', changeKind: 'Create', changedAtUtc: '2026-06-01T12:00:00Z' }] })
  if (/\/api\/families\/f\d\/visits/.test(path)) return Response.json(path.includes('/f1/') ? [{ id: 'v1', familyId: 'f1', propertyId: 'p2', street: 'Rua das Acácias', houseNumber: '80', visitedAtUtc: '2026-09-10T12:00:00Z', agentName: 'Agente de demonstração', type: 'Routine', outcome: 'Completed', observedSituation: 'Occupied', accessDifficulty: false, note: 'Registro fictício anterior à mudança de imóvel.', archivedAtUtc: null, concurrencyToken: 'visual' }] : [])
  if (url.pathname === '/api/properties/address-suggestion') return Response.json({ found: true, street: 'Rua fictícia de validação', postalCode: '45990-000' })
  if (url.pathname === '/api/health-unit-users/reference-data') return Response.json({ healthUnit: session.healthUnit, roles: [{ id: 'manager-role', name: 'HealthUnitManager', displayName: 'Gerente da UBS', description: null, isSystem: true }, { id: 'agent-role', name: 'HealthAgent', displayName: 'Agente de saúde', description: null, isSystem: true }] })
  if (url.pathname === '/api/health-unit-users') return Response.json({ items: [{ id: 'visual-user', userName: 'gerente.ubs', displayName: 'Gerente da UBS Piloto', role: { id: 'manager-role', name: 'HealthUnitManager', displayName: 'Gerente da UBS', description: null, isSystem: true }, mustChangePassword: false, lockoutEnd: null, archivedAtUtc: null, manageable: false }, { id: 'agent-user', userName: 'maria.silva', displayName: 'Maria da Silva', role: { id: 'agent-role', name: 'HealthAgent', displayName: 'Agente de saúde', description: null, isSystem: true }, mustChangePassword: true, lockoutEnd: null, archivedAtUtc: null, manageable: true }, { id: 'archived-user', userName: 'joao.santos', displayName: 'João Santos', role: { id: 'agent-role', name: 'HealthAgent', displayName: 'Agente de saúde', description: null, isSystem: true }, mustChangePassword: false, lockoutEnd: null, archivedAtUtc: '2026-08-01T12:00:00Z', manageable: true }] })
  if (path.startsWith('/api/dashboard')) return Response.json({ healthUnitName: session.healthUnit?.name, activeProperties: 4, activeFamilyIdentifiers: 4, visitsThisMonth: 18, visitsPreviousMonth: 15, visitsChangePercent: 20, coverageAlerts: 2, unassignedMicroregions: 0, coverage: { covered: 2, overdue: 1, neverVisited: 1, notConfigured: 0 }, microregions: [{ id: 'micro', code: 'MR01', name: 'Microrregião de demonstração', color: '#6D4AFF', assigned: true, activeProperties: 4, covered: 2, alerts: 2 }], stage: '1 microrregião ativa no mapa' })
  if (path.startsWith('/api/notifications')) return Response.json({ items: [], unreadCount: 3 })
  if (url.pathname === '/api/territories/map') return Response.json({ type: 'FeatureCollection', features: [{ type: 'Feature', id: 'micro', geometry: boundary, properties: { entityType: 'microregion', id: 'micro', code: 'MR01', name: 'Microrregião de demonstração', color: '#6D4AFF', assignedAgentId: 'visual-user', neighborhoodIds: [] } }] })
  if (url.pathname === '/api/territories/microregions/archived') return Response.json({ type: 'FeatureCollection', features: [] })
  if (url.pathname === '/api/properties/map') return Response.json({ type: 'FeatureCollection', features: records.map(item => ({ type: 'Feature', id: item.id, geometry: item.geometry, properties: { entityType: 'property', houseNumber: item.houseNumber, familyNumber: item.familyNumber, coverageStatus: item.coverageStatus } })) })
  if (url.pathname === '/api/territories/reference-data') return Response.json({ selectedHealthUnitId: 'visual-unit', neighborhoods: [], agents: [{ id: 'visual-user', displayName: 'Agente de demonstração' }], healthUnits: [session.healthUnit] })
  if (url.pathname === '/api/properties/reference-data') return Response.json({ selectedHealthUnitId: 'visual-unit', microregions: [{ id: 'micro', name: 'Microrregião de demonstração', code: 'MR01', boundary }], tags: [], coverageRules: [] })
  if (url.pathname === '/api/operations/indicators') return Response.json({ activeProperties: 1284, drafts: 36, unassignedMicroregions: 1, pendingJobs: 1 })
  if (url.pathname === '/api/operations/jobs') return Response.json([{ id: 'import-preview', kind: 'PropertyImport', status: 'AwaitingApproval', format: 'Csv', originalFileName: 'imoveis-setembro.csv', recordCount: 248, validationErrorCount: 0, errorSummary: null, attemptCount: 1, progressPercentage: 50, createdAtUtc: '2026-09-18T14:30:00Z', createdByName: 'Gerente de validação', canDownload: false }])
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
const targetNavigation = visualView === 'families' || visualView === 'overview' ? 'Visão geral' : visualView === 'map' || visualView === 'territory-editor' ? 'Território' : visualView === 'coverage' ? 'Cobertura' : visualView === 'users' ? 'Servidores' : visualView === 'operations' ? 'Arquivos e dados' : null
if (targetNavigation) window.setTimeout(() => {
  const targetButton = [...document.querySelectorAll<HTMLButtonElement>('nav button')].find(button => button.textContent?.includes(targetNavigation))
  targetButton?.click()
}, 250)
if (visualView === 'territory-editor') window.setTimeout(() => {
  const createButton = [...document.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.includes('Microrregião'))
  createButton?.click()
}, 1800)
if (visualView === 'families') window.setTimeout(() => {
  const metric = [...document.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.includes('Famílias cadastradas'))
  metric?.click()
}, 500)
if (visualView === 'families') window.setTimeout(() => {
  document.querySelector<HTMLButtonElement>('.family-row')?.click()
}, 900)
if (visualView === 'operations') window.setTimeout(() => {
  const expectedColumns = document.querySelector<HTMLDetailsElement>('.operations-import-columns')
  if (expectedColumns) expectedColumns.open = true
}, 900)
