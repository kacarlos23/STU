import { createContext, useContext, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useAccessibleDialog } from '../../../../stu-shared/src/accessibility/useAccessibleDialog'
import { CloseIcon } from '../../../../stu-shared/src/components/CloseIcon'
import { StuIcon } from '../dashboard/StuIcon'

import type { Address, Agent, Assignment, Data, Editor, Family, Neighborhood, Path, Region } from './types'
import './registration.css'
import { useUnsavedChanges } from '../../../../stu-shared/src/interaction/InteractionProvider'

export type FlowPermissions = { regions: boolean; agents: boolean; createAgents: boolean; addresses: boolean; families: boolean }
const allPermissions: FlowPermissions = { regions: true, agents: true, createAgents: true, addresses: true, families: true }
export type PersistChange = (previous: Data, next: Data, path?: Path) => Promise<{ data: Data; path?: Path }>
type FlowProps = { initialData: Data; persist?: PersistChange; demo?: boolean; embedded?: boolean; healthUnitName?: string; permissions?: FlowPermissions; onViewFamily?: (id: string) => void }
const ActionContext = createContext({ busy: false, error: '' })

function includes(value: string, query: string) {
  const normalize = (text: string) => text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('pt-BR')
  return normalize(value).includes(normalize(query.trim()))
}
function addressText(address: Address) { return `${address.street}, ${address.noNumber ? 's/n' : address.houseNumber}` }
function initials(name: string) { return name.split(' ').filter(Boolean).map(part => part[0]).slice(0, 2).join('') }
function id() { return crypto.randomUUID() }

