export type PropertyGeometry = {
  type: "Point" | "Polygon" | "MultiPolygon";
  coordinates: unknown;
};

export type Coordinate = [number, number];
export type CoordinateBounds = [Coordinate, Coordinate];

const epsilon = 1e-10;

export function geometryBounds(
  geometry?: PropertyGeometry,
): CoordinateBounds | null {
  if (!geometry) return null;
  const coordinates: Coordinate[] = [];
  visitCoordinates(geometry.coordinates, (value) => coordinates.push(value));
  if (!coordinates.length) return null;

  return coordinates.reduce<CoordinateBounds>(
    (bounds, [longitude, latitude]) => [
      [
        Math.min(bounds[0][0], longitude),
        Math.min(bounds[0][1], latitude),
      ],
      [
        Math.max(bounds[1][0], longitude),
        Math.max(bounds[1][1], latitude),
      ],
    ],
    [
      [...coordinates[0]] as Coordinate,
      [...coordinates[0]] as Coordinate,
    ],
  );
}

export function centerOfGeometry(
  geometry?: PropertyGeometry,
): Coordinate | null {
  const bounds = geometryBounds(geometry);
  if (!bounds) return null;
  return [
    (bounds[0][0] + bounds[1][0]) / 2,
    (bounds[0][1] + bounds[1][1]) / 2,
  ];
}

export function geometryContainsPoint(
  geometry: PropertyGeometry | undefined,
  point: Coordinate,
): boolean {
  if (!geometry) return false;
  if (geometry.type === "Point") {
    const candidate = asCoordinate(geometry.coordinates);
    return candidate !== null && pointsEqual(candidate, point);
  }

  if (geometry.type === "Polygon") {
    return polygonContainsPoint(geometry.coordinates, point);
  }

  if (!Array.isArray(geometry.coordinates)) return false;
  return geometry.coordinates.some((polygon) =>
    polygonContainsPoint(polygon, point),
  );
}

function polygonContainsPoint(value: unknown, point: Coordinate): boolean {
  if (!Array.isArray(value) || value.length === 0) return false;
  const rings = value.map(asRing);
  const exterior = rings[0];
  if (!exterior || !ringContainsPoint(exterior, point)) return false;

  for (const hole of rings.slice(1)) {
    if (!hole) continue;
    if (pointOnRing(hole, point)) return true;
    if (ringContainsPoint(hole, point)) return false;
  }
  return true;
}

function ringContainsPoint(ring: Coordinate[], point: Coordinate): boolean {
  if (pointOnRing(ring, point)) return true;
  let inside = false;
  for (let current = 0, previous = ring.length - 1; current < ring.length; previous = current++) {
    const [currentX, currentY] = ring[current];
    const [previousX, previousY] = ring[previous];
    const crossesLatitude = currentY > point[1] !== previousY > point[1];
    if (!crossesLatitude) continue;
    const intersection =
      ((previousX - currentX) * (point[1] - currentY)) /
        (previousY - currentY) +
      currentX;
    if (point[0] < intersection) inside = !inside;
  }
  return inside;
}

function pointOnRing(ring: Coordinate[], point: Coordinate): boolean {
  for (let index = 0; index < ring.length - 1; index += 1) {
    if (pointOnSegment(point, ring[index], ring[index + 1])) return true;
  }
  return false;
}

function pointOnSegment(
  point: Coordinate,
  start: Coordinate,
  end: Coordinate,
): boolean {
  const cross =
    (point[1] - start[1]) * (end[0] - start[0]) -
    (point[0] - start[0]) * (end[1] - start[1]);
  if (Math.abs(cross) > epsilon) return false;
  return (
    point[0] >= Math.min(start[0], end[0]) - epsilon &&
    point[0] <= Math.max(start[0], end[0]) + epsilon &&
    point[1] >= Math.min(start[1], end[1]) - epsilon &&
    point[1] <= Math.max(start[1], end[1]) + epsilon
  );
}

function asRing(value: unknown): Coordinate[] | null {
  if (!Array.isArray(value)) return null;
  const ring = value.map(asCoordinate);
  return ring.every((coordinate) => coordinate !== null)
    ? (ring as Coordinate[])
    : null;
}

function asCoordinate(value: unknown): Coordinate | null {
  return Array.isArray(value) &&
    value.length >= 2 &&
    typeof value[0] === "number" &&
    typeof value[1] === "number"
    ? [value[0], value[1]]
    : null;
}

function pointsEqual(first: Coordinate, second: Coordinate): boolean {
  return (
    Math.abs(first[0] - second[0]) <= epsilon &&
    Math.abs(first[1] - second[1]) <= epsilon
  );
}

function visitCoordinates(
  value: unknown,
  visitor: (point: Coordinate) => void,
) {
  const point = asCoordinate(value);
  if (point) {
    visitor(point);
    return;
  }
  if (!Array.isArray(value)) return;
  for (const child of value) visitCoordinates(child, visitor);
}
