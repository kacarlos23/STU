import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { TerritoryImpactPanel } from '../../../../stu-shared/src/territory/TerritoryImpactPanel'

afterEach(cleanup)

describe('territorial impact review', () => {
  it('identifies houses and families that prevent saving', () => {
    render(<TerritoryImpactPanel impact={{
      valid: false, affectedPropertyCount: 1, blockedPropertyCount: 1,
      conflicts: ['O imóvel ficará fora do limite.'],
      affectedProperties: [{ id: 'p1', street: 'Rua de teste', houseNumber: '20', familyNumber: 'F30', reason: 'Imóvel ficará fora do novo limite.' }],
    }} />)
    expect(screen.getByRole('status').textContent).toContain('Revise os impedimentos')
    expect(screen.getByText('Rua de teste, nº 20 · Família F30')).toBeTruthy()
    expect(screen.getByText('O imóvel ficará fora do limite.')).toBeTruthy()
  })

  it('allows an agent change and clearly indicates a limited preview list', () => {
    render(<TerritoryImpactPanel impact={{ valid: true, affectedPropertyCount: 101, blockedPropertyCount: 0, conflicts: [], affectedProperties: [] }} />)
    expect(screen.getByRole('status').textContent).toContain('Alteração disponível para salvar')
    expect(screen.getByText('Exibindo os primeiros 0 de 101 imóveis afetados.')).toBeTruthy()
  })
})
