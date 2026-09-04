import { describe, expect, it } from "vitest";
import {
  centerOfGeometry,
  geometryBounds,
  geometryContainsPoint,
  type PropertyGeometry,
} from "@stu/shared/property-geometry";

const polygon: PropertyGeometry = {
  type: "Polygon",
  coordinates: [
    [
      [-40, -18],
      [-39, -18],
      [-39, -17],
      [-40, -17],
      [-40, -18],
    ],
  ],
};

describe("localização do imóvel", () => {
  it("aceita pontos internos e sobre a borda da microrregião", () => {
    expect(geometryContainsPoint(polygon, [-39.5, -17.5])).toBe(true);
    expect(geometryContainsPoint(polygon, [-40, -17.5])).toBe(true);
  });

  it("recusa pontos externos e áreas vazadas", () => {
    const withHole: PropertyGeometry = {
      type: "Polygon",
      coordinates: [
        ...((polygon.coordinates as number[][][])),
        [
          [-39.7, -17.7],
          [-39.3, -17.7],
          [-39.3, -17.3],
          [-39.7, -17.3],
          [-39.7, -17.7],
        ],
      ],
    };

    expect(geometryContainsPoint(polygon, [-38.9, -17.5])).toBe(false);
    expect(geometryContainsPoint(withHole, [-39.5, -17.5])).toBe(false);
  });

  it("calcula limites e centro para enquadrar a área no mapa", () => {
    expect(geometryBounds(polygon)).toEqual([
      [-40, -18],
      [-39, -17],
    ]);
    expect(centerOfGeometry(polygon)).toEqual([-39.5, -17.5]);
  });

  it("localiza o imóvel em qualquer parte de uma microrregião multipolígono", () => {
    const multi: PropertyGeometry = {
      type: "MultiPolygon",
      coordinates: [
        polygon.coordinates,
        [
          [
            [-38, -16],
            [-37, -16],
            [-37, -15],
            [-38, -15],
            [-38, -16],
          ],
        ],
      ],
    };

    expect(geometryContainsPoint(multi, [-37.5, -15.5])).toBe(true);
  });
});
