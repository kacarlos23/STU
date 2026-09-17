import { lazy, Suspense, useEffect, useRef, useState, type FormEvent } from 'react'
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
type Section = 'overview' | 'map' | 'coverage' | 'operations' | 'users' | 'onboarding' | 'pilot-release'
type NavItem = { id: Section; label: string; icon: Parameters<typeof StuIcon>[0]['name']; management?: boolean }

const navigation: NavItem[] = [
  { id: 'overview', label: 'Visão geral', icon: 'home' },
  { id: 'map', label: 'Território', icon: 'map' },
  { id: 'coverage', label: 'Cobertura', icon: 'coverage' },
  { id: 'operations', label: 'Arquivos e dados', icon: 'database' },
]

export function Dashboard(props: AuthenticatedContext) {
  return <InteractionProvider key={`${props.session.id}:${props.session.healthUnit?.id ?? 'global'}`}><DashboardContent {...props} /></InteractionProvider>
}

function DashboardContent({ session, logout }: AuthenticatedContext) {
  const { guard } = useUiActions()
  const [createRequest, setCreateRequest] = useState(0)
  const [headerSearch, setHeaderSearch] = useState('')
  const [propertySearchRequest, setPropertySearchRequest] = useState({ value: '', nonce: 0 })
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [section, changeSection] = useState<Section>('overview')
  const [mobileMoreOpen, setMobileMoreOpen] = useState(false)
  const [profileMenuOpen, setProfileMenuOpen] = useState(false)
  const online = useOnlineStatus()
  const pageHeading = useRef<HTMLHeadingElement>(null)
  const mobileMore = useRef<HTMLDivElement>(null)
  const profileMenu = useRef<HTMLDivElement>(null)
  const profileMenuTrigger = useRef<HTMLButtonElement>(null)
  const previousSection = useRef<Section>('overview')

  function setSection(next: Section) {
    setMobileMoreOpen(false)
    if (next !== section) void guard(() => changeSection(next))
  }
  function openCreate() {
    void guard(() => { setCreateRequest(value => value + 1); changeSection('coverage') })
  }
  function submitHeaderSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const value = headerSearch.trim()
    if (!value) return
    void guard(() => {
      setPropertySearchRequest(current => ({ value, nonce: current.nonce + 1 }))
      changeSection('coverage')
    })
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

  useEffect(() => {
    if (!mobileMoreOpen && !profileMenuOpen) return

    function dismissOpenMenus(event: PointerEvent) {
      const target = event.target as Node
      if (mobileMoreOpen && !mobileMore.current?.contains(target)) setMobileMoreOpen(false)
      if (profileMenuOpen && !profileMenu.current?.contains(target)) setProfileMenuOpen(false)
    }
    function dismissWithKeyboard(event: KeyboardEvent) {
      if (event.key !== 'Escape') return
      setMobileMoreOpen(false)
      if (profileMenuOpen) profileMenuTrigger.current?.focus()
      setProfileMenuOpen(false)
    }

    document.addEventListener('pointerdown', dismissOpenMenus)
    document.addEventListener('keydown', dismissWithKeyboard)
    return () => {
      document.removeEventListener('pointerdown', dismissOpenMenus)
      document.removeEventListener('keydown', dismissWithKeyboard)
    }
  }, [mobileMoreOpen, profileMenuOpen])

  const roleLabel = session.roles.map(role => role.displayName).join(', ')
  const firstName = session.displayName.trim().split(/\s+/)[0]
  const canOnboard = session.roles.some(role => role.name === 'HealthUnitManager' || role.name === 'GlobalAdministrator')
  const canManageUsers = session.permissions.includes('*') || session.permissions.includes('health_unit.users.manage')
  const canManageProperties = session.permissions.includes('*') || session.permissions.includes('properties.manage')
  const managementItems: NavItem[] = [
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
          {navigation.map(item => <NavigationButton current={section} item={item} key={item.id} select={setSection} />)}
          {managementItems.map(item => <NavigationButton current={section} item={item} key={item.id} select={setSection} />)}
          <button aria-label="Configurações (em breve)" className="nav-item nav-item--management" disabled title="Disponível em breve" type="button"><span><StuIcon name="settings" /></span>Configurações</button>
          <div className="mobile-more" ref={mobileMore}>
            <button aria-controls="mobile-more-menu" aria-expanded={mobileMoreOpen} aria-haspopup="menu" className={managementItems.some(item => item.id === section) ? 'nav-item nav-item--active' : 'nav-item'} onClick={() => { setProfileMenuOpen(false); setMobileMoreOpen(value => !value) }} type="button"><span><StuIcon name="more" /></span>Mais</button>
            {mobileMoreOpen && <div className="mobile-more-menu" id="mobile-more-menu" role="menu">{managementItems.map(item => <button key={item.id} onClick={() => setSection(item.id)} role="menuitem" type="button"><StuIcon name={item.icon} />{item.label}</button>)}<button onClick={() => { setMobileMoreOpen(false); void guard(() => { void logout() }) }} role="menuitem" type="button"><StuIcon name="logout" />Sair</button></div>}
          </div>
        </nav>
        <SidebarTopography />
        <div className="sidebar-signature"><span>Território,<br />mais saúde<br />para todos.</span><i /></div>
      </aside>
      <main className={`dashboard${section === 'map' ? ' dashboard--map' : ''}`} id="main-content">
        <header className={`site-header${section === 'map' ? ' site-header--map' : ''}`}>
          {section === 'map' ? <div className="map-page-context"><span><StuIcon name="map" size={22} /></span><div><h1 ref={pageHeading} tabIndex={-1}>Mapa territorial</h1><p>Visualize as microrregiões, a cobertura e os limites do território da UBS.</p></div></div> : <div className="unit-context"><span><strong>{summary?.healthUnitName ?? session.healthUnit?.name ?? 'Administração global'}</strong><StuIcon name="chevron" size={13} /></span><small>{session.healthUnit?.code ? `${session.healthUnit.code} · Município de Teixeira de Freitas` : 'Município de Teixeira de Freitas'}</small></div>}
          {section !== 'map' && <form aria-label="Busca geral" className="site-search" onSubmit={submitHeaderSearch}><button aria-label="Buscar imóveis" type="submit"><StuIcon name="search" size={17} /></button><input aria-label="Buscar imóvel, família ou endereço" onChange={event => setHeaderSearch(event.target.value)} placeholder="Buscar imóvel, família ou endereço..." type="search" value={headerSearch} /></form>}
          <div className="site-header-tools"><div className="site-notifications"><Suspense fallback={null}><NotificationCenter /></Suspense></div><div className={`site-profile${profileMenuOpen ? ' site-profile--open' : ''}`} ref={profileMenu}><button aria-controls="site-profile-menu" aria-expanded={profileMenuOpen} aria-haspopup="menu" aria-label="Menu do usuário" className="site-profile-trigger" onClick={() => { setMobileMoreOpen(false); setProfileMenuOpen(value => !value) }} ref={profileMenuTrigger} type="button"><span className="avatar">{initials(session.displayName)}</span><span className="site-profile-copy"><strong>{session.displayName}</strong><small>{roleLabel}</small></span><StuIcon name="chevron" size={14} /></button>{profileMenuOpen && <div className="site-profile-menu" id="site-profile-menu" role="menu"><button onClick={() => { setProfileMenuOpen(false); void guard(() => { void logout() }) }} role="menuitem" type="button"><StuIcon name="logout" size={17} />Sair do STU</button></div>}</div></div>
        </header>
        <div className={`dashboard-content${section === 'map' ? ' dashboard-content--map' : ''}`}>
          {!online && <div className="connectivity-banner" role="status"><StuIcon name="warning" /><span><strong>Você está sem conexão.</strong> A consulta e o envio de informações estão temporariamente indisponíveis.</span></div>}
          {section !== 'map' && <header className="dashboard-header">
            <div><span className="breadcrumb">{section === 'overview' ? formatToday() : 'Operação territorial'}</span><h1 ref={pageHeading} tabIndex={-1}>{sectionTitle(section, firstName)}</h1><p>{summary?.healthUnitName ?? session.healthUnit?.name ?? 'Administração global'}</p></div>
            <div className="header-actions">{section === 'overview' && canManageProperties && <button className="primary-action" disabled={!online} onClick={openCreate} title={online ? undefined : 'Conecte-se para cadastrar um imóvel'} type="button"><StuIcon name="plus" size={18} /> Cadastrar imóvel</button>}</div>
          </header>}
          {error && <div className="dashboard-error" role="alert">{error}</div>}
          {section === 'overview' && <Overview canCreate={canManageProperties} healthUnitId={session.healthUnit?.id} online={online} openCreate={openCreate} openCoverage={() => setSection('coverage')} openMap={() => setSection('map')} summary={summary} />}
          {section === 'map' && <Suspense fallback={<WorkspaceSkeleton label="Carregando o mapa" />}><TerritoryWorkspace global={session.roles.some(role => role.name === 'GlobalAdministrator')} session={session} /></Suspense>}
          {section === 'coverage' && <Suspense fallback={<WorkspaceSkeleton label="Carregando imóveis e cobertura" />}><PropertyWorkspace createRequest={createRequest} onOpenTerritory={() => setSection('map')} searchRequest={propertySearchRequest} session={session} /></Suspense>}
          {section === 'operations' && <Suspense fallback={<WorkspaceSkeleton label="Carregando arquivos e dados" />}><OperationalWorkspace session={session} /></Suspense>}
          {section === 'users' && canManageUsers && <Suspense fallback={<WorkspaceSkeleton label="Carregando a equipe" />}><HealthUnitUsersWorkspace session={session} /></Suspense>}
          {section === 'onboarding' && canOnboard && <Suspense fallback={<WorkspaceSkeleton label="Verificando a pré-implantação" />}><OnboardingWorkspace session={session} /></Suspense>}
          {section === 'pilot-release' && canOnboard && <Suspense fallback={<WorkspaceSkeleton label="Conferindo a liberação" />}><PilotReleaseWorkspace session={session} /></Suspense>}
        </div>
      </main>
    </div>
  </>
}

function NavigationButton({ current, item, select }: { current: Section; item: NavItem; select: (section: Section) => void }) {
  return <button aria-current={item.id === current ? 'page' : undefined} className={`${item.id === current ? 'nav-item nav-item--active' : 'nav-item'}${item.management ? ' nav-item--management' : ''}`} onClick={() => select(item.id)} type="button"><span><StuIcon name={item.icon} /></span>{item.label}</button>
}

function SidebarTopography() {
  return <svg aria-hidden="true" className="sidebar-topography" fill="none" viewBox="0 0 224 410">
    <g>
      <path d="M224 2c-23 27-42 40-71 58-34 21-59 42-65 76-7 38 15 58 53 69 40 12 66 28 83 54" />
      <path d="M224 24c-24 26-44 38-70 55-29 18-50 38-55 67-5 31 13 47 47 57 40 12 64 28 78 49" />
      <path d="M224 49c-22 21-40 32-63 46-25 16-43 33-47 57-4 25 12 37 41 46 36 11 56 24 69 42" />
      <path d="M224 77c-18 16-34 25-52 37-22 13-37 28-40 46-3 18 10 28 34 35 30 9 48 20 58 34" />
      <path d="M224 108c-15 11-27 18-40 27-17 10-29 21-31 34-2 13 7 20 27 26 20 6 35 14 44 24" />
      <path d="M0 309c27-30 69-40 91-22 25 20 7 52 23 78 17 29 58 34 110 44" />
      <path d="M0 333c24-25 58-33 76-19 20 16 5 42 19 63 10 16 29 25 53 33" />
      <path d="M0 358c20-20 45-25 59-14 15 12 4 31 14 47 5 8 12 14 21 19" />
    </g>
  </svg>
}

function Overview({ canCreate, healthUnitId, online, openCreate, openCoverage, openMap, summary }: { canCreate: boolean; healthUnitId?: string; online: boolean; openCreate: () => void; openCoverage: () => void; openMap: () => void; summary: DashboardSummary | null }) {
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
          <div className="quick-actions">{canCreate && <button disabled={!online} onClick={openCreate} type="button"><span className="quick-icon quick-icon--green"><StuIcon name="plus" /></span><span><strong>Cadastrar imóvel</strong><small>Adicionar endereço ao território</small></span></button>}<button onClick={openCoverage} type="button"><span className="quick-icon quick-icon--amber"><StuIcon name="calendar" /></span><span><strong>Registrar visita</strong><small>Escolher imóvel e informar resultado</small></span></button><button onClick={openMap} type="button"><span className="quick-icon quick-icon--blue"><StuIcon name="map" /></span><span><strong>Explorar território</strong><small>Consultar áreas e agentes</small></span></button></div>
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
function sectionTitle(section: Section, firstName: string) { return section === 'map' ? 'Território da UBS' : section === 'coverage' ? 'Cobertura' : section === 'operations' ? 'Arquivos e dados' : section === 'users' ? 'Equipe da UBS' : section === 'onboarding' ? 'Preparação do piloto' : section === 'pilot-release' ? 'Decisão do piloto' : `Bom trabalho, ${firstName}.` }
