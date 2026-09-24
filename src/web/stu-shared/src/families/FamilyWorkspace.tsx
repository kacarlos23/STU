import { useCallback, useEffect, useId, useRef, useState, type FormEvent } from 'react'
import type { Session } from '../auth/types'
import { useAccessibleDialog } from '../accessibility/useAccessibleDialog'
import { useUiActions, useUnsavedChanges } from '../interaction/InteractionProvider'
import { CloseIcon } from '../components/CloseIcon'
import { PropertyEditor, VisitEditor, mutate, type ReferenceData, type Visit } from '../properties/PropertyWorkspace'
import './families.css'

type Property = { id: string; street: string; houseNumber: string; situation?: string; concurrencyToken: string; startedAtUtc?: string }
type Family = {
  id: string; number: string; responsibleName: string; healthUnitId: string; concurrencyToken: string
  archivedAtUtc: string | null; state: 'linked' | 'unlinked' | 'archived'; coverageStatus: string; lastVisitAtUtc: string | null
  currentProperty: Property | null
}
type Link = { id: string; propertyId: string; street: string; houseNumber: string; startedAtUtc: string; endedAtUtc: string | null }
type History = { links: Link[]; versions: { id: string; versionNumber: number; changeKind: string; changedAtUtc: string; number: string; responsibleName: string }[] }
type Page<T> = { items: T[]; total: number; page: number; pageSize: number }
const states = { linked: 'Com imóvel', unlinked: 'Sem imóvel', archived: 'Arquivada' }
const coverage: Record<string, string> = { covered: 'Visita em dia', overdue: 'Visita fora do prazo', neverVisited: 'Ainda sem visita', notConfigured: 'Prazo de cobertura não definido', noProperty: 'Sem imóvel', archived: 'Arquivada' }
const changes: Record<string, string> = { Create: 'Cadastro', Update: 'Edição', Archive: 'Arquivamento', Restore: 'Reativação', Link: 'Vínculo inicial', Move: 'Mudança de imóvel', Unlink: 'Encerramento de vínculo', Import: 'Importação' }
const outcomes: Record<string, string> = { Completed: 'Concluída', NoAnswer: 'Sem resposta', Refused: 'Recusada', AccessBlocked: 'Acesso impedido', Rescheduled: 'Reagendada' }

async function read<T>(path: string): Promise<T> {
  const response = await fetch(path, { credentials: 'include', cache: 'no-store' })
  if (!response.ok) throw new Error('Não foi possível carregar os dados autorizados. Atualize a consulta.')
  return response.json() as Promise<T>
}
function message(error: unknown) { return error instanceof Error ? error.message : 'Não foi possível concluir a operação.' }
function date(value: string) { return new Date(value).toLocaleString('pt-BR') }

