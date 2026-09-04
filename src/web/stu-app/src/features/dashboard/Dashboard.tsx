import { lazy, Suspense, useEffect, useRef, useState } from 'react'
import type { AuthenticatedContext } from '@stu/shared'

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

const HealthUnitUsersWorkspace = lazy(async () => {
  const module = await import('@stu/shared/health-unit-users')
  return { default: module.HealthUnitUsersWorkspace }
})

type DashboardSummary = { healthUnitName: string | null; activeProperties: number; visitsThisMonth: number; coverageAlerts: number; unassignedMicroregions: number; stage: string }
type Section = 'overview' | 'map' | 'properties' | 'visits' | 'operations' | 'users' | 'onboarding' | 'pilot-release'

const navigation: { id: Section; label: string; icon: string }[] = [
  { id: 'overview', label: 'Visão geral', icon: '⌂' }, { id: 'map', label: 'Mapa territorial', icon: '◇' },
  { id: 'properties', label: 'Imóveis', icon: '▦' }, { id: 'visits', label: 'Visitas', icon: '✓' },
  { id: 'operations', label: 'Arquivos e dados', icon: '⇄' },
]

export function Dashboard({ session, logout }: AuthenticatedContext) {
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [section, setSection] = useState<Section>('overview')
  const pageHeading = useRef<HTMLHeadingElement>(null)
  const previousSection = useRef<Section>('overview')

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

  return <><a className="skip-link" href="#main-content">Ir para o conteúdo principal</a><div className="workspace">
    <aside className="sidebar">
      <div className="brand"><span className="brand-mark">STU</span><span className="brand-copy">Sistema Territorial<br />das UBS</span></div>
      <nav aria-label="Navegação principal"><span className="nav-caption">Operação</span>{navigation.map(item => <button aria-current={item.id === section ? 'page' : undefined} className={item.id === section ? 'nav-item nav-item--active' : 'nav-item'} key={item.id} onClick={() => setSection(item.id)} type="button"><span aria-hidden="true">{item.icon}</span>{item.label}</button>)}<span className="nav-caption nav-caption--second">Gestão</span><button className="nav-item" onClick={() => setSection('properties')} type="button"><span aria-hidden="true">◫</span>Cobertura</button>{canManageUsers && <button aria-current={section === 'users' ? 'page' : undefined} className={section === 'users' ? 'nav-item nav-item--active' : 'nav-item'} onClick={() => setSection('users')} type="button"><span aria-hidden="true">◎</span>Servidores</button>}{canOnboard && <><button aria-current={section === 'onboarding' ? 'page' : undefined} className={section === 'onboarding' ? 'nav-item nav-item--active' : 'nav-item'} onClick={() => setSection('onboarding')} type="button"><span aria-hidden="true">✓</span>Pré-implantação</button><button aria-current={section === 'pilot-release' ? 'page' : undefined} className={section === 'pilot-release' ? 'nav-item nav-item--active' : 'nav-item'} onClick={() => setSection('pilot-release')} type="button"><span aria-hidden="true">◆</span>Liberação do piloto</button></>}<button className="nav-item" disabled type="button"><span aria-hidden="true">⚙</span>Configurações</button></nav>
      <div className="sidebar-user"><span className="avatar">{session.displayName.slice(0, 1).toUpperCase()}</span><span><strong>{session.displayName}</strong><small>{roleLabel}</small></span><button aria-label="Sair do STU" onClick={() => void logout()} title="Sair" type="button">↗</button></div>
    </aside>
    <main className="dashboard" id="main-content">
      <header className="dashboard-header"><div><span className="breadcrumb">{section === 'overview' ? 'Visão geral' : 'Operação territorial'}</span><h1 ref={pageHeading} tabIndex={-1}>{sectionTitle(section, firstName)}</h1><p>{summary?.healthUnitName ?? session.healthUnit?.name ?? 'Administração global'}</p></div><div className="header-actions"><Suspense fallback={null}><NotificationCenter /></Suspense>{section === 'overview' && <button className="primary-action" onClick={() => setSection('properties')} type="button"><span aria-hidden="true">＋</span> Cadastrar imóvel</button>}</div></header>
      {error && <div className="dashboard-error" role="alert">{error}</div>}
      {section === 'overview' && <Overview summary={summary} openMap={() => setSection('map')} />}
      {section === 'map' && <Suspense fallback={<div className="dashboard-error">Carregando o mapa…</div>}><TerritoryWorkspace global={session.roles.some(role => role.name === 'GlobalAdministrator')} session={session} /></Suspense>}
      {(section === 'properties' || section === 'visits') && <Suspense fallback={<div className="dashboard-error">Carregando os cadastros…</div>}><PropertyWorkspace initialView={section} onOpenTerritory={() => setSection('map')} session={session} /></Suspense>}
      {section === 'operations' && <Suspense fallback={<div className="dashboard-error">Carregando os fluxos operacionais…</div>}><OperationalWorkspace session={session} /></Suspense>}
      {section === 'users' && canManageUsers && <Suspense fallback={<div className="dashboard-error">Carregando os servidores…</div>}><HealthUnitUsersWorkspace session={session} /></Suspense>}
      {section === 'onboarding' && canOnboard && <Suspense fallback={<div className="dashboard-error">Verificando a pré-implantação…</div>}><OnboardingWorkspace session={session} /></Suspense>}
      {section === 'pilot-release' && canOnboard && <Suspense fallback={<div className="dashboard-error">Conferindo a liberação…</div>}><PilotReleaseWorkspace session={session} /></Suspense>}
    </main>
  </div></>
}

