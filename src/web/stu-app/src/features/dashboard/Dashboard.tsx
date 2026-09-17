import { lazy, Suspense, useEffect, useRef, useState } from 'react'
import { InteractionProvider, useUiActions, type AuthenticatedContext } from '@stu/shared'
import { StuIcon } from './StuIcon'

const TerritoryWorkspace = lazy(async () => ({ default: (await import('@stu/shared/territory')).TerritoryWorkspace }))
const PropertyWorkspace = lazy(async () => ({ default: (await import('@stu/shared/properties')).PropertyWorkspace }))
const OperationalWorkspace = lazy(async () => ({ default: (await import('@stu/shared/operations')).OperationalWorkspace }))
const NotificationCenter = lazy(async () => ({ default: (await import('@stu/shared/notifications')).NotificationCenter }))
const OnboardingWorkspace = lazy(async () => ({ default: (await import('@stu/shared/onboarding')).OnboardingWorkspace }))
const PilotReleaseWorkspace = lazy(async () => ({ default: (await import('@stu/shared/pilot-release')).PilotReleaseWorkspace }))
const HealthUnitUsersWorkspace = lazy(async () => ({ default: (await import('@stu/shared/health-unit-users')).HealthUnitUsersWorkspace }))
const TerritoryOverviewMap = lazy(async () => ({ default: (await import('./TerritoryOverviewMap')).TerritoryOverviewMap }))

type CoverageSummary = { covered: number; overdue: number; neverVisited: number; notConfigured: number }
type MicroregionSummary = { id: string; code: string; name: string; color: string; assigned: boolean; activeProperties: number; covered: number; alerts: number }
type DashboardSummary = {
  healthUnitName: string | null
  activeProperties: number
  activeFamilyIdentifiers: number
  visitsThisMonth: number
  visitsPreviousMonth: number
  visitsChangePercent: number | null
  coverageAlerts: number
  unassignedMicroregions: number
  coverage: CoverageSummary
  microregions: MicroregionSummary[]
  stage: string
}
type Section = 'overview' | 'map' | 'properties' | 'coverage' | 'operations' | 'users' | 'onboarding' | 'pilot-release'
type NavItem = { id: Section; label: string; icon: Parameters<typeof StuIcon>[0]['name']; management?: boolean }

const navigation: NavItem[] = [
  { id: 'overview', label: 'Visão geral', icon: 'home' },
  { id: 'map', label: 'Mapa territorial', icon: 'map' },
  { id: 'properties', label: 'Imóveis', icon: 'building' },
  { id: 'operations', label: 'Arquivos e dados', icon: 'database' },
]

export function Dashboard(props: AuthenticatedContext) {
  return <InteractionProvider key={`${props.session.id}:${props.session.healthUnit?.id ?? 'global'}`}><DashboardContent {...props} /></InteractionProvider>
}

