import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Session } from '../auth/types'
import './onboarding.css'
import './onboarding-progress.css'

type Unit = { id: string; code: string; name: string; archivedAtUtc?: string | null }
type Check = { id: string; label: string; status: 'passed' | 'blocked'; detail: string }
export type OnboardingDestination = 'territory' | 'coverage' | 'operations' | 'users' | 'backups'
type Report = {
  generatedAtUtc: string
  snapshotHash: string
  healthUnit: Unit
  counts: {
    neighborhoods: number; microregions: number; properties: number; activeUsers: number; temporaryPasswords: number
    healthAgents: number; receptionists: number; doctors: number; managers: number
  }
  importSummary: { beforeLatestImport: number; latestImported: number; currentTotal: number; pendingImports: number }
  backup: { scheduleEnabled: boolean; runId: string | null; completedAtUtc: string | null; sha256: string | null; sizeBytes: number | null; recent: boolean }
  readyForApproval: boolean
  checks: Check[]
  latestApproval: { approvedAtUtc: string; approvedBy: string; snapshotHash: string | null; offsiteDestination: string | null } | null
}

export function OnboardingWorkspace({ session, global = false, actions }: { session: Session; global?: boolean; actions?: Partial<Record<OnboardingDestination, () => void>> }) {
  const [units, setUnits] = useState<Unit[]>(session.healthUnit ? [session.healthUnit] : [])
  const [unitId, setUnitId] = useState(session.healthUnit?.id ?? '')
  const [report, setReport] = useState<Report | null>(null)
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    if (!global) return
    fetch('/api/admin/health-units?includeArchived=false', { credentials: 'include' })
      .then(async response => {
        if (!response.ok) throw new Error('Não foi possível listar as UBS.')
        return await response.json() as Unit[]
      })
      .then(items => {
        const active = items.filter(item => !item.archivedAtUtc)
        setUnits(active)
        setUnitId(current => current || active[0]?.id || '')
      })
      .catch(caught => setError(messageOf(caught)))
  }, [global])

  const load = useCallback(async () => {
    if (!unitId) return
    setLoading(true)
    try {
      const query = new URLSearchParams({ healthUnitId: unitId })
      const response = await fetch(`/api/onboarding/readiness?${query}`, { credentials: 'include' })
      if (!response.ok) throw new Error('Não foi possível avaliar a pré-implantação desta UBS.')
      setReport(await response.json() as Report)
      setError(null)
    } catch (caught) {
      setError(messageOf(caught))
    } finally {
      setLoading(false)
    }
  }, [unitId])

  useEffect(() => { void load() }, [load])

  async function approve(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!report) return
    setBusy(true); setError(null); setNotice(null)
    const data = new FormData(event.currentTarget)
    try {
      const token = await csrfToken()
      const response = await fetch('/api/onboarding/approve', {
        method: 'POST', credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'X-STU-CSRF': token },
        body: JSON.stringify({
          healthUnitId: unitId,
          backupSha256: data.get('backupSha256'),
          offsiteDestination: data.get('offsiteDestination'),
          note: data.get('note'),
        }),
      })
      await ensureOk(response)
      setNotice('Pré-implantação aprovada e registrada na auditoria. Os dados oficiais não foram alterados por esta confirmação.')
      await load()
    } catch (caught) {
      setError(messageOf(caught))
    } finally {
      setBusy(false)
    }
  }

  const approvalCurrent = Boolean(report?.latestApproval?.snapshotHash && report.latestApproval.snapshotHash === report.snapshotHash)
  const passedChecks = report?.checks.filter(item => item.status === 'passed').length ?? 0
  const blockedChecks = report?.checks.filter(item => item.status === 'blocked') ?? []
  const completion = report?.checks.length ? Math.round((passedChecks / report.checks.length) * 100) : 0
  const nextStep = blockedChecks[0]
  const nextAction = nextStep ? actionForCheck(nextStep) : null

  return <section className="onboarding-workspace">
    <header className="onboarding-heading"><div><span>Entrada controlada</span><h2>Pré-implantação da UBS</h2><p>Revise território, cadastros, servidores, importações e backup antes de autorizar o piloto.</p></div><div className="onboarding-heading-actions">{units.length > 1 && <label>UBS<select onChange={event => setUnitId(event.target.value)} value={unitId}>{units.map(unit => <option key={unit.id} value={unit.id}>{unit.code} — {unit.name}</option>)}</select></label>}<button disabled={loading || !unitId} onClick={() => void load()} type="button">Atualizar verificação</button></div></header>
    {error && <div className="onboarding-message onboarding-message--error" role="alert">{error}</div>}
    {notice && <div className="onboarding-message" role="status">{notice}</div>}
    {loading && !report ? <p className="onboarding-loading" role="status">Verificando a UBS…</p> : report && <>
      <section className="onboarding-decision" aria-label="Resultado da pré-implantação"><div className={report.readyForApproval ? 'onboarding-decision-icon ready' : 'onboarding-decision-icon blocked'} aria-hidden="true">{report.readyForApproval ? '✓' : '!'}</div><div><span>{report.healthUnit.code}</span><h3>{report.readyForApproval ? 'Pronta para registrar a aprovação' : 'Existem pendências antes da aprovação'}</h3><p>{report.readyForApproval ? 'Todos os controles automáticos passaram. Confirme abaixo a cópia criptografada externa.' : 'Corrija os itens bloqueados e execute a verificação novamente.'}</p></div><small>Verificado em {new Date(report.generatedAtUtc).toLocaleString('pt-BR')}</small></section>

      <section className="onboarding-progress" aria-label={`Progresso da pré-implantação: ${completion}%`}>
        <div><span>Progresso real</span><strong>{passedChecks} de {report.checks.length} controles aprovados</strong><small>{blockedChecks.length ? `${blockedChecks.length} pendência${blockedChecks.length === 1 ? '' : 's'} restante${blockedChecks.length === 1 ? '' : 's'}` : 'Validação automática concluída'}</small></div>
        <div aria-hidden="true" className="onboarding-progress-track"><i style={{ width: `${completion}%` }} /></div>
        <b>{completion}%</b>
      </section>

      {nextStep && <section className="onboarding-next" aria-label="Próxima ação recomendada"><div><span>Próxima ação</span><h3>{nextStep.label}</h3><p>{nextStep.detail}</p></div>{nextAction && actions?.[nextAction.destination] ? <button onClick={actions[nextAction.destination]} type="button">{nextAction.label}</button> : <small>{nextAction?.label ?? 'Esta pendência depende da administração global.'}</small>}</section>}

      <section className="onboarding-counts" aria-label="Contagens da UBS"><Count label="Bairros" value={report.counts.neighborhoods} /><Count label="Microrregiões" value={report.counts.microregions} /><Count label="Imóveis" value={report.counts.properties} /><Count label="Servidores" value={report.counts.activeUsers} /></section>

      <section className="onboarding-import-flow" aria-label="Contagem antes e depois da importação"><div><small>Antes da última importação</small><strong>{report.importSummary.beforeLatestImport}</strong></div><span aria-hidden="true">＋</span><div><small>Importados no último lote</small><strong>{report.importSummary.latestImported}</strong></div><span aria-hidden="true">＝</span><div><small>Total atual</small><strong>{report.importSummary.currentTotal}</strong></div></section>

      <div className="onboarding-layout"><section className="onboarding-checks"><header><div><span>Validação automática</span><h3>Itens obrigatórios</h3></div><strong>{passedChecks}/{report.checks.length}</strong></header>{report.checks.map(item => { const action = actionForCheck(item); const handler = action ? actions?.[action.destination] : undefined; return <article className={item.status === 'passed' ? 'check-passed' : 'check-blocked'} key={item.id}><i aria-hidden="true">{item.status === 'passed' ? '✓' : '!'}</i><div><strong>{item.label}</strong><p>{item.detail}</p>{item.status === 'blocked' && action && handler && <button onClick={handler} type="button">{action.label}</button>}</div><span>{item.status === 'passed' ? 'Aprovado' : 'Bloqueado'}</span></article>})}</section>

        <aside className="onboarding-side"><section><span>Perfis operacionais</span><h3>Contas preparadas</h3><dl><div><dt>Agentes</dt><dd>{report.counts.healthAgents}</dd></div><div><dt>Recepção</dt><dd>{report.counts.receptionists}</dd></div><div><dt>Médicos</dt><dd>{report.counts.doctors}</dd></div><div><dt>Gerentes</dt><dd>{report.counts.managers}</dd></div><div><dt>Senhas temporárias</dt><dd>{report.counts.temporaryPasswords}</dd></div></dl></section>
          <section><span>Evidência atual</span><h3>Última aprovação</h3>{report.latestApproval ? <><p><strong>{approvalCurrent ? 'Válida para o estado atual' : 'Desatualizada após mudanças'}</strong></p><small>{new Date(report.latestApproval.approvedAtUtc).toLocaleString('pt-BR')} por {report.latestApproval.approvedBy}</small><small>{report.latestApproval.offsiteDestination}</small></> : <p>Nenhuma aprovação registrada.</p>}</section>
        </aside>
      </div>

      <form className="onboarding-approval" onSubmit={approve}><div><span>Confirmação do gerente</span><h3>Registrar evidência de backup externo</h3><p>Faça a cópia criptografada fora deste servidor e informe apenas o nome do destino. Não registre senha, chave ou caminho com credenciais.</p></div><label>Destino externo<input disabled={!report.readyForApproval} maxLength={160} name="offsiteDestination" placeholder="Ex.: cofre digital da prefeitura" required /></label><label>SHA-256 do backup<input defaultValue={report.backup.sha256 ?? ''} disabled={!report.readyForApproval} name="backupSha256" pattern="[a-fA-F0-9]{64}" required /></label><label>Observação operacional<textarea disabled={!report.readyForApproval} maxLength={500} name="note" rows={3} /></label><button disabled={!report.readyForApproval || busy} type="submit">{busy ? 'Registrando…' : 'Aprovar pré-implantação'}</button>{!report.readyForApproval && <small>A aprovação será liberada quando todos os itens obrigatórios estiverem aprovados.</small>}</form>
    </>}
  </section>
}

