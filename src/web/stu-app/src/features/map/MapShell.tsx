export function MapShell() {
  return (
    <section className="map-shell" aria-label="Área reservada para o mapa territorial">
      <div className="map-grid" aria-hidden="true" />
      <div className="map-message">
        <span>Mapa territorial</span>
        <strong>MapLibre será conectado ao PostGIS na fase de territórios.</strong>
      </div>
    </section>
  )
}