function DashboardContent({ session, logout }: AuthenticatedContext) {
  const { guard } = useUiActions()
  const [createRequest, setCreateRequest] = useState(0)
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [section, changeSection] = useState<Section>('overview')
  const [mobileMoreOpen, setMobileMoreOpen] = useState(false)
  const online = useOnlineStatus()
  const pageHeading = useRef<HTMLHeadingElement>(null)
  const previousSection = useRef<Section>('overview')

  function setSection(next: Section) {
    setMobileMoreOpen(false)
    if (next !== section) void guard(() => changeSection(next))
  }
  function openCreate() {
    void guard(() => { setCreateRequest(value => value + 1); changeSection('properties') })
  }

  useEffect(() => {
    let active = true
    fetch('/api/dashboard/summary', { credentials: 'include' }).then(async response => {
      if (!response.ok) throw new Error('Não foi possível carregar os indicadores.')
      return await response.json() as DashboardSummary
    }).then(data => { if (active) setSummary(data) }).catch(caught => { if (active) setError(caught instanceof Error ? caught.message : 'Falha ao carregar o painel.') })
    return () => { active = false }
  }, [])

  useEffect(() => {
    if (previousSection.current !== section) pageHeading.current?.focus()
    previousSection.current = section
  }, [section])

  const roleLabel = session.roles.map(role => role.displayName).join(', ')
  const firstName = session.displayName.trim().split(/\s+/)[0]
  const canOnboard = session.roles.some(role => role.name === 'HealthUnitManager' || role.name === 'GlobalAdministrator')
  const canManageUsers = session.permissions.includes('*') || session.permissions.includes('health_unit.users.manage')
  const canManageProperties = session.permissions.includes('*') || session.permissions.includes('properties.manage')
  const managementItems: NavItem[] = [
    { id: 'coverage', label: 'Cobertura', icon: 'coverage', management: true },
    ...(canManageUsers ? [{ id: 'users', label: 'Servidores', icon: 'users', management: true } as NavItem] : []),
    ...(canOnboard ? [
      { id: 'onboarding', label: 'Pré-implantação', icon: 'check', management: true } as NavItem,
      { id: 'pilot-release', label: 'Liberação do piloto', icon: 'flag', management: true } as NavItem,
    ] : []),
  ]

  return <>
    <a className="skip-link" href="#main-content">Ir para o conteúdo principal</a>
    <div className="workspace">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark">STU</span><span className="brand-copy">Sistema Territorial<br />das UBS</span></div>
        <nav aria-label="Navegação principal">
          <span className="nav-caption">Operação</span>
          {navigation.map(item => <NavigationButton current={section} item={item} key={item.id} select={setSection} />)}
          <span className="nav-caption nav-caption--second">Gestão</span>
          {managementItems.map(item => <NavigationButton current={section} item={item} key={item.id} select={setSection} />)}
          <button className="nav-item nav-item--management" disabled type="button"><span><StuIcon name="settings" /></span>Configurações<small>em breve</small></button>
          <div className="mobile-more">
            <button aria-expanded={mobileMoreOpen} className={managementItems.some(item => item.id === section) ? 'nav-item nav-item--active' : 'nav-item'} onClick={() => setMobileMoreOpen(value => !value)} type="button"><span><StuIcon name="more" /></span>Mais</button>
            {mobileMoreOpen && <div className="mobile-more-menu">{managementItems.map(item => <button key={item.id} onClick={() => setSection(item.id)} type="button"><StuIcon name={item.icon} />{item.label}</button>)}<button onClick={() => void guard(() => { void logout() })} type="button"><StuIcon name="logout" />Sair</button></div>}
          </div>
        </nav>
        <div className="sidebar-user"><span className="avatar">{initials(session.displayName)}</span><span><strong>{session.displayName}</strong><small>{roleLabel}</small></span><button aria-label="Sair do STU" onClick={() => void guard(() => { void logout() })} title="Sair" type="button"><StuIcon name="logout" size={18} /></button></div>
      </aside>
      <main className="dashboard" id="main-content">
        {!online && <div className="connectivity-banner" role="status"><StuIcon name="warning" /><span><strong>Você está sem conexão.</strong> A consulta e o envio de informações estão temporariamente indisponíveis.</span></div>}
        <header className="dashboard-header">
          <div><span className="breadcrumb">{section === 'overview' ? formatToday() : 'Operação territorial'}</span><h1 ref={pageHeading} tabIndex={-1}>{sectionTitle(section, firstName)}</h1><p>{summary?.healthUnitName ?? session.healthUnit?.name ?? 'Administração global'}</p></div>
          <div className="header-actions"><Suspense fallback={null}><NotificationCenter /></Suspense>{section === 'overview' && canManageProperties && <button className="primary-action" disabled={!online} onClick={openCreate} title={online ? undefined : 'Conecte-se para cadastrar um imóvel'} type="button"><StuIcon name="plus" size={18} /> Cadastrar imóvel</button>}</div>
        </header>
        {error && <div className="dashboard-error" role="alert">{error}</div>}
        {section === 'overview' && <Overview canCreate={canManageProperties} healthUnitId={session.healthUnit?.id} online={online} openCreate={openCreate} openCoverage={() => setSection('coverage')} openMap={() => setSection('map')} openProperties={() => setSection('properties')} summary={summary} />}
        {section === 'map' && <Suspense fallback={<WorkspaceSkeleton label="Carregando o mapa" />}><TerritoryWorkspace global={session.roles.some(role => role.name === 'GlobalAdministrator')} session={session} /></Suspense>}
        {(section === 'properties' || section === 'coverage') && <Suspense fallback={<WorkspaceSkeleton label="Carregando os imóveis" />}><PropertyWorkspace key={section} mode={section === 'coverage' ? 'coverage' : 'properties'} createRequest={section === 'properties' ? createRequest : 0} onOpenTerritory={() => setSection('map')} session={session} /></Suspense>}
        {section === 'operations' && <Suspense fallback={<WorkspaceSkeleton label="Carregando arquivos e dados" />}><OperationalWorkspace session={session} /></Suspense>}
        {section === 'users' && canManageUsers && <Suspense fallback={<WorkspaceSkeleton label="Carregando a equipe" />}><HealthUnitUsersWorkspace session={session} /></Suspense>}
        {section === 'onboarding' && canOnboard && <Suspense fallback={<WorkspaceSkeleton label="Verificando a pré-implantação" />}><OnboardingWorkspace session={session} /></Suspense>}
        {section === 'pilot-release' && canOnboard && <Suspense fallback={<WorkspaceSkeleton label="Conferindo a liberação" />}><PilotReleaseWorkspace session={session} /></Suspense>}
      </main>
    </div>
  </>
}

