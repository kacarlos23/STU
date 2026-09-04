import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Session } from '../auth/types'
import './pilot-release.css'

type Unit = { id: string; code: string; name: string; archivedAtUtc?: string | null }
type Check = { id: string; label: string; status: 'passed' | 'blocked'; detail: string }
type Decision = {
  decision: 'go' | 'no-go'; recordedAtUtc: string; recordedBy: string; releaseSnapshotHash: string | null
  supportOwner: string | null; incidentOwner: string | null; rollbackOwner: string | null; note: string | null; current: boolean
}
type Report = {
  generatedAtUtc: string; releaseSnapshotHash: string; onboardingSnapshotHash: string; healthUnit: Unit
  readyForDecision: boolean; released: boolean; checks: Check[]
  evidence: { label: string; document: string }[]; latestDecision: Decision | null
}

const guides = [
  { title: 'Agente de saúde', items: ['Consulte apenas a microrregião atribuída.', 'Cadastre o imóvel e o número de família sem dados pessoais ou clínicos.', 'Registre a visita com campos estruturados e observação curta.', 'Comunique ao gerente erros de limite ou reatribuição.'] },
  { title: 'Recepção', items: ['Pesquise o imóvel pelo endereço ou número de família.', 'Confirme a situação cadastral antes do atendimento.', 'Não altere território ou atribuição de agente.', 'Não registre informações pessoais ou clínicas nas observações.'] },
  { title: 'Médico', items: ['Consulte o imóvel e o histórico operacional de visitas.', 'Use as informações apenas no trabalho da UBS.', 'Não inclua diagnóstico, prontuário ou informação clínica.', 'Informe inconsistências à gerência para correção auditada.'] },
  { title: 'Gerente', items: ['Revise cobertura, áreas sem agente e cadastros pendentes.', 'Aprove importações e mudanças territoriais somente após conferir o impacto.', 'Gerencie contas, senhas temporárias e transferências da própria UBS.', 'Acompanhe backups, alertas e registros de auditoria.'] },
]

const runbooks = [
  { title: 'Suporte', steps: ['Registrar horário, usuário, tela e mensagem apresentada.', 'Verificar monitoramento antes de reiniciar qualquer serviço.', 'Preservar evidências e encaminhar ao responsável definido abaixo.'] },
  { title: 'Incidente', steps: ['Suspender a operação afetada sem apagar registros.', 'Avisar gerente e administrador global.', 'Registrar alcance, horário, ações e resultado na auditoria.'] },
  { title: 'Restauração', steps: ['Interromper escritas e gerar uma cópia de segurança do estado atual.', 'Restaurar primeiro em ambiente isolado e validar acesso, contagens e geometrias.', 'Somente então autorizar retorno, documentando responsável e horário.'] },
]

