import { useCallback, useEffect, useId, useState, type FormEvent } from 'react'
import type { Session } from '../auth/types'
import { useAccessibleDialog } from '../accessibility/useAccessibleDialog'
import './health-unit-users.css'

type Role = { id: string; name: string; displayName: string; description: string | null; isSystem: boolean }
type User = { id: string; userName: string; displayName: string; role: Role | null; mustChangePassword: boolean; lockoutEnd: string | null; archivedAtUtc: string | null; manageable: boolean }
type Reference = { healthUnit: { id: string; code: string; name: string }; roles: Role[] }
type Credentials = { userName: string; temporaryPassword: string }

export function HealthUnitUsersWorkspace({ session }: { session: Session }) {
  const [users, setUsers] = useState<User[]>([])
  const [reference, setReference] = useState<Reference | null>(null)
  const [query, setQuery] = useState('')
  const [editing, setEditing] = useState<User | 'new' | null>(null)
  const [credentials, setCredentials] = useState<Credentials | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const load = useCallback(async (search = query) => {
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
  }, [query])

  useEffect(() => { void load('') }, []) // oxlint-disable-line react-hooks/exhaustive-deps

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
  return <section className="unit-users-workspace">
    <header className="unit-users-heading"><div><span>Equipe da UBS</span><h2>Servidores cadastrados</h2><p>Contas individuais vinculadas exclusivamente à {reference?.healthUnit.name ?? session.healthUnit?.name ?? 'sua UBS'}.</p></div><button onClick={() => setEditing('new')} type="button">＋ Adicionar servidor</button></header>
    {error && <div className="unit-users-message unit-users-error" role="alert">{error}<button aria-label="Dispensar erro" onClick={() => setError(null)} type="button">×</button></div>}
    {notice && <div className="unit-users-message" role="status">{notice}<button aria-label="Dispensar aviso" onClick={() => setNotice(null)} type="button">×</button></div>}
    <section className="unit-users-metrics" aria-label="Resumo da equipe"><article><small>Contas ativas</small><strong>{activeCount}</strong></article><article><small>Primeiro acesso pendente</small><strong>{pendingCount}</strong></article><article><small>Funções disponíveis</small><strong>{reference?.roles.length ?? 0}</strong></article></section>
    <form className="unit-users-search" onSubmit={event => { event.preventDefault(); void load() }}><input aria-label="Buscar servidor" onChange={event => setQuery(event.target.value)} placeholder="Buscar por nome ou usuário" value={query} /><button disabled={loading} type="submit">Buscar</button></form>
    <div className="unit-users-table-wrap"><table className="unit-users-table"><thead><tr><th>Servidor</th><th>Função</th><th>Situação</th><th><span className="sr-only">Ações</span></th></tr></thead><tbody>{users.map(user => <tr className={user.archivedAtUtc ? 'archived' : ''} key={user.id}><td><strong>{user.displayName}</strong><small>@{user.userName}</small></td><td>{user.role?.displayName ?? 'Sem função'}</td><td><Status user={user} /></td><td>{user.manageable ? <div className="unit-users-actions"><button disabled={Boolean(user.archivedAtUtc)} onClick={() => setEditing(user)} type="button">Editar</button><button disabled={Boolean(user.archivedAtUtc)} onClick={() => void resetPassword(user)} type="button">Senha</button><button onClick={() => void toggleArchive(user)} type="button">{user.archivedAtUtc ? 'Reativar' : 'Arquivar'}</button></div> : <small className="unit-users-protected">Gerência protegida</small>}</td></tr>)}</tbody></table>{users.length === 0 && !loading && <div className="unit-users-empty"><strong>Nenhum servidor encontrado</strong><p>Use “Adicionar servidor” para criar a primeira conta da equipe.</p></div>}{loading && <p className="unit-users-loading" role="status">Atualizando servidores…</p>}</div>
    {editing && reference && <UserEditor item={editing} roles={reference.roles} busy={loading} onClose={() => setEditing(null)} onSubmit={save} />}
    {credentials && <CredentialsDialog credentials={credentials} onClose={() => setCredentials(null)} />}
  </section>
}

