import { useCallback, useEffect, useId, useRef, useState, type FormEvent } from 'react'
import type { Session } from '../auth/types'
import { useAccessibleDialog } from '../accessibility/useAccessibleDialog'
import { CloseIcon } from '../components/CloseIcon'
import './health-unit-users.css'

type Role = { id: string; name: string; displayName: string; description: string | null; isSystem: boolean }
type User = { id: string; userName: string; displayName: string; role: Role | null; mustChangePassword: boolean; lockoutEnd: string | null; archivedAtUtc: string | null; manageable: boolean }
type Reference = { healthUnit: { id: string; code: string; name: string }; roles: Role[] }
type Credentials = { userName: string; temporaryPassword: string }

export function HealthUnitUsersWorkspace({ session }: { session: Session }) {
  const [users, setUsers] = useState<User[]>([])
  const [reference, setReference] = useState<Reference | null>(null)
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState('all')
  const [roleFilter, setRoleFilter] = useState('all')
  const [openActionsId, setOpenActionsId] = useState<string | null>(null)
  const [editing, setEditing] = useState<User | 'new' | null>(null)
  const [credentials, setCredentials] = useState<Credentials | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const actionMenu = useRef<HTMLDivElement>(null)

  const load = useCallback(async (search = '') => {
    setLoading(true)
    try {
      const [usersResponse, referenceResponse] = await Promise.all([
        fetch(`/api/health-unit-users?includeArchived=true&pageSize=100&query=${encodeURIComponent(search)}`, { credentials: 'include' }),
        fetch('/api/health-unit-users/reference-data', { credentials: 'include' }),
      ])
      if (!usersResponse.ok || !referenceResponse.ok) throw new Error('Não foi possível carregar os servidores da UBS.')
      setUsers((await usersResponse.json() as { items: User[] }).items)
      setReference(await referenceResponse.json() as Reference)
      setError(null)
    } catch (caught) { setError(messageOf(caught)) }
    finally { setLoading(false) }
  }, [])

  useEffect(() => { void load('') }, []) // oxlint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!openActionsId) return
    function dismiss(event: PointerEvent) { if (!actionMenu.current?.contains(event.target as Node)) setOpenActionsId(null) }
    function dismissWithKeyboard(event: KeyboardEvent) { if (event.key === 'Escape') setOpenActionsId(null) }
    document.addEventListener('pointerdown', dismiss)
    document.addEventListener('keydown', dismissWithKeyboard)
    return () => { document.removeEventListener('pointerdown', dismiss); document.removeEventListener('keydown', dismissWithKeyboard) }
  }, [openActionsId])

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const creating = editing === 'new'
    const fields = new FormData(event.currentTarget)
    const body = { userName: creating ? String(fields.get('userName')) : null, displayName: String(fields.get('displayName')), roleName: String(fields.get('roleName')) }
    try {
      setLoading(true); setError(null); setNotice(null)
      const result = await mutate<{ userName: string; temporaryPassword: string }>(creating ? '/api/health-unit-users' : `/api/health-unit-users/${(editing as User).id}`, creating ? 'POST' : 'PUT', body)
      if (creating) setCredentials({ userName: result.userName, temporaryPassword: result.temporaryPassword })
      setEditing(null)
      setNotice(creating ? 'Servidor cadastrado. Entregue a senha temporária de forma segura.' : 'Cadastro do servidor atualizado.')
      await load('')
    } catch (caught) { setError(messageOf(caught)) }
    finally { setLoading(false) }
  }

  async function resetPassword(user: User) {
    if (!window.confirm(`Gerar uma nova senha temporária para ${user.displayName}?`)) return
    try {
      const result = await mutate<{ temporaryPassword: string }>(`/api/health-unit-users/${user.id}/reset-password`, 'POST')
      setCredentials({ userName: user.userName, temporaryPassword: result.temporaryPassword }); setNotice('Senha temporária redefinida.'); await load()
    } catch (caught) { setError(messageOf(caught)) }
  }

  async function toggleArchive(user: User) {
    const action = user.archivedAtUtc ? 'reativar' : 'arquivar'
    if (!window.confirm(`Deseja ${action} a conta de ${user.displayName}?`)) return
    try {
      await mutate(`/api/health-unit-users/${user.id}/${user.archivedAtUtc ? 'restore' : 'archive'}`, 'POST')
      setNotice(user.archivedAtUtc ? 'Conta reativada.' : 'Conta arquivada com o histórico preservado.'); await load()
    } catch (caught) { setError(messageOf(caught)) }
  }

  const activeCount = users.filter(user => !user.archivedAtUtc).length
  const pendingCount = users.filter(user => !user.archivedAtUtc && user.mustChangePassword).length
  const normalizedQuery = query.trim().toLocaleLowerCase('pt-BR')
  const filteredUsers = users.filter(user => {
    const matchesQuery = !normalizedQuery || `${user.displayName} ${user.userName}`.toLocaleLowerCase('pt-BR').includes(normalizedQuery)
    const matchesStatus = statusFilter === 'all' || statusOf(user) === statusFilter
    const matchesRole = roleFilter === 'all' || user.role?.name === roleFilter
    return matchesQuery && matchesStatus && matchesRole
  })
  const hasActiveFilters = Boolean(normalizedQuery) || statusFilter !== 'all' || roleFilter !== 'all'
  function clearFilters() { setQuery(''); setStatusFilter('all'); setRoleFilter('all') }
  return <section className="unit-users-workspace">
    <header className="unit-users-heading"><div><h2>Servidores cadastrados</h2><p>Contas individuais vinculadas exclusivamente à {reference?.healthUnit.name ?? session.healthUnit?.name ?? 'sua UBS'}.</p></div><button onClick={() => setEditing('new')} type="button">＋ Adicionar servidor</button></header>
    {error && <div className="unit-users-message unit-users-error" role="alert">{error}<button aria-label="Dispensar erro" className="stu-close-button" onClick={() => setError(null)} type="button"><CloseIcon /></button></div>}
    {notice && <div className="unit-users-message" role="status">{notice}<button aria-label="Dispensar aviso" className="stu-close-button" onClick={() => setNotice(null)} type="button"><CloseIcon /></button></div>}
    <section className="unit-users-metrics" aria-label="Resumo da equipe">
      <TeamMetric icon="users" label="Contas ativas" helper={pluralize(activeCount, 'servidor com acesso', 'servidores com acesso')} value={activeCount} />
      <TeamMetric icon="key" label="Primeiro acesso pendente" helper={pendingCount ? 'Precisam trocar a senha temporária' : 'Nenhuma troca de senha pendente'} tone="amber" value={pendingCount} />
      <TeamMetric icon="roles" label="Funções disponíveis" helper="Perfis que podem ser atribuídos" tone="blue" value={reference?.roles.length ?? 0} />
    </section>
    <section className="unit-users-filter-panel" aria-labelledby="unit-users-filter-title">
      <div className="unit-users-filter-heading"><div><h3 id="unit-users-filter-title">Buscar e filtrar equipe</h3><p>Localize uma conta e refine a lista por situação ou função.</p></div>{hasActiveFilters && <button onClick={clearFilters} type="button">Limpar filtros</button>}</div>
      <div className="unit-users-filters">
        <label className="unit-users-search">Buscar servidor<span><SearchIcon /><input onChange={event => setQuery(event.target.value)} placeholder="Nome ou usuário" type="search" value={query} /></span></label>
        <label>Situação<select onChange={event => setStatusFilter(event.target.value)} value={statusFilter}><option value="all">Todas as situações</option><option value="active">Ativos</option><option value="pending">Primeiro acesso pendente</option><option value="locked">Bloqueados</option><option value="archived">Arquivados</option></select></label>
        <label>Função<select onChange={event => setRoleFilter(event.target.value)} value={roleFilter}><option value="all">Todas as funções</option>{reference?.roles.map(role => <option key={role.id} value={role.name}>{role.displayName}</option>)}</select></label>
      </div>
      <p className="unit-users-result-count" aria-live="polite"><strong>{filteredUsers.length}</strong> {pluralize(filteredUsers.length, 'servidor encontrado', 'servidores encontrados')}</p>
    </section>
    <div className="unit-users-table-wrap"><table className="unit-users-table"><thead><tr><th>Servidor</th><th>Função</th><th>Situação</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{filteredUsers.map(user => <tr className={user.archivedAtUtc ? 'archived' : ''} key={user.id}><td data-label="Servidor"><div className="unit-user-identity"><span aria-hidden="true" className="unit-user-avatar">{initials(user.displayName)}</span><span><strong>{user.displayName}</strong><small>@{user.userName}</small></span></div></td><td data-label="Função">{user.role?.displayName ?? 'Sem função'}</td><td data-label="Situação"><Status user={user} /></td><td data-label="Ações">{user.manageable ? <div className="unit-users-actions"><button disabled={Boolean(user.archivedAtUtc)} onClick={() => setEditing(user)} type="button">Editar</button><button disabled={Boolean(user.archivedAtUtc)} onClick={() => void resetPassword(user)} type="button">Redefinir senha</button><div className="unit-users-overflow" ref={openActionsId === user.id ? actionMenu : undefined}><button aria-expanded={openActionsId === user.id} aria-haspopup="menu" aria-label={`Mais ações para ${user.displayName}`} onClick={() => setOpenActionsId(current => current === user.id ? null : user.id)} type="button">•••</button>{openActionsId === user.id && <div className="unit-users-action-menu" role="menu"><button onClick={() => { setOpenActionsId(null); void toggleArchive(user) }} role="menuitem" type="button">{user.archivedAtUtc ? 'Reativar conta' : 'Arquivar conta'}</button></div>}</div></div> : <small className="unit-users-protected">Conta protegida</small>}</td></tr>)}</tbody></table>{filteredUsers.length === 0 && !loading && <div className="unit-users-empty"><strong>Nenhum servidor encontrado</strong><p>{hasActiveFilters ? 'Tente ajustar ou limpar os filtros aplicados.' : 'Use “Adicionar servidor” para criar a primeira conta da equipe.'}</p>{hasActiveFilters && <button onClick={clearFilters} type="button">Limpar filtros</button>}</div>}{loading && <p className="unit-users-loading" role="status">Atualizando servidores…</p>}</div>
    {editing && reference && <UserEditor item={editing} roles={reference.roles} busy={loading} onClose={() => setEditing(null)} onSubmit={save} />}
    {credentials && <CredentialsDialog credentials={credentials} onClose={() => setCredentials(null)} />}
  </section>
}

