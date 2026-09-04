export type TerritoryGeometry = {
  type: "Point" | "Polygon" | "MultiPolygon";
  coordinates: unknown;
};

export type OsmAreaCandidate = {
  id: string;
  label: string;
  detail: string;
  externalReference: string;
  geometry: TerritoryGeometry;
};

type Coordinate = [number, number];
type OsmWay = { id: string; refs: string[]; tags: Record<string, string> };

export function parseOsmAreas(xml: string): OsmAreaCandidate[] {
  if (/<!DOCTYPE|<!ENTITY/i.test(xml))
    throw new Error("O arquivo OSM contém declarações XML não permitidas.");
  const document = new DOMParser().parseFromString(xml, "application/xml");
  if (
    document.querySelector("parsererror") ||
    document.documentElement.tagName.toLowerCase() !== "osm"
  )
    throw new Error("O arquivo não é um XML OSM válido.");

  const nodes = new Map<string, Coordinate>();
  for (const element of Array.from(document.getElementsByTagName("node"))) {
    const id = element.getAttribute("id");
    const latitudeValue = element.getAttribute("lat");
    const longitudeValue = element.getAttribute("lon");
    const latitude = Number(latitudeValue);
    const longitude = Number(longitudeValue);
    if (
      id &&
      latitudeValue !== null &&
      longitudeValue !== null &&
      Number.isFinite(latitude) &&
      Number.isFinite(longitude)
    )
      nodes.set(id, [longitude, latitude]);
  }

  const ways = new Map<string, OsmWay>();
  for (const element of Array.from(document.getElementsByTagName("way"))) {
    const id = element.getAttribute("id");
    if (!id) continue;
    ways.set(id, {
      id,
      refs: children(element, "nd")
        .map((item) => item.getAttribute("ref") ?? "")
        .filter(Boolean),
      tags: tagsOf(element),
    });
  }

  const candidates: Array<OsmAreaCandidate & { priority: number }> = [];
  const bounds = document.getElementsByTagName("bounds")[0];
  if (bounds) {
    const values = ["minlat", "minlon", "maxlat", "maxlon"].map((attribute) =>
      bounds.getAttribute(attribute),
    );
    const [minLat, minLon, maxLat, maxLon] = values.map(Number);
    if (
      values.every((value) => value !== null) &&
      [minLat, minLon, maxLat, maxLon].every(Number.isFinite) &&
      minLat < maxLat &&
      minLon < maxLon
    ) {
      candidates.push({
        id: "bounds",
        label: "Área completa exportada",
        detail: "Retângulo selecionado ao exportar no OpenStreetMap",
        externalReference: `OSM bounds ${minLon},${minLat},${maxLon},${maxLat}`,
        geometry: {
          type: "Polygon",
          coordinates: [
            [
              [minLon, minLat],
              [maxLon, minLat],
              [maxLon, maxLat],
              [minLon, maxLat],
              [minLon, minLat],
            ],
          ],
        },
        priority: 1,
      });
    }
  }

  const relationWayIds = new Set<string>();
  for (const relation of Array.from(
    document.getElementsByTagName("relation"),
  )) {
    const id = relation.getAttribute("id");
    if (!id) continue;
    const tags = tagsOf(relation);
    const relationType = tags.type?.toLowerCase();
    if (relationType !== "multipolygon" && relationType !== "boundary")
      continue;
    const members = children(relation, "member").filter(
      (item) => item.getAttribute("type") === "way",
    );
    const outerIds = members
      .filter(
        (item) =>
          !item.getAttribute("role") || item.getAttribute("role") === "outer",
      )
      .map((item) => item.getAttribute("ref") ?? "")
      .filter(Boolean);
    const innerIds = members
      .filter((item) => item.getAttribute("role") === "inner")
      .map((item) => item.getAttribute("ref") ?? "")
      .filter(Boolean);
    for (const wayId of [...outerIds, ...innerIds]) relationWayIds.add(wayId);
    const outerRings = assembleRings(
      outerIds.map((wayId) => ways.get(wayId)?.refs ?? []),
      nodes,
    );
    if (outerRings.length === 0) continue;
    const innerRings = assembleRings(
      innerIds.map((wayId) => ways.get(wayId)?.refs ?? []),
      nodes,
    );
    const polygons = outerRings.map((outer) => [
      outer,
      ...innerRings.filter((inner) => pointInRing(inner[0], outer)),
    ]);
    const geometry: TerritoryGeometry =
      polygons.length === 1
        ? { type: "Polygon", coordinates: polygons[0] }
        : { type: "MultiPolygon", coordinates: polygons };
    const name =
      tags.name || tags["official_name"] || tags.ref || `Relação ${id}`;
    candidates.push({
      id: `relation/${id}`,
      label: name,
      detail: tags.boundary
        ? `Limite ${tags.boundary}${tags.admin_level ? ` · nível ${tags.admin_level}` : ""}`
        : "Relação multipolígono",
      externalReference: `OSM relation/${id}`,
      geometry,
      priority: 0,
    });
  }

  for (const way of ways.values()) {
    if (
      relationWayIds.has(way.id) ||
      !isClosed(way.refs) ||
      !isTerritorialArea(way.tags)
    )
      continue;
    const ring = coordinatesFor(way.refs, nodes);
    if (!ring) continue;
    const name =
      way.tags.name ||
      way.tags["official_name"] ||
      way.tags.ref ||
      `Área ${way.id}`;
    candidates.push({
      id: `way/${way.id}`,
      label: name,
      detail: way.tags.boundary
        ? `Limite ${way.tags.boundary}`
        : way.tags.place
          ? `Localidade ${way.tags.place}`
          : "Área fechada",
      externalReference: `OSM way/${way.id}`,
      geometry: { type: "Polygon", coordinates: [ring] },
      priority: 2,
    });
  }

  return candidates
    .sort(
      (left, right) =>
        left.priority - right.priority ||
        left.label.localeCompare(right.label, "pt-BR"),
    )
    .slice(0, 500)
    .map((candidate) => ({
      id: candidate.id,
      label: candidate.label,
      detail: candidate.detail,
      externalReference: candidate.externalReference,
      geometry: candidate.geometry,
    }));
}

