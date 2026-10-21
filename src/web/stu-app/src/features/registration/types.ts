export type Neighborhood = { id: string; name: string }
export type Region = { id: string; code: string; name: string; neighborhoodId: string; neighborhood: string; concurrencyToken?: string }
export type Agent = { id: string; name: string; userName: string; isActive?: boolean }
export type Assignment = { id: string; regionId: string; agentId: string; endedAtUtc?: string; concurrencyToken?: string; addressCount?: number }
export type AssignmentTransfer = { fromAssignmentId: string; toAssignmentId: string; addressIds: string[]; changedAtUtc: string }
export type Family = { id: string; number: string; responsibleName: string; concurrencyToken?: string }
export type Address = {
  id: string; assignmentId: string; street: string; houseNumber: string; noNumber: boolean
  neighborhood: string; complement: string; postalCode: string; reference: string; familyId: string
  regionId?: string; neighborhoodId?: string; concurrencyToken?: string
}
export type Path = { regionId?: string; assignmentId?: string; addressId?: string }
export type Editor = 'region' | 'edit-region' | 'agent' | 'address' | 'edit-address' | 'edit-family' | 'guide' | { kind: 'unlink-agent'; assignmentId: string } | { kind: 'link-family'; addressId: string } | null
export type Data = { neighborhoods: Neighborhood[]; regions: Region[]; agents: Agent[]; assignments: Assignment[]; addresses: Address[]; families: Family[]; assignmentTransfers?: AssignmentTransfer[]; temporaryCredentials?: { userName: string; password: string } }
