/* oxlint-disable react/set-state-in-effect -- effects synchronize state with the administration API */
import { useCallback, useEffect, useId, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { useAccessibleDialog } from '@stu/shared'
import { adminApi, type AuditItem, type BackupRunItem, type BackupSettingsItem, type HealthUnitItem, type PermissionItem, type RoleItem, type UserItem } from './adminApi'

type ManagementProps = { onChanged: () => void }

function message(error: unknown) {
  return error instanceof Error ? error.message : 'Não foi possível concluir a operação.'
}

function formatDate(value: string | null) {
  return value ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) : '—'
}

export function HealthUnitsManager({ onChanged }: ManagementProps) {
  const [items, setItems] = useState<HealthUnitItem[]>([])
  const [editing, setEditing] = useState<HealthUnitItem | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async () => {
    try { setItems(await adminApi.healthUnits()) } catch (caught) { setError(message(caught)) }
  }, [])
  useEffect(() => { void load() }, [load])

  async function toggleArchive(item: HealthUnitItem) {
    const action = item.archivedAtUtc ? 'reativar' : 'arquivar'
    if (!window.confirm(`Deseja ${action} ${item.name}?`)) return
    setError(null)
    try {
      if (item.archivedAtUtc) await adminApi.restoreHealthUnit(item.id)
      else await adminApi.archiveHealthUnit(item.id)
      await load(); onChanged()
    } catch (caught) { setError(message(caught)) }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(null)
    const data = new FormData(event.currentTarget)
    const body = { code: String(data.get('code') ?? ''), name: String(data.get('name') ?? '') }
    try {
      if (editing === 'new') await adminApi.createHealthUnit(body)
      else if (editing) await adminApi.updateHealthUnit(editing.id, body)
      setEditing(null); await load(); onChanged()
    } catch (caught) { setError(message(caught)) } finally { setBusy(false) }
  }

  return (
    <ManagementLayout kicker="Organização" title="UBS cadastradas" description="Gerencie as unidades que utilizarão o STU." action={<button className="management-primary" onClick={() => setEditing('new')} type="button">＋ Nova UBS</button>}>
      {error && <ErrorBanner>{error}</ErrorBanner>}
      <div className="management-table-wrap">
        <table className="management-table">
          <thead><tr><th>Unidade</th><th>Código</th><th>Servidores ativos</th><th>Situação</th><th><span className="sr-only">Ações</span></th></tr></thead>
          <tbody>
            {items.map((item) => <tr key={item.id} className={item.archivedAtUtc ? 'row-archived' : ''}>
              <td><strong>{item.name}</strong><small>Criada em {formatDate(item.createdAtUtc)}</small></td>
              <td><code>{item.code}</code></td><td>{item.activeUsers}</td>
              <td><StatusBadge archived={Boolean(item.archivedAtUtc)} /></td>
              <td><div className="row-actions"><button disabled={Boolean(item.archivedAtUtc)} onClick={() => setEditing(item)} type="button">Editar</button><button onClick={() => void toggleArchive(item)} type="button">{item.archivedAtUtc ? 'Reativar' : 'Arquivar'}</button></div></td>
            </tr>)}
          </tbody>
        </table>
        {items.length === 0 && <EmptyState>Nenhuma UBS cadastrada.</EmptyState>}
      </div>
      {editing && <Modal title={editing === 'new' ? 'Cadastrar UBS' : 'Editar UBS'} onClose={() => setEditing(null)}>
        <form className="management-form" onSubmit={(event) => void submit(event)}>
          <label><span>Código da UBS</span><input defaultValue={editing === 'new' ? '' : editing.code} maxLength={32} name="code" placeholder="Ex.: UBS-CENTRO" required /></label>
          <label><span>Nome da unidade</span><input defaultValue={editing === 'new' ? '' : editing.name} maxLength={160} name="name" placeholder="Ex.: UBS Centro" required /></label>
          <FormActions busy={busy} onCancel={() => setEditing(null)} />
        </form>
      </Modal>}
    </ManagementLayout>
  )
}

