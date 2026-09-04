import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Session } from '../auth/types'
import './operations.css'

type Unit = { id: string; code: string; name: string; archivedAtUtc?: string | null }
type Microregion = { id: string; code: string; name: string }
type ReferenceData = { microregions: Microregion[] }
type Indicators = { activeProperties: number; drafts: number; unassignedMicroregions: number; pendingJobs: number }
type Job = {
  id: string; kind: 'PropertyExport' | 'PropertyImport'; status: 'Pending' | 'Processing' | 'AwaitingApproval' | 'Completed' | 'Failed'; format: string
  originalFileName: string | null; recordCount: number; validationErrorCount: number; errorSummary: string | null; attemptCount: number
  progressPercentage: number; createdAtUtc: string; createdByName: string; canDownload: boolean
}

const statusLabels: Record<Job['status'], string> = { Pending: 'Na fila', Processing: 'Processando', AwaitingApproval: 'Aguardando aprovação', Completed: 'Concluído', Failed: 'Falhou' }
const formatLabels: Record<string, string> = { Csv: 'Planilha CSV', GeoJson: 'GeoJSON', Kml: 'KML', GeoPackage: 'GeoPackage' }

export function OperationalWorkspace({ session, global = false }: { session: Session; global?: boolean }) {
  const [units, setUnits] = useState<Unit[]>(session.healthUnit ? [session.healthUnit] : [])
  const [unitId, setUnitId] = useState(session.healthUnit?.id ?? '')
  const [microregions, setMicroregions] = useState<Microregion[]>([])
  const [jobs, setJobs] = useState<Job[]>([])
  const [indicators, setIndicators] = useState<Indicators | null>(null)
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const canExport = global || session.permissions.includes('*') || session.permissions.includes('reports.export')
  const canImport = global || session.permissions.includes('*') || session.permissions.includes('territory.manage')

  useEffect(() => {
    if (!global) return
    fetch('/api/admin/health-units', { credentials: 'include' }).then(async response => {
      if (!response.ok) throw new Error('Não foi possível listar as UBS.')
      return await response.json() as Unit[]
    }).then(items => {
      const active = items.filter(item => !item.archivedAtUtc)
      setUnits(active); setUnitId(current => current || active[0]?.id || '')
    }).catch(caught => setError(messageOf(caught)))
  }, [global])

  const load = useCallback(async (quiet = false) => {
    if (!unitId) return
    if (!quiet) setLoading(true)
    try {
      const query = new URLSearchParams({ healthUnitId: unitId }).toString()
      const requests: Promise<Response>[] = [
        fetch(`/api/operations/indicators?${query}`, { credentials: 'include' }),
        fetch(`/api/properties/reference-data?${query}`, { credentials: 'include' }),
      ]
      if (canExport || canImport) requests.push(fetch(`/api/operations/jobs?${query}`, { credentials: 'include' }))
      const responses = await Promise.all(requests)
      if (responses.some(response => !response.ok)) throw new Error('Não foi possível carregar os fluxos operacionais.')
      setIndicators(await responses[0].json() as Indicators)
      setMicroregions((await responses[1].json() as ReferenceData).microregions)
      if (responses[2]) setJobs(await responses[2].json() as Job[])
      setError(null)
    } catch (caught) { if (!quiet) setError(messageOf(caught)) }
    finally { if (!quiet) setLoading(false) }
  }, [canExport, canImport, unitId])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    if (!jobs.some(job => job.status === 'Pending' || job.status === 'Processing')) return
    const timer = window.setInterval(() => void load(true), 3000)
    return () => window.clearInterval(timer)
  }, [jobs, load])

  async function createExport(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(null); setNotice(null)
    const data = new FormData(event.currentTarget)
    try {
      await mutate('/api/operations/exports', 'POST', { healthUnitId: unitId, format: data.get('format'), microregionId: valueOrNull(data.get('microregionId')), situation: valueOrNull(data.get('situation')), fromUtc: dateOrNull(data.get('from')), toUtc: dateOrNull(data.get('to'), true) })
      setNotice('Exportação adicionada à fila. Você será avisado quando o arquivo estiver pronto.'); await load()
    } catch (caught) { setError(messageOf(caught)) } finally { setBusy(false) }
  }

  async function createImport(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(null); setNotice(null)
    const form = event.currentTarget; const data = new FormData(form); data.set('healthUnitId', unitId)
    try {
      await mutateForm('/api/operations/imports', data)
      form.reset(); setNotice('Arquivo recebido. A validação será feita antes de qualquer cadastro.'); await load()
    } catch (caught) { setError(messageOf(caught)) } finally { setBusy(false) }
  }

  async function action(job: Job, kind: 'approve' | 'retry') {
    setBusy(true); setError(null)
    try {
      const query = new URLSearchParams({ healthUnitId: unitId })
      const path = kind === 'approve' ? `/api/operations/imports/${job.id}/approve?${query}` : `/api/operations/jobs/${job.id}/retry?${query}`
      await mutate(path, 'POST'); setNotice(kind === 'approve' ? 'Importação aprovada e enviada para efetivação.' : 'Operação recolocada na fila.'); await load()
    } catch (caught) { setError(messageOf(caught)) } finally { setBusy(false) }
  }

  return <section className="operations-workspace">
    <header className="operations-heading"><div><span>Fluxos seguros</span><h2>Importações e exportações</h2><p>Arquivos grandes são processados em segundo plano, com validação e auditoria.</p></div>{units.length > 1 && <label>UBS<select value={unitId} onChange={event => setUnitId(event.target.value)}>{units.map(unit => <option key={unit.id} value={unit.id}>{unit.code} — {unit.name}</option>)}</select></label>}</header>
    {error && <div className="operations-message operations-message--error" role="alert">{error}</div>}
    {notice && <div className="operations-message" role="status">{notice}</div>}
    <section className="operations-metrics" aria-label="Indicadores operacionais">
      <OperationMetric label="Imóveis ativos" value={indicators?.activeProperties} />
      <OperationMetric label="Rascunhos" value={indicators?.drafts} />
      <OperationMetric label="Microrregiões sem agente" value={indicators?.unassignedMicroregions} />
      <OperationMetric label="Operações pendentes" value={indicators?.pendingJobs} />
    </section>
    <div className="operations-grid">
      {canExport && <form className="operations-card" onSubmit={createExport}><span className="operations-kicker">Saída de dados</span><h3>Gerar exportação</h3><p>Filtre os imóveis e escolha o formato compatível com sua ferramenta.</p><label>Formato<select defaultValue="Csv" name="format"><option value="Csv">Planilha CSV</option><option value="GeoJson">GeoJSON</option><option value="Kml">KML</option><option value="GeoPackage">GeoPackage (QGIS)</option></select></label><label>Microrregião<select name="microregionId"><option value="">Todas</option>{microregions.map(item => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}</select></label><label>Situação<select name="situation"><option value="">Todas</option><option value="Active">Ativo</option><option value="Vacant">Vago</option><option value="Abandoned">Abandonado</option><option value="Demolished">Demolido</option></select></label><div className="operations-dates"><label>Visitas desde<input name="from" type="date" /></label><label>Até<input name="to" type="date" /></label></div><button className="operations-primary" disabled={busy || !unitId} type="submit">Gerar arquivo</button></form>}
      {canImport && <form className="operations-card" onSubmit={createImport}><span className="operations-kicker">Entrada controlada</span><h3>Importar imóveis</h3><p>Envie CSV ou GeoJSON. O sistema apenas efetiva os registros depois da sua aprovação.</p><label className="operations-file">Arquivo<input accept=".csv,.json,.geojson,text/csv,application/geo+json" name="file" required type="file" /><small>Máximo de 20 MB e 10.000 imóveis.</small></label><details><summary>Colunas esperadas no CSV</summary><code>microregionCode,street,houseNumber,familyNumber,longitude,latitude,situation,registrationStatus</code></details><button className="operations-primary" disabled={busy || !unitId} type="submit">Validar arquivo</button></form>}
    </div>
    {(canExport || canImport) && <section className="operations-history"><div><span className="operations-kicker">Acompanhamento</span><h3>Histórico de operações</h3></div>{loading ? <p>Carregando operações…</p> : jobs.length === 0 ? <p>Nenhuma importação ou exportação realizada nesta UBS.</p> : <div className="operations-table-wrap"><table><thead><tr><th>Operação</th><th>Situação</th><th>Registros</th><th>Solicitante</th><th>Data</th><th>Ação</th></tr></thead><tbody>{jobs.map(job => <tr key={job.id}><td><strong>{job.kind === 'PropertyImport' ? 'Importação' : 'Exportação'}</strong><small>{job.originalFileName ?? formatLabels[job.format] ?? job.format}</small></td><td><span className={`job-status job-status--${job.status.toLowerCase()}`}>{statusLabels[job.status]}</span>{job.errorSummary && <small className="job-error">{job.errorSummary}</small>}</td><td>{job.recordCount}{job.validationErrorCount > 0 && <small className="job-error">{job.validationErrorCount} erro(s)</small>}</td><td>{job.createdByName}</td><td>{new Date(job.createdAtUtc).toLocaleString('pt-BR')}</td><td className="job-actions">{canImport && job.status === 'AwaitingApproval' && <button disabled={busy} onClick={() => void action(job, 'approve')} type="button">Aprovar</button>}{job.status === 'Failed' && job.attemptCount < 3 && ((job.kind === 'PropertyImport' && canImport) || (job.kind === 'PropertyExport' && canExport)) && <button disabled={busy} onClick={() => void action(job, 'retry')} type="button">Tentar novamente</button>}{canExport && job.canDownload && <a href={`/api/operations/jobs/${job.id}/download?healthUnitId=${encodeURIComponent(unitId)}`}>Baixar</a>}</td></tr>)}</tbody></table></div>}</section>}
  </section>
}

