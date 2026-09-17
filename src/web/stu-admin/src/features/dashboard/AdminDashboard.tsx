import { lazy, Suspense, useEffect, useRef, useState } from 'react'
import { InteractionProvider, useUiActions, type AuthenticatedContext } from '@stu/shared'
import { AuditManager, BackupsManager, HealthUnitsManager, RolesManager, UsersManager } from '../administration/ManagementSections'
import { MonitoringSection } from '../administration/MonitoringSection'

const TerritoryWorkspace = lazy(async () => {
  const module = await import('@stu/shared/territory')
  return { default: module.TerritoryWorkspace }
})

const PropertyWorkspace = lazy(async () => {
  const module = await import('@stu/shared/properties')
  return { default: module.PropertyWorkspace }
})

const OperationalWorkspace = lazy(async () => {
  const module = await import('@stu/shared/operations')
  return { default: module.OperationalWorkspace }
})

const NotificationCenter = lazy(async () => {
  const module = await import('@stu/shared/notifications')
  return { default: module.NotificationCenter }
})

const OnboardingWorkspace = lazy(async () => {
  const module = await import('@stu/shared/onboarding')
  return { default: module.OnboardingWorkspace }
})

const PilotReleaseWorkspace = lazy(async () => {
  const module = await import('@stu/shared/pilot-release')
  return { default: module.PilotReleaseWorkspace }
})

type AdminOverview = { activeHealthUnits: number; activeUsers: number; pendingPasswordChanges: number; activeRoles: number }
type Section = 'overview' | 'health-units' | 'territories' | 'coverage' | 'workflows' | 'onboarding' | 'pilot-release' | 'users' | 'roles' | 'backups' | 'monitoring' | 'audit'

const navigation: { id: Section; icon: string; label: string }[] = [
  { id: 'overview', icon: '⌂', label: 'Visão geral' },
  { id: 'health-units', icon: '▣', label: 'UBS e territórios' },
  { id: 'territories', icon: '◇', label: 'Mapa territorial' },
  { id: 'coverage', icon: '◫', label: 'Cobertura' },
  { id: 'workflows', icon: '⇄', label: 'Arquivos e dados' },
  { id: 'onboarding', icon: '✓', label: 'Pré-implantação' },
  { id: 'pilot-release', icon: '◆', label: 'Liberação do piloto' },
  { id: 'users', icon: '◎', label: 'Usuários' },
  { id: 'roles', icon: '◇', label: 'Funções e permissões' },
  { id: 'backups', icon: '↻', label: 'Backups' },
  { id: 'monitoring', icon: '◉', label: 'Monitoramento' },
  { id: 'audit', icon: '≡', label: 'Auditoria' },
]

const areas: { section: Section; icon: string; title: string; description: string; action: string }[] = [
  { section: 'health-units', icon: '▣', title: 'UBS e territórios', description: 'Cadastre unidades e acompanhe a distribuição das áreas.', action: 'Gerenciar UBS' },
  { section: 'territories', icon: '◇', title: 'Mapa territorial', description: 'Importe bairros, desenhe microrregiões e atribua agentes.', action: 'Gerenciar territórios' },
  { section: 'coverage', icon: '◫', title: 'Cobertura', description: 'Consulte imóveis, visitas, pendências e indicadores de qualquer UBS.', action: 'Abrir cobertura' },
  { section: 'users', icon: '◎', title: 'Usuários', description: 'Crie contas, redefina senhas e transfira servidores.', action: 'Gerenciar usuários' },
  { section: 'roles', icon: '◇', title: 'Funções e permissões', description: 'Crie novas funções e determine cada permissão.', action: 'Configurar acessos' },
  { section: 'backups', icon: '↻', title: 'Backups', description: 'Execute cópias manuais e ajuste a rotina semanal.', action: 'Gerenciar backups' },
  { section: 'onboarding', icon: '✓', title: 'Pré-implantação', description: 'Valide território, contas, importações e backup antes de autorizar uma UBS.', action: 'Verificar prontidão' },
  { section: 'pilot-release', icon: '◆', title: 'Liberação do piloto', description: 'Consolide evidências, responsáveis e a decisão final de início operacional.', action: 'Avaliar liberação' },
  { section: 'monitoring', icon: '◉', title: 'Monitoramento', description: 'Acompanhe banco, worker, armazenamento e alertas operacionais.', action: 'Ver saúde do sistema' },
  { section: 'audit', icon: '≡', title: 'Auditoria', description: 'Consulte acréscimos e todas as modificações registradas.', action: 'Consultar registros' },
  { section: 'health-units', icon: '⇄', title: 'Importações', description: 'Importe limites e referências do OpenStreetMap.', action: 'Após cadastrar UBS' },
]

export function AdminDashboard(props: AuthenticatedContext) {
  return <InteractionProvider key={`${props.session.id}:${props.session.healthUnit?.id ?? 'global'}`}><AdminDashboardContent {...props} /></InteractionProvider>
}