function Status({ user }: { user: User }) { if (user.archivedAtUtc) return <span className="unit-user-status archived">Arquivado</span>; if (user.lockoutEnd && new Date(user.lockoutEnd) > new Date()) return <span className="unit-user-status locked">Bloqueado</span>; if (user.mustChangePassword) return <span className="unit-user-status pending">Senha temporária</span>; return <span className="unit-user-status active">Ativo</span> }

function UserEditor({ item, roles, busy, onClose, onSubmit }: { item: User | 'new'; roles: Role[]; busy: boolean; onClose: () => void; onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void> }) {
  const titleId = useId(); const dialogRef = useAccessibleDialog<HTMLFormElement>(true, onClose)
  return <div className="unit-users-backdrop" onMouseDown={event => { if (event.currentTarget === event.target) onClose() }} role="presentation"><form aria-labelledby={titleId} aria-modal="true" className="unit-users-dialog" onSubmit={event => void onSubmit(event)} ref={dialogRef} role="dialog" tabIndex={-1}><header><div><span>{item === 'new' ? 'Nova conta individual' : 'Atualizar servidor'}</span><h3 id={titleId}>{item === 'new' ? 'Adicionar servidor' : item.displayName}</h3></div><button aria-label="Fechar cadastro de servidor" onClick={onClose} type="button">×</button></header>{item === 'new' && <label>Usuário de acesso<input autoComplete="off" maxLength={120} name="userName" placeholder="nome.sobrenome" required /></label>}<label>Nome completo<input defaultValue={item === 'new' ? '' : item.displayName} maxLength={160} name="displayName" required /></label><label>Função<select defaultValue={item === 'new' ? '' : item.role?.name ?? ''} name="roleName" required><option value="">Selecione</option>{roles.map(role => <option key={role.id} value={role.name}>{role.displayName}</option>)}</select></label>{item === 'new' && <p>Uma senha temporária segura será exibida apenas uma vez. O servidor deverá trocá-la no primeiro acesso.</p>}<footer><button onClick={onClose} type="button">Cancelar</button><button className="primary" disabled={busy} type="submit">{busy ? 'Salvando…' : 'Salvar servidor'}</button></footer></form></div>
}

function CredentialsDialog({ credentials, onClose }: { credentials: Credentials; onClose: () => void }) {
  const titleId = useId(); const dialogRef = useAccessibleDialog<HTMLDivElement>(true, onClose)
  return <div className="unit-users-backdrop" role="presentation"><div aria-labelledby={titleId} aria-modal="true" className="unit-users-dialog credentials" ref={dialogRef} role="dialog" tabIndex={-1}><header><div><span>Exibição única</span><h3 id={titleId}>Senha temporária</h3></div></header><p>Entregue estes dados diretamente ao servidor. A senha não poderá ser consultada novamente.</p><dl><div><dt>Usuário</dt><dd>{credentials.userName}</dd></div><div><dt>Senha</dt><dd>{credentials.temporaryPassword}</dd></div></dl><button className="primary" onClick={onClose} type="button">Já guardei os dados</button></div></div>
}

async function mutate<T = void>(path: string, method: 'POST' | 'PUT', body?: unknown): Promise<T> { const csrf = await fetch('/api/auth/csrf', { credentials: 'include' }); if (!csrf.ok) throw new Error('Sua sessão precisa ser renovada.'); const token = (await csrf.json() as { token: string }).token; const response = await fetch(path, { method, credentials: 'include', headers: { 'X-STU-CSRF': token, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) }, body: body === undefined ? undefined : JSON.stringify(body) }); if (!response.ok) { const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string; errors?: Record<string, string[]> }; throw new Error(Object.values(problem.errors ?? {}).flat()[0] ?? problem.detail ?? problem.title ?? 'Não foi possível concluir a operação.') } return response.status === 204 ? undefined as T : await response.json() as T }
function messageOf(caught: unknown) { return caught instanceof Error ? caught.message : 'Não foi possível concluir a operação.' }
