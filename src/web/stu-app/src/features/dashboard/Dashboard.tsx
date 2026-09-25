import { lazy, Suspense, useEffect, useRef, useState, type FormEvent } from 'react'
import { InteractionProvider, useUiActions, type AuthenticatedContext } from '@stu/shared'
import { StuIcon } from './StuIcon'

const TerritoryWorkspace = lazy(async () => ({ default: (await import('@stu/shared/territory')).TerritoryWorkspace }))
const FamilyWorkspace = lazy(async () => ({ default: (await import('@stu/shared/families')).FamilyWorkspace }))
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
  propertiesWithoutFamily: number
  familiesWithoutProperty: number
  visitsThisMonth: number
  visitsPreviousMonth: number
  visitsChangePercent: number | null
  coverageAlerts: number
  unassignedMicroregions: number
  coverage: CoverageSummary
  microregions: MicroregionSummary[]
  stage: string
}
type Section = 'overview' | 'map' | 'families' | 'coverage' | 'operations' | 'users' | 'onboarding' | 'pilot-release'
type NavItem = { id: Section; label: string; icon: Parameters<typeof StuIcon>[0]['name']; management?: boolean }
type WorkspaceRequest = { value: string; nonce: number }

const navigation: NavItem[] = [
  { id: 'overview', label: 'Visão geral', icon: 'home' },
  { id: 'map', label: 'Território', icon: 'map' },
  { id: 'families', label: 'Famílias', icon: 'users' },
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
  const requestNonce = useRef(0)
  const [familySearchRequest, setFamilySearchRequest] = useState<WorkspaceRequest>()
  const [propertySearchRequest, setPropertySearchRequest] = useState<WorkspaceRequest>()
  const [propertySelectionRequest, setPropertySelectionRequest] = useState<WorkspaceRequest>()
  const [propertyCoverageRequest, setPropertyCoverageRequest] = useState({ value: '', nonce: 0 })
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
    void guard(() => { setCreateRequest(value => value + 1); changeSection('families') })
  }
  function openCoverage(filter = '') {
    void guard(() => {
      setPropertyCoverageRequest(current => ({ value: filter, nonce: current.nonce + 1 }))
      changeSection('coverage')
    })
  }
  function submitHeaderSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const value = headerSearch.trim()
    if (!value) return
    void guard(() => {
      const request = { value, nonce: ++requestNonce.current }
      if (canViewFamilies) setFamilySearchRequest(request)
      else setPropertySearchRequest(request)
      changeSection(canViewFamilies ? 'families' : 'coverage')
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
  const canViewFamilies = session.permissions.includes('*') || session.permissions.includes('families.view')
  const canManageFamilies = canViewFamilies && (session.permissions.includes('*') || session.permissions.includes('families.manage'))
  const managementItems: NavItem[] = [
    ...(canManageUsers ? [{ id: 'users', label: 'Servidores', icon: 'users', management: true } as NavItem] : []),
    ...(canOnboard ? [
      { id: 'onboarding', label: 'Pré-implantação', icon: 'check', management: true } as NavItem,
      { id: 'pilot-release', label: 'Liberação do piloto', icon: 'flag', management: true } as NavItem,
    ] : []),
  ]
  const activeModule = moduleContext(section)

  return <>
    <a className="skip-link" href="#main-content">Ir para o conteúdo principal</a>
    <div className="workspace">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark">STU</span><span className="brand-copy">Sistema Territorial<br />das UBS</span></div>
        <nav aria-label="Navegação principal">
          {navigation.filter(item => item.id !== 'families' || canViewFamilies).map(item => <NavigationButton current={section} item={item} key={item.id} select={setSection} />)}
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
      <main className={`dashboard${section === 'overview' ? ' dashboard--overview' : ''}${section === 'map' ? ' dashboard--map' : ''}`} id="main-content">
        <header className={`site-header${activeModule ? ' site-header--module' : ''}`}>
          {activeModule ? <div className="module-page-context"><span><StuIcon name={activeModule.icon} size={22} /></span><div><h1 ref={pageHeading} tabIndex={-1}>{activeModule.title}</h1><p>{activeModule.description}</p></div></div> : <div className="unit-context"><span><strong>{summary?.healthUnitName ?? session.healthUnit?.name ?? 'Administração global'}</strong><StuIcon name="chevron" size={13} /></span><small>{session.healthUnit?.code ? `${session.healthUnit.code} · Município de Teixeira de Freitas` : 'Município de Teixeira de Freitas'}</small></div>}
          {!activeModule && <form aria-label="Busca geral" className="site-search" onSubmit={submitHeaderSearch}><button aria-label="Buscar famílias" type="submit"><StuIcon name="search" size={17} /></button><input aria-label="Buscar família por número ou responsável" onChange={event => setHeaderSearch(event.target.value)} placeholder="Buscar família por número ou responsável..." type="search" value={headerSearch} /></form>}
          <div className="site-header-tools"><div className="site-notifications"><Suspense fallback={null}><NotificationCenter /></Suspense></div><div className={`site-profile${profileMenuOpen ? ' site-profile--open' : ''}`} ref={profileMenu}><button aria-controls="site-profile-menu" aria-expanded={profileMenuOpen} aria-haspopup="menu" aria-label="Menu do usuário" className="site-profile-trigger" onClick={() => { setMobileMoreOpen(false); setProfileMenuOpen(value => !value) }} ref={profileMenuTrigger} type="button"><span className="avatar">{initials(session.displayName)}</span><span className="site-profile-copy"><strong>{session.displayName}</strong><small>{roleLabel}</small></span><StuIcon name="chevron" size={14} /></button>{profileMenuOpen && <div className="site-profile-menu" id="site-profile-menu" role="menu"><button onClick={() => { setProfileMenuOpen(false); void guard(() => { void logout() }) }} role="menuitem" type="button"><StuIcon name="logout" size={17} />Sair do STU</button></div>}</div></div>
        </header>
        <div className={`dashboard-content${section === 'overview' ? ' dashboard-content--overview' : ''}${section === 'map' ? ' dashboard-content--map' : ''}`}>
          {!online && <div className="connectivity-banner" role="status"><StuIcon name="warning" /><span><strong>Você está sem conexão.</strong> A consulta e o envio de informações estão temporariamente indisponíveis.</span></div>}
          {section === 'overview' && <header className="dashboard-header">
            <div><span className="breadcrumb">{section === 'overview' ? formatToday() : 'Operação territorial'}</span><h1 ref={pageHeading} tabIndex={-1}>{sectionTitle(section, firstName)}</h1><p>{summary?.healthUnitName ?? session.healthUnit?.name ?? 'Administração global'}</p></div>
            <div className="header-actions">{section === 'overview' && canManageFamilies && <button className="primary-action" disabled={!online} onClick={openCreate} title={online ? undefined : 'Conecte-se para cadastrar uma família'} type="button"><StuIcon name="plus" size={18} /> Cadastrar família</button>}</div>
          </header>}
          {error && <div className="dashboard-error" role="alert">{error}</div>}
          {section === 'overview' && <Overview openFamilies={canViewFamilies ? () => setSection('families') : undefined} canCreate={canManageFamilies} healthUnitId={session.healthUnit?.id} online={online} openCreate={openCreate} openCoverage={openCoverage} openMap={() => setSection('map')} summary={summary} />}
          {section === 'map' && <Suspense fallback={<WorkspaceSkeleton label="Carregando o mapa" />}><TerritoryWorkspace global={session.roles.some(role => role.name === 'GlobalAdministrator')} session={session} /></Suspense>}
          {section === 'families' && canViewFamilies && <Suspense fallback={<WorkspaceSkeleton label="Carregando famílias" />}><FamilyWorkspace session={session} searchRequest={familySearchRequest} onSearchHandled={nonce => setFamilySearchRequest(current => current?.nonce === nonce ? undefined : current)} createRequest={createRequest} onOpenProperty={id => { setPropertySelectionRequest({ value: id, nonce: ++requestNonce.current }); changeSection('coverage') }} /></Suspense>}
          {section === 'coverage' && <Suspense fallback={<WorkspaceSkeleton label="Carregando imóveis e cobertura" />}><PropertyWorkspace coverageRequest={propertyCoverageRequest} onOpenTerritory={() => setSection('map')} searchRequest={propertySearchRequest} onSearchHandled={nonce => setPropertySearchRequest(current => current?.nonce === nonce ? undefined : current)} selectionRequest={propertySelectionRequest} onSelectionHandled={nonce => setPropertySelectionRequest(current => current?.nonce === nonce ? undefined : current)} session={session} /></Suspense>}
          {section === 'operations' && <Suspense fallback={<WorkspaceSkeleton label="Carregando arquivos e dados" />}><OperationalWorkspace session={session} /></Suspense>}
          {section === 'users' && canManageUsers && <Suspense fallback={<WorkspaceSkeleton label="Carregando a equipe" />}><HealthUnitUsersWorkspace session={session} /></Suspense>}
          {section === 'onboarding' && canOnboard && <Suspense fallback={<WorkspaceSkeleton label="Verificando a pré-implantação" />}><OnboardingWorkspace actions={{ territory: () => setSection('map'), coverage: () => openCoverage(''), operations: () => setSection('operations'), users: () => setSection('users') }} session={session} /></Suspense>}
          {section === 'pilot-release' && canOnboard && <Suspense fallback={<WorkspaceSkeleton label="Conferindo a liberação" />}><PilotReleaseWorkspace actions={{ onboarding: () => setSection('onboarding'), operations: () => setSection('operations') }} session={session} /></Suspense>}
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

function Overview({ openFamilies, canCreate, healthUnitId, online, openCreate, openCoverage, openMap, summary }: { openFamilies?: () => void; canCreate: boolean; healthUnitId?: string; online: boolean; openCreate: () => void; openCoverage: (filter?: string) => void; openMap: () => void; summary: DashboardSummary | null }) {
  const coverageTotal = summary ? Object.values(summary.coverage ?? {}).reduce((total, value) => total + value, 0) : 0
  const alertPercent = percentOf(summary?.coverageAlerts ?? 0, coverageTotal)
  const unassignedPercent = percentOf(summary?.unassignedMicroregions ?? 0, summary?.microregions?.length ?? 0)
  return <div className="overview">
    <section className="metrics" aria-label="Indicadores da UBS">
      <Metric detail={openFamilies ? `${summary?.familiesWithoutProperty ?? 0} sem imóvel · ${summary?.activeProperties ?? 0} imóveis ativos` : `${summary?.activeProperties ?? 0} endereços cadastrados`} icon="building" label={openFamilies ? 'Famílias cadastradas' : 'Imóveis ativos'} onClick={openFamilies ?? (() => openCoverage(''))} tone="green" value={openFamilies ? summary?.activeFamilyIdentifiers : summary?.activeProperties} />
      <Metric detail={trendLabel(summary?.visitsChangePercent)} icon="calendar" label="Visitas neste mês" tone="blue" value={summary?.visitsThisMonth} />
      <Metric detail={`${summary?.coverageAlerts ?? 0} de ${coverageTotal} famílias · ${alertPercent}%`} icon="warning" label="Alertas de cobertura" onClick={() => openCoverage('pending')} tone="coral" value={summary?.coverageAlerts} />
      <Metric detail={`${summary?.unassignedMicroregions ?? 0} de ${summary?.microregions?.length ?? 0} áreas · ${unassignedPercent}%`} icon="users" label="Microrregiões sem agente" onClick={openMap} tone="violet" value={summary?.unassignedMicroregions} />
    </section>
    <section className="dashboard-grid">
      <article className="territory-card">
        <div className="card-heading"><div><span className="section-kicker">Território</span><h2>Mapa da área atendida</h2><p>Limites e cobertura atualizados com os dados da UBS.</p></div><button onClick={openMap} type="button">Abrir mapa <StuIcon name="arrow" size={16} /></button></div>
        <Suspense fallback={<div className="overview-map-shell overview-map-loading" role="status">Preparando a prévia territorial…</div>}><TerritoryOverviewMap healthUnitId={healthUnitId} onOpenMap={openMap} /></Suspense>
      </article>
      <aside className="overview-rail">
        <article className="coverage-card">
          <div className="card-heading"><div><span className="section-kicker">Cobertura</span><h2>Situação dos imóveis</h2></div><button onClick={() => openCoverage('')} type="button">Ver os {coverageTotal} imóveis</button></div>
          {coverageTotal > 0 ? <>
            <div aria-label={`Cobertura de ${coverageTotal} famílias`} className="coverage-bar"><i className="coverage-bar__covered" style={{ flexGrow: summary?.coverage?.covered ?? 0 }} /><i className="coverage-bar__attention" style={{ flexGrow: summary?.coverage?.neverVisited ?? 0 }} /><i className="coverage-bar__alert" style={{ flexGrow: summary?.coverage?.overdue ?? 0 }} /><i className="coverage-bar__neutral" style={{ flexGrow: summary?.coverage?.notConfigured ?? 0 }} /></div>
            <dl className="coverage-list"><CoverageRow color="green" label="Em dia" onClick={() => openCoverage('covered')} total={coverageTotal} value={summary?.coverage?.covered} /><CoverageRow color="amber" label="Nunca visitado" onClick={() => openCoverage('neverVisited')} total={coverageTotal} value={summary?.coverage?.neverVisited} /><CoverageRow color="coral" label="Fora do prazo" onClick={() => openCoverage('overdue')} total={coverageTotal} value={summary?.coverage?.overdue} /><CoverageRow color="violet" label="Sem regra" onClick={() => openCoverage('notConfigured')} total={coverageTotal} value={summary?.coverage?.notConfigured} /></dl>
          </> : <div className="card-empty"><strong>A cobertura aparecerá aqui</strong><span>Vincule famílias aos imóveis e defina os prazos das microrregiões.</span></div>}
        </article>
        <article className="quick-card">
          <div><span className="section-kicker">Atalhos</span><h2>Ações rápidas</h2></div>
          <div className="quick-actions">{canCreate && <button disabled={!online} onClick={openCreate} type="button"><span className="quick-icon quick-icon--green"><StuIcon name="plus" /></span><span><strong>Cadastrar família</strong><small>Adicionar número e responsável</small></span></button>}<button onClick={() => openCoverage('pending')} type="button"><span className="quick-icon quick-icon--amber"><StuIcon name="calendar" /></span><span><strong>Registrar visita</strong><small>Escolher entre {summary?.coverageAlerts ?? 0} famílias que pedem atenção</small></span></button><button onClick={openMap} type="button"><span className="quick-icon quick-icon--blue"><StuIcon name="map" /></span><span><strong>Explorar território</strong><small>Consultar áreas e agentes</small></span></button></div>
          <p className="stage-note">{summary?.stage ?? 'Carregando situação da implantação…'} · {summary?.propertiesWithoutFamily ?? 0} imóveis sem família · {summary?.familiesWithoutProperty ?? 0} famílias sem imóvel</p>
        </article>
      </aside>
    </section>
  </div>
}

function Metric({ detail, icon, label, onClick, tone, value }: { detail: string; icon: Parameters<typeof StuIcon>[0]['name']; label: string; onClick?: () => void; tone: string; value: number | undefined }) {
  const content = <><span className="metric-icon"><StuIcon name={icon} /></span><div><small>{label}</small><strong>{value ?? '—'}</strong><p>{detail}</p></div></>
  return onClick
    ? <button aria-label={`${label}: ${value ?? 0}. ${detail}`} className={`metric metric--${tone} metric--action`} onClick={onClick} type="button">{content}</button>
    : <article className={`metric metric--${tone}`}>{content}</article>
}

function CoverageRow({ color, label, onClick, total, value }: { color: string; label: string; onClick: () => void; total: number; value?: number }) {
  const count = value ?? 0
  return <div><dt><i className={`coverage-key coverage-key--${color}`} />{label}</dt><dd><button aria-label={`${label}: ${count} de ${total} famílias`} onClick={onClick} type="button"><strong>{count}</strong><span>de {total} · {percentOf(count, total)}%</span></button></dd></div>
}

function percentOf(value: number, total: number) {
  return total > 0 ? Math.round((value / total) * 100) : 0
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
function sectionTitle(section: Section, firstName: string) { return section === 'map' ? 'Território da UBS' : section === 'families' ? 'Famílias' : section === 'coverage' ? 'Cobertura' : section === 'operations' ? 'Arquivos e dados' : section === 'users' ? 'Equipe da UBS' : section === 'onboarding' ? 'Preparação do piloto' : section === 'pilot-release' ? 'Decisão do piloto' : `Bom trabalho, ${firstName}.` }
function moduleContext(section: Section): { title: string; description: string; icon: NavItem['icon'] } | null {
  if (section === 'map') return { title: 'Mapa territorial', description: 'Visualize as microrregiões, a cobertura e os limites do território da UBS.', icon: 'map' }
  if (section === 'families') return { title: 'Famílias', description: 'Cadastre e acompanhe famílias, seus imóveis e visitas.', icon: 'users' }
  if (section === 'coverage') return { title: 'Cobertura', description: 'Consulte os imóveis, as visitas e a situação de cobertura da UBS.', icon: 'coverage' }
  if (section === 'operations') return { title: 'Arquivos e dados', description: 'Importe, exporte e acompanhe os dados operacionais da UBS.', icon: 'database' }
  if (section === 'users') return { title: 'Equipe da UBS', description: 'Gerencie os servidores, as funções e os acessos vinculados à UBS.', icon: 'users' }
  if (section === 'onboarding') return { title: 'Pré-implantação', description: 'Acompanhe os requisitos necessários para iniciar a operação da UBS.', icon: 'check' }
  if (section === 'pilot-release') return { title: 'Liberação do piloto', description: 'Confira as condições e registre a decisão de liberação do piloto.', icon: 'flag' }
  return null
}