export function RegistrationFlow({ initialData, persist, demo = false, embedded = false, healthUnitName = 'UBS', permissions = allPermissions, onViewFamily }: FlowProps) {
  const [data, setData] = useState(() => initialData)
  const [path, setPath] = useState<Path>({})
  const [editor, setEditor] = useState<Editor>(null)
  const [query, setQuery] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState('')
  const heading = useRef<HTMLHeadingElement>(null)
  const region = data.regions.find(item => item.id === path.regionId)
  const assignment = data.assignments.find(item => item.id === path.assignmentId && !item.endedAtUtc)
  const agent = data.agents.find(item => item.id === assignment?.agentId)
  const address = data.addresses.find(item => item.id === path.addressId)
  const family = data.families.find(item => item.id === address?.familyId)
  const step = address ? 3 : agent ? 2 : region ? 1 : 0
  const scopedAssignments = data.assignments.filter(item => !item.endedAtUtc && (!region || item.regionId === region.id))
  const scopedAddresses = data.addresses.filter(item => assignment ? item.assignmentId === assignment.id : scopedAssignments.some(link => link.id === item.assignmentId))
  const unlinking = editor && typeof editor === 'object' && editor.kind === 'unlink-agent' ? scopedAssignments.find(item => item.id === editor.assignmentId) : undefined
  const unlinkingAgent = data.agents.find(item => item.id === unlinking?.agentId)
  const linkingFamily = editor && typeof editor === 'object' && editor.kind === 'link-family' ? data.addresses.find(item => item.id === editor.addressId) : undefined
  const headings = ['Microrregiões', `Agentes da ${region?.name ?? ''}`, `Endereços de ${agent?.name ?? ''}`, `Família ${family?.number ?? ''}`]
  const subtitles = [
    'Escolha uma microrregião para acessar os agentes de saúde.',
    'Escolha um agente para consultar os endereços sob sua responsabilidade.',
    'Consulte os endereços e acesse a família vinculada a cada um.',
    'Consulte a família e o endereço sob responsabilidade do agente.',
  ]

  useEffect(() => { heading.current?.focus() }, [path])
  function go(next: Path) { if (busy) return; setPath(next); setQuery(''); setNotice(''); setActionError('') }
  function done(next: Data, message: string, nextPath?: Path) {
    if (busy) return
    if (persist) {
      setBusy(true); setActionError('')
      void persist(data, next, nextPath).then(saved => {
        setData(saved.data); setEditor(null); setQuery(''); setNotice(message.replace(' nesta prévia', ''))
        if (saved.path ?? nextPath) setPath(saved.path ?? nextPath!)
      }).catch(error => setActionError(error instanceof Error ? error.message : 'Não foi possível salvar. Tente novamente.')).finally(() => setBusy(false))
      return
    }
    setData(next); setEditor(null); setQuery(''); setNotice(message)
    if (nextPath) setPath(nextPath)
  }
  function reset() { setData(initialData); go({}); setNotice('A prévia foi reiniciada com os dados de exemplo.') }
  function unlinkAgent(recipientId: string) {
    if (!unlinking || !unlinkingAgent || !region) return
    const addresses = data.addresses.filter(item => item.assignmentId === unlinking.id)
    const recipient = scopedAssignments.find(item => item.id === recipientId && item.id !== unlinking.id)
    if ((addresses.length || unlinking.addressCount) && !recipient) return
    const changedAtUtc = new Date().toISOString()
    done({
      ...data,
      assignments: data.assignments.map(item => item.id === unlinking.id ? { ...item, endedAtUtc: changedAtUtc } : item),
      addresses: data.addresses.map(item => item.assignmentId === unlinking.id && recipient ? { ...item, assignmentId: recipient.id } : item),
      assignmentTransfers: recipient ? [...(data.assignmentTransfers ?? []), {
        fromAssignmentId: unlinking.id, toAssignmentId: recipient.id, addressIds: addresses.map(item => item.id), changedAtUtc,
      }] : data.assignmentTransfers,
    }, `${unlinkingAgent.name} foi desvinculado da microrregião.${addresses.length ? ' Endereços e famílias preservados com o agente selecionado.' : ''}`, { regionId: region.id })
  }

  const ContentTag = embedded ? 'section' : 'main'
  return <ActionContext.Provider value={{ busy, error: actionError }}><div className={`registration-preview${embedded ? ' registration-embedded' : ''}`}>
    {persist && <RegistrationChangesGuard dirty={editor !== null && editor !== 'guide'} />}
    <a className="skip-link" href="#preview-content">Ir para o conteúdo principal</a>
    <div className={embedded ? 'registration-inner' : 'workspace'}>
      {!embedded && <aside className="sidebar">
        <div className="brand"><span className="brand-mark">STU</span><span className="brand-copy">Sistema Territorial<br />das UBS</span></div>
        <span className="preview-nav-label">ATENDIMENTO</span>
        <nav aria-label="Navegação principal">
          <button className="nav-item nav-item--active" aria-current="page" onClick={() => go({})}><span><StuIcon name="map" /></span>Microrregiões</button>
          <button className="nav-item" onClick={() => setEditor('guide')}><span><StuIcon name="flag" /></span>Guia do fluxo</button>
        </nav>
        <div className="preview-sidebar-note"><span className="preview-small-dot" /><strong>Uma etapa de cada vez.</strong><p>Do território à família,<br />um caminho de acesso.</p></div>
        <div className="sidebar-signature">Município de<br />Teixeira de Freitas<i /></div>
      </aside>}
      <ContentTag className={embedded ? 'registration-main' : 'dashboard'} id="preview-content">
        {!embedded && <header className="site-header preview-site-header">
          <div className="unit-context"><strong>UBS de demonstração</strong><small>Teixeira de Freitas · Bahia</small></div>
          <div className="preview-version"><span className="preview-small-dot" />Esboço interativo</div>
          <div className="preview-profile"><span className="avatar">GU</span><span><strong>Gestor da UBS</strong><small>Visão de demonstração</small></span></div>
        </header>}
        {demo && <div className="preview-banner"><span><strong>Prévia do novo funcionamento</strong><span className="preview-banner-detail"> · Dados fictícios. Os cadastros de teste ficam somente nesta aba.</span></span><button onClick={reset}>Reiniciar prévia <span aria-hidden="true">↺</span></button></div>}
        <div className="preview-content">
          <nav className="preview-breadcrumb" aria-label="Caminho de acesso">
            <button onClick={() => go({})}>Microrregiões</button>
            {region && <><span aria-hidden="true">/</span><button onClick={() => go({ regionId: region.id })}>{region.code}</button></>}
            {agent && <><span aria-hidden="true">/</span><button onClick={() => go({ regionId: region!.id, assignmentId: assignment!.id })}>{agent.name}</button></>}
            {address && <><span aria-hidden="true">/</span><span aria-current="page">Família {family!.number}</span></>}
          </nav>
          <div className="preview-stepper" aria-label="Etapas do fluxo">
            {['Microrregiões', 'Agentes de saúde', 'Endereços', 'Família'].map((label, index) => <div className={`preview-step ${index === step ? 'current' : index < step ? 'complete' : ''}`} key={label} aria-current={index === step ? 'step' : undefined}><span>{index < step ? <StuIcon name="check" size={15} /> : `0${index + 1}`}</span><strong>{label}</strong>{index < 3 && <i />}</div>)}
          </div>
          <header className="preview-heading">
            <div><span className="section-kicker">{step === 0 ? 'ORGANIZAÇÃO DO TERRITÓRIO' : `${region?.code} · ${region?.neighborhood}`}</span><h1 ref={heading} tabIndex={-1}>{headings[step]}</h1><p>{subtitles[step]}</p></div>
            <div className="preview-heading-actions">
              {step > 0 && <button className="preview-secondary" onClick={() => go(step === 3 ? { regionId: region!.id, assignmentId: assignment!.id } : step === 2 ? { regionId: region!.id } : {})}><span aria-hidden="true">←</span> {step === 3 ? 'Endereços' : step === 2 ? 'Agentes' : 'Microrregiões'}</button>}
              {step < 3 && (step === 0 ? permissions.regions : step === 1 ? permissions.agents : permissions.addresses && permissions.families) && <button className="primary-action" onClick={() => { setActionError(''); setEditor(step === 0 ? 'region' : step === 1 ? 'agent' : 'address') }}><StuIcon name="plus" size={18} />{step === 0 ? 'Nova microrregião' : step === 1 ? 'Vincular agente' : 'Cadastrar endereço'}</button>}
            </div>
          </header>
          {notice && <div className="preview-notice" role="status"><StuIcon name="check" size={17} />{notice}</div>}
          {data.temporaryCredentials && <section className="registration-credentials" aria-label="Acesso do novo agente"><strong>Acesso do novo agente</strong><p>Usuário: <b>{data.temporaryCredentials.userName}</b></p><p>Senha temporária: <code>{data.temporaryCredentials.password}</code></p><p>Entregue estes dados ao agente. A senha deverá ser alterada no primeiro acesso.</p><button className="preview-secondary" onClick={() => setData(current => ({ ...current, temporaryCredentials: undefined }))}>Ocultar senha</button></section>}
          {step < 3 && <>
            <div className="preview-metrics">
              <Metric icon="map" label={region ? 'Microrregião selecionada' : 'Microrregiões'} value={region ? region.code : data.regions.length} detail={region ? region.name : 'Áreas cadastradas na UBS'} />
              <Metric icon="users" label="Agentes de saúde" value={new Set(scopedAssignments.map(item => item.agentId)).size} detail={region ? 'Nesta microrregião' : 'Vinculados às microrregiões'} />
              <Metric icon="home" label="Endereços" value={scopedAddresses.length} detail={agent ? 'Sob responsabilidade do agente' : 'Registros para consulta'} />
              <Metric icon="family" label="Famílias vinculadas" value={new Set(scopedAddresses.filter(item => item.familyId).map(item => item.familyId)).size} detail="Uma família por endereço" />
            </div>
            <div className="preview-toolbar"><label className="preview-search"><StuIcon name="search" size={18} /><input type="search" value={query} onChange={event => setQuery(event.target.value)} aria-label={step === 0 ? 'Buscar microrregião' : step === 1 ? 'Buscar agente' : 'Buscar endereço ou família'} placeholder={step === 0 ? 'Buscar por nome, código ou bairro...' : step === 1 ? 'Buscar agente pelo nome...' : 'Buscar rua, número, família ou responsável...'} /></label><span>{step === 0 ? 'Selecione uma área para começar' : step === 1 ? `${region!.name} · ${scopedAssignments.length} ${scopedAssignments.length === 1 ? 'agente' : 'agentes'}` : 'Endereços cadastrados pelo agente'}</span></div>
          </>}

          {step === 0 && <div className="preview-region-grid">{data.regions.filter(item => includes(`${item.code} ${item.name} ${item.neighborhood}`, query)).map((item, index) => {
            const links = data.assignments.filter(link => link.regionId === item.id && !link.endedAtUtc)
            const records = data.addresses.filter(record => links.some(link => link.id === record.assignmentId))
            return <button className="preview-region-card" key={item.id} onClick={() => go({ regionId: item.id })} aria-label={`Abrir ${item.name}`}>
              <div className={`preview-region-art preview-region-art--${index % 3}`}><StuIcon name="map" size={48} /><span>{item.code}</span><svg aria-hidden="true" viewBox="0 0 300 100"><path d="M-20 100 70 55l45 20 55-60 70 30L310-5M-20 60 70 15l45 20 55 50 70-30 70 30M35-10l20 120M225-10l-40 120" /></svg></div>
              <div className="preview-region-body"><span className="section-kicker">{item.code}</span><h2>{item.name}</h2><p>{item.neighborhood}</p><div className="preview-region-counts"><span><StuIcon name="users" size={16} />{links.length} {links.length === 1 ? 'agente' : 'agentes'}</span><span><StuIcon name="home" size={16} />{records.length} {records.length === 1 ? 'endereço' : 'endereços'}</span></div><div className="preview-card-footer">Ver agentes de saúde <StuIcon name="arrow" size={18} /></div></div>
            </button>
          })}{!data.regions.some(item => includes(`${item.code} ${item.name} ${item.neighborhood}`, query)) && <Empty>Nenhuma microrregião encontrada. Ajuste a busca ou cadastre uma nova.</Empty>}</div>}

          {step === 1 && <section className="preview-panel" aria-label="Agentes da microrregião"><div className="preview-panel-heading"><div><h2>Equipe da microrregião</h2><p>Cada agente reúne os seus endereços e famílias.</p></div>{permissions.regions && <button className="preview-text-button" onClick={() => setEditor('edit-region')}>Editar microrregião</button>}</div><div className="preview-agent-list">{scopedAssignments.filter(link => includes(data.agents.find(item => item.id === link.agentId)!.name, query)).map(link => {
            const person = data.agents.find(item => item.id === link.agentId)!
            const count = data.addresses.filter(item => item.assignmentId === link.id).length
            return <div className="preview-agent-row" key={link.id}>
              <button className="preview-agent-open" onClick={() => go({ regionId: region!.id, assignmentId: link.id })} aria-label={`Consultar endereços de ${person.name}`}>
                <span className="preview-agent-avatar">{initials(person.name)}</span>
                <span className="preview-agent-name"><strong>{person.name}</strong><small>Agente comunitário de saúde</small></span>
                <span className="preview-pill">Vinculado</span>
                <span className="preview-agent-count"><strong>{count}</strong> {count === 1 ? 'endereço' : 'endereços'}</span>
                <span className="preview-row-link">Consultar endereços <StuIcon name="arrow" size={18} /></span>
              </button>
              {permissions.agents && <button className="preview-unlink-button" aria-label={`Desvincular ${person.name}`} onClick={() => { setActionError(''); setEditor({ kind: 'unlink-agent', assignmentId: link.id }) }}>Desvincular</button>}
            </div>
          })}{!scopedAssignments.some(link => includes(data.agents.find(item => item.id === link.agentId)!.name, query)) && <Empty>{query ? 'Nenhum agente encontrado. Ajuste a busca.' : 'Esta microrregião ainda não tem agentes. Use “Vincular agente” para continuar.'}</Empty>}</div></section>}

          {step === 2 && <section className="preview-panel"><div className="preview-panel-heading"><div><h2>Endereços e suas famílias</h2><p>Selecione um endereço para abrir a ficha da família.</p></div><span className="preview-info-chip"><StuIcon name="home" size={15} />Cadastro de consulta</span></div><div className="preview-table-wrap"><table className="preview-table"><thead><tr><th>Endereço</th><th>Família</th><th>Responsável pela família</th><th><span className="preview-sr-only">Acessar</span></th></tr></thead><tbody>{scopedAddresses.filter(item => {
            const linked = data.families.find(f => f.id === item.familyId)!
            return includes(`${addressText(item)} ${item.neighborhood} ${item.complement} ${linked?.number ?? ''} ${linked?.responsibleName ?? ''}`, query)
          }).map(item => {
            const linked = data.families.find(f => f.id === item.familyId)!
            return <tr key={item.id}><td><strong>{addressText(item)}</strong><small>{item.neighborhood}{item.complement ? ` · ${item.complement}` : ''}</small></td><td><span className="preview-family-number">{linked?.number ?? 'Sem família'}</span></td><td>{linked?.responsibleName ?? '—'}</td><td><button className="preview-text-button" disabled={!linked && !permissions.families} onClick={() => linked ? go({ ...path, addressId: item.id }) : setEditor({ kind: 'link-family', addressId: item.id })} aria-label={linked ? `Consultar família ${linked.number}` : `Vincular família ao endereço ${addressText(item)}`}>{linked ? 'Consultar família' : 'Vincular família'} <StuIcon name="arrow" size={16} /></button></td></tr>
          })}</tbody></table></div>{!scopedAddresses.some(item => {
            const linked = data.families.find(f => f.id === item.familyId)!
            return includes(`${addressText(item)} ${item.neighborhood} ${item.complement} ${linked?.number ?? ''} ${linked?.responsibleName ?? ''}`, query)
          }) && <Empty>{query ? 'Nenhum endereço ou família encontrado. Ajuste a busca.' : 'Nenhum endereço cadastrado para este agente. Use “Cadastrar endereço” para começar.'}</Empty>}<div className="preview-panel-footnote">Os endereços são preenchidos manualmente e não geram pontos no mapa.</div></section>}

          {step === 3 && address && family && <div className="preview-detail-grid"><section className="preview-panel preview-family-sheet"><div className="preview-panel-heading"><span className="preview-detail-icon"><StuIcon name="family" size={26} /></span><span className="preview-pill">Família vinculada</span></div><span className="section-kicker">FICHA DA FAMÍLIA</span><h2>Família {family.number}</h2><dl className="preview-facts"><div><dt>Número da família</dt><dd>{family.number}</dd></div><div><dt>Responsável pela família</dt><dd>{family.responsibleName}</dd></div></dl>{permissions.families && <button className="primary-action" onClick={() => setEditor('edit-family')}>Editar família <StuIcon name="arrow" size={17} /></button>}</section><section className="preview-panel preview-address-sheet"><div className="preview-panel-heading"><div><h2>Endereço da família</h2><p>{addressText(address)}</p></div>{permissions.addresses && <button className="preview-text-button" onClick={() => setEditor('edit-address')}>Editar endereço</button>}</div><dl className="preview-facts preview-facts-grid"><div><dt>Bairro</dt><dd>{address.neighborhood}</dd></div><div><dt>Complemento</dt><dd>{address.complement || 'Não informado'}</dd></div><div><dt>CEP</dt><dd>{address.postalCode || 'Não informado'}</dd></div><div><dt>Ponto de referência</dt><dd>{address.reference || 'Não informado'}</dd></div></dl><div className="preview-responsibility"><span className="preview-agent-avatar">{initials(agent!.name)}</span><div><small>AGENTE RESPONSÁVEL</small><strong>{agent!.name}</strong><span>{region!.code} · {region!.name}</span></div></div></section></div>}

          {step === 3 && family && onViewFamily && <button className="preview-secondary registration-history-button" onClick={() => onViewFamily(family.id)}>Visitas e histórico da família <StuIcon name="calendar" size={17} /></button>}
          <div className="preview-guidance"><span className="preview-detail-icon"><StuIcon name="flag" size={19} /></span><div><strong>{['Comece pela microrregião', 'A equipe vem antes dos endereços', 'Cadastre o endereço e vincule uma família', 'Tudo no mesmo caminho de consulta'][step]}</strong><p>{['Abra um cartão para visualizar os agentes daquela área. “Nova microrregião” inicia o cadastro de uma área.', '“Vincular agente” permite selecionar um agente da UBS ou cadastrar um novo. Depois, abra os endereços desse agente.', '“Cadastrar endereço” abre uma sequência: endereço → família → conferência. É possível criar a família ou vincular uma já cadastrada e sem endereço.', 'Edite os dados da família ou do endereço separadamente. Use o caminho no topo para voltar a qualquer etapa anterior.'][step]}</p></div><button className="preview-text-button" onClick={() => setEditor('guide')}>Ver o fluxo <StuIcon name="arrow" size={17} /></button></div>
          <footer className="preview-footer"><span>{demo ? 'STU · Proposta para revisão' : healthUnitName}</span><span>Microrregião → Agente → Endereço → Família</span></footer>
        </div>
      </ContentTag>
    </div>

    {editor === 'guide' && <Dialog title="Como o novo fluxo funciona" onClose={() => setEditor(null)}><div className="preview-guide-list">{[
      ['01', 'Microrregiões', 'Nova microrregião: informe código e nome e selecione um bairro já cadastrado na UBS. Salvar e continuar abre a equipe da área. Clicar em uma microrregião existente também abre a equipe.'],
      ['02', 'Agentes de saúde', 'Vincular agente: selecione um agente já cadastrado na UBS ou use Cadastrar novo. Salvar registra o vínculo e fecha o formulário, mantendo a lista aberta. Consultar endereços abre os cadastros do agente. Desvincular encerra somente o vínculo com esta microrregião; se houver endereços, escolha outro agente da área para recebê-los.'],
      ['03', 'Endereços', 'Cadastrar endereço: informe os dados manualmente, escolha uma família nova ou existente e confira antes de salvar. A busca localiza rua, número, família e responsável.'],
      ['04', 'Família', 'Consultar família abre a ficha com número, responsável e endereço. Editar família e Editar endereço atualizam cada cadastro. Os botões de voltar e o caminho no topo retornam às etapas anteriores.'],
    ].map(([number, title, description]) => <div key={number}><span>{number}</span><div><h3>{title}</h3><p>{description}</p></div></div>)}</div><div className="preview-inline-note">Uma família por endereço. Endereços sem geolocalização.{demo && ' Os dados desta prévia são fictícios e reiniciam ao atualizar a página.'}</div><div className="preview-dialog-actions"><button className="primary-action" onClick={() => setEditor(null)}>{demo ? 'Explorar a prévia' : 'Consultar microrregiões'} <StuIcon name="arrow" size={17} /></button></div></Dialog>}
    {(editor === 'region' || editor === 'edit-region') && <RegionEditor region={editor === 'edit-region' ? region : undefined} regions={data.regions} neighborhoods={data.neighborhoods} onClose={() => setEditor(null)} onSave={item => done({ ...data, regions: editor === 'region' ? [...data.regions, item] : data.regions.map(existing => existing.id === item.id ? item : existing) }, 'Microrregião salva nesta prévia.', { regionId: item.id })} />}
    {editor === 'agent' && region && <AgentEditor data={data} region={region} canCreate={permissions.createAgents} onClose={() => setEditor(null)} onSave={(person, link) => done({ ...data, agents: data.agents.some(item => item.id === person.id) ? data.agents : [...data.agents, person], assignments: [...data.assignments, link] }, 'Agente vinculado à microrregião nesta prévia.')} />}
    {unlinking && unlinkingAgent && region && <UnlinkAgentDialog data={data} assignment={unlinking} agent={unlinkingAgent} region={region} onClose={() => setEditor(null)} onSave={unlinkAgent} />}
    {(editor === 'address' || editor === 'edit-address' || linkingFamily) && region && agent && assignment && <AddressEditor data={data} region={region} agent={agent} assignment={assignment} address={linkingFamily ?? (editor === 'edit-address' ? address : undefined)} linkingFamily={Boolean(linkingFamily)} onClose={() => setEditor(null)} onSave={(record, linked) => done({ ...data, addresses: data.addresses.some(item => item.id === record.id) ? data.addresses.map(item => item.id === record.id ? record : item) : [...data.addresses, record], families: data.families.some(item => item.id === linked.id) ? data.families : [...data.families, linked] }, editor === 'edit-address' ? 'Endereço atualizado nesta prévia.' : 'Endereço e família salvos nesta prévia.', { regionId: region.id, assignmentId: assignment.id, addressId: record.id })} />}
    {editor === 'edit-family' && family && <FamilyEditor family={family} families={data.families} onClose={() => setEditor(null)} onSave={item => done({ ...data, families: data.families.map(existing => existing.id === item.id ? item : existing) }, 'Família atualizada nesta prévia.')} />}
  </div></ActionContext.Provider>
}