function TeamMetric({ icon, label, helper, tone = 'green', value }: { icon: 'users' | 'key' | 'roles'; label: string; helper: string; tone?: string; value: number }) {
  return <article className={`unit-users-metric unit-users-metric--${tone}`}><span className="unit-users-metric-icon"><TeamMetricIcon name={icon} /></span><span><small>{label}</small><strong>{value}</strong><p>{helper}</p></span></article>
}

function TeamMetricIcon({ name }: { name: 'users' | 'key' | 'roles' }) {
  const paths = {
    users: <><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M22 21v-2a4 4 0 0 0-3-3.9M16 3.1a4 4 0 0 1 0 7.8" /></>,
    key: <><circle cx="8" cy="15" r="4" /><path d="m11 12 8-8m-2 2 2 2m-5 1 2 2" /></>,
    roles: <><rect height="16" rx="2" width="18" x="3" y="4" /><circle cx="9" cy="10" r="2" /><path d="M6 16c.6-1.5 1.6-2 3-2s2.4.5 3 2m3-6h3m-3 4h3" /></>,
  } as const
  return <svg aria-hidden="true" fill="none" height="22" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8" viewBox="0 0 24 24" width="22">{paths[name]}</svg>
}

function SearchIcon() { return <svg aria-hidden="true" fill="none" height="18" stroke="currentColor" strokeLinecap="round" strokeWidth="1.8" viewBox="0 0 24 24" width="18"><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></svg> }
function statusOf(user: User) { if (user.archivedAtUtc) return 'archived'; if (user.lockoutEnd && new Date(user.lockoutEnd) > new Date()) return 'locked'; if (user.mustChangePassword) return 'pending'; return 'active' }
function Status({ user }: { user: User }) { const status = statusOf(user); return <span className={`unit-user-status ${status}`}>{status === 'archived' ? 'Arquivado' : status === 'locked' ? 'Bloqueado' : status === 'pending' ? 'Senha temporária' : 'Ativo'}</span> }
function initials(value: string) { return value.trim().split(/\s+/).slice(0, 2).map(part => part[0]?.toUpperCase()).join('') }
function pluralize(value: number, singular: string, plural: string) { return value === 1 ? singular : plural }