function AdminDashboardContent({ session, logout }: AuthenticatedContext) {
  const { guard } = useUiActions()
  const [section, changeSection] = useState<Section>('overview')
  function setSection(next: Section) { if (next !== section) void guard(() => changeSection(next)) }
  const [overview, setOverview] = useState<AdminOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const pageHeading = useRef<HTMLHeadingElement>(null)
  const previousSection = useRef<Section>('overview')

  useEffect(() => {
    let active = true
    fetch('/api/admin/overview', { credentials: 'include' })
      .then(async (response) => { if (!response.ok) throw new Error('Não foi possível carregar os dados administrativos.'); return await response.json() as AdminOverview })
      .then((data) => { if (active) setOverview(data) })
      .catch((caught: unknown) => { if (active) setError(caught instanceof Error ? caught.message : 'Falha ao carregar o painel.') })
    return () => { active = false }
  }, [refreshKey])

  useEffect(() => {
    if (previousSection.current !== section) pageHeading.current?.focus()
    previousSection.current = section
  }, [section])

  function navigate(target: Section) { setSection(target) }

  const currentLabel = navigation.find((item) => item.id === section)?.label ?? 'Administração global'

  return (
    <><a className="skip-link" href="#admin-main-content">Ir para o conteúdo principal</a><div className="admin-workspace">
      <aside className="admin-sidebar">
        <div className="admin-brand"><span>STU</span><strong>Administração global</strong></div>
        <nav aria-label="Navegação administrativa">
          {navigation.map((item) => <button aria-current={item.id === section ? 'page' : undefined} className={item.id === section ? 'admin-nav-active' : ''} key={item.id} onClick={() => navigate(item.id)} type="button"><span aria-hidden="true">{item.icon}</span>{item.label}</button>)}
        </nav>
        <div className="admin-account"><span>{session.displayName.slice(0, 1).toUpperCase()}</span><div><strong>{session.displayName}</strong><small>Administrador global</small></div><button aria-label="Sair do painel" onClick={() => void guard(() => { void logout() })} type="button">↗</button></div>
      </aside>

      <main className="admin-main" id="admin-main-content">
        <header className="admin-header"><div><span>Painel isolado</span><h1 ref={pageHeading} tabIndex={-1}>{currentLabel}</h1><p>Controle protegido da plataforma STU</p></div><div className="admin-security"><Suspense fallback={null}><NotificationCenter /></Suspense><i aria-hidden="true" /> Ambiente protegido</div></header>
        {error && section === 'overview' && <div className="admin-error" role="alert">{error}</div>}
        {section === 'overview' && <Overview overview={overview} onNavigate={navigate} />}
        {section === 'health-units' && <HealthUnitsManager onChanged={() => setRefreshKey((value) => value + 1)} />}
        {section === 'territories' && <Suspense fallback={<div className="admin-error">Carregando o mapa…</div>}><TerritoryWorkspace global session={session} /></Suspense>}
        {section === 'coverage' && <Suspense fallback={<div className="admin-error">Carregando imóveis e cobertura…</div>}><PropertyWorkspace global onOpenTerritory={() => setSection('territories')} session={session} /></Suspense>}
        {section === 'workflows' && <Suspense fallback={<div className="admin-error">Carregando os fluxos operacionais…</div>}><OperationalWorkspace global session={session} /></Suspense>}
        {section === 'onboarding' && <Suspense fallback={<div className="admin-error">Verificando a pré-implantação…</div>}><OnboardingWorkspace global session={session} /></Suspense>}
        {section === 'pilot-release' && <Suspense fallback={<div className="admin-error">Conferindo a liberação…</div>}><PilotReleaseWorkspace global session={session} /></Suspense>}
        {section === 'users' && <UsersManager currentUserId={session.id} onChanged={() => setRefreshKey((value) => value + 1)} />}
        {section === 'roles' && <RolesManager onChanged={() => setRefreshKey((value) => value + 1)} />}
        {section === 'backups' && <BackupsManager />}
        {section === 'monitoring' && <MonitoringSection />}
        {section === 'audit' && <AuditManager />}
      </main>
    </div></>
  )
}

function Overview({ overview, onNavigate }: { overview: AdminOverview | null; onNavigate: (section: Section) => void }) {
  return <>
    <section className="admin-metrics" aria-label="Resumo da plataforma">
      <AdminMetric label="UBS ativas" value={overview?.activeHealthUnits} helper="unidades cadastradas" />
      <AdminMetric label="Usuários ativos" value={overview?.activeUsers} helper="contas operacionais" />
      <AdminMetric label="Funções ativas" value={overview?.activeRoles} helper="perfis de acesso" />
      <AdminMetric alert label="Trocas de senha" value={overview?.pendingPasswordChanges} helper="pendentes no primeiro acesso" />
    </section>
    <section className="admin-section-heading"><div><span>Controle integral</span><h2>Áreas administrativas</h2></div><p>As ações sensíveis são registradas automaticamente na auditoria.</p></section>
    <section className="admin-area-grid" aria-label="Áreas administrativas">{areas.map((area) => <article key={area.title}><span className="area-icon" aria-hidden="true">{area.icon}</span><h3>{area.title}</h3><p>{area.description}</p><button onClick={() => onNavigate(area.section)} type="button">{area.action} <span>→</span></button></article>)}</section>
  </>
}

function AdminMetric({ label, value, helper, alert = false }: { label: string; value: number | undefined; helper: string; alert?: boolean }) {
  return <article className={alert ? 'admin-metric admin-metric--alert' : 'admin-metric'}><small>{label}</small><strong>{value ?? '—'}</strong><p>{helper}</p></article>
}