function Metric({ icon, label, value, detail }: { icon: Parameters<typeof StuIcon>[0]['name']; label: string; value: string | number; detail: string }) {
  return <div className="preview-metric"><div><small>{label}</small><strong>{value}</strong><span>{detail}</span></div><span className="preview-metric-icon"><StuIcon name={icon} size={21} /></span></div>
}
function Empty({ children }: { children: ReactNode }) { return <div className="preview-empty"><StuIcon name="search" size={25} /><p>{children}</p></div> }
function RegistrationChangesGuard({ dirty }: { dirty: boolean }) { useUnsavedChanges(dirty); return null }

function Dialog({ title, children, onClose, wide = false }: { title: string; children: ReactNode; onClose: () => void; wide?: boolean }) {
  const { busy, error } = useContext(ActionContext)
  const close = () => { if (!busy) onClose() }
  const dialogRef = useAccessibleDialog<HTMLDivElement>(true, close)
  return <div className="preview-overlay"><div className={`preview-dialog ${wide ? 'preview-dialog--wide' : ''}`} role="dialog" aria-modal="true" aria-labelledby="preview-dialog-title" ref={dialogRef} tabIndex={-1}><header><div><span className="section-kicker">STU</span><h2 id="preview-dialog-title">{title}</h2></div><button disabled={busy} className="stu-close-button" aria-label="Fechar formulário" onClick={close}><CloseIcon /></button></header>{error && <p className="preview-form-error" role="alert">{error}</p>}{busy && <p role="status" className="preview-dialog-intro">Salvando…</p>}<fieldset className="registration-dialog-fields" disabled={busy}>{children}</fieldset></div></div>
}