function NavigationButton({ current, item, select }: { current: Section; item: NavItem; select: (section: Section) => void }) {
  return <button aria-current={item.id === current ? 'page' : undefined} className={`${item.id === current ? 'nav-item nav-item--active' : 'nav-item'}${item.management ? ' nav-item--management' : ''}`} onClick={() => select(item.id)} type="button"><span><StuIcon name={item.icon} /></span>{item.label}</button>
}

function Overview({ canCreate, healthUnitId, online, openCreate, openCoverage, openMap, openProperties, summary }: { canCreate: boolean; healthUnitId?: string; online: boolean; openCreate: () => void; openCoverage: () => void; openMap: () => void; openProperties: () => void; summary: DashboardSummary | null }) {
  const coverageTotal = summary ? Object.values(summary.coverage ?? {}).reduce((total, value) => total + value, 0) : 0
  return <div className="overview">
    <section className="metrics" aria-label="Indicadores da UBS">
      <Metric detail={`${summary?.activeFamilyIdentifiers ?? 0} identificações familiares`} icon="building" label="Imóveis ativos" tone="green" value={summary?.activeProperties} />
      <Metric detail={trendLabel(summary?.visitsChangePercent)} icon="calendar" label="Visitas neste mês" tone="blue" value={summary?.visitsThisMonth} />
      <Metric detail="fora do prazo ou sem visita" icon="warning" label="Alertas de cobertura" tone="coral" value={summary?.coverageAlerts} />
      <Metric detail="requerem atribuição" icon="users" label="Microrregiões sem agente" tone="violet" value={summary?.unassignedMicroregions} />
    </section>
    <section className="dashboard-grid">
      <article className="territory-card">
        <div className="card-heading"><div><span className="section-kicker">Território</span><h2>Mapa da área atendida</h2><p>Limites e cobertura atualizados com os dados da UBS.</p></div><button onClick={openMap} type="button">Abrir mapa <StuIcon name="arrow" size={16} /></button></div>
        <Suspense fallback={<div className="overview-map-shell overview-map-loading" role="status">Preparando a prévia territorial…</div>}><TerritoryOverviewMap healthUnitId={healthUnitId} onOpenMap={openMap} /></Suspense>
      </article>
      <aside className="overview-rail">
        <article className="coverage-card">
          <div className="card-heading"><div><span className="section-kicker">Cobertura</span><h2>Situação dos imóveis</h2></div><button onClick={openCoverage} type="button">Ver detalhes</button></div>
          {coverageTotal > 0 ? <>
            <div aria-label={`Cobertura de ${coverageTotal} imóveis`} className="coverage-bar"><i className="coverage-bar__covered" style={{ flexGrow: summary?.coverage?.covered ?? 0 }} /><i className="coverage-bar__attention" style={{ flexGrow: summary?.coverage?.neverVisited ?? 0 }} /><i className="coverage-bar__alert" style={{ flexGrow: summary?.coverage?.overdue ?? 0 }} /><i className="coverage-bar__neutral" style={{ flexGrow: summary?.coverage?.notConfigured ?? 0 }} /></div>
            <dl className="coverage-list"><CoverageRow color="green" label="Em dia" value={summary?.coverage?.covered} /><CoverageRow color="amber" label="Nunca visitado" value={summary?.coverage?.neverVisited} /><CoverageRow color="coral" label="Fora do prazo" value={summary?.coverage?.overdue} /><CoverageRow color="violet" label="Sem regra" value={summary?.coverage?.notConfigured} /></dl>
          </> : <div className="card-empty"><strong>A cobertura aparecerá aqui</strong><span>Cadastre imóveis e defina os prazos das microrregiões.</span></div>}
        </article>
        <article className="quick-card">
          <div><span className="section-kicker">Atalhos</span><h2>Ações rápidas</h2></div>
          <div className="quick-actions">{canCreate && <button disabled={!online} onClick={openCreate} type="button"><span className="quick-icon quick-icon--green"><StuIcon name="plus" /></span><span><strong>Cadastrar imóvel</strong><small>Adicionar endereço ao território</small></span></button>}<button onClick={openProperties} type="button"><span className="quick-icon quick-icon--amber"><StuIcon name="calendar" /></span><span><strong>Registrar visita</strong><small>Escolher imóvel e informar resultado</small></span></button><button onClick={openMap} type="button"><span className="quick-icon quick-icon--blue"><StuIcon name="map" /></span><span><strong>Explorar território</strong><small>Consultar áreas e agentes</small></span></button></div>
          <p className="stage-note">{summary?.stage ?? 'Carregando situação da implantação…'}</p>
        </article>
      </aside>
    </section>
  </div>
}

