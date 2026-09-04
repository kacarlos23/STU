import { describe, expect, it, vi } from "vitest";
import { getWorkerUrl } from "maplibre-gl";
import {
  buildDraftFeatures,
  editableVerticesFromGeometry,
  insertGeometryVertexNear,
  mapLibreWorkerUrl,
  moveGeometryVertex,
  polygonFromPoints,
  setGeoJsonSourceData,
  teixeiraDeFreitasCenter,
} from "@stu/shared/territory";

describe("desenho territorial", () => {
  it("fecha a área usando o primeiro ponto como último vértice", () => {
    const polygon = polygonFromPoints([
      [-47, -24],
      [-46, -24],
      [-46, -23],
    ]);
    const ring = polygon.coordinates as [number, number][][];

    expect(ring[0]).toHaveLength(4);
    expect(ring[0][3]).toEqual(ring[0][0]);
  });

  it("impede fechar uma área com menos de três pontos", () => {
    expect(() =>
      polygonFromPoints([
        [-47, -24],
        [-46, -24],
      ]),
    ).toThrow("Marque ao menos três pontos");
  });

  it("usa Teixeira de Freitas como centro inicial do mapa", () => {
    expect(teixeiraDeFreitasCenter).toEqual([-39.7451701, -17.5384774]);
  });

  it("configura o worker vetorial gerado pelo Vite", () => {
    expect(mapLibreWorkerUrl).toBeTruthy();
    expect(getWorkerUrl()).toBe(mapLibreWorkerUrl);
  });

  it("mantém os pontos numerados visíveis depois de fechar a área", () => {
    const points: [number, number][] = [
      [-39.76, -17.55],
      [-39.74, -17.55],
      [-39.74, -17.53],
    ];
    const features = buildDraftFeatures(polygonFromPoints(points), points) as {
      properties: { vertex?: number };
      geometry: { type: string };
    }[];

    expect(features.map((feature) => feature.geometry.type)).toEqual([
      "Polygon",
      "Point",
      "Point",
      "Point",
    ]);
    expect(
      features.slice(1).map((feature) => feature.properties.vertex),
    ).toEqual([1, 2, 3]);
  });

  it("atualiza a camada mesmo enquanto os blocos do mapa-base ainda carregam", () => {
    const setData = vi.fn();
    const once = vi.fn();
    const map = {
      getSource: () => ({ setData }),
      once,
    };

    setGeoJsonSourceData(map as never, "draft", {
      type: "FeatureCollection",
      features: [],
    });

    expect(setData).toHaveBeenCalledOnce();
    expect(once).not.toHaveBeenCalled();
  });

  it("move qualquer vértice e mantém o anel fechado", () => {
    const geometry = polygonFromPoints([
      [-39.76, -17.55],
      [-39.74, -17.55],
      [-39.74, -17.53],
    ]);
    const moved = moveGeometryVertex(geometry, [0, 0], [-39.77, -17.56]);
    const ring = moved.coordinates as [number, number][][];

    expect(ring[0][0]).toEqual([-39.77, -17.56]);
    expect(ring[0].at(-1)).toEqual([-39.77, -17.56]);
  });

  it("insere um novo ponto na borda mais próxima da área fechada", () => {
    const geometry = polygonFromPoints([
      [0, 0],
      [10, 0],
      [10, 10],
      [0, 10],
    ]);
    const expanded = insertGeometryVertexNear(geometry, [5, -1]);
    const ring = expanded.coordinates as [number, number][][];

    expect(ring[0]).toHaveLength(6);
    expect(ring[0]).toContainEqual([5, -1]);
    expect(ring[0].at(-1)).toEqual(ring[0][0]);
  });

  it("expõe todos os vértices de um multipolígono para edição", () => {
    const geometry = {
      type: "MultiPolygon" as const,
      coordinates: [
        [[[0, 0], [1, 0], [1, 1], [0, 0]]],
        [[[2, 2], [3, 2], [3, 3], [2, 2]]],
      ],
    };
    const vertices = editableVerticesFromGeometry(geometry);

    expect(vertices).toHaveLength(6);
    expect(vertices.at(-1)?.path).toEqual([1, 0, 2]);
  });

  it("oculta os vértices fora da tela de edição", () => {
    const geometry = polygonFromPoints([
      [-39.76, -17.55],
      [-39.74, -17.55],
      [-39.74, -17.53],
    ]);
    const features = buildDraftFeatures(geometry, [], false) as {
      geometry: { type: string };
    }[];

    expect(features).toHaveLength(1);
    expect(features[0].geometry.type).toBe("Polygon");
  });
});