export function UsersManager({ onChanged, currentUserId }: ManagementProps & { currentUserId: string }) {
  const [items, setItems] = useState<UserItem[]>([])
  const [units, setUnits] = useState<HealthUnitItem[]>([])
  const [roles, setRoles] = useState<RoleItem[]>([])
  const [editing, setEditing] = useState<UserItem | 'new' | null>(null)
  const [credentials, setCredentials] = useState<{ userName: string; password: string } | null>(null)
  const [query, setQuery] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async (search = query) => {
    try {
      const [users, healthUnits, roleItems] = await Promise.all([adminApi.users(search), adminApi.healthUnits(), adminApi.roles()])
      setItems(users.items); setUnits(healthUnits); setRoles(roleItems)
    } catch (caught) { setError(message(caught)) }
  }, [query])
  useEffect(() => { void load('') }, []) // oxlint-disable-line react-hooks/exhaustive-deps

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(null)
    const data = new FormData(event.currentTarget)
    const roleName = String(data.get('roleName') ?? '')
    const body = {
      displayName: String(data.get('displayName') ?? ''),
      roleName,
      healthUnitId: roleName === 'GlobalAdministrator' ? null : String(data.get('healthUnitId') || '') || null,
    }
    try {
      if (editing === 'new') {
        const created = await adminApi.createUser({ ...body, userName: String(data.get('userName') ?? '') })
        setCredentials({ userName: created.userName, password: created.temporaryPassword })
      } else if (editing) await adminApi.updateUser(editing.id, body)
      setEditing(null); await load(); onChanged()
    } catch (caught) { setError(message(caught)) } finally { setBusy(false) }
  }

  async function resetPassword(item: UserItem) {
    if (!window.confirm(`Gerar uma nova senha temporária para ${item.displayName}?`)) return
    setError(null)
    try {
      const result = await adminApi.resetUserPassword(item.id)
      setCredentials({ userName: item.userName, password: result.temporaryPassword })
      await load(); onChanged()
    } catch (caught) { setError(message(caught)) }
  }

  async function toggleArchive(item: UserItem) {
    const action = item.archivedAtUtc ? 'reativar' : 'arquivar'
    if (!window.confirm(`Deseja ${action} a conta de ${item.displayName}?`)) return
    setError(null)
    try {
      if (item.archivedAtUtc) await adminApi.restoreUser(item.id)
      else await adminApi.archiveUser(item.id)
      await load(); onChanged()
    } catch (caught) { setError(message(caught)) }
  }

  return (
    <ManagementLayout kicker="Acessos" title="Servidores" description="Contas individuais, transferências e senhas temporárias." action={<button className="management-primary" onClick={() => setEditing('new')} type="button">＋ Novo servidor</button>}>
      {error && <ErrorBanner>{error}</ErrorBanner>}
      <form className="management-search" onSubmit={(event) => { event.preventDefault(); void load() }}><input aria-label="Buscar servidor por nome ou usuário" onChange={(event) => setQuery(event.target.value)} placeholder="Buscar por nome ou usuário" value={query} /><button type="submit">Buscar</button></form>
      <div className="management-table-wrap">
        <table className="management-table">
          <thead><tr><th>Servidor</th><th>UBS</th><th>Função</th><th>Situação</th><th><span className="sr-only">Ações</span></th></tr></thead>
          <tbody>{items.map((item) => <tr key={item.id} className={item.archivedAtUtc ? 'row-archived' : ''}>
            <td><strong>{item.displayName}</strong><small>@{item.userName}</small></td><td>{item.healthUnit?.name ?? 'Administração global'}</td><td>{item.role?.displayName ?? 'Sem função'}</td>
            <td><StatusBadge archived={Boolean(item.archivedAtUtc)} pending={item.mustChangePassword} /></td>
            <td><div className="row-actions"><button disabled={item.id === currentUserId || Boolean(item.archivedAtUtc)} onClick={() => setEditing(item)} type="button">Editar</button><button disabled={item.id === currentUserId || Boolean(item.archivedAtUtc)} onClick={() => void resetPassword(item)} type="button">Senha</button><button disabled={item.id === currentUserId} onClick={() => void toggleArchive(item)} type="button">{item.archivedAtUtc ? 'Reativar' : 'Arquivar'}</button></div></td>
          </tr>)}</tbody>
        </table>
        {items.length === 0 && <EmptyState>Nenhum servidor encontrado.</EmptyState>}
      </div>
      {editing && <UserFormModal item={editing} units={units} roles={roles.filter((role) => !role.archivedAtUtc)} busy={busy} onClose={() => setEditing(null)} onSubmit={submit} />}
      {credentials && <CredentialsModal {...credentials} onClose={() => setCredentials(null)} />}
    </ManagementLayout>
  )
}