function Overview({ summary, openMap }: { summary: DashboardSummary | null; openMap: () => void }) {
  return <><section className="metrics" aria-label="Indicadores da UBS"><Metric label="Imóveis ativos" value={summary?.activeProperties} detail="na área atendida" tone="green" /><Metric label="Visitas neste mês" value={summary?.visitsThisMonth} detail="registros realizados" tone="blue" /><Metric label="Alertas de cobertura" value={summary?.coverageAlerts} detail="fora do prazo" tone="amber" /><Metric label="Microrregiões sem agente" value={summary?.unassignedMicroregions} detail="requerem atribuição" tone="violet" /></section><section className="dashboard-grid"><article className="territory-card"><div className="card-heading"><div><span className="section-kicker">Território</span><h2>Mapa da área atendida</h2></div><button onClick={openMap} type="button">Abrir mapa <span>→</span></button></div><div className="map-preview"><div className="map-roads" /><div className="region region-a"><span>MR 01</span></div><div className="region region-b"><span>MR 02</span></div><div className="region region-c"><span>MR 03</span></div><div className="map-note"><strong>Mapa territorial disponível</strong><span>Cadastre bairros, desenhe limites e atribua agentes.</span></div></div></article><aside className="activity-card"><div className="card-heading"><div><span className="section-kicker">Agora</span><h2>Primeiros passos</h2></div></div><ol className="steps"><li className="step-ready"><span>1</span><div><strong>Acesso seguro configurado</strong><small>Contas, funções e permissões ativas</small></div></li><li className="step-ready"><span>2</span><div><strong>Organização preparada</strong><small>UBS e servidores disponíveis</small></div></li><li><span>3</span><div><strong>Importar territórios</strong><small>Adicione bairros e microrregiões</small></div></li><li><span>4</span><div><strong>Vincular agentes</strong><small>Distribua as áreas de responsabilidade</small></div></li></ol><div className="stage-note">{summary?.stage ?? 'Carregando situação da implantação…'}</div></aside></section></>
}

function Metric({ label, value, detail, tone }: { label: string; value: number | undefined; detail: string; tone: string }) { return <article className={`metric metric--${tone}`}><span className="metric-icon" /><div><small>{label}</small><strong>{value ?? '—'}</strong><p>{detail}</p></div></article> }

function sectionTitle(section: Section, firstName: string) { return section === 'map' ? 'Território da UBS' : section === 'properties' ? 'Imóveis atendidos' : section === 'visits' ? 'Visitas operacionais' : section === 'operations' ? 'Arquivos e dados' : section === 'users' ? 'Equipe da UBS' : section === 'onboarding' ? 'Preparação do piloto' : section === 'pilot-release' ? 'Decisão do piloto' : `Bom trabalho, ${firstName}.` }