export function FamilyWorkspace({ session, searchRequest, createRequest = 0, onOpenProperty }: {
  session: Session; searchRequest?: { value: string; nonce: number }; createRequest?: number; onOpenProperty?: (id: string) => void
}) {
  const { confirm, guard } = useUiActions()
  const has = (permission: string) => session.permissions.includes('*') || session.permissions.includes(permission)
  const canManage = has('families.manage')
  const canViewVisits = has('visits.view')
  const canManageVisits = has('visits.manage')
  const canViewProperties = has('properties.view')
  const canCreateProperty = has('properties.manage')
  const global = session.roles.some(r => r.name === 'GlobalAdministrator')
  const [units, setUnits] = useState(session.healthUnit ? [session.healthUnit] : [])
  const [unitId, setUnitId] = useState(session.healthUnit?.id ?? '')
  const [query, setQuery] = useState(searchRequest?.value ?? '')
  const [state, setState] = useState('active')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<Page<Family>>({ items: [], total: 0, page: 1, pageSize: 25 })
  const [selectedId, setSelectedId] = useState('')
  const [selected, setSelected] = useState<Family | null>(null)
  const [history, setHistory] = useState<History>({ links: [], versions: [] })
  const [visits, setVisits] = useState<Visit[]>([])
  const [editor, setEditor] = useState<Family | 'new' | null>(null)
  const [linking, setLinking] = useState(false)
  const [newProperty, setNewProperty] = useState(false)
  const [reference, setReference] = useState<ReferenceData | null>(null)
  const [visitEditor, setVisitEditor] = useState<Visit | 'new' | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const detailHeading = useRef<HTMLHeadingElement>(null)
  const handledCreate = useRef(0)

  useEffect(() => {
    if (!global) return
    let active = true
    void read<typeof units>('/api/admin/health-units').then(items => { if (active) { setUnits(items); setUnitId(current => current || items[0]?.id || '') } }).catch(e => { if (active) setError(message(e)) })
    return () => { active = false }
  }, [global])
  useEffect(() => { if (searchRequest?.nonce) { setQuery(searchRequest.value); setState('active'); setPage(1) } }, [searchRequest])
  useEffect(() => {
    if (createRequest > handledCreate.current && canManage && unitId) { handledCreate.current = createRequest; setEditor('new') }
  }, [createRequest, canManage, unitId])

  useEffect(() => {
    let active = true
    if (!unitId) return
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      const params = new URLSearchParams({ healthUnitId: unitId, query, state, page: String(page), pageSize: '25' })
      void read<Page<Family>>(`/api/families?${params}`).then(data => {
        if (!active) return
        setResult(data)
        if (page > Math.max(1, Math.ceil(data.total / 25))) setPage(Math.max(1, Math.ceil(data.total / 25)))
      }).catch(e => { if (active) { setResult({ items: [], total: 0, page, pageSize: 25 }); setError(message(e)) } }).finally(() => { if (active) setLoading(false) })
    }, 200)
    return () => { active = false; window.clearTimeout(timer) }
  }, [unitId, query, state, page, refresh])

  const loadDetail = useCallback(async (id: string) => {
    const [family, record, familyVisits] = await Promise.all([
      read<Family>(`/api/families/${id}`), read<History>(`/api/families/${id}/history`),
      canViewVisits ? read<Visit[]>(`/api/families/${id}/visits`) : Promise.resolve([]),
    ])
    return { family, record, familyVisits }
  }, [canViewVisits])
  useEffect(() => {
    let active = true
    setSelected(null); setHistory({ links: [], versions: [] }); setVisits([])
    if (!selectedId) return
    void loadDetail(selectedId).then(data => {
      if (!active) return
      setSelected(data.family); setHistory(data.record); setVisits(data.familyVisits)
      window.requestAnimationFrame(() => detailHeading.current?.focus())
    }).catch(e => { if (active) setError(message(e)) })
    return () => { active = false }
  }, [selectedId, refresh, loadDetail])

  function reload() { setRefresh(value => value + 1) }
  async function run(action: () => Promise<void>) {
    if (busy) return
    setBusy(true); setError(null); setNotice(null)
    try { await action() } catch (e) { setError(message(e)); reload() } finally { setBusy(false) }
  }
  async function changeArchive() {
    if (!selected) return
    const archived = Boolean(selected.archivedAtUtc)
    if (!await confirm({ title: `${archived ? 'Reativar' : 'Arquivar'} família?`, message: `Família ${selected.number}. O histórico de imóveis e visitas será preservado.`, confirmLabel: archived ? 'Reativar' : 'Arquivar' })) return
    await run(async () => { await mutate(`/api/families/${selected.id}/${archived ? 'restore' : 'archive'}`, 'POST', { expectedVersion: selected.concurrencyToken }); reload(); setNotice(archived ? 'Família reativada.' : 'Família arquivada.') })
  }
  async function unlink() {
    if (!selected) return
    if (!await confirm({ title: 'Encerrar vínculo com o imóvel?', message: `A família ${selected.number} ficará sem imóvel e fora do cálculo territorial de cobertura. O imóvel continuará disponível; as visitas e o histórico serão preservados.`, confirmLabel: 'Encerrar vínculo' })) return
    await run(async () => { await mutate(`/api/families/${selected.id}/unlink`, 'POST', { expectedVersion: selected.concurrencyToken }); reload(); setNotice('Vínculo encerrado. A família está sem imóvel.') })
  }
  async function link(property: Property) {
    if (!selected) return
    if (selected.currentProperty && !await confirm({ title: 'Confirmar mudança de imóvel?', message: `A família ${selected.number} passará para ${property.street}, nº ${property.houseNumber}. O imóvel anterior ficará disponível. As visitas antigas manterão o endereço em que ocorreram.`, confirmLabel: 'Confirmar mudança' })) return
    await run(async () => {
      await mutate(`/api/families/${selected.id}/property`, 'POST', { propertyId: property.id, expectedVersion: selected.concurrencyToken, expectedPropertyVersion: property.concurrencyToken })
      setLinking(false); reload(); setNotice('Vínculo residencial salvo. O acompanhamento continua com a família.')
    })
  }
  async function openPropertyEditor() {
    await run(async () => {
      const data = await read<ReferenceData>(`/api/properties/reference-data?healthUnitId=${encodeURIComponent(unitId)}`)
      if (!data.microregions.length) throw new Error('Cadastre uma microrregião ativa antes de incluir um imóvel.')
      setReference(data); setLinking(false); setNewProperty(true)
    })
  }
  async function propertySaved(id?: string) {
    setNewProperty(false); setLinking(true)
    if (!id) return
    const property = await read<Property>(`/api/properties/${id}`)
    await link(property)
  }
  async function toggleVisit(visit: Visit) {
    if (!await confirm({ title: `${visit.archivedAtUtc ? 'Reativar' : 'Arquivar'} visita?`, message: 'A cobertura será recalculada pela última visita não arquivada da família. O endereço histórico será preservado.', confirmLabel: visit.archivedAtUtc ? 'Reativar' : 'Arquivar' })) return
    await run(async () => { await mutate(`/api/families/${visit.familyId}/visits/${visit.id}/${visit.archivedAtUtc ? 'restore' : 'archive'}`, 'POST', { expectedVersion: visit.concurrencyToken }); reload() })
  }

  return <section className="families-workspace" aria-label="Famílias">
    <header className="families-heading"><div><span className="section-kicker">Acompanhamento da UBS</span><h2>Famílias</h2><p>Encontre a família pelo número ou responsável e acompanhe seu vínculo residencial.</p></div>{canManage && <button className="property-primary" disabled={!unitId || busy} onClick={() => setEditor('new')} type="button">Cadastrar família</button>}</header>
    {global && <label className="families-unit">UBS<select value={unitId} onChange={e => { setUnitId(e.target.value); setSelectedId(''); setPage(1) }}>{units.map(unit => <option key={unit.id} value={unit.id}>{unit.name}</option>)}</select></label>}
    <div className="families-filters"><label>Buscar família<input type="search" placeholder="Número ou nome do responsável" value={query} onChange={e => { setQuery(e.target.value); setPage(1) }} /></label><label>Situação<select value={state} onChange={e => { setState(e.target.value); setPage(1) }}><option value="active">Ativas</option><option value="linked">Com imóvel</option><option value="unlinked">Sem imóvel</option><option value="archived">Arquivadas</option><option value="all">Todas</option></select></label><button disabled={loading || busy} onClick={reload} type="button">Atualizar</button></div>
    {error && <p className="property-message property-error" role="alert">{error}</p>}{notice && <p className="property-message" role="status">{notice}</p>}
    <div className="families-layout">
      <section className="families-list" aria-label="Resultado da busca" aria-busy={loading}><div className="families-list-heading"><strong>{result.total} família{result.total === 1 ? '' : 's'}</strong>{loading && <span role="status">Carregando…</span>}</div>
        {!loading && !result.items.length && <div className="families-empty"><h3>Nenhuma família encontrada</h3><p>Revise a busca ou cadastre uma família com número e responsável.</p></div>}
        {result.items.map(family => <button className={`family-row${selectedId === family.id ? ' family-row--selected' : ''}`} key={family.id} type="button" aria-pressed={selectedId === family.id} onClick={() => void guard(() => setSelectedId(family.id))}><span className="family-number">Família {family.number}</span><strong>{family.responsibleName}</strong><span className={`family-state family-state--${family.state}`}>{states[family.state]}</span><small>{family.currentProperty ? `${family.currentProperty.street}, nº ${family.currentProperty.houseNumber}` : 'Vínculo residencial não definido'}</small></button>)}
        <nav className="families-pagination" aria-label="Paginação de famílias"><button disabled={page === 1 || loading} onClick={() => setPage(p => p - 1)} type="button">Anterior</button><span>Página {page} de {Math.max(1, Math.ceil(result.total / 25))}</span><button disabled={page * 25 >= result.total || loading} onClick={() => setPage(p => p + 1)} type="button">Próxima</button></nav>
      </section>
      <article className="family-detail" aria-label="Ficha da família">
        {!selected ? <div className="families-empty"><h3>{selectedId ? 'Carregando ficha…' : 'Selecione uma família'}</h3><p>A ficha reúne o imóvel atual, os vínculos anteriores e as visitas da família.</p></div> : <>
          <header><span className={`family-state family-state--${selected.state}`}>{states[selected.state]}</span><h2 ref={detailHeading} tabIndex={-1}>Família {selected.number}</h2><p className="family-responsible">{selected.responsibleName}</p></header>
          {canManage && <div className="family-actions">{!selected.archivedAtUtc && <button disabled={busy} onClick={() => setEditor(selected)} type="button">Editar família</button>}<button disabled={busy || Boolean(selected.currentProperty)} onClick={() => void changeArchive()} type="button">{selected.archivedAtUtc ? 'Reativar família' : 'Arquivar família'}</button></div>}
          {canManage && selected.currentProperty && <p className="family-hint">Para arquivar, encerre primeiro o vínculo com o imóvel.</p>}
          <section className="family-residence"><h3>Imóvel atual</h3>{selected.currentProperty ? <><strong>{selected.currentProperty.street}, nº {selected.currentProperty.houseNumber}</strong><p>Vinculada desde {date(selected.currentProperty.startedAtUtc!)}</p>{canViewProperties && onOpenProperty && <button type="button" onClick={() => onOpenProperty(selected.currentProperty!.id)}>Abrir ficha do imóvel</button>}</> : <p>Esta família ainda não possui imóvel e está fora do cálculo territorial de cobertura.</p>}
            {canManage && !selected.archivedAtUtc && <div className="family-actions">{canViewProperties && <button className="property-primary" disabled={busy} type="button" onClick={() => setLinking(true)}>{selected.currentProperty ? 'Alterar imóvel' : 'Vincular imóvel'}</button>}{selected.currentProperty && <button disabled={busy} type="button" onClick={() => void unlink()}>Encerrar vínculo</button>}</div>}
          </section>
          <section className="family-coverage"><h3>Acompanhamento</h3><strong>{coverage[selected.coverageStatus] ?? selected.coverageStatus}</strong><p>{selected.lastVisitAtUtc ? `Última visita: ${date(selected.lastVisitAtUtc)}` : 'Nenhuma visita ativa registrada.'}</p>{canManageVisits && selected.currentProperty && !selected.archivedAtUtc && <button className="property-primary" disabled={busy} type="button" onClick={() => setVisitEditor('new')}>Registrar visita</button>}</section>
          <section><h3>Histórico de imóveis</h3>{!history.links.length && <p>Nenhum vínculo residencial registrado.</p>}<ol className="family-timeline">{history.links.map(item => <li key={item.id}><strong>{item.street}, nº {item.houseNumber}</strong><span>{date(item.startedAtUtc)} — {item.endedAtUtc ? date(item.endedAtUtc) : 'Atual'}</span></li>)}</ol></section>
          {canViewVisits && <section><h3>Visitas da família</h3>{!visits.length && <p>Nenhuma visita registrada.</p>}<ol className="family-timeline">{visits.map(visit => <li key={visit.id}><strong>{date(visit.visitedAtUtc)} · {visit.archivedAtUtc ? 'Arquivada' : outcomes[visit.outcome] ?? visit.outcome}</strong><span>{visit.street}, nº {visit.houseNumber} · {visit.agentName}</span>{visit.note && <p>{visit.note}</p>}{canManageVisits && <div className="family-actions"><button disabled={busy} onClick={() => setVisitEditor(visit)} type="button">Editar visita</button><button disabled={busy} onClick={() => void toggleVisit(visit)} type="button">{visit.archivedAtUtc ? 'Reativar visita' : 'Arquivar visita'}</button></div>}</li>)}</ol></section>}
          <details><summary>Alterações do cadastro</summary><ol className="family-timeline">{history.versions.map(v => <li key={v.id}><strong>{changes[v.changeKind] ?? v.changeKind} · versão {v.versionNumber}</strong><span>{date(v.changedAtUtc)} · {v.number} · {v.responsibleName}</span></li>)}</ol></details>
        </>}
      </article>
    </div>
    {editor && <FamilyEditor item={editor} unitId={unitId} onClose={() => setEditor(null)} onSaved={id => { setEditor(null); setSelectedId(id); reload(); setNotice('Cadastro familiar salvo.') }} />}
    {linking && selected && <LinkDialog actionError={error} unitId={unitId} family={selected} busy={busy} canCreate={canCreateProperty} onChoose={link} onCreate={openPropertyEditor} onClose={() => setLinking(false)} />}
    {newProperty && reference && <PropertyEditor item="new" reference={reference} onCancel={() => { setNewProperty(false); setLinking(true) }} onSaved={propertySaved} setError={setError} />}
    {visitEditor && selected && <VisitEditor item={visitEditor} property={{ id: visitEditor === 'new' ? selected.currentProperty!.id : visitEditor.propertyId, houseNumber: visitEditor === 'new' ? selected.currentProperty!.houseNumber : visitEditor.houseNumber ?? '', familyNumber: selected.number, familyId: selected.id, familyConcurrencyToken: selected.concurrencyToken, situation: selected.currentProperty?.situation ?? 'Occupied' }} onCancel={() => setVisitEditor(null)} onSaved={async () => { setVisitEditor(null); reload(); setNotice('Visita salva. O imóvel da visita foi preservado.') }} setError={setError} />}
  </section>
}