function UserFormModal({ item, units, roles, busy, onClose, onSubmit }: { item: UserItem | 'new'; units: HealthUnitItem[]; roles: RoleItem[]; busy: boolean; onClose: () => void; onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void> }) {
  const initialRole = item === 'new' ? '' : item.role?.name ?? ''
  const [roleName, setRoleName] = useState(initialRole)
  return <Modal title={item === 'new' ? 'Cadastrar servidor' : 'Editar servidor'} onClose={onClose}>
    <form className="management-form" onSubmit={(event) => void onSubmit(event)}>
      {item === 'new' && <label><span>Usuário de acesso</span><input autoComplete="off" maxLength={120} name="userName" placeholder="nome.sobrenome" required /></label>}
      <label><span>Nome completo</span><input defaultValue={item === 'new' ? '' : item.displayName} maxLength={160} name="displayName" required /></label>
      <label><span>Função</span><select name="roleName" onChange={(event) => setRoleName(event.target.value)} required value={roleName}><option value="">Selecione</option>{roles.map((role) => <option key={role.id} value={role.name}>{role.displayName}</option>)}</select></label>
      {roleName !== 'GlobalAdministrator' && <label><span>UBS</span><select defaultValue={item === 'new' ? '' : item.healthUnitId ?? ''} name="healthUnitId" required><option value="">Selecione</option>{units.filter((unit) => !unit.archivedAtUtc).map((unit) => <option key={unit.id} value={unit.id}>{unit.name}</option>)}</select></label>}
      {item === 'new' && <p className="form-note">Uma senha temporária segura será gerada e exibida apenas uma vez.</p>}
      <FormActions busy={busy} onCancel={onClose} />
    </form>
  </Modal>
}

export function RolesManager({ onChanged }: ManagementProps) {
  const [items, setItems] = useState<RoleItem[]>([])
  const [permissions, setPermissions] = useState<PermissionItem[]>([])
  const [editing, setEditing] = useState<RoleItem | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const load = useCallback(async () => {
    try { const [roleItems, permissionItems] = await Promise.all([adminApi.roles(), adminApi.permissions()]); setItems(roleItems); setPermissions(permissionItems) } catch (caught) { setError(message(caught)) }
  }, [])
  useEffect(() => { void load() }, [load])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(null)
    const data = new FormData(event.currentTarget)
    const body = { displayName: String(data.get('displayName') ?? ''), description: String(data.get('description') ?? ''), permissions: data.getAll('permissions').map(String) }
    try {
      if (editing === 'new') await adminApi.createRole(body)
      else if (editing) await adminApi.updateRole(editing.id, body)
      setEditing(null); await load(); onChanged()
    } catch (caught) { setError(message(caught)) } finally { setBusy(false) }
  }

  async function toggleArchive(item: RoleItem) {
    const action = item.archivedAtUtc ? 'reativar' : 'arquivar'
    if (!window.confirm(`Deseja ${action} a função ${item.displayName}?`)) return
    try {
      if (item.archivedAtUtc) await adminApi.restoreRole(item.id)
      else await adminApi.archiveRole(item.id)
      await load(); onChanged()
    } catch (caught) { setError(message(caught)) }
  }

  return <ManagementLayout kicker="Autorização" title="Funções e permissões" description="Crie perfis operacionais sem alterar as funções protegidas do STU." action={<button className="management-primary" onClick={() => setEditing('new')} type="button">＋ Nova função</button>}>
    {error && <ErrorBanner>{error}</ErrorBanner>}
    <div className="role-grid">{items.map((item) => <article className={item.archivedAtUtc ? 'role-card row-archived' : 'role-card'} key={item.id}><div className="role-card-top"><span className={item.isSystem ? 'role-kind role-kind--system' : 'role-kind'}>{item.isSystem ? 'Protegida' : 'Personalizada'}</span><StatusBadge archived={Boolean(item.archivedAtUtc)} /></div><h3>{item.displayName}</h3><p>{item.description ?? 'Sem descrição.'}</p><small>{item.permissions.includes('*') ? 'Acesso integral' : `${item.permissions.length} permissões`}</small><div className="row-actions"><button disabled={item.isSystem || Boolean(item.archivedAtUtc)} onClick={() => setEditing(item)} type="button">Editar</button><button disabled={item.isSystem} onClick={() => void toggleArchive(item)} type="button">{item.archivedAtUtc ? 'Reativar' : 'Arquivar'}</button></div></article>)}</div>
    {editing && <RoleFormModal item={editing} permissions={permissions} busy={busy} onClose={() => setEditing(null)} onSubmit={submit} />}
  </ManagementLayout>
}

