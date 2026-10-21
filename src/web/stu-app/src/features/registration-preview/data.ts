import type { Data } from '../registration/types'

export function exampleData(): Data {
  return {
    neighborhoods: [
      { id: 'b1', name: 'Bairro de demonstração A' },
      { id: 'b2', name: 'Bairro de demonstração B' },
      { id: 'b3', name: 'Bairro de demonstração C' },
    ],
    regions: [
      { id: 'r1', code: 'MR01', name: 'Microrregião 01', neighborhoodId: 'b1', neighborhood: 'Bairro de demonstração A' },
      { id: 'r2', code: 'MR02', name: 'Microrregião 02', neighborhoodId: 'b2', neighborhood: 'Bairro de demonstração B' },
      { id: 'r3', code: 'MR03', name: 'Microrregião 03', neighborhoodId: 'b3', neighborhood: 'Bairro de demonstração C' },
    ],
    agents: [
      { id: 'u1', name: 'Mariana Alves', userName: 'mariana.alves' },
      { id: 'u2', name: 'Carlos Santos', userName: 'carlos.santos' },
      { id: 'u3', name: 'Renata Lima', userName: 'renata.lima' },
      { id: 'u4', name: 'Paulo Oliveira', userName: 'paulo.oliveira' },
      { id: 'u5', name: 'Juliana Costa', userName: 'juliana.costa' },
    ],
    assignments: [
      { id: 'a1', regionId: 'r1', agentId: 'u1' }, { id: 'a2', regionId: 'r1', agentId: 'u2' },
      { id: 'a3', regionId: 'r2', agentId: 'u3' }, { id: 'a4', regionId: 'r3', agentId: 'u4' },
    ],
    addresses: [
      { id: 'e1', assignmentId: 'a1', street: 'Rua das Acácias', houseNumber: '120', noNumber: false, neighborhood: 'Bairro de demonstração A', complement: 'Casa', postalCode: '', reference: 'Próximo à praça', familyId: 'f1' },
      { id: 'e2', assignmentId: 'a1', street: 'Rua das Acácias', houseNumber: '128', noNumber: false, neighborhood: 'Bairro de demonstração A', complement: 'Fundos', postalCode: '', reference: '', familyId: 'f2' },
      { id: 'e3', assignmentId: 'a1', street: 'Rua dos Ipês', houseNumber: '45', noNumber: false, neighborhood: 'Bairro de demonstração A', complement: '', postalCode: '', reference: '', familyId: 'f3' },
      { id: 'e4', assignmentId: 'a2', street: 'Rua das Flores', houseNumber: '82', noNumber: false, neighborhood: 'Bairro de demonstração A', complement: '', postalCode: '', reference: '', familyId: 'f4' },
      { id: 'e5', assignmentId: 'a3', street: 'Rua do Sol', houseNumber: '16', noNumber: false, neighborhood: 'Bairro de demonstração B', complement: '', postalCode: '', reference: '', familyId: 'f5' },
      { id: 'e6', assignmentId: 'a4', street: 'Rua da Paz', houseNumber: '', noNumber: true, neighborhood: 'Bairro de demonstração C', complement: 'Casa azul', postalCode: '', reference: '', familyId: 'f6' },
    ],
    families: [
      { id: 'f1', number: '001', responsibleName: 'Ana Souza' },
      { id: 'f2', number: '002', responsibleName: 'José Pereira' },
      { id: 'f3', number: '003', responsibleName: 'Rosa Almeida' },
      { id: 'f4', number: '004', responsibleName: 'Antônio Santos' },
      { id: 'f5', number: '005', responsibleName: 'Lúcia Oliveira' },
      { id: 'f6', number: '006', responsibleName: 'Pedro Lima' },
      { id: 'f7', number: '007', responsibleName: 'Helena Costa' },
    ],
  }
}