function RegionEditor({ region, regions, neighborhoods, onSave, onClose }: { region?: Region; regions: Region[]; neighborhoods: Neighborhood[]; onSave: (item: Region) => void; onClose: () => void }) {
  const [error, setError] = useState('')
  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const fields = new FormData(event.currentTarget)
    const code = String(fields.get('code')).trim().toUpperCase()
    const name = String(fields.get('name')).trim()
    const neighborhood = neighborhoods.find(item => item.id === fields.get('neighborhoodId'))
    if (!code || !name) { setError('Preencha os campos obrigatórios.'); return }
    if (!neighborhood) { setError('Selecione um bairro já cadastrado na UBS.'); return }
    if (regions.some(item => item.id !== region?.id && item.code === code)) { setError('Já existe uma microrregião com este código.'); return }
    onSave({ id: region?.id ?? id(), code, name, neighborhoodId: neighborhood.id, neighborhood: neighborhood.name })
  }
  return <Dialog title={region ? 'Editar microrregião' : 'Nova microrregião'} onClose={onClose}>
    <p className="preview-dialog-intro">Cadastre a área para depois vincular os agentes de saúde.</p>
    <form onSubmit={save}>
      <div className="preview-form-grid">
        <label>Código *<input name="code" required maxLength={32} defaultValue={region?.code} placeholder="Ex.: MR04" /></label>
        <label>Nome da microrregião *<input name="name" required maxLength={120} defaultValue={region?.name} placeholder="Ex.: Microrregião 04" /></label>
        <label className="preview-full">Bairro *
          <select name="neighborhoodId" required defaultValue={region?.neighborhoodId ?? ''}>
            <option value="" disabled>Selecione um bairro cadastrado</option>
            {neighborhoods.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </label>
      </div>
      {error && <p className="preview-form-error" role="alert">{error}</p>}
      <FormActions onClose={onClose} submit={region ? 'Salvar alterações' : 'Salvar e continuar para agentes'} />
    </form>
  </Dialog>
}