function RoleFormModal({ item, permissions, busy, onClose, onSubmit }: { item: RoleItem | 'new'; permissions: PermissionItem[]; busy: boolean; onClose: () => void; onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void> }) {
  const categories = useMemo(() => permissions.reduce<Record<string, PermissionItem[]>>((groups, permission) => {
    groups[permission.category] ??= []
    groups[permission.category].push(permission)
    return groups
  }, {}), [permissions])
  return <Modal title={item === 'new' ? 'Criar função' : 'Editar função'} onClose={onClose}><form className="management-form" onSubmit={(event) => void onSubmit(event)}><label><span>Nome da função</span><input defaultValue={item === 'new' ? '' : item.displayName} maxLength={120} name="displayName" required /></label><label><span>Descrição</span><textarea defaultValue={item === 'new' ? '' : item.description ?? ''} maxLength={320} name="description" rows={3} /></label><fieldset className="permission-fieldset"><legend>Permissões</legend>{Object.entries(categories).map(([category, categoryPermissions]) => <div className="permission-group" key={category}><strong>{category}</strong>{categoryPermissions.map((permission) => <label key={permission.value}><input defaultChecked={item !== 'new' && item.permissions.includes(permission.value)} name="permissions" type="checkbox" value={permission.value} /><span>{permission.displayName}</span></label>)}</div>)}</fieldset><FormActions busy={busy} onCancel={onClose} /></form></Modal>
}

export function AuditManager() {
  const [items, setItems] = useState<AuditItem[]>([])
  const [filter, setFilter] = useState('')
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { adminApi.audit(filter).then((result) => setItems(result.items)).catch((caught: unknown) => setError(message(caught))) }, [filter])
  return <ManagementLayout kicker="Rastreabilidade" title="Auditoria" description="Registro somente de acréscimo de todas as alterações administrativas." action={<select aria-label="Filtrar tipo" onChange={(event) => setFilter(event.target.value)} value={filter}><option value="">Todos os tipos</option><option value="HealthUnit">UBS</option><option value="User">Usuários</option><option value="Role">Funções</option></select>}>
    {error && <ErrorBanner>{error}</ErrorBanner>}
    <div className="audit-list">{items.map((item) => <article key={item.id}><span className="audit-dot" /><div><div className="audit-meta"><strong>{item.actorUserName}</strong><span>{formatDate(item.occurredAtUtc)}</span><code>{item.action}</code></div><p>{item.summary}</p>{(item.beforeJson || item.afterJson) && <details><summary>Ver detalhes técnicos</summary><div className="audit-json"><pre>{item.beforeJson ?? 'Sem estado anterior'}</pre><pre>{item.afterJson ?? 'Sem estado posterior'}</pre></div></details>}</div></article>)}</div>
    {items.length === 0 && <EmptyState>Nenhuma alteração registrada neste filtro.</EmptyState>}
  </ManagementLayout>
}