function FamilyEditor({ item, unitId, onClose, onSaved }: { item: Family | 'new'; unitId: string; onClose: () => void; onSaved: (id: string) => void }) {
  const { guard } = useUiActions()
  const editing = item === 'new' ? null : item
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const title = useId()
  useUnsavedChanges(dirty)
  function close() { if (!busy) void guard(onClose) }
  const dialog = useAccessibleDialog<HTMLFormElement>(true, close)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return
    const fields = new FormData(event.currentTarget); setBusy(true); setError(null)
    try {
      const saved = await mutate(editing ? `/api/families/${editing.id}` : '/api/families', editing ? 'PUT' : 'POST', { number: String(fields.get('number')), responsibleName: String(fields.get('responsibleName')), healthUnitId: unitId, expectedVersion: editing?.concurrencyToken }) as { id?: string }
      setDirty(false); onSaved(saved.id ?? editing!.id)
    } catch (e) { setError(message(e)) } finally { setBusy(false) }
  }
  return <div className="property-modal-backdrop"><form className="property-modal family-editor" aria-labelledby={title} aria-modal="true" role="dialog" ref={dialog} tabIndex={-1} onChangeCapture={() => setDirty(true)} onSubmit={e => void submit(e)}><header><h3 id={title}>{editing ? 'Editar família' : 'Cadastrar família'}</h3><button className="stu-close-button" aria-label="Fechar cadastro familiar" disabled={busy} onClick={close} type="button"><CloseIcon /></button></header><label>Número da família<input name="number" defaultValue={editing?.number} maxLength={32} required autoComplete="off" /></label><label>Nome do responsável<input name="responsibleName" defaultValue={editing?.responsibleName} maxLength={120} required autoComplete="off" /></label><p className="family-hint">O imóvel será vinculado pela ficha após salvar a família.</p>{error && <p role="alert" className="property-error">{error}</p>}<footer><button disabled={busy} type="button" onClick={close}>Cancelar</button><button className="property-primary" disabled={busy} type="submit">{busy ? 'Salvando…' : 'Salvar família'}</button></footer></form></div>
}