export function PilotReleaseWorkspace({ session, global = false }: { session: Session; global?: boolean }) {
  const [units, setUnits] = useState<Unit[]>(session.healthUnit ? [session.healthUnit] : [])
  const [unitId, setUnitId] = useState(session.healthUnit?.id ?? '')
  const [report, setReport] = useState<Report | null>(null)
  const [decision, setDecision] = useState<'go' | 'no-go'>('go')
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
      const response = await fetch(`/api/pilot-release/readiness?${new URLSearchParams({ healthUnitId: unitId })}`, { credentials: 'include' })
      if (!response.ok) throw new Error('Não foi possível avaliar a liberação desta UBS.')
      setReport(await response.json() as Report)
      setError(null)
    } catch (caught) {
      setError(messageOf(caught))
    } finally {
      setLoading(false)
    }
  }, [unitId])

  useEffect(() => { void load() }, [load])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!report) return
    setBusy(true); setError(null); setNotice(null)
    const data = new FormData(event.currentTarget)
    try {
      const token = await csrfToken()
      const response = await fetch('/api/pilot-release/decision', {
        method: 'POST', credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'X-STU-CSRF': token },
        body: JSON.stringify({
          healthUnitId: unitId, decision,
          humanAccessibilityAccepted: data.get('humanAccessibilityAccepted') === 'on',
          trainingCompleted: data.get('trainingCompleted') === 'on',
          userAcceptanceCompleted: data.get('userAcceptanceCompleted') === 'on',
          noCriticalHighDefects: data.get('noCriticalHighDefects') === 'on',
          supportOwner: data.get('supportOwner'), incidentOwner: data.get('incidentOwner'), rollbackOwner: data.get('rollbackOwner'), note: data.get('note'),
        }),
      })
      await ensureOk(response)
      setNotice(decision === 'go' ? 'Liberação do piloto registrada na auditoria.' : 'Decisão de não liberar registrada na auditoria.')
      await load()
    } catch (caught) {
      setError(messageOf(caught))
    } finally {
      setBusy(false)
    }
  }

  return <section className="release-workspace">
    <header className="release-heading"><div><span>Decisão controlada</span><h2>Liberação do piloto</h2><p>Consolide as evidências, responsáveis e confirmações humanas antes do início operacional.</p></div><div>{units.length > 1 && <label>UBS<select onChange={event => setUnitId(event.target.value)} value={unitId}>{units.map(unit => <option key={unit.id} value={unit.id}>{unit.code} — {unit.name}</option>)}</select></label>}<button disabled={loading || !unitId} onClick={() => void load()} type="button">Atualizar</button><button onClick={() => window.print()} type="button">Imprimir guia</button></div></header>
    {error && <div className="release-message release-message--error" role="alert">{error}</div>}
    {notice && <div className="release-message" role="status">{notice}</div>}
    {loading && !report ? <p className="release-loading" role="status">Conferindo o portão final…</p> : report && <>
      <section className={`release-decision ${report.released ? 'released' : report.readyForDecision ? 'ready' : 'blocked'}`} aria-label="Situação da liberação"><div aria-hidden="true">{report.released ? '✓' : report.readyForDecision ? '→' : '!'}</div><div><span>{report.healthUnit.code}</span><h3>{report.released ? 'Piloto formalmente liberado' : report.readyForDecision ? 'Controles automáticos aprovados' : 'Liberação bloqueada por pendências'}</h3><p>{report.released ? 'A decisão corresponde ao estado atual das evidências.' : report.readyForDecision ? 'Realize as confirmações humanas e registre a decisão.' : 'Corrija os itens abaixo; ainda é possível registrar uma decisão de não liberar.'}</p></div><small>{new Date(report.generatedAtUtc).toLocaleString('pt-BR')}</small></section>

      <div className="release-layout"><section className="release-checks"><header><div><span>Portão automático</span><h3>Evidências obrigatórias</h3></div><strong>{report.checks.filter(item => item.status === 'passed').length}/{report.checks.length}</strong></header>{report.checks.map(item => <article className={item.status === 'passed' ? 'passed' : 'blocked'} key={item.id}><i aria-hidden="true">{item.status === 'passed' ? '✓' : '!'}</i><div><strong>{item.label}</strong><p>{item.detail}</p></div><span>{item.status === 'passed' ? 'Aprovado' : 'Bloqueado'}</span></article>)}</section>
        <aside className="release-latest"><span>Registro auditado</span><h3>Última decisão</h3>{report.latestDecision ? <><strong>{report.latestDecision.decision === 'go' ? 'Liberar' : 'Não liberar'}</strong><p>{report.latestDecision.current ? 'Válida para as evidências atuais.' : 'Desatualizada após mudanças no sistema.'}</p><small>{new Date(report.latestDecision.recordedAtUtc).toLocaleString('pt-BR')} por {report.latestDecision.recordedBy}</small>{report.latestDecision.note && <blockquote>{report.latestDecision.note}</blockquote>}</> : <p>Nenhuma decisão registrada.</p>}<h4>Documentos técnicos</h4><ul>{report.evidence.map(item => <li key={item.document}>{item.label}<small>{item.document}</small></li>)}</ul></aside>
      </div>

      <section className="release-guides"><header><span>Ajuda por função</span><h3>Guia rápido para o início</h3></header><div>{guides.map(guide => <details key={guide.title}><summary>{guide.title}</summary><ol>{guide.items.map(item => <li key={item}>{item}</li>)}</ol></details>)}</div></section>
      <section className="release-guides"><header><span>Continuidade</span><h3>Procedimentos operacionais</h3></header><div>{runbooks.map(runbook => <details key={runbook.title}><summary>{runbook.title}</summary><ol>{runbook.steps.map(item => <li key={item}>{item}</li>)}</ol></details>)}</div></section>

      <form className="release-form" onSubmit={submit}><header><span>Decisão formal</span><h3>Registrar go/no-go</h3><p>O registro não importa, edita ou ativa cadastros. Ele preserva somente a decisão e suas evidências na auditoria.</p></header><fieldset><legend>Decisão</legend><label><input checked={decision === 'go'} name="decision" onChange={() => setDecision('go')} type="radio" /> Liberar piloto</label><label><input checked={decision === 'no-go'} name="decision" onChange={() => setDecision('no-go')} type="radio" /> Não liberar</label></fieldset>
        {decision === 'go' && <fieldset className="release-confirmations"><legend>Confirmações humanas obrigatórias</legend><label><input name="humanAccessibilityAccepted" required type="checkbox" /> Aceitação assistida de acessibilidade realizada</label><label><input name="trainingCompleted" required type="checkbox" /> Treinamento por função concluído</label><label><input name="userAcceptanceCompleted" required type="checkbox" /> Aceitação dos usuários concluída</label><label><input name="noCriticalHighDefects" required type="checkbox" /> Nenhum defeito crítico ou alto permanece aberto</label></fieldset>}
        <div className="release-owners"><label>Responsável pelo suporte<input maxLength={120} name="supportOwner" required={decision === 'go'} /></label><label>Responsável por incidentes<input maxLength={120} name="incidentOwner" required={decision === 'go'} /></label><label>Responsável pela restauração<input maxLength={120} name="rollbackOwner" required={decision === 'go'} /></label></div><label>Justificativa ou observação<textarea maxLength={500} minLength={decision === 'no-go' ? 10 : undefined} name="note" required={decision === 'no-go'} rows={4} /></label><button className={decision === 'go' ? 'release-go' : 'release-hold'} disabled={busy || (decision === 'go' && !report.readyForDecision)} type="submit">{busy ? 'Registrando…' : decision === 'go' ? 'Registrar liberação' : 'Registrar não liberação'}</button>{decision === 'go' && !report.readyForDecision && <small>A liberação permanecerá indisponível enquanto houver item automático bloqueado.</small>}
      </form>
    </>}
  </section>
}

function messageOf(caught: unknown) { return caught instanceof Error ? caught.message : 'Não foi possível concluir a operação.' }
async function csrfToken() { const response = await fetch('/api/auth/csrf', { credentials: 'include' }); if (!response.ok) throw new Error('Sua sessão precisa ser renovada.'); return (await response.json() as { token: string }).token }
async function ensureOk(response: Response) { if (response.ok) return; const problem = await response.json().catch(() => ({})) as { title?: string; detail?: string; errors?: Record<string, string[]> }; throw new Error(problem.detail ?? Object.values(problem.errors ?? {})[0]?.[0] ?? problem.title ?? 'Não foi possível concluir a operação.') }