const weekDays: { value: BackupSettingsItem['dayOfWeek']; label: string }[] = [
  { value: 'Sunday', label: 'Domingo' },
  { value: 'Monday', label: 'Segunda-feira' },
  { value: 'Tuesday', label: 'Terça-feira' },
  { value: 'Wednesday', label: 'Quarta-feira' },
  { value: 'Thursday', label: 'Quinta-feira' },
  { value: 'Friday', label: 'Sexta-feira' },
  { value: 'Saturday', label: 'Sábado' },
]

export function BackupsManager() {
  const [settings, setSettings] = useState<BackupSettingsItem | null>(null)
  const [runs, setRuns] = useState<BackupRunItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async () => {
    try {
      const [currentSettings, currentRuns] = await Promise.all([adminApi.backupSettings(), adminApi.backupRuns()])
      setSettings(currentSettings); setRuns(currentRuns); setError(null)
    } catch (caught) { setError(message(caught)) }
  }, [])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    if (!runs.some((run) => run.status === 'Queued' || run.status === 'Running')) return
    const timer = window.setInterval(() => void load(), 4000)
    return () => window.clearInterval(timer)
  }, [load, runs])

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(null); setNotice(null)
    const data = new FormData(event.currentTarget)
    try {
      const updated = await adminApi.updateBackupSettings({
        enabled: data.get('enabled') === 'on',
        dayOfWeek: String(data.get('dayOfWeek')) as BackupSettingsItem['dayOfWeek'],
        localHour: Number(data.get('localHour')),
        retentionCount: Number(data.get('retentionCount')),
      })
      setSettings(updated); setNotice('Rotina semanal atualizada.')
    } catch (caught) { setError(message(caught)) } finally { setBusy(false) }
  }

  async function requestBackup() {
    if (!window.confirm('Deseja solicitar um backup completo agora?')) return
    setBusy(true); setError(null); setNotice(null)
    try {
      await adminApi.requestBackup(); setNotice('Backup incluído na fila de execução.'); await load()
    } catch (caught) { setError(message(caught)) } finally { setBusy(false) }
  }

  return <ManagementLayout kicker="Continuidade" title="Backups" description="Controle a cópia semanal e acompanhe as execuções do banco de dados." action={<button className="management-primary" disabled={busy} onClick={() => void requestBackup()} type="button">＋ Fazer backup agora</button>}>
    {error && <ErrorBanner>{error}</ErrorBanner>}
    {notice && <div className="admin-notice" role="status">{notice}</div>}
    {settings && <form className="backup-settings" onSubmit={(event) => void save(event)}>
      <div className="backup-settings__intro"><div><strong>Rotina semanal</strong><span>Horário de Teixeira de Freitas ({settings.timeZoneId})</span></div><label className="backup-switch"><input defaultChecked={settings.enabled} name="enabled" type="checkbox" /><span>Ativada</span></label></div>
      <label><span>Dia da semana</span><select defaultValue={settings.dayOfWeek} name="dayOfWeek">{weekDays.map((day) => <option key={day.value} value={day.value}>{day.label}</option>)}</select></label>
      <label><span>Horário</span><select defaultValue={settings.localHour} name="localHour">{Array.from({ length: 24 }, (_, hour) => <option key={hour} value={hour}>{String(hour).padStart(2, '0')}:00</option>)}</select></label>
      <label><span>Arquivos mantidos</span><input defaultValue={settings.retentionCount} max={52} min={1} name="retentionCount" type="number" /></label>
      <button className="management-primary" disabled={busy} type="submit">{busy ? 'Salvando…' : 'Salvar rotina'}</button>
      <small>O histórico das execuções permanece na auditoria mesmo quando arquivos antigos são removidos.</small>
    </form>}
    <section className="backup-history"><header><div><span>Histórico</span><h3>Execuções recentes</h3></div><button onClick={() => void load()} type="button">Atualizar</button></header>
      <div className="management-table-wrap"><table className="management-table"><thead><tr><th>Solicitação</th><th>Origem</th><th>Situação</th><th>Arquivo</th><th><span className="sr-only">Ação</span></th></tr></thead><tbody>{runs.map((run) => <tr key={run.id}>
        <td><strong>{formatDate(run.requestedAtUtc)}</strong><small>{run.completedAtUtc ? `Concluído em ${formatDate(run.completedAtUtc)}` : run.startedAtUtc ? `Iniciado em ${formatDate(run.startedAtUtc)}` : 'Aguardando o worker'}</small></td>
        <td>{run.trigger === 'Manual' ? 'Manual' : 'Semanal'}</td><td><BackupStatus status={run.status} /></td>
        <td><strong>{run.fileName ?? '—'}</strong><small>{run.sizeBytes ? formatBytes(run.sizeBytes) : run.errorSummary ?? (run.filePrunedAtUtc ? 'Arquivo removido pela retenção' : '—')}</small>{run.sha256 && <code className="backup-hash" title={run.sha256}>SHA-256 {run.sha256.slice(0, 12)}…</code>}</td>
        <td><div className="row-actions">{run.canDownload ? <a href={`/api/admin/backups/runs/${run.id}/download`}>Baixar</a> : <span>—</span>}</div></td>
      </tr>)}</tbody></table>{runs.length === 0 && <EmptyState>Nenhum backup solicitado.</EmptyState>}</div>
    </section>
  </ManagementLayout>
}