function LinkDialog({ actionError, unitId, family, busy, canCreate, onChoose, onCreate, onClose }: { actionError: string | null; unitId: string; family: Family; busy: boolean; canCreate: boolean; onChoose: (property: Property) => Promise<void>; onCreate: () => Promise<void>; onClose: () => void }) {
  const title = useId()
  const dialog = useAccessibleDialog<HTMLDivElement>(true, () => { if (!busy) onClose() })
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [items, setItems] = useState<Page<Property>>({ items: [], total: 0, page: 1, pageSize: 25 })
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  useEffect(() => {
    let active = true
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      const params = new URLSearchParams({ healthUnitId: unitId, query, page: String(page), pageSize: '25' })
      void read<Page<Property>>(`/api/families/available-properties?${params}`).then(data => { if (active) setItems(data) }).catch(e => { if (active) setError(message(e)) }).finally(() => { if (active) setLoading(false) })
    }, 200)
    return () => { active = false; window.clearTimeout(timer) }
  }, [unitId, query, page])
  return <div className="property-modal-backdrop"><div className="property-modal family-link-dialog" ref={dialog} role="dialog" aria-modal="true" aria-labelledby={title} tabIndex={-1}><header><div><small>Família {family.number}</small><h3 id={title}>{family.currentProperty ? 'Alterar imóvel' : 'Vincular imóvel'}</h3></div><button className="stu-close-button" aria-label="Fechar seleção de imóvel" disabled={busy} onClick={onClose} type="button"><CloseIcon /></button></header><p>Escolha um imóvel ativo e sem família na UBS.</p>{actionError && <p role="alert" className="property-error">{actionError}</p>}<label>Buscar imóvel disponível<input type="search" value={query} onChange={e => { setQuery(e.target.value); setPage(1) }} placeholder="Logradouro ou número" /></label>{error && <p role="alert" className="property-error">{error}</p>}{loading ? <p role="status">Carregando imóveis…</p> : <><p>{items.total} imóvel(is) disponível(is)</p><div className="family-property-options">{items.items.map(property => <button key={property.id} disabled={busy} type="button" onClick={() => void onChoose(property)}><strong>{property.street}, nº {property.houseNumber}</strong><span>Selecionar imóvel</span></button>)}</div><nav className="families-pagination" aria-label="Paginação de imóveis disponíveis"><button disabled={page === 1 || busy} onClick={() => setPage(p => p - 1)} type="button">Anterior</button><span>{page} de {Math.max(1, Math.ceil(items.total / 25))}</span><button disabled={page * 25 >= items.total || busy} onClick={() => setPage(p => p + 1)} type="button">Próxima</button></nav></>}<footer><button disabled={busy} onClick={onClose} type="button">Cancelar</button>{canCreate && <button className="property-primary" disabled={busy} onClick={() => void onCreate()} type="button">Cadastrar novo imóvel</button>}</footer></div></div>
}