function UserEditor({ item, roles, busy, onClose, onSubmit }: { item: User | 'new'; roles: Role[]; busy: boolean; onClose: () => void; onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void> }) {
  const titleId = useId(); const dialogRef = useAccessibleDialog<HTMLFormElement>(true, onClose)
  return <div className="unit-users-backdrop" onMouseDown={event => { if (event.currentTarget === event.target) onClose() }} role="presentation"><form aria-labelledby={titleId} aria-modal="true" className="unit-users-dialog" onSubmit={event => void onSubmit(event)} ref={dialogRef} role="dialog" tabIndex={-1}><header><div><span>{item === 'new' ? 'Nova conta individual' : 'Atualizar servidor'}</span><h3 id={titleId}>{item === 'new' ? 'Adicionar servidor' : item.displayName}</h3></div><button aria-label="Fechar cadastro de servidor" className="stu-close-button" onClick={onClose} type="button"><CloseIcon /></button></header>{item === 'new' && <label>Usuário de acesso<input autoComplete="off" maxLength={120} name="userName" placeholder="nome.sobrenome" required /></label>}<label>Nome completo<input defaultValue={item === 'new' ? '' : item.displayName} maxLength={160} name="displayName" required /></label><label>Função<select defaultValue={item === 'new' ? '' : item.role?.name ?? ''} name="roleName" required><option value="">Selecione</option>{roles.map(role => <option key={role.id} value={role.name}>{role.displayName}</option>)}</select></label>{item === 'new' && <p>Uma senha temporária segura será exibida apenas uma vez. O servidor deverá trocá-la no primeiro acesso.</p>}<footer><button onClick={onClose} type="button">Cancelar</button><button className="primary" disabled={busy} type="submit">{busy ? 'Salvando…' : 'Salvar servidor'}</button></footer></form></div>
}