function AgentEditor({ data, region, onSave, onClose, canCreate }: { data: Data; region: Region; onSave: (agent: Agent, assignment: Assignment) => void; onClose: () => void; canCreate: boolean }) {
  const [mode, setMode] = useState('existing')
  const [error, setError] = useState('')
  const available = data.agents.filter(item => item.isActive !== false && !data.assignments.some(link => link.regionId === region.id && link.agentId === item.id && !link.endedAtUtc))
  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const fields = new FormData(event.currentTarget)
    let person: Agent
    if (mode === 'existing') {
      const found = available.find(item => item.id === fields.get('agentId'))
      if (!found) { setError('Selecione um agente da UBS.'); return }
      person = found
    } else {
      const name = String(fields.get('name')).trim(), userName = String(fields.get('userName')).trim()
      if (!name || !userName) { setError('Informe o nome e o usuário do agente.'); return }
      if (data.agents.some(item => item.userName.toLowerCase() === userName.toLowerCase())) { setError('Este usuário já está cadastrado. Selecione o agente na opção existente.'); return }
      person = { id: id(), name, userName }
    }
    onSave(person, { id: id(), agentId: person.id, regionId: region.id })
  }
  return <Dialog title="Vincular agente de saúde" onClose={onClose}><Context region={region} /><p className="preview-dialog-intro">Escolha um agente da UBS ou cadastre um novo servidor com perfil de agente.</p><ModeChoice options={canCreate ? [['existing', 'Agente existente'], ['new', 'Cadastrar novo']] : [['existing', 'Agente existente']]} value={mode} onChange={value => { setMode(value); setError('') }} /><form onSubmit={save} key={mode}>{mode === 'existing' ? <label>Agente da UBS *<select required name="agentId" defaultValue=""><option value="" disabled>Selecione um agente</option>{available.map(item => <option value={item.id} key={item.id}>{item.name}</option>)}</select>{available.length === 0 && <small>Todos os agentes já estão vinculados. Use “Cadastrar novo”.</small>}</label> : <div className="preview-form-grid"><label className="preview-full">Nome completo *<input name="name" required maxLength={120} placeholder="Nome do agente de saúde" /></label><label className="preview-full">Usuário de acesso *<input name="userName" required maxLength={100} placeholder="Ex.: nome.sobrenome" /></label><p className="preview-inline-note preview-full">O agente fará parte dos servidores da UBS e ficará vinculado a esta microrregião. A senha temporária será exibida após o cadastro.</p></div>}{error && <p className="preview-form-error" role="alert">{error}</p>}<FormActions onClose={onClose} submit="Salvar" icon="check" /></form></Dialog>
}

