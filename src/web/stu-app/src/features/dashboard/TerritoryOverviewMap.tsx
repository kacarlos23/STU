import { useEffect, useRef, useState } from 'react'
import * as maplibregl from 'maplibre-gl'
import type { GeoJSONSource, Map as MapLibreMap } from 'maplibre-gl'
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'
import 'maplibre-gl/dist/maplibre-gl.css'

type Coordinate = [number, number]
type Geometry = { type: string; coordinates: unknown }
type MapFeature = { type: 'Feature'; id?: string; geometry: Geometry; properties: Record<string, unknown> }
type FeatureCollection = { type: 'FeatureCollection'; features: MapFeature[] }

const emptyCollection: FeatureCollection = { type: 'FeatureCollection', features: [] }
const fallbackCenter: Coordinate = [-39.7451701, -17.5384774]
const rasterTilesUrl = import.meta.env.VITE_STU_RASTER_TILES_URL || 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'

export function TerritoryOverviewMap({ healthUnitId, onOpenMap }: { healthUnitId?: string; onOpenMap: () => void }) {
  const node = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MapLibreMap | null>(null)
  const [state, setState] = useState<'loading' | 'ready' | 'empty' | 'error'>(healthUnitId ? 'loading' : 'empty')

  useEffect(() => {
    if (!healthUnitId || !node.current || import.meta.env.MODE === 'test') return
    maplibregl.setWorkerUrl(workerUrl)
    const map = new maplibregl.Map({
      container: node.current,
      center: fallbackCenter,
      zoom: 11.5,
      interactive: false,
      attributionControl: {},
      style: {
        version: 8,
        sources: { osm: { type: 'raster', tiles: [rasterTilesUrl], tileSize: 256, attribution: '© OpenStreetMap contributors' } },
        layers: [{ id: 'osm', type: 'raster', source: 'osm', paint: { 'raster-opacity': .72, 'raster-saturation': -.55, 'raster-contrast': -.08, 'raster-brightness-max': .96 } }],
      },
    })
    mapRef.current = map
    let cancelled = false
    map.on('load', async () => {
      map.addSource('overview-territory', { type: 'geojson', data: emptyCollection })
      const territorialColor = ['match', ['get', 'color'], '#2e8b72', '#A98BFF', '#4f9a7d', '#6D4AFF', '#2F6BBD', '#6D4AFF', '#7c3aed', '#7C5AC7', '#db2777', '#C05A9D', '#dc2626', '#B96CB0', '#d97706', '#8B6FD6', '#65a30d', '#5C3BB8', '#475569', '#3A245A', ['coalesce', ['get', 'color'], '#6D4AFF']] as maplibregl.ExpressionSpecification
      map.addLayer({ id: 'overview-fill', type: 'fill', source: 'overview-territory', filter: ['==', ['get', 'entityType'], 'microregion'], paint: { 'fill-color': territorialColor, 'fill-opacity': .36 } })
      map.addLayer({ id: 'overview-line', type: 'line', source: 'overview-territory', filter: ['==', ['get', 'entityType'], 'microregion'], paint: { 'line-color': territorialColor, 'line-width': 2.4, 'line-dasharray': [3, 1.5] } })
      map.addLayer({ id: 'overview-properties', type: 'circle', source: 'overview-territory', filter: ['all', ['==', ['get', 'entityType'], 'property'], ['==', ['geometry-type'], 'Point']], paint: { 'circle-color': ['match', ['get', 'coverageStatus'], 'covered', '#267158', 'overdue', '#D95D52', 'neverVisited', '#E9A23B', '#C05A9D'], 'circle-radius': 4, 'circle-stroke-color': '#fff', 'circle-stroke-width': 1 } })
      try {
        const suffix = `?healthUnitId=${encodeURIComponent(healthUnitId)}`
        const [territoryResponse, propertyResponse] = await Promise.all([
          fetch(`/api/territories/map${suffix}`, { credentials: 'include' }),
          fetch(`/api/properties/map${suffix}`, { credentials: 'include' }),
        ])
        if (!territoryResponse.ok || !propertyResponse.ok) throw new Error('preview unavailable')
        const territory = await territoryResponse.json() as FeatureCollection
        const properties = await propertyResponse.json() as FeatureCollection
        if (cancelled) return
        const data = { type: 'FeatureCollection' as const, features: [...territory.features, ...properties.features] }
        ;(map.getSource('overview-territory') as GeoJSONSource).setData(data as never)
        const bounds = boundsOf(data)
        if (bounds) map.fitBounds(bounds, { padding: 34, maxZoom: 15, duration: 0 })
        setState(data.features.length ? 'ready' : 'empty')
      } catch {
        if (!cancelled) setState('error')
      }
    })
    return () => {
      cancelled = true
      map.remove()
      mapRef.current = null
    }
  }, [healthUnitId])

  return <div className="overview-map-shell">
    <div aria-label="Prévia do mapa territorial" className="overview-map" ref={node} role="img" />
    {state === 'loading' && <div className="overview-map-status">Carregando o território…</div>}
    {state === 'empty' && <div className="overview-map-empty"><strong>Território pronto para começar</strong><span>Cadastre ou selecione uma UBS para visualizar suas áreas.</span></div>}
    {state === 'error' && <div className="overview-map-empty"><strong>Prévia temporariamente indisponível</strong><span>O mapa completo continua disponível.</span></div>}
    <div className="overview-map-legend" aria-label="Legenda de cobertura"><span><i className="legend-dot legend-dot--covered" />Em dia</span><span><i className="legend-dot legend-dot--attention" />Sem visita</span><span><i className="legend-dot legend-dot--alert" />Fora do prazo</span></div>
    <button className="overview-map-open" onClick={onOpenMap} type="button">Explorar mapa <span aria-hidden="true">→</span></button>
  </div>
}

function boundsOf(collection: FeatureCollection): [Coordinate, Coordinate] | null {
  const coordinates: Coordinate[] = []
  for (const feature of collection.features) collectCoordinates(feature.geometry.coordinates, coordinates)
  if (!coordinates.length) return null
  let minX = coordinates[0][0], maxX = minX, minY = coordinates[0][1], maxY = minY
  for (const [x, y] of coordinates) { minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y) }
  return [[minX, minY], [maxX, maxY]]
}

function collectCoordinates(value: unknown, result: Coordinate[]) {
  if (!Array.isArray(value)) return
  if (value.length >= 2 && typeof value[0] === 'number' && typeof value[1] === 'number') { result.push([value[0], value[1]]); return }
  for (const nested of value) collectCoordinates(nested, result)
}
