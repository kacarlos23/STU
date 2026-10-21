import { useEffect, useState } from 'react'
import { useUiActions, type Session } from '@stu/shared'
import { RegistrationFlow, type FlowPermissions } from './RegistrationFlow'
import { loadRegistration, readRegistration, registrationPersistence, type Unit } from './registrationApi'
import type { Data } from './types'

export function RegistrationWorkspace({ session, onViewFamily }: { session: Session; onViewFamily?: (id: string) => void }) {
  const { guard } = useUiActions()
  const [units, setUnits] = useState<Unit[]>([])
  const [unitId, setUnitId] = useState(session.healthUnit?.id ?? '')
  const [data, setData] = useState<Data | null>(null)
  const [error, setError] = useState('')
  const [unitsLoaded, setUnitsLoaded] = useState(false)
  const [refresh, setRefresh] = useState(0)
  const has = (permission: string) => session.permissions.includes('*') || session.permissions.includes(permission)
  const permissions: FlowPermissions = { regions: has('territory.manage'), agents: has('territory.manage'), createAgents: has('health_unit.users.manage'), addresses: has('properties.manage'), families: has('families.manage') }
  useEffect(() => {
    const controller = new AbortController()
    void readRegistration<Unit[]>('/api/registration/units', controller.signal).then(items => {
      setUnits(items); setUnitsLoaded(true); setUnitId(current => current || items[0]?.id || '')
    }).catch(caught => { if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Não foi possível carregar as UBS.') })
    return () => controller.abort()
  }, [])
  useEffect(() => {
    if (!unitId) return
    const controller = new AbortController()
    void loadRegistration(unitId, controller.signal).then(setData).catch(caught => {
      if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Não foi possível carregar os cadastros.')
    })
    return () => controller.abort()
  }, [unitId, refresh])
  return <section className="registration-production" aria-label="Cadastro territorial">
    <div className="registration-unit-toolbar">
      {session.roles.some(role => role.name === 'GlobalAdministrator') && <label>UBS<select value={unitId} onChange={event => { const value = event.target.value; void guard(() => { setData(null); setError(''); setUnitId(value) }) }}>{units.map(unit => <option key={unit.id} value={unit.id}>{unit.code} · {unit.name}</option>)}</select></label>}
      <button type="button" className="preview-secondary" onClick={() => void guard(() => { setData(null); setError(''); setRefresh(value => value + 1) })}>Atualizar consulta</button>
    </div>
    {error && <p role="alert" className="preview-form-error">{error}</p>}
    {!error && !data && <p role="status">{unitsLoaded && !unitId ? 'Nenhuma UBS ativa disponível para este acesso.' : 'Carregando microrregiões e cadastros da UBS…'}</p>}
    {data && <RegistrationFlow key={`${unitId}:${refresh}`} initialData={data} persist={registrationPersistence(unitId)} permissions={permissions} embedded healthUnitName={units.find(unit => unit.id === unitId)?.name ?? session.healthUnit?.name ?? 'UBS'} onViewFamily={has('visits.view') ? onViewFamily : undefined} />}
  </section>
}