function UnlinkAgentDialog({ data, assignment, agent, region, onClose, onSave }: {
  data: Data; assignment: Assignment; agent: Agent; region: Region; onClose: () => void; onSave: (recipientId: string) => void
}) {
  const [recipientId, setRecipientId] = useState('')
  const [error, setError] = useState('')
  const addressCount = Math.max(assignment.addressCount ?? 0, data.addresses.filter(item => item.assignmentId === assignment.id).length)
  const recipients = data.assignments.filter(item => item.regionId === region.id && item.id !== assignment.id && !item.endedAtUtc && data.agents.some(person => person.id === item.agentId && person.isActive !== false))
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (addressCount && !recipients.some(item => item.id === recipientId)) {
      setError('Selecione outro agente da microrregião para receber os endereços.'); return
    }
    onSave(recipientId)
  }
  return <Dialog title="Desvincular agente" onClose={onClose}>
    <Context region={region} />
    <p className="preview-dialog-intro">Deseja desvincular <strong>{agent.name}</strong> desta microrregião? O agente continuará cadastrado na UBS e poderá ser vinculado novamente.</p>
    <form onSubmit={submit}>
      {addressCount > 0 ? <>
        <p className="preview-inline-note preview-unlink-note">Este agente possui {addressCount} {addressCount === 1 ? 'endereço' : 'endereços'} nesta microrregião. Os endereços e as famílias serão preservados com o agente que você selecionar abaixo.</p>
        {recipients.length ? <label>Novo agente responsável *
          <select required value={recipientId} onChange={event => { setRecipientId(event.target.value); setError('') }}>
            <option value="" disabled>Selecione outro agente da microrregião</option>
            {recipients.map(item => <option key={item.id} value={item.id}>{data.agents.find(person => person.id === item.agentId)!.name}</option>)}
          </select>
        </label> : <p className="preview-form-error" role="status">Vincule outro agente à microrregião para receber os endereços antes de concluir a desvinculação.</p>}
      </> : <p className="preview-inline-note">Este agente não possui endereços cadastrados nesta microrregião.</p>}
      {error && <p className="preview-form-error" role="alert">{error}</p>}
      <div className="preview-dialog-actions">
        <button className="preview-secondary" type="button" onClick={onClose}>Cancelar</button>
        <button className="preview-unlink-confirm" type="submit" disabled={addressCount > 0 && recipients.length === 0}>Desvincular</button>
      </div>
    </form>
  </Dialog>
}

