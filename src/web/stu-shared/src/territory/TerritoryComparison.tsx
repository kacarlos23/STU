import { useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import * as maplibregl from 'maplibre-gl'
import type { GeoJSONSource } from 'maplibre-gl'
import { useAccessibleDialog } from '../accessibility/useAccessibleDialog'
import { geometryBounds, type CoordinateBounds } from '../properties/propertyGeometry'
import type { TerritoryGeometry } from './osmImport'
import './territory-comparison.css'

export function comparisonBounds(before: TerritoryGeometry, after: TerritoryGeometry): CoordinateBounds | null {
  const a = geometryBounds(before), b = geometryBounds(after)
  if (!a || !b) return a ?? b
  return [[Math.min(a[0][0], b[0][0]), Math.min(a[0][1], b[0][1])], [Math.max(a[1][0], b[1][0]), Math.max(a[1][1], b[1][1])]]
}

export function TerritoryComparison({ before, after, validated, onClose }: { before: TerritoryGeometry; after: TerritoryGeometry; validated: boolean; onClose: () => void }) {
  const titleId = useId()
  const dialog = useAccessibleDialog<HTMLDivElement>(true, onClose)
  const bounds = comparisonBounds(before, after)
  return createPortal(<div className="territory-comparison-backdrop"><div className="territory-comparison" ref={dialog} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
    <header><h2 id={titleId}>Comparação dos limites</h2><button type="button" onClick={onClose}>Voltar à edição</button></header>
    <p>As duas áreas são exibidas na mesma escala e posição. {validated ? 'A proposta reflete o último resultado da validação de impacto.' : 'Proposta em edição: ainda pode ser ajustada ao validar ou salvar.'} Nenhuma alteração foi salva nesta comparação.</p>
    <div className="territory-comparison-maps"><ComparisonMap label="Limite atual salvo" geometry={before} bounds={bounds} previous /><ComparisonMap label="Limite proposto" geometry={after} bounds={bounds} /></div>
  </div></div>, document.body)
}

function ComparisonMap({ label, geometry, bounds, previous = false }: { label: string; geometry: TerritoryGeometry; bounds: CoordinateBounds | null; previous?: boolean }) {
  const node = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)
  const [error, setError] = useState(false)
  useEffect(() => {
    if (!node.current) return
    let map: maplibregl.Map
    try {
      map = new maplibregl.Map({ container: node.current, interactive: false, center: [-39.7451701, -17.5384774], zoom: 13, attributionControl: {}, style: { version: 8, sources: { osm: { type: 'raster', tiles: [import.meta.env.VITE_STU_RASTER_TILES_URL || 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'], tileSize: 256, attribution: '© OpenStreetMap contributors' } }, layers: [{ id: 'osm', type: 'raster', source: 'osm', paint: { 'raster-opacity': .7 } }] } })
      mapRef.current = map
    } catch { setError(true); return }
    return () => { mapRef.current = null; map.remove() }
  }, [])
  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    const update = () => {
      const data = { type: 'Feature' as const, properties: {}, geometry }
      const color = previous ? '#A98BFF' : '#6D4AFF'
      const source = map.getSource('comparison') as GeoJSONSource | undefined
      if (source) source.setData(data as never)
      else {
        map.addSource('comparison', { type: 'geojson', data: data as never })
        map.addLayer({ id: 'area', source: 'comparison', type: 'fill', paint: { 'fill-color': color, 'fill-opacity': .28 } })
        map.addLayer({ id: 'border', source: 'comparison', type: 'line', paint: { 'line-color': color, 'line-width': 3, 'line-dasharray': previous ? [2, 2] : [1, 0] } })
        map.addLayer({ id: 'point', source: 'comparison', type: 'circle', filter: ['==', '$type', 'Point'], paint: { 'circle-color': color, 'circle-radius': 7 } })
      }
      if (bounds) map.fitBounds(bounds, { padding: 32, maxZoom: 17, duration: 0 })
    }
    if (map.loaded()) update()
    else map.on('load', update)
    return () => { map.off('load', update) }
  }, [geometry, bounds, previous])
    return <section><h3>{label}</h3><p>{previous ? 'Contorno lilás tracejado' : 'Contorno violeta contínuo'}</p><div role="region" aria-label={label} className="territory-comparison-map" ref={node} />{error && <p role="alert">Não foi possível abrir esta visualização. Retorne à edição e confira o mapa principal.</p>}</section>
}