function children(element: Element, tagName: string) {
  return Array.from(element.children).filter(
    (child) => child.tagName.toLowerCase() === tagName,
  );
}
function tagsOf(element: Element) {
  const tags: Record<string, string> = {};
  for (const tag of children(element, "tag")) {
    const key = tag.getAttribute("k");
    const value = tag.getAttribute("v");
    if (key && value !== null) tags[key] = value;
  }
  return tags;
}
function isClosed(refs: string[]) {
  return refs.length >= 4 && refs[0] === refs.at(-1);
}
function isTerritorialArea(tags: Record<string, string>) {
  return Boolean(
    tags.boundary ||
    tags.place ||
    tags.admin_level ||
    (tags.area === "yes" && (tags.name || tags.ref)),
  );
}
function coordinatesFor(refs: string[], nodes: Map<string, Coordinate>) {
  const coordinates = refs.map((ref) => nodes.get(ref));
  return coordinates.every(Boolean) ? (coordinates as Coordinate[]) : null;
}

function assembleRings(segments: string[][], nodes: Map<string, Coordinate>) {
  const remaining = segments
    .filter((segment) => segment.length >= 2)
    .map((segment) => [...segment]);
  const rings: Coordinate[][] = [];
  while (remaining.length > 0) {
    const ring = remaining.shift()!;
    while (ring[0] !== ring.at(-1)) {
      const first = ring[0];
      const last = ring.at(-1);
      const index = remaining.findIndex(
        (segment) =>
          [segment[0], segment.at(-1)].includes(first) ||
          [segment[0], segment.at(-1)].includes(last),
      );
      if (index < 0) break;
      const segment = remaining.splice(index, 1)[0];
      const segmentLast = segment.at(-1);
      if (segment[0] === last) ring.push(...segment.slice(1));
      else if (segmentLast === last) ring.push(...segment.reverse().slice(1));
      else if (segmentLast === first) ring.unshift(...segment.slice(0, -1));
      else if (segment[0] === first)
        ring.unshift(...segment.reverse().slice(0, -1));
    }
    if (isClosed(ring)) {
      const coordinates = coordinatesFor(ring, nodes);
      if (coordinates) rings.push(coordinates);
    }
  }
  return rings;
}

function pointInRing(point: Coordinate, ring: Coordinate[]) {
  let inside = false;
  for (
    let index = 0, previous = ring.length - 1;
    index < ring.length;
    previous = index++
  ) {
    const [x, y] = ring[index];
    const [previousX, previousY] = ring[previous];
    if (
      y > point[1] !== previousY > point[1] &&
      point[0] < ((previousX - x) * (point[1] - y)) / (previousY - y) + x
    )
      inside = !inside;
  }
  return inside;
}