function OperationMetric({ label, value }: { label: string; value: number | undefined }) { return <article><small>{label}</small><strong>{value ?? '—'}</strong></article> }
function valueOrNull(value: FormDataEntryValue | null) { const text = String(value ?? '').trim(); return text || null }
function dateOrNull(value: FormDataEntryValue | null, endOfDay = false) { const text = valueOrNull(value); return text ? new Date(`${text}T${endOfDay ? '23:59:59.999' : '00:00:00'}`).toISOString() : null }
function messageOf(caught: unknown) { return caught instanceof Error ? caught.message : 'Não foi possível concluir a operação.' }
async function csrfToken() { const response = await fetch('/api/auth/csrf', { credentials: 'include' }); if (!response.ok) throw new Error('Sua sessão precisa ser renovada.'); return (await response.json() as { token: string }).token }
async function mutate(path: string, method: 'POST', body?: unknown) { const token = await csrfToken(); const response = await fetch(path, { method, credentials: 'include', headers: { 'Content-Type': 'application/json', 'X-STU-CSRF': token }, body: body === undefined ? undefined : JSON.stringify(body) }); await ensureOk(response); return response }
async function mutateForm(path: string, body: FormData) { const token = await csrfToken(); const response = await fetch(path, { method: 'POST', credentials: 'include', headers: { 'X-STU-CSRF': token }, body }); await ensureOk(response); return response }
async function ensureOk(response: Response) { if (response.ok) return; const problem = await response.json().catch(() => ({})) as { detail?: string; error?: string; errors?: Record<string, string[]> }; throw new Error(problem.detail ?? problem.error ?? Object.values(problem.errors ?? {})[0]?.[0] ?? 'Não foi possível concluir a operação.') }
