import { createRoot } from 'react-dom/client'
import { Dashboard } from '../dashboard/Dashboard'
import { InteractionProvider, type Session } from '@stu/shared'
import { TerritoryComparison } from '../../../../stu-shared/src/territory/TerritoryComparison'
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
  if (url.pathname === '/api/onboarding/readiness') return Response.json({ generatedAtUtc: '2026-09-18T15:00:00Z', snapshotHash: 'visual', healthUnit: session.healthUnit, counts: { neighborhoods: 3, microregions: 5, properties: 1284, activeUsers: 18, temporaryPasswords: 2, healthAgents: 10, receptionists: 3, doctors: 4, managers: 1 }, importSummary: { beforeLatestImport: 1036, latestImported: 248, currentTotal: 1284, pendingImports: 1 }, backup: { scheduleEnabled: true, runId: 'backup', completedAtUtc: '2026-09-16T02:00:00Z', sha256: 'a'.repeat(64), sizeBytes: 2048, recent: true }, readyForApproval: false, checks: [{ id: 'neighborhood-count', label: 'Três bairros vinculados', status: 'passed', detail: '3 de 3 bairros vinculados.' }, { id: 'microregions', label: 'Microrregiões cadastradas', status: 'passed', detail: '5 microrregiões ativas.' }, { id: 'properties', label: 'Imóveis cadastrados para o ensaio', status: 'passed', detail: '1.284 imóveis ativos.' }, { id: 'agent-assignments', label: 'Todas as microrregiões com agente ativo da UBS', status: 'blocked', detail: '1 microrregião ainda está sem agente responsável.' }, { id: 'pending-imports', label: 'Sem importação aguardando processamento ou aprovação', status: 'blocked', detail: '1 importação aguarda aprovação.' }, { id: 'backup-schedule', label: 'Rotina semanal de backup ativa', status: 'passed', detail: 'Rotina ativa.' }], latestApproval: null })
  if (url.pathname === '/api/pilot-release/readiness') return Response.json({ generatedAtUtc: '2026-09-18T15:10:00Z', releaseSnapshotHash: 'release', onboardingSnapshotHash: 'visual', healthUnit: session.healthUnit, readyForDecision: false, released: false, checks: [{ id: 'onboarding', label: 'Pré-implantação aprovada e atual', status: 'blocked', detail: 'Conclua e aprove a pré-implantação da UBS para o estado atual.' }, { id: 'offsite-backup', label: 'Cópia externa criptografada comprovada', status: 'passed', detail: 'Destino registrado.' }, { id: 'recent-backup', label: 'Backup íntegro e recente', status: 'passed', detail: 'Cópia íntegra dos últimos sete dias.' }, { id: 'worker', label: 'Processamento em segundo plano ativo', status: 'passed', detail: 'Processador respondendo.' }, { id: 'storage', label: 'Armazenamento operacional disponível', status: 'passed', detail: 'Armazenamento disponível.' }, { id: 'operations', label: 'Sem operação crítica pendente', status: 'blocked', detail: '1 importação exige revisão.' }], evidence: [{ label: 'Plano de contingência', document: 'docs/contingencia.pdf' }], latestDecision: null })
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
const targetNavigation = visualView === 'families' ? 'Famílias' : visualView === 'map' || visualView === 'territory-editor' ? 'Território' : visualView === 'coverage' ? 'Cobertura' : visualView === 'users' ? 'Servidores' : visualView === 'operations' ? 'Arquivos e dados' : visualView === 'onboarding' ? 'Pré-implantação' : visualView === 'release' ? 'Liberação do piloto' : null
if (targetNavigation) window.setTimeout(() => {
  const targetButton = [...document.querySelectorAll<HTMLButtonElement>('nav button')].find(button => button.textContent?.includes(targetNavigation))
  targetButton?.click()
}, 250)
if (visualView === 'territory-editor') window.setTimeout(() => {
  const createButton = [...document.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.includes('Microrregião'))
  createButton?.click()
}, 1800)