function CredentialsDialog({ credentials, onClose }: { credentials: Credentials; onClose: () => void }) {
  const titleId = useId(); const dialogRef = useAccessibleDialog<HTMLDivElement>(true, onClose)
  return <div className="unit-users-backdrop" role="presentation"><div aria-labelledby={titleId} aria-modal="true" className="unit-users-dialog credentials" ref={dialogRef} role="dialog" tabIndex={-1}><header><div><span>Exibição única</span><h3 id={titleId}>Senha temporária</h3></div></header><p>Entregue estes dados diretamente ao servidor. A senha não poderá ser consultada novamente.</p><dl><div><dt>Usuário</dt><dd>{credentials.userName}</dd></div><div><dt>Senha</dt><dd>{credentials.temporaryPassword}</dd></div></dl><button className="primary" onClick={onClose} type="button">Já guardei os dados</button></div></div>
}

async function mutate<T = void>(path: string, method: 'POST' | 'PUT', body?: unknown): Promise<T> { const csrf = await fetch('/api/auth/csrf', { credentials: 'include' }); if (!csrf.ok) throw new Error('Sua sessão precisa ser renovada.'); const token = (await csrf.json() as { token: string }).token; const response = await fetch(path, { method, credentials: 'include', headers: { 'X-STU-CSRF': token, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) }, body: body === undefined ? undefined : JSON.stringify(body) }); if (!response.ok) { const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string; errors?: Record<string, string[]> }; throw new Error(Object.values(problem.errors ?? {}).flat()[0] ?? problem.detail ?? problem.title ?? 'Não foi possível concluir a operação.') } return response.status === 204 ? undefined as T : await response.json() as T }
function messageOf(caught: unknown) { return caught instanceof Error ? caught.message : 'Não foi possível concluir a operação.' }
