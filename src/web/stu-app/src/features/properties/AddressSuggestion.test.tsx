import { act, cleanup, renderHook } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { useAddressSuggestion } from '../../../../stu-shared/src/properties/useAddressSuggestion'
import type { Coordinate } from '../../../../stu-shared/src/properties/propertyGeometry'

afterEach(() => { cleanup(); vi.useRealTimers(); vi.unstubAllGlobals() })

it('ignora respostas antigas após outro clique e cancela ao desativar a consulta', async () => {
  vi.useFakeTimers()
  const pending: ((value: Response) => void)[] = []
  const fetch = vi.fn(() => new Promise<Response>(resolve => pending.push(resolve)))
  vi.stubGlobal('fetch', fetch)
  const { result, rerender } = renderHook(({ point, enabled }: { point: Coordinate; enabled: boolean }) => useAddressSuggestion(point, 'micro', 1, enabled), { initialProps: { point: [-39.5, -17.5] as Coordinate, enabled: true } })
  await act(() => vi.advanceTimersByTimeAsync(600))
  rerender({ point: [-39.6, -17.6], enabled: true })
  await act(() => vi.advanceTimersByTimeAsync(600))
  await act(async () => pending[1](Response.json({ found: true, street: 'Rua nova', postalCode: null })))
  expect(result.current.result?.street).toBe('Rua nova')
  await act(async () => pending[0](Response.json({ found: true, street: 'Rua antiga', postalCode: null })))
  expect(result.current.result?.street).toBe('Rua nova')
  rerender({ point: [-39.7, -17.7], enabled: false })
  await act(() => vi.advanceTimersByTimeAsync(1000))
  expect(result.current.result).toBeNull()
  expect(fetch).toHaveBeenCalledTimes(2)
})

it('não consulta ao apenas abrir um imóvel e agrupa cliques rápidos', async () => {
  vi.useFakeTimers()
  const fetch = vi.fn(async () => Response.json({ found: false, street: null, postalCode: null }))
  vi.stubGlobal('fetch', fetch)
  const { result, rerender } = renderHook(({ revision }) => useAddressSuggestion([-39.5, -17.5], 'micro', revision, true), { initialProps: { revision: 0 } })
  await act(() => vi.advanceTimersByTimeAsync(1000))
  expect(fetch).not.toHaveBeenCalled()
  rerender({ revision: 1 })
  await act(() => vi.advanceTimersByTimeAsync(300))
  rerender({ revision: 2 })
  await act(() => vi.advanceTimersByTimeAsync(600))
  expect(fetch).toHaveBeenCalledTimes(1)
  expect(result.current.status).toContain('Nenhum endereço encontrado')
})