type AddressDraft = Omit<Address, 'id' | 'assignmentId' | 'familyId'>
function AddressEditor({ data, region, agent, assignment, address, onSave, onClose, linkingFamily = false }: { data: Data; region: Region; agent: Agent; assignment: Assignment; address?: Address; linkingFamily?: boolean; onSave: (record: Address, family: Family) => void; onClose: () => void }) {
  const [step, setStep] = useState(linkingFamily ? 1 : 0)
  const [draft, setDraft] = useState<AddressDraft>(address ?? { street: '', houseNumber: '', noNumber: false, neighborhood: region.neighborhood, neighborhoodId: region.neighborhoodId, complement: '', postalCode: '', reference: '' })
  const [mode, setMode] = useState('new')
  const [number, setNumber] = useState('')
  const [responsibleName, setResponsibleName] = useState('')
  const [familyId, setFamilyId] = useState('')
  const [error, setError] = useState('')
  const availableFamilies = data.families.filter(item => !data.addresses.some(record => record.familyId === item.id))
  const selectedFamily = address?.familyId && !linkingFamily ? data.families.find(item => item.id === address.familyId)! : mode === 'existing' ? availableFamilies.find(item => item.id === familyId) : { id: '', number: number.trim().toUpperCase(), responsibleName: responsibleName.trim() }
  const update = (field: keyof AddressDraft, value: string | boolean) => setDraft(current => ({ ...current, [field]: value }))
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError('')
    if (step === 0) {
      if (!draft.street.trim() || !draft.neighborhood.trim() || (!draft.noNumber && !draft.houseNumber.trim())) { setError('Informe logradouro, bairro e número ou marque “Sem número”.'); return }
      if (draft.postalCode && !/^\d{5}-?\d{3}$/.test(draft.postalCode.trim())) { setError('Informe um CEP com 8 dígitos ou deixe o campo vazio.'); return }
      if (address && !linkingFamily) { save(); return }
      setStep(1); return
    }
    if (step === 1) {
      if (!selectedFamily || !selectedFamily.number || !selectedFamily.responsibleName) { setError('Preencha número e responsável ou selecione uma família existente.'); return }
      if (mode === 'new' && data.families.some(item => item.number.toUpperCase() === selectedFamily.number)) { setError('Este número de família já existe na UBS. Use uma família existente sem endereço ou informe outro número.'); return }
      setStep(2); return
    }
    save()
  }
  function save() {
    if (!selectedFamily) return
    const linked = selectedFamily.id ? selectedFamily : { ...selectedFamily, id: id() }
    onSave({ ...draft, street: draft.street.trim(), neighborhood: draft.neighborhood.trim(), houseNumber: draft.noNumber ? '' : draft.houseNumber.trim(), complement: draft.complement.trim(), reference: draft.reference.trim(), postalCode: draft.postalCode.trim(), id: address?.id ?? id(), assignmentId: assignment.id, familyId: linked.id }, linked)
  }
  return <Dialog title={linkingFamily ? 'Vincular família ao endereço' : address ? 'Editar endereço' : 'Cadastrar endereço e família'} onClose={onClose} wide><Context region={region} agent={agent} />{(!address || linkingFamily) && <ol className="preview-form-steps">{['Endereço', 'Família', 'Conferência'].map((label, index) => <li className={index === step ? 'active' : index < step ? 'done' : ''} aria-current={index === step ? 'step' : undefined} key={label}><span>{index + 1}</span>{label}</li>)}</ol>}<form onSubmit={submit}>
    {step === 0 && <><p className="preview-dialog-intro">Informe o endereço para consulta. Não é necessário marcar um ponto no mapa.</p><div className="preview-form-grid"><label className="preview-full">Logradouro *<input required maxLength={180} value={draft.street} onChange={event => update('street', event.target.value)} placeholder="Ex.: Rua das Acácias" /></label><label>Número {draft.noNumber ? '' : '*'}<input required={!draft.noNumber} disabled={draft.noNumber} maxLength={20} value={draft.houseNumber} onChange={event => update('houseNumber', event.target.value)} placeholder={draft.noNumber ? 's/n' : 'Ex.: 120'} /></label><label className="preview-checkbox"><input type="checkbox" checked={draft.noNumber} onChange={event => update('noNumber', event.target.checked)} />Sem número</label><label>Bairro *<input required readOnly maxLength={120} value={draft.neighborhood} /></label><label>Complemento<input maxLength={120} value={draft.complement} onChange={event => update('complement', event.target.value)} placeholder="Ex.: casa, fundos, apartamento" /></label><label>CEP<input inputMode="numeric" maxLength={9} value={draft.postalCode} onChange={event => update('postalCode', event.target.value)} placeholder="Opcional" /></label><label>Ponto de referência<input maxLength={180} value={draft.reference} onChange={event => update('reference', event.target.value)} placeholder="Opcional" /></label></div></>}
    {step === 1 && <><p className="preview-dialog-intro">Cada endereço será vinculado a uma família. Escolha como deseja continuar.</p><ModeChoice options={[['new', 'Cadastrar família'], ['existing', 'Família existente']]} value={mode} onChange={value => { setMode(value); setError('') }} />{mode === 'new' ? <div className="preview-form-grid"><label>Número da família *<input required maxLength={32} value={number} onChange={event => setNumber(event.target.value)} placeholder="Ex.: 008" /></label><label>Responsável pela família *<input required maxLength={120} value={responsibleName} onChange={event => setResponsibleName(event.target.value)} placeholder="Nome do responsável" /></label></div> : <label>Família sem endereço vinculado *<select required value={familyId} onChange={event => setFamilyId(event.target.value)}><option value="" disabled>Selecione uma família</option>{availableFamilies.map(item => <option key={item.id} value={item.id}>{item.number} · {item.responsibleName}</option>)}</select><small>{availableFamilies.length ? 'São exibidas somente famílias da UBS que ainda não têm endereço vinculado.' : 'Não há famílias disponíveis. Escolha “Cadastrar família”.'}</small></label>}</>}
    {step === 2 && <><p className="preview-dialog-intro">Confira o cadastro antes de salvar.</p><div className="preview-review"><dl className="preview-facts"><div><dt>Microrregião</dt><dd>{region.code} · {region.name}</dd></div><div><dt>Agente de saúde</dt><dd>{agent.name}</dd></div><div><dt>Endereço</dt><dd>{addressText({ ...draft, id: '', assignmentId: '', familyId: '' })}{draft.complement ? ` · ${draft.complement}` : ''}<small>{draft.neighborhood}{draft.postalCode ? ` · CEP ${draft.postalCode}` : ''}</small></dd></div><div><dt>Família</dt><dd>{selectedFamily?.number} · {selectedFamily?.responsibleName}</dd></div></dl></div><div className="preview-inline-note">Ao salvar, o endereço ficará na lista deste agente e a ficha da família será aberta.</div></>}
    {error && <p className="preview-form-error" role="alert">{error}</p>}<div className="preview-dialog-actions"><button className="preview-secondary" type="button" onClick={step > 0 ? () => { setStep(value => value - 1); setError('') } : onClose}>{step > 0 ? 'Voltar' : 'Cancelar'}</button><button className="primary-action" type="submit">{address && !linkingFamily ? 'Salvar endereço' : step === 0 ? 'Continuar para família' : step === 1 ? 'Conferir cadastro' : 'Salvar endereço e família'}<StuIcon name={step === 2 || address ? 'check' : 'arrow'} size={17} /></button></div>
    </form></Dialog>
}

