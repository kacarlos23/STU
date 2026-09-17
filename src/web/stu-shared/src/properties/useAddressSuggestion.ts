import { useEffect, useState } from 'react'
import type { Coordinate } from './propertyGeometry'

export type AddressSuggestion = { found: boolean; street: string | null; postalCode: string | null }

export function useAddressSuggestion(point: Coordinate | null, microregionId: string, revision: number, enabled: boolean) {
  const [result, setResult] = useState<AddressSuggestion | null>(null)
  const [status, setStatus] = useState('')
  const [loading, setLoading] = useState(false)
  const longitude = point?.[0], latitude = point?.[1]
  useEffect(() => {
    setResult(null)
    setStatus('')
    setLoading(false)
    if (!enabled || !revision || latitude === undefined || longitude === undefined) return
    const controller = new AbortController()
    let active = true
    setLoading(true)
    setStatus('Buscando endereço do ponto marcado…')
    const timer = window.setTimeout(async () => {
      try {
        const query = new URLSearchParams({ microregionId, latitude: String(latitude), longitude: String(longitude) })
        const response = await fetch(`/api/properties/address-suggestion?${query}`, { credentials: 'include', cache: 'no-store', signal: controller.signal })
        if (!response.ok) throw new Error(response.status === 429 ? 'Consulta ocupada. Aguarde um momento e tente novamente, ou preencha manualmente.' : 'Não foi possível consultar o endereço. Você pode preencher manualmente.')
        const data: AddressSuggestion = await response.json()
        if (!active) return
        setResult(data)
        setStatus(data.found ? 'Sugestão encontrada. Confira o endereço; ele pode corresponder a uma rua vizinha. Campos já preenchidos foram preservados.' : 'Nenhum endereço encontrado para este ponto. Preencha os campos manualmente.')
      } catch (error) {
        if (active) setStatus(error instanceof Error ? error.message : 'Consulta indisponível. Preencha manualmente.')
      } finally { if (active) setLoading(false) }
    }, 600)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [latitude, longitude, microregionId, revision, enabled])
  return { result, status, loading }
}