function BackupStatus({ status }: { status: BackupRunItem['status'] }) {
  const labels = { Queued: 'Na fila', Running: 'Executando', Completed: 'Concluído', Failed: 'Falhou' }
  return <span className={`backup-status backup-status--${status.toLowerCase()}`}>{labels[status]}</span>
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} bytes`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function ManagementLayout({ kicker, title, description, action, children }: { kicker: string; title: string; description: string; action: ReactNode; children: ReactNode }) {
  return <section className="management-page"><header className="management-heading"><div><span>{kicker}</span><h2>{title}</h2><p>{description}</p></div>{action}</header>{children}</section>
}

function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const titleId = useId()
  const dialogRef = useAccessibleDialog<HTMLElement>(true, onClose)
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.currentTarget === event.target) onClose() }}><section aria-labelledby={titleId} aria-modal="true" className="management-modal" ref={dialogRef} role="dialog" tabIndex={-1}><header><h2 id={titleId}>{title}</h2><button aria-label="Fechar janela" onClick={onClose} type="button">×</button></header>{children}</section></div>
}

function CredentialsModal({ userName, password, onClose }: { userName: string; password: string; onClose: () => void }) {
  const [copied, setCopied] = useState(false)
  async function copy() { await navigator.clipboard.writeText(`Usuário: ${userName}\nSenha temporária: ${password}`); setCopied(true) }
  return <Modal title="Credencial temporária" onClose={onClose}><div className="credentials-box"><p>Copie e entregue de forma segura. A senha não será exibida novamente.</p><label><span>Usuário</span><code>{userName}</code></label><label><span>Senha temporária</span><code>{password}</code></label><button className="management-primary" onClick={() => void copy()} type="button">{copied ? 'Copiado' : 'Copiar credencial'}</button></div></Modal>
}

function FormActions({ busy, onCancel }: { busy: boolean; onCancel: () => void }) { return <div className="form-actions"><button onClick={onCancel} type="button">Cancelar</button><button className="management-primary" disabled={busy} type="submit">{busy ? 'Salvando…' : 'Salvar'}</button></div> }
function StatusBadge({ archived, pending = false }: { archived: boolean; pending?: boolean }) { if (archived) return <span className="status-badge status-badge--archived">Arquivado</span>; if (pending) return <span className="status-badge status-badge--pending">Senha temporária</span>; return <span className="status-badge">Ativo</span> }
function ErrorBanner({ children }: { children: ReactNode }) { return <div className="admin-error" role="alert">{children}</div> }
function EmptyState({ children }: { children: ReactNode }) { return <div className="empty-state">{children}</div> }
