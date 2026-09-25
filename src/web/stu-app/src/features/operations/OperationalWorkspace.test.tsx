import { cleanup, render, screen } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { InteractionProvider, type Session } from '@stu/shared'
import { OperationalWorkspace } from '../../../../stu-shared/src/operations/OperationalWorkspace'

const session: Session = {
  id: 'manager', userName: 'manager', displayName: 'Gerente', mustChangePassword: false,
  healthUnit: { id: 'unit', name: 'UBS Sintética', code: 'UBS' },
  roles: [{ name: 'HealthUnitManager', displayName: 'Gerente' }],
  permissions: ['territory.manage', 'families.manage', 'properties.manage'],
}

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('Arquivos e dados', () => {
  it('oferece uma planilha Excel modelo e aceita o arquivo preenchido', async () => {
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const url = new URL(String(input), 'https://test.local')
      if (url.pathname === '/api/operations/indicators') return Promise.resolve(Response.json({ activeProperties: 0, drafts: 0, unassignedMicroregions: 0, pendingJobs: 0 }))
      if (url.pathname === '/api/properties/reference-data') return Promise.resolve(Response.json({ selectedHealthUnitId: 'unit', microregions: [], tags: [], coverageRules: [] }))
      if (url.pathname === '/api/operations/jobs') return Promise.resolve(Response.json([]))
      return Promise.resolve(Response.json({}, { status: 404 }))
    }))

    render(<InteractionProvider><OperationalWorkspace session={session} /></InteractionProvider>)

    expect(await screen.findByRole('heading', { name: 'Importar imóveis' })).toBeInTheDocument()
    expect(screen.getByText('familyResponsibleName')).toBeInTheDocument()
    expect(screen.getByText('Responsável pela família')).toBeInTheDocument()
    const download = screen.getByRole('link', { name: 'Baixar planilha Excel modelo' })
    expect(download).toHaveAttribute('download', 'modelo-importacao-imoveis.xlsx')
    expect(download).toHaveAttribute('href', '/modelo-importacao-imoveis.xlsx')
    expect(screen.getByLabelText(/^Arquivo/)).toHaveAttribute('accept', expect.stringContaining('.xlsx'))
  })
})
