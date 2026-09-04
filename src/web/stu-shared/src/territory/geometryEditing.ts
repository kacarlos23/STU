import type { TerritoryGeometry } from "./osmImport";

export type Coordinate = [number, number];
export type EditableVertex = {
  coordinate: Coordinate;
  number: number;
  path: number[];
};

type RingReference = { path: number[]; ring: Coordinate[] };

export function isEditableAreaGeometry(geometry: TerritoryGeometry | null): boolean {
  return geometry?.type === "Polygon" || geometry?.type === "MultiPolygon";
}

export function editableVerticesFromGeometry(geometry: TerritoryGeometry | null): EditableVertex[] {
  if (!geometry) return [];
  let number = 0;
  return ringsFromGeometry(geometry).flatMap(({ path, ring }) =>
    openRing(ring).map((coordinate, index) => ({
      coordinate,
      number: ++number,
      path: [...path, index],
    })),
  );
}

export function moveGeometryVertex(
  geometry: TerritoryGeometry,
  path: number[],
  coordinate: Coordinate,
): TerritoryGeometry {
  const next = cloneGeometry(geometry);
  const ring = ringAtPath(next, path.slice(0, -1));
  const vertexIndex = path.at(-1);
  if (!ring || vertexIndex === undefined || vertexIndex < 0 || vertexIndex >= openRing(ring).length) return geometry;

  ring[vertexIndex] = coordinate;
  if (vertexIndex === 0) ring[ring.length - 1] = coordinate;
  return next;
}

export function insertGeometryVertexNear(
  geometry: TerritoryGeometry,
  coordinate: Coordinate,
): TerritoryGeometry {
  const rings = ringsFromGeometry(geometry);
  let nearest: { path: number[]; segment: number; distance: number } | null = null;

  for (const { path, ring } of rings) {
    const vertices = openRing(ring);
    for (let index = 0; index < vertices.length; index += 1) {
      const distance = pointSegmentDistanceSquared(
        coordinate,
        vertices[index],
        vertices[(index + 1) % vertices.length],
      );
      if (!nearest || distance < nearest.distance) nearest = { path, segment: index, distance };
    }
  }

  if (!nearest) return geometry;
  const next = cloneGeometry(geometry);
  const ring = ringAtPath(next, nearest.path);
  if (!ring) return geometry;
  ring.splice(nearest.segment + 1, 0, coordinate);
  return next;
}

function ringsFromGeometry(geometry: TerritoryGeometry): RingReference[] {
  if (geometry.type === "Polygon") {
    return (geometry.coordinates as Coordinate[][]).map((ring, ringIndex) => ({ path: [ringIndex], ring }));
  }
  if (geometry.type === "MultiPolygon") {
    return (geometry.coordinates as Coordinate[][][]).flatMap((polygon, polygonIndex) =>
      polygon.map((ring, ringIndex) => ({ path: [polygonIndex, ringIndex], ring })),
    );
  }
  return [];
}

function ringAtPath(geometry: TerritoryGeometry, path: number[]): Coordinate[] | null {
  if (geometry.type === "Polygon" && path.length === 1)
    return (geometry.coordinates as Coordinate[][])[path[0]] ?? null;
  if (geometry.type === "MultiPolygon" && path.length === 2)
    return (geometry.coordinates as Coordinate[][][])[path[0]]?.[path[1]] ?? null;
  return null;
}

function openRing(ring: Coordinate[]): Coordinate[] {
  if (ring.length > 1 && sameCoordinate(ring[0], ring[ring.length - 1])) return ring.slice(0, -1);
  return ring;
}

function sameCoordinate(first: Coordinate, second: Coordinate): boolean {
  return first[0] === second[0] && first[1] === second[1];
}

function cloneGeometry(geometry: TerritoryGeometry): TerritoryGeometry {
  return { ...geometry, coordinates: structuredClone(geometry.coordinates) };
}

function pointSegmentDistanceSquared(point: Coordinate, start: Coordinate, end: Coordinate): number {
  const deltaX = end[0] - start[0];
  const deltaY = end[1] - start[1];
  if (deltaX === 0 && deltaY === 0)
    return (point[0] - start[0]) ** 2 + (point[1] - start[1]) ** 2;
  const position = Math.max(
    0,
    Math.min(1, ((point[0] - start[0]) * deltaX + (point[1] - start[1]) * deltaY) / (deltaX ** 2 + deltaY ** 2)),
  );
  const projectedX = start[0] + position * deltaX;
  const projectedY = start[1] + position * deltaY;
  return (point[0] - projectedX) ** 2 + (point[1] - projectedY) ** 2;
}