function FamilyEditor({ family, families, onSave, onClose }: { family: Family; families: Family[]; onSave: (item: Family) => void; onClose: () => void }) {
  const [error, setError] = useState('')
  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const fields = new FormData(event.currentTarget)
    const number = String(fields.get('number')).trim().toUpperCase(), responsibleName = String(fields.get('responsibleName')).trim()
    if (!number || !responsibleName) { setError('Preencha número e responsável.'); return }
    if (families.some(item => item.id !== family.id && item.number.toUpperCase() === number)) { setError('Já existe uma família com este número na UBS.'); return }
    onSave({ ...family, number, responsibleName })
  }
  return <Dialog title={`Editar família ${family.number}`} onClose={onClose}><p className="preview-dialog-intro">A identificação da família contém somente número e responsável.</p><form onSubmit={save}><div className="preview-form-grid"><label>Número da família *<input required name="number" maxLength={32} defaultValue={family.number} /></label><label>Responsável pela família *<input required name="responsibleName" maxLength={120} defaultValue={family.responsibleName} /></label></div>{error && <p role="alert" className="preview-form-error">{error}</p>}<FormActions onClose={onClose} submit="Salvar família" /></form></Dialog>
}

function Context({ region, agent }: { region: Region; agent?: Agent }) {
  return <div className="preview-form-context"><StuIcon name="map" size={16} /><span>{region.code} · {region.name}</span>{agent && <><span aria-hidden="true">/</span><StuIcon name="users" size={16} /><strong>{agent.name}</strong></>}</div>
}
function ModeChoice({ options, value, onChange }: { options: string[][]; value: string; onChange: (value: string) => void }) {
  return <div className="preview-mode-choice">{options.map(([key, label]) => <button key={key} type="button" aria-pressed={value === key} className={value === key ? 'selected' : ''} onClick={() => onChange(key)}>{label}</button>)}</div>
}
function FormActions({ onClose, submit, icon = 'arrow' }: { onClose: () => void; submit: string; icon?: 'arrow' | 'check' }) {
  return <div className="preview-dialog-actions"><button className="preview-secondary" type="button" onClick={onClose}>Cancelar</button><button className="primary-action" type="submit">{submit}<StuIcon name={icon} size={17} /></button></div>
}