function Count({ label, value }: { label: string; value: number }) { return <article><small>{label}</small><strong>{value}</strong></article> }
function actionForCheck(check: Check): { destination: OnboardingDestination; label: string } | null {
  if (['neighborhood-count', 'pilot-neighborhoods', 'microregions', 'coordinate-system', 'overlaps', 'coverage-gaps', 'neighborhood-links', 'agent-assignments', 'property-boundaries'].includes(check.id)) return { destination: 'territory', label: 'Resolver em Território' }
  if (['properties', 'family-numbers'].includes(check.id)) return { destination: 'coverage', label: 'Resolver em Cobertura' }
  if (check.id === 'role-accounts') return { destination: 'users', label: 'Resolver em Servidores' }
  if (check.id === 'pending-imports') return { destination: 'operations', label: 'Revisar importações' }
  if (['backup-schedule', 'recent-backup'].includes(check.id)) return { destination: 'backups', label: 'Revisar backups' }
  return null
}
function messageOf(caught: unknown) { return caught instanceof Error ? caught.message : 'Não foi possível concluir a operação.' }
async function csrfToken() { const response = await fetch('/api/auth/csrf', { credentials: 'include' }); if (!response.ok) throw new Error('Sua sessão precisa ser renovada.'); return (await response.json() as { token: string }).token }
async function ensureOk(response: Response) { if (response.ok) return; const problem = await response.json().catch(() => ({})) as { title?: string; detail?: string; errors?: Record<string, string[]> }; throw new Error(problem.detail ?? Object.values(problem.errors ?? {})[0]?.[0] ?? problem.title ?? 'Não foi possível concluir a operação.') }
