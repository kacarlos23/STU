import { mutate } from '../../../../stu-shared/src/http/mutate'
import type { Address, Data, Family, Path } from './types'
import type { PersistChange } from './RegistrationFlow'

export type Unit = { id: string; name: string; code: string }
export async function readRegistration<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, { credentials: 'include', cache: 'no-store', signal })
  if (!response.ok) throw new Error(response.status === 403 ? 'Você não tem permissão para consultar estes cadastros.' : 'Não foi possível carregar os cadastros. Atualize a consulta.')
  return response.json() as Promise<T>
}
export async function loadRegistration(unitId: string, signal?: AbortSignal): Promise<Data> {
  const data = await readRegistration<Data>(`/api/registration?healthUnitId=${encodeURIComponent(unitId)}`, signal)
  return { ...data, addresses: data.addresses.map(address => ({ ...address, familyId: address.familyId ?? '', assignmentId: address.assignmentId ?? '' })) }
}
const changed = <T>(old: T, current: T) => JSON.stringify(old) !== JSON.stringify(current)

export function registrationPersistence(unitId: string): PersistChange {
  return async (previous, next, desiredPath) => {
    let path: Path | undefined = desiredPath
    let credentials: Data['temporaryCredentials']
    const addedRegion = next.regions.find(item => !previous.regions.some(old => old.id === item.id))
    const editedRegion = next.regions.find(item => previous.regions.some(old => old.id === item.id && changed(old, item)))
    const addedAssignment = next.assignments.find(item => !previous.assignments.some(old => old.id === item.id))
    const endedAssignment = next.assignments.find(item => item.endedAtUtc && previous.assignments.some(old => old.id === item.id && !old.endedAtUtc))
    const addedAddress = next.addresses.find(item => !previous.addresses.some(old => old.id === item.id))
    const editedAddress = next.addresses.find(item => previous.addresses.some(old => old.id === item.id && changed(old, item)))
    const editedFamily = next.families.find(item => previous.families.some(old => old.id === item.id && changed(old, item)))

    if (addedRegion || editedRegion) {
      const region = addedRegion ?? editedRegion!
      const old = previous.regions.find(item => item.id === region.id)
      const result = await mutate(addedRegion ? '/api/registration/regions' : `/api/registration/regions/${region.id}`, addedRegion ? 'POST' : 'PUT', {
        healthUnitId: unitId, code: region.code, name: region.name, neighborhoodId: region.neighborhoodId, expectedVersion: old?.concurrencyToken,
      }) as { id: string }
      path = { regionId: result.id }
    } else if (addedAssignment) {
      const person = next.agents.find(item => item.id === addedAssignment.agentId)!
      const exists = previous.agents.some(item => item.id === person.id)
      const result = await mutate(`/api/registration/regions/${addedAssignment.regionId}/agents`, 'POST', {
        agentId: exists ? person.id : null, name: person.name, userName: person.userName,
        expectedVersion: previous.regions.find(item => item.id === addedAssignment.regionId)?.concurrencyToken,
      }) as { userName: string; temporaryPassword?: string }
      if (result.temporaryPassword) credentials = { userName: result.userName, password: result.temporaryPassword }
    } else if (endedAssignment) {
      const recipientId = next.assignmentTransfers?.at(-1)?.fromAssignmentId === endedAssignment.id ? next.assignmentTransfers.at(-1)?.toAssignmentId : null
      await mutate(`/api/registration/assignments/${endedAssignment.id}/unlink`, 'POST', {
        recipientId, expectedVersion: previous.assignments.find(item => item.id === endedAssignment.id)?.concurrencyToken,
        expectedRegionVersion: previous.regions.find(item => item.id === endedAssignment.regionId)?.concurrencyToken,
        expectedRecipientVersion: previous.assignments.find(item => item.id === recipientId)?.concurrencyToken,
      })
    } else if (addedAddress || editedAddress) {
      const address = addedAddress ?? editedAddress!
      const family = next.families.find(item => item.id === address.familyId)!
      const old = previous.addresses.find(item => item.id === address.id)
      const linking = !old?.familyId && Boolean(family)
      const payload = addressBody(address, family, previous, unitId)
      const result = await mutate(addedAddress || linking ? '/api/registration/addresses' : `/api/registration/addresses/${address.id}`, addedAddress || linking ? 'POST' : 'PUT', {
        ...payload, existingAddressId: linking && old ? old.id : null, expectedVersion: old?.concurrencyToken,
      }) as { id?: string }
      if (result.id) path = { ...desiredPath, addressId: result.id }
    } else if (editedFamily) {
      await mutate(`/api/families/${editedFamily.id}`, 'PUT', {
        healthUnitId: unitId, number: editedFamily.number, responsibleName: editedFamily.responsibleName,
        expectedVersion: previous.families.find(item => item.id === editedFamily.id)?.concurrencyToken,
      })
    }
    const data = await loadRegistration(unitId)
    return { data: { ...data, temporaryCredentials: credentials }, path }
  }
}

function addressBody(address: Address, family: Family | undefined, previous: Data, _unitId: string) {
  const assignment = previous.assignments.find(item => item.id === address.assignmentId)!
  const region = previous.regions.find(item => item.id === assignment.regionId)!
  const existingFamily = previous.families.find(item => item.id === family?.id)
  return {
    assignmentId: address.assignmentId, street: address.street, houseNumber: address.houseNumber, noNumber: address.noNumber,
    neighborhoodId: address.neighborhoodId ?? region.neighborhoodId, complement: address.complement, postalCode: address.postalCode, reference: address.reference,
    familyId: existingFamily?.id, familyNumber: family?.number, familyResponsibleName: family?.responsibleName,
    expectedAssignmentVersion: assignment.concurrencyToken, expectedFamilyVersion: existingFamily?.concurrencyToken,
  }
}