function Metric({ detail, icon, label, tone, value }: { detail: string; icon: Parameters<typeof StuIcon>[0]['name']; label: string; tone: string; value: number | undefined }) {
  return <article className={`metric metric--${tone}`}><span className="metric-icon"><StuIcon name={icon} /></span><div><small>{label}</small><strong>{value ?? '—'}</strong><p>{detail}</p></div></article>
}

function CoverageRow({ color, label, value }: { color: string; label: string; value?: number }) {
  return <div><dt><i className={`coverage-key coverage-key--${color}`} />{label}</dt><dd>{value ?? 0}</dd></div>
}

function WorkspaceSkeleton({ label }: { label: string }) {
  return <div aria-label={label} className="workspace-skeleton" role="status"><i /><i /><i /><span>{label}…</span></div>
}

function useOnlineStatus() {
  const [online, setOnline] = useState(() => typeof navigator === 'undefined' || navigator.onLine)
  useEffect(() => {
    const update = () => setOnline(navigator.onLine)
    window.addEventListener('online', update)
    window.addEventListener('offline', update)
    return () => { window.removeEventListener('online', update); window.removeEventListener('offline', update) }
  }, [])
  return online
}

function trendLabel(value: number | null | undefined) {
  if (value === null || value === undefined) return 'sem base comparável no mês anterior'
  if (value === 0) return 'mesmo volume do mês anterior'
  return `${value > 0 ? '+' : ''}${value}% em relação ao mês anterior`
}
function initials(name: string) { return name.trim().split(/\s+/).slice(0, 2).map(part => part[0]?.toUpperCase()).join('') }
function formatToday() { return new Intl.DateTimeFormat('pt-BR', { weekday: 'long', day: '2-digit', month: 'long' }).format(new Date()) }
function sectionTitle(section: Section, firstName: string) { return section === 'map' ? 'Território da UBS' : section === 'properties' ? 'Imóveis' : section === 'coverage' ? 'Cobertura' : section === 'operations' ? 'Arquivos e dados' : section === 'users' ? 'Equipe da UBS' : section === 'onboarding' ? 'Preparação do piloto' : section === 'pilot-release' ? 'Decisão do piloto' : `Bom trabalho, ${firstName}.` }
