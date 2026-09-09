import { useCallback, useEffect, useId, useRef, useState } from "react";
import * as maplibregl from "maplibre-gl";
import type { GeoJSONSource, Map as MapLibreMap } from "maplibre-gl";
import workerUrl from "maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url";
import "maplibre-gl/dist/maplibre-gl.css";
import "./territory.css";
import "./territory-drawing.css";
import "./territory-archive.css";
import type { Session } from "../auth/types";
import { useAccessibleDialog } from "../accessibility/useAccessibleDialog";
import {
  parseOsmAreas,
  type OsmAreaCandidate,
  type TerritoryGeometry,
} from "./osmImport";
import { TerritoryColorPicker } from "./TerritoryColorPicker";
import { TerritoryImpactPanel, type TerritoryImpact } from "./TerritoryImpactPanel";
import {
  editableVerticesFromGeometry,
  insertGeometryVertexNear,
  isEditableAreaGeometry,
  moveGeometryVertex,
} from "./geometryEditing";

type Geometry = TerritoryGeometry;
type Feature = {
  type: "Feature";
  id: string;
  geometry: Geometry;
  properties: Record<string, string | number | boolean | string[] | null>;
};
type FeatureCollection = { type: "FeatureCollection"; features: Feature[] };
type ReferenceData = {
  selectedHealthUnitId: string;
  neighborhoods: {
    id: string;
    name: string;
    source: string;
    externalReference: string | null;
    color: string;
    geometry: Geometry;
    concurrencyToken: string;
  }[];
  agents: { id: string; displayName: string }[];
  healthUnits: { id: string; code: string; name: string }[];
};
type Unit = { id: string; code: string; name: string };
type EditorKind = "neighborhood" | "microregion";

export const mapLibreWorkerUrl = workerUrl;
maplibregl.setWorkerUrl(mapLibreWorkerUrl);

const emptyCollection: FeatureCollection = {
  type: "FeatureCollection",
  features: [],
};
export const teixeiraDeFreitasCenter: [number, number] = [
  -39.7451701, -17.5384774,
];
const rasterTilesUrl =
  import.meta.env.VITE_STU_RASTER_TILES_URL ||
  "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
const baseStyle = {
  version: 8 as const,
  glyphs: "https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf",
  sources: {
    osm: {
      type: "raster" as const,
      tiles: [rasterTilesUrl],
      tileSize: 256,
      attribution: "© OpenStreetMap contributors",
    },
  },
  layers: [
    {
      id: "osm",
      type: "raster" as const,
      source: "osm",
      paint: {
        "raster-opacity": 0.78,
        "raster-saturation": -0.35,
        "raster-contrast": -0.08,
      },
    },
  ],
};

export function TerritoryWorkspace({
  session,
  global = false,
}: {
  session: Session;
  global?: boolean;
}) {
  const mapNode = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibreMap | null>(null);
  const [units, setUnits] = useState<Unit[]>(
    session.healthUnit ? [session.healthUnit] : [],
  );
  const [unitId, setUnitId] = useState(session.healthUnit?.id ?? "");
  const [data, setData] = useState<FeatureCollection>(emptyCollection);
  const [archivedData, setArchivedData] =
    useState<FeatureCollection>(emptyCollection);
  const [microregionView, setMicroregionView] = useState<
    "active" | "archived"
  >("active");
  const [reference, setReference] = useState<ReferenceData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [editor, setEditor] = useState<EditorKind | null>(null);
  const [draftPoints, setDraftPoints] = useState<[number, number][]>([]);
  const [draftGeometry, setDraftGeometry] = useState<Geometry | null>(null);
  const [editing, setEditing] = useState<Feature | null>(null);
  const [territorySource, setTerritorySource] = useState("Manual");
  const [externalReference, setExternalReference] = useState("");
  const [territoryColor, setTerritoryColor] = useState("#2e8b72");
  const [osmCandidates, setOsmCandidates] = useState<OsmAreaCandidate[]>([]);
  const [selectedOsmId, setSelectedOsmId] = useState("");
  const [focusedFeatureId, setFocusedFeatureId] = useState("");
  const [focusedGeometry, setFocusedGeometry] = useState<Geometry | null>(null);
  const [previewed, setPreviewedState] = useState(false);
  const [impact, setImpact] = useState<TerritoryImpact | null>(null);
  const setPreviewed = useCallback((value: boolean) => {
    setPreviewedState(value);
    if (!value) setImpact(null);
  }, []);
  const [addingVertex, setAddingVertex] = useState(false);
  const [history, setHistory] = useState<
    { versionNumber: number; changeKind: string; changedAtUtc: string }[] | null
  >(null);
  const editorTitleId = useId();
  const editorDialogRef = useAccessibleDialog<HTMLFormElement>(
    Boolean(editor),
    cancel,
  );
  const canManage =
    global ||
    session.permissions.includes("*") ||
    session.permissions.includes("territory.manage");
  const canViewProperties =
    global ||
    session.permissions.includes("*") ||
    session.permissions.includes("properties.view");
  const hasDraftGeometry = Boolean(draftGeometry);
  const hasEditableDraftGeometry = isEditableAreaGeometry(draftGeometry);

  const load = useCallback(
    async (selected: string) => {
      if (!selected) return;
      setLoading(true);
      setError(null);
      try {
        const suffix = `?healthUnitId=${encodeURIComponent(selected)}`;
        const [
          mapResponse,
          referenceResponse,
          propertiesResponse,
          archivedResponse,
        ] =
          await Promise.all([
            fetch(`/api/territories/map${suffix}`, { credentials: "include" }),
            fetch(`/api/territories/reference-data${suffix}`, {
              credentials: "include",
            }),
            canViewProperties
              ? fetch(`/api/properties/map${suffix}`, {
                  credentials: "include",
                })
              : Promise.resolve(null),
            canManage
              ? fetch(`/api/territories/microregions/archived${suffix}`, {
                  credentials: "include",
                })
              : Promise.resolve(null),
          ]);
        if (
          !mapResponse.ok ||
          !referenceResponse.ok ||
          (propertiesResponse && !propertiesResponse.ok) ||
          (archivedResponse && !archivedResponse.ok)
        )
          throw new Error("Não foi possível carregar o território autorizado.");
        const territoryData = (await mapResponse.json()) as FeatureCollection;
        const propertyData = propertiesResponse
          ? ((await propertiesResponse.json()) as FeatureCollection)
          : emptyCollection;
        setData({
          type: "FeatureCollection",
          features: [...territoryData.features, ...propertyData.features],
        });
        setArchivedData(
          archivedResponse
            ? ((await archivedResponse.json()) as FeatureCollection)
            : emptyCollection,
        );
        setReference((await referenceResponse.json()) as ReferenceData);
      } catch (caught) {
        setError(
          caught instanceof Error
            ? caught.message
            : "Falha ao carregar o território.",
        );
      } finally {
        setLoading(false);
      }
    },
    [canManage, canViewProperties],
  );

  useEffect(() => {
    if (!global) return;
    fetch("/api/admin/health-units", { credentials: "include" })
      .then(async (response) => {
        if (!response.ok) throw new Error("Não foi possível listar as UBS.");
        return (await response.json()) as Unit[];
      })
      .then((items) => {
        setUnits(items);
        setUnitId((current) => current || items[0]?.id || "");
      })
      .catch((caught) =>
        setError(
          caught instanceof Error ? caught.message : "Falha ao listar UBS.",
        ),
      );
  }, [global]);

  useEffect(() => {
    void load(unitId);
  }, [load, unitId]);

  useEffect(() => {
    if (!mapNode.current || mapRef.current) return;
    const map = new maplibregl.Map({
      container: mapNode.current,
      style: baseStyle,
      center: teixeiraDeFreitasCenter,
      zoom: 12,
      attributionControl: {},
    });
    map.addControl(
      new maplibregl.NavigationControl({ showCompass: false }),
      "top-right",
    );
    map.on("load", () => {
      map.addSource("territories", { type: "geojson", data: emptyCollection });
      map.addSource("draft", { type: "geojson", data: emptyCollection });
      map.addLayer({
        id: "neighborhood-fill",
        type: "fill",
        source: "territories",
        filter: [
          "all",
          ["==", ["get", "entityType"], "neighborhood"],
          ["!=", ["geometry-type"], "Point"],
        ],
        paint: {
          "fill-color": territoryColorExpression("#2e8b72"),
          "fill-opacity": 0.24,
        },
      });
      map.addLayer({
        id: "neighborhood-line",
        type: "line",
        source: "territories",
        filter: [
          "all",
          ["==", ["get", "entityType"], "neighborhood"],
          ["!=", ["geometry-type"], "Point"],
        ],
        paint: {
          "line-color": territoryColorExpression("#17614f"),
          "line-width": 3,
          "line-opacity": 0.95,
        },
      });
      map.addLayer({
        id: "neighborhood-point",
        type: "circle",
        source: "territories",
        filter: [
          "all",
          ["==", ["get", "entityType"], "neighborhood"],
          ["==", ["geometry-type"], "Point"],
        ],
        paint: {
          "circle-color": territoryColorExpression("#356d5d"),
          "circle-radius": 7,
          "circle-stroke-color": "#fff",
          "circle-stroke-width": 2,
        },
      });
      map.addLayer({
        id: "microregion-fill",
        type: "fill",
        source: "territories",
        filter: ["==", ["get", "entityType"], "microregion"],
        paint: {
          "fill-color": territoryColorExpression("#4f9a7d"),
          "fill-opacity": 0.36,
        },
      });
      map.addLayer({
        id: "microregion-line",
        type: "line",
        source: "territories",
        filter: ["==", ["get", "entityType"], "microregion"],
        paint: {
          "line-color": territoryColorExpression("#244f45"),
          "line-width": 2,
        },
      });
      map.addLayer({
        id: "property-fill",
        type: "fill",
        source: "territories",
        minzoom: 15,
        filter: [
          "all",
          ["==", ["get", "entityType"], "property"],
          ["!=", ["geometry-type"], "Point"],
        ],
        paint: { "fill-color": coverageColor(), "fill-opacity": 0.72 },
      });
      map.addLayer({
        id: "property-line",
        type: "line",
        source: "territories",
        minzoom: 15,
        filter: [
          "all",
          ["==", ["get", "entityType"], "property"],
          ["!=", ["geometry-type"], "Point"],
        ],
        paint: { "line-color": "#fff", "line-width": 1.5 },
      });
      map.addLayer({
        id: "property-point",
        type: "circle",
        source: "territories",
        minzoom: 15,
        filter: [
          "all",
          ["==", ["get", "entityType"], "property"],
          ["==", ["geometry-type"], "Point"],
        ],
        paint: {
          "circle-color": coverageColor(),
          "circle-radius": 6,
          "circle-stroke-color": "#fff",
          "circle-stroke-width": 1.5,
        },
      });
      map.addLayer({
        id: "property-label",
        type: "symbol",
        source: "territories",
        minzoom: 16,
        filter: ["==", ["get", "entityType"], "property"],
        layout: {
          "text-field": [
            "concat",
            ["get", "houseNumber"],
            " · F ",
            ["get", "familyNumber"],
          ],
          "text-font": ["Noto Sans Regular"],
          "text-size": 11,
          "text-offset": [0, 1.25],
          "text-anchor": "top",
          "text-allow-overlap": false,
        },
        paint: {
          "text-color": "#173f37",
          "text-halo-color": "#fff",
          "text-halo-width": 1.5,
        },
      });
      map.addLayer({
        id: "draft-fill",
        type: "fill",
        source: "draft",
        filter: [
          "in",
          ["geometry-type"],
          ["literal", ["Polygon", "MultiPolygon"]],
        ],
        paint: { "fill-color": "#f27a2e", "fill-opacity": 0.38 },
      });
      map.addLayer({
        id: "draft-line",
        type: "line",
        source: "draft",
        filter: [
          "in",
          ["geometry-type"],
          ["literal", ["LineString", "Polygon", "MultiPolygon"]],
        ],
        paint: {
          "line-color": "#b83e0b",
          "line-width": 5,
          "line-dasharray": [2, 1],
        },
      });
      map.addLayer({
        id: "draft-points",
        type: "circle",
        source: "draft",
        filter: ["==", ["geometry-type"], "Point"],
        paint: {
          "circle-color": "#e2672c",
          "circle-radius": 10,
          "circle-stroke-color": "#fff",
          "circle-stroke-width": 3,
        },
      });
      map.addLayer({
        id: "draft-point-labels",
        type: "symbol",
        source: "draft",
        filter: ["==", ["geometry-type"], "Point"],
        layout: {
          "text-field": ["to-string", ["get", "vertex"]],
          "text-font": ["Noto Sans Regular"],
          "text-size": 10,
          "text-allow-overlap": true,
        },
        paint: { "text-color": "#fff" },
      });
    });
    mapRef.current = map;
    if (import.meta.env.DEV) {
      const updateDiagnostics = () => {
        if (!mapNode.current) return;
        mapNode.current.dataset.layerOrder =
          map
            .getStyle()
            .layers?.map((layer) => layer.id)
            .join(",") ?? "";
        const renderedOverlays = map
          .queryRenderedFeatures()
          .filter(
            (feature) =>
              feature.source === "territories" || feature.source === "draft",
          );
        mapNode.current.dataset.renderedOverlays = String(
          renderedOverlays.length,
        );
        mapNode.current.dataset.renderedOverlayLayers = [
          ...new Set(renderedOverlays.map((feature) => feature.layer.id)),
        ].join(",");
        mapNode.current.dataset.draftLineColor = String(
          map.getPaintProperty("draft-line", "line-color"),
        );
        const draftData = (
          map.getSource("draft") as GeoJSONSource | undefined
        )?.serialize().data as FeatureCollection | undefined;
        mapNode.current.dataset.draftFeatureCount = String(
          draftData?.features?.length ?? 0,
        );
      };
      map.on("idle", updateDiagnostics);
    }
    return () => {
      map.remove();
      mapRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const update = () => {
      setGeoJsonSourceData(map, "territories", data);
      setFocusedFeatureId("");
      setFocusedGeometry(null);
      map.easeTo({
        center: teixeiraDeFreitasCenter,
        zoom: 12,
        duration: 500,
      });
    };
    map.getSource("territories") ? update() : map.once("load", update);
  }, [data]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const features = buildDraftFeatures(
      draftGeometry ?? focusedGeometry,
      focusedGeometry && !draftGeometry ? [] : draftPoints,
      Boolean(editor),
    );
    const update = () =>
      setGeoJsonSourceData(map, "draft", {
        type: "FeatureCollection",
        features,
      });
    map.getSource("draft") ? update() : map.once("load", update);
  }, [draftGeometry, draftPoints, editor, focusedGeometry]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !editor) return;
    const click = (event: maplibregl.MapMouseEvent) => {
      event.preventDefault();
      if (hasEditableDraftGeometry) {
        if (!addingVertex || map.queryRenderedFeatures(event.point, { layers: ["draft-points"] }).length > 0) return;
        setDraftGeometry((geometry) =>
          geometry ? insertGeometryVertexNear(geometry, [event.lngLat.lng, event.lngLat.lat]) : geometry,
        );
        setAddingVertex(false);
        setPreviewed(false);
        setError(null);
        setNotice("Novo ponto inserido na borda mais próxima. Arraste-o para ajustar a área.");
        return;
      }
      if (hasDraftGeometry) return;
      setDraftGeometry(null);
      setDraftPoints((points) => [
        ...points,
        [event.lngLat.lng, event.lngLat.lat],
      ]);
      setPreviewed(false);
      setError(null);
    };
    map.doubleClickZoom.disable();
    map.getCanvas().style.cursor = addingVertex || !hasDraftGeometry ? "crosshair" : "";
    map.on("click", click);
    return () => {
      map.off("click", click);
      map.doubleClickZoom.enable();
      map.getCanvas().style.cursor = "";
    };
  }, [addingVertex, editor, hasDraftGeometry, hasEditableDraftGeometry]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !editor || !hasEditableDraftGeometry) return;
    let activeMove: ((event: maplibregl.MapMouseEvent) => void) | null = null;
    let activeEnd: (() => void) | null = null;
    const startDrag = (event: maplibregl.MapLayerMouseEvent) => {
      if (addingVertex) return;
      const encodedPath = String(event.features?.[0]?.properties?.vertexPath ?? "");
      const path = encodedPath.split(".").map(Number);
      if (!encodedPath || path.some((part) => !Number.isInteger(part))) return;
      event.preventDefault();
      map.dragPan.disable();
      map.getCanvas().style.cursor = "grabbing";
      activeMove = (moveEvent) => {
        setDraftGeometry((geometry) =>
          geometry
            ? moveGeometryVertex(geometry, path, [moveEvent.lngLat.lng, moveEvent.lngLat.lat])
            : geometry,
        );
        setPreviewed(false);
      };
      activeEnd = () => {
        if (activeMove) map.off("mousemove", activeMove);
        if (activeEnd) map.off("mouseup", activeEnd);
        activeMove = null;
        activeEnd = null;
        map.dragPan.enable();
        map.getCanvas().style.cursor = "pointer";
        setNotice("Ponto reposicionado. Valide novamente o impacto antes de salvar.");
      };
      map.on("mousemove", activeMove);
      map.on("mouseup", activeEnd);
    };
    const showPointer = () => {
      if (!addingVertex) map.getCanvas().style.cursor = "pointer";
    };
    const clearPointer = () => {
      if (!activeMove) map.getCanvas().style.cursor = addingVertex ? "crosshair" : "";
    };
    map.on("mousedown", "draft-points", startDrag);
    map.on("mouseenter", "draft-points", showPointer);
    map.on("mouseleave", "draft-points", clearPointer);
    return () => {
      map.off("mousedown", "draft-points", startDrag);
      map.off("mouseenter", "draft-points", showPointer);
      map.off("mouseleave", "draft-points", clearPointer);
      if (activeMove) map.off("mousemove", activeMove);
      if (activeEnd) map.off("mouseup", activeEnd);
      map.dragPan.enable();
    };
  }, [addingVertex, editor, hasEditableDraftGeometry]);

  function closePolygon() {
    if (draftPoints.length < 3) {
      setError("Marque ao menos três pontos no mapa.");
      return;
    }
    setDraftGeometry(polygonFromPoints(draftPoints));
    setDraftPoints([]);
    setAddingVertex(false);
    setPreviewed(false);
    setError(null);
    setNotice(
      "Área fechada. Os pontos numerados permanecem visíveis para conferência.",
    );
  }

  function useCenterPoint() {
    const center = mapRef.current?.getCenter();
    if (!center) return;
    setDraftGeometry({ type: "Point", coordinates: [center.lng, center.lat] });
    setDraftPoints([]);
    setAddingVertex(false);
  }

  function start(kind: EditorKind, feature?: Feature) {
    setFocusedGeometry(null);
    setEditor(kind);
    setEditing(feature ?? null);
    setDraftGeometry(feature?.geometry ?? null);
    setDraftPoints([]);
    setTerritorySource(String(feature?.properties.source ?? "Manual"));
    setExternalReference(String(feature?.properties.externalReference ?? ""));
    setTerritoryColor(
      String(
        feature?.properties.color ??
          (kind === "neighborhood" ? "#2e8b72" : "#4f9a7d"),
      ),
    );
    setOsmCandidates([]);
    setSelectedOsmId("");
    setPreviewed(false);
    setAddingVertex(false);
    setHistory(null);
    setError(null);
    setNotice(null);
  }

  function cancel() {
    setEditor(null);
    setEditing(null);
    setDraftGeometry(null);
    setDraftPoints([]);
    setOsmCandidates([]);
    setSelectedOsmId("");
    setPreviewed(false);
    setAddingVertex(false);
  }

  function focusGeometry(geometry: Geometry, id = "") {
    const map = mapRef.current;
    if (!map) return;
    setFocusedFeatureId(id);
    setFocusedGeometry(id ? geometry : null);
    if (geometry.type === "Point") {
      const coordinate = geometry.coordinates as [number, number];
      map.easeTo({
        center: coordinate,
        zoom: Math.max(map.getZoom(), 16),
        duration: 700,
      });
      return;
    }
    const bounds = new maplibregl.LngLatBounds();
    visitCoordinates(geometry.coordinates, (coordinate) =>
      bounds.extend(coordinate),
    );
    if (!bounds.isEmpty())
      map.fitBounds(bounds, { padding: 85, maxZoom: 16, duration: 700 });
  }

  async function mutation(
    path: string,
    method: "POST" | "PUT",
    body?: unknown,
  ) {
    const csrf = await fetch("/api/auth/csrf", { credentials: "include" });
    if (!csrf.ok) throw new Error("Sua sessão precisa ser renovada.");
    const { token } = (await csrf.json()) as { token: string };
    const response = await fetch(path, {
      method,
      credentials: "include",
      headers: { "Content-Type": "application/json", "X-STU-CSRF": token },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) {
      const problem = (await response.json().catch(() => ({}))) as {
        detail?: string;
        errors?: Record<string, string[]>;
      };
      throw new Error(
        problem.detail ??
          Object.values(problem.errors ?? {})[0]?.[0] ??
          "Não foi possível concluir a operação.",
      );
    }
    return response.status === 204 ? null : await response.json();
  }

  async function submit(
    event: React.FormEvent<HTMLFormElement>,
    preview = false,
  ) {
    event.preventDefault();
    setError(null);
    setNotice(null);
    const fields = new FormData(event.currentTarget);
    if (!draftGeometry) {
      setError("Desenhe ou importe a geometria antes de continuar.");
      return;
    }
    const common = {
      name: String(fields.get("name") ?? ""),
      geometry: draftGeometry,
      source: territorySource,
      color: territoryColor,
      expectedVersion: editing?.properties.concurrencyToken ?? null,
    };
    try {
      setLoading(true);
      if (editor === "neighborhood") {
        const path = editing
          ? `/api/territories/neighborhoods/${editing.id}`
          : "/api/territories/neighborhoods";
        const result = (await mutation(path, editing ? "PUT" : "POST", {
          ...common,
          externalReference: externalReference.trim() || null,
        })) as { adjustedToExistingBoundaries?: boolean };
        setNotice(
          result.adjustedToExistingBoundaries
            ? "Bairro salvo e encaixado automaticamente nos limites existentes."
            : "Bairro salvo e versionado.",
        );
      } else {
        const body = {
          ...common,
          microregionId: editing?.id ?? null,
          code: String(fields.get("code") ?? ""),
          healthUnitId: unitId,
          assignedAgentId: String(fields.get("assignedAgentId") ?? "") || null,
        };
        if (preview) {
          const result = (await mutation(
            "/api/territories/microregions/preview",
            "POST",
            body,
          )) as TerritoryImpact & {
            adjustedToExistingBoundaries?: boolean;
            after?: { geometry?: Geometry };
          };
          if (result.after?.geometry) {
            setDraftGeometry(result.after.geometry);
            setDraftPoints([]);
            focusGeometry(result.after.geometry);
          }
          setPreviewed(result.valid);
          setImpact(result);
          setNotice(
            !result.valid
              ? "A alteração possui impedimentos. Consulte os imóveis afetados abaixo."
              : result.adjustedToExistingBoundaries
              ? "Pré-visualização ajustada às margens existentes. Confira o novo contorno antes de salvar."
              : "Pré-visualização validada: o limite já está encaixado.",
          );
          return;
        }
        if (!previewed) {
          setError("Valide o impacto antes de salvar a microrregião.");
          return;
        }
        const path = editing
          ? `/api/territories/microregions/${editing.id}`
          : "/api/territories/microregions";
        const result = (await mutation(
          path,
          editing ? "PUT" : "POST",
          body,
        )) as { adjustedToExistingBoundaries?: boolean };
        setNotice(
          editing?.properties.archivedAtUtc
            ? "Microrregião arquivada atualizada, auditada e versionada. Ela permanece arquivada."
            : result.adjustedToExistingBoundaries
            ? "Microrregião salva e encaixada automaticamente nos limites existentes."
            : "Microrregião salva, auditada e versionada.",
        );
      }
      cancel();
      await load(unitId);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Falha ao salvar.");
    } finally {
      setLoading(false);
    }
  }

  async function archive(feature: Feature) {
    if (
      !confirm(
        `Arquivar ${String(feature.properties.name)}? O histórico será preservado.`,
      )
    )
      return;
    try {
      setLoading(true);
      await mutation(
        `/api/territories/${feature.properties.entityType === "neighborhood" ? "neighborhoods" : "microregions"}/${feature.id}/archive`,
        "POST",
      );
      setNotice("Registro arquivado com sucesso.");
      await load(unitId);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Falha ao arquivar.");
    } finally {
      setLoading(false);
    }
  }

  async function restore(feature: Feature) {
    if (
      !confirm(
        `Desarquivar ${String(feature.properties.name)}? O sistema verificará identificadores, bairros e limites antes de reativar.`,
      )
    )
      return;
    try {
      setLoading(true);
      await mutation(
        `/api/territories/microregions/${feature.id}/restore`,
        "POST",
      );
      setNotice("Microrregião desarquivada, auditada e versionada.");
      setFocusedFeatureId("");
      setFocusedGeometry(null);
      await load(unitId);
    } catch (caught) {
      setError(
        caught instanceof Error
          ? caught.message
          : "Não foi possível desarquivar a microrregião.",
      );
    } finally {
      setLoading(false);
    }
  }

  async function showHistory(id: string) {
    const response = await fetch(
      `/api/territories/microregions/${id}/versions`,
      { credentials: "include" },
    );
    if (!response.ok) {
      setError("Não foi possível carregar as versões.");
      return;
    }
    setHistory(
      (await response.json()) as {
        versionNumber: number;
        changeKind: string;
        changedAtUtc: string;
      }[],
    );
  }

  async function importGeometryFile(file: File | undefined) {
    if (!file) return;
    try {
      if (file.size > 20 * 1024 * 1024)
        throw new Error("O arquivo deve ter no máximo 20 MB.");
      const content = await file.text();
      if (
        file.name.toLowerCase().endsWith(".osm") ||
        file.name.toLowerCase().endsWith(".xml")
      ) {
        const candidates = parseOsmAreas(content);
        if (candidates.length === 0)
          throw new Error(
            "Nenhuma área fechada ou limite completo foi encontrado no arquivo OSM.",
          );
        setOsmCandidates(candidates);
        applyOsmCandidate(candidates[0]);
        setNotice(
          `${candidates.length} área(s) detectada(s). Confira a opção selecionada antes de salvar.`,
        );
        return;
      }
      const json = JSON.parse(content) as {
        type: string;
        geometry?: Geometry;
        features?: { geometry: Geometry }[];
      };
      const geometry =
        json.type === "FeatureCollection"
          ? json.features?.[0]?.geometry
          : json.type === "Feature"
            ? json.geometry
            : (json as Geometry);
      if (
        !geometry ||
        !["Point", "Polygon", "MultiPolygon"].includes(geometry.type)
      )
        throw new Error("O arquivo não contém uma geometria compatível.");
      setDraftGeometry(geometry);
      setDraftPoints([]);
      setAddingVertex(false);
      setOsmCandidates([]);
      setSelectedOsmId("");
      setTerritorySource("GeoJsonImport");
      setPreviewed(false);
      setError(null);
      focusGeometry(geometry);
      setNotice("Geometria GeoJSON importada. Confira o limite no mapa.");
    } catch (caught) {
      setError(
        caught instanceof Error
          ? caught.message
          : "Não foi possível interpretar o arquivo de mapa.",
      );
    }
  }

  function applyOsmCandidate(candidate: OsmAreaCandidate) {
    setSelectedOsmId(candidate.id);
    setDraftGeometry(candidate.geometry);
    setDraftPoints([]);
    setAddingVertex(false);
    setTerritorySource("OpenStreetMap");
    setExternalReference(candidate.externalReference);
    setPreviewed(false);
    setError(null);
    focusGeometry(candidate.geometry);
  }

  const microregions = data.features.filter(
    (feature) => feature.properties.entityType === "microregion",
  );
  const archivedMicroregions = archivedData.features.filter(
    (feature) => feature.properties.entityType === "microregion",
  );
  const visibleMicroregions =
    microregionView === "archived" ? archivedMicroregions : microregions;
  const neighborhoods = reference?.neighborhoods ?? [];
  const editableVertexCount = editableVerticesFromGeometry(draftGeometry).length;
  return (
    <section className="territory-workspace">
      <header className="territory-heading">
        <div>
          <span>Território georreferenciado</span>
          <h2>Mapa de bairros e microrregiões</h2>
          <p>Os dados exibidos respeitam a UBS e as permissões da conta.</p>
        </div>
        <div className="territory-actions">
          {global && (
            <label>
              UBS
              <select
                value={unitId}
                onChange={(event) => setUnitId(event.target.value)}
              >
                {units.map((unit) => (
                  <option key={unit.id} value={unit.id}>
                    {unit.code} — {unit.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          {canManage && (
            <>
              <button onClick={() => start("neighborhood")} type="button">
                ＋ Bairro
              </button>
              <button
                className="territory-primary"
                onClick={() => start("microregion")}
                type="button"
              >
                ＋ Microrregião
              </button>
            </>
          )}
        </div>
      </header>
      {error && !editor && (
        <div className="territory-message territory-error" role="alert">
          {error}
        </div>
      )}
      {notice && !editor && (
        <div className="territory-message territory-success" role="status">
          {notice}
        </div>
      )}
      <div className="territory-layout">
        <div
          aria-label="Mapa territorial interativo. Os bairros e microrregiões também estão disponíveis na lista ao lado."
          className="territory-map"
          ref={mapNode}
          role="region"
        >
          <span className="sr-only">
            Use a lista de bairros e microrregiões para selecionar uma área sem
            depender do mapa visual.
          </span>
          {loading && <i>Atualizando…</i>}
        </div>
        <aside className="territory-list">
          <div>
            <strong>Bairros</strong>
            <span>{neighborhoods.length} ativo(s)</span>
          </div>
          {neighborhoods.map((item) => {
            const feature: Feature = {
              type: "Feature",
              id: item.id,
              geometry: item.geometry,
              properties: {
                entityType: "neighborhood",
                id: item.id,
                name: item.name,
                source: item.source,
                externalReference: item.externalReference,
                color: item.color,
                concurrencyToken: item.concurrencyToken,
              },
            };
            return (
              <article
                aria-label={`Visualizar bairro ${item.name}`}
                className={
                  focusedFeatureId === item.id ? "territory-card-focused" : ""
                }
                key={item.id}
                onClick={() => focusGeometry(item.geometry, item.id)}
                onKeyDown={(event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    focusGeometry(item.geometry, item.id);
                  }
                }}
                role="button"
                tabIndex={0}
              >
                <div>
                  <b className="territory-kind-label">
                    <i style={{ backgroundColor: item.color }} /> BAIRRO
                  </b>
                  <strong>{item.name}</strong>
                  <small>
                    {item.geometry.type === "Point"
                      ? "Referência por ponto"
                      : "Contorno cadastrado"}{" "}
                    · clique para visualizar
                  </small>
                </div>
                {canManage && (
                  <span>
                    <button
                      onClick={(event) => {
                        event.stopPropagation();
                        start("neighborhood", feature);
                      }}
                      type="button"
                    >
                      Editar
                    </button>
                    <button
                      onClick={(event) => {
                        event.stopPropagation();
                        void archive(feature);
                      }}
                      type="button"
                    >
                      Arquivar
                    </button>
                  </span>
                )}
              </article>
            );
          })}
          <div className="territory-microregion-heading">
            <span>
              <strong>Microrregiões</strong>
              <small>
                {microregionView === "archived"
                  ? `${archivedMicroregions.length} arquivada(s)`
                  : `${microregions.length} ativa(s)`}
              </small>
            </span>
            {canManage && (
              <label>
                Exibir
                <select
                  aria-label="Exibir microrregiões"
                  value={microregionView}
                  onChange={(event) => {
                    setMicroregionView(
                      event.target.value as "active" | "archived",
                    );
                    setFocusedFeatureId("");
                    setFocusedGeometry(null);
                    setHistory(null);
                  }}
                >
                  <option value="active">Ativas</option>
                  <option value="archived">Arquivadas</option>
                </select>
              </label>
            )}
          </div>
          {visibleMicroregions.length === 0 && (
            <p>
              {microregionView === "archived"
                ? "Nenhuma microrregião arquivada nesta UBS."
                : "Cadastre ou importe os primeiros limites para esta UBS."}
            </p>
          )}
          {visibleMicroregions.map((feature) => (
            <article
              aria-label={`Visualizar microrregião ${String(feature.properties.name)}`}
              className={[
                focusedFeatureId === feature.id
                  ? "territory-card-focused"
                  : "",
                feature.properties.archivedAtUtc
                  ? "territory-card-archived"
                  : "",
              ]
                .filter(Boolean)
                .join(" ")}
              key={feature.id}
              onClick={() => focusGeometry(feature.geometry, feature.id)}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  focusGeometry(feature.geometry, feature.id);
                }
              }}
              role="button"
              tabIndex={0}
            >
              <div>
                <b className="territory-kind-label">
                  <i
                    style={{
                      backgroundColor: String(
                        feature.properties.color ?? "#4f9a7d",
                      ),
                    }}
                  />{" "}
                  {String(feature.properties.code)}
                  {feature.properties.archivedAtUtc ? " · ARQUIVADA" : ""}
                </b>
                <strong>{String(feature.properties.name)}</strong>
                <small>
                  {Array.isArray(feature.properties.neighborhoodIds)
                    ? `${feature.properties.neighborhoodIds.length} bairro(s) · `
                    : ""}
                  {feature.properties.assignedAgentId
                    ? "Agente atribuído"
                    : "Sem agente"}{" "}
                  · clique para visualizar
                </small>
                {typeof feature.properties.archivedAtUtc === "string" && (
                  <small>
                    Arquivada em{" "}
                    {new Date(
                      feature.properties.archivedAtUtc,
                    ).toLocaleString("pt-BR")}
                  </small>
                )}
              </div>
              {canManage && (
                <span>
                  <button
                    onClick={(event) => {
                      event.stopPropagation();
                      start("microregion", feature);
                    }}
                    type="button"
                  >
                    Editar
                  </button>
                  <button
                    onClick={(event) => {
                      event.stopPropagation();
                      void showHistory(feature.id);
                    }}
                    type="button"
                  >
                    Histórico
                  </button>
                  {!feature.properties.archivedAtUtc && (
                    <button
                      onClick={(event) => {
                        event.stopPropagation();
                        void archive(feature);
                      }}
                      type="button"
                    >
                      Arquivar
                    </button>
                  )}
                  {feature.properties.archivedAtUtc && (
                    <button
                      className="territory-restore-action"
                      onClick={(event) => {
                        event.stopPropagation();
                        void restore(feature);
                      }}
                      type="button"
                    >
                      Desarquivar
                    </button>
                  )}
                </span>
              )}
            </article>
          ))}
          {history && (
            <div className="territory-history">
              <strong>Versões</strong>
              {history.map((item) => (
                <p key={item.versionNumber}>
                  <b>v{item.versionNumber}</b>{" "}
                  {translateAction(item.changeKind)}
                  <small>
                    {new Date(item.changedAtUtc).toLocaleString("pt-BR")}
                  </small>
                </p>
              ))}
            </div>
          )}
        </aside>
      </div>
      {editor && (
        <div
          className="territory-editor-backdrop"
          onMouseDown={(event) => {
            if (event.currentTarget === event.target) cancel();
          }}
          role="presentation"
        >
          <form
            aria-labelledby={editorTitleId}
            aria-modal="true"
            className="territory-editor"
            onChange={() => {
              if (editor === "microregion") setPreviewed(false);
            }}
            onSubmit={(event) => void submit(event)}
            ref={editorDialogRef}
            role="dialog"
            tabIndex={-1}
          >
            <header>
              <div>
                <span>
                  {editing?.properties.archivedAtUtc
                    ? "Editar registro arquivado"
                    : editing
                      ? "Editar e versionar"
                      : "Novo cadastro"}
                </span>
                <h3 id={editorTitleId}>
                  {editor === "neighborhood" ? "Bairro" : "Microrregião"}
                </h3>
              </div>
              <button aria-label="Fechar editor de território" onClick={cancel} type="button">
                ×
              </button>
            </header>
            {editor === "microregion" &&
              editing?.properties.archivedAtUtc && (
                <p className="territory-archived-warning" role="status">
                  Esta microrregião continuará arquivada após a edição. As
                  alterações serão registradas no histórico.
                </p>
              )}
            <label>
              Nome
              <input
                defaultValue={String(editing?.properties.name ?? "")}
                name="name"
                required
                maxLength={160}
              />
            </label>
            {editor === "microregion" && (
              <>
                <label>
                  Código
                  <input
                    defaultValue={String(editing?.properties.code ?? "")}
                    name="code"
                    required
                    maxLength={32}
                  />
                  <small className="territory-field-help">
                    Identificador curto e único na UBS. Ex.: MR-01.
                  </small>
                </label>
                <label>
                  Agente responsável
                  <select
                    defaultValue={String(
                      editing?.properties.assignedAgentId ?? "",
                    )}
                    name="assignedAgentId"
                  >
                    <option value="">Sem agente</option>
                    {reference?.agents.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.displayName}
                      </option>
                    ))}
                  </select>
                </label>
              </>
            )}
            {editor === "neighborhood" && (
              <label>
                Referência externa (opcional)
                <input
                  name="externalReference"
                  onChange={(event) => setExternalReference(event.target.value)}
                  placeholder="Ex.: OSM relation/123"
                  value={externalReference}
                />
              </label>
            )}
            <TerritoryColorPicker
              onChange={(color) => {
                setTerritoryColor(color);
                if (editor === "microregion") setPreviewed(false);
              }}
              value={territoryColor}
            />
            <label>
              Origem
              <select
                name="source"
                onChange={(event) => setTerritorySource(event.target.value)}
                value={territorySource}
              >
                <option value="Manual">Desenho manual</option>
                <option value="GeoJsonImport">Importação GeoJSON</option>
                <option value="OpenStreetMap">OpenStreetMap</option>
              </select>
            </label>
            <div className="geometry-tools">
              <strong>Geometria</strong>
              <p>
                Desenhe clicando em pelo menos três locais do mapa ou importe um
                arquivo .osm/.xml exportado pelo OpenStreetMap. Depois de fechar
                a área, arraste qualquer ponto numerado para reposicioná-lo. Use
                “Adicionar ponto” e clique no mapa para inserir um novo vértice
                na borda mais próxima. O encaixe automático com áreas vizinhas
                continua sendo aplicado na validação.
              </p>
              <span>
                {!draftGeometry && (
                  <>
                    <button
                      className={draftPoints.length >= 3 ? "drawing-ready" : ""}
                      onClick={closePolygon}
                      type="button"
                    >
                      Fechar área ({draftPoints.length}/3)
                    </button>
                    <button
                      disabled={draftPoints.length === 0}
                      onClick={() => {
                        setDraftPoints((points) => points.slice(0, -1));
                        setPreviewed(false);
                      }}
                      type="button"
                    >
                      Desfazer ponto
                    </button>
                  </>
                )}
                {hasEditableDraftGeometry && (
                  <button
                    aria-pressed={addingVertex}
                    className={addingVertex ? "drawing-ready" : ""}
                    onClick={() => setAddingVertex((current) => !current)}
                    type="button"
                  >
                    {addingVertex ? "Cancelar novo ponto" : "Adicionar ponto"}
                  </button>
                )}
                {editor === "neighborhood" && (
                  <button onClick={useCenterPoint} type="button">
                    Usar centro como ponto
                  </button>
                )}
                <label>
                  Importar mapa
                  <input
                    accept=".json,.geojson,.osm,.xml,application/geo+json,application/xml,text/xml"
                    onChange={(event) => {
                      const input = event.currentTarget;
                      void importGeometryFile(input.files?.[0]).finally(() => {
                        input.value = "";
                      });
                    }}
                    type="file"
                  />
                </label>
                <button
                  onClick={() => {
                    setDraftGeometry(null);
                    setDraftPoints([]);
                    setOsmCandidates([]);
                    setSelectedOsmId("");
                    setPreviewed(false);
                    setAddingVertex(false);
                  }}
                  type="button"
                >
                  Limpar
                </button>
              </span>
              {osmCandidates.length > 0 && (
                <label className="osm-candidate-picker">
                  Área encontrada no arquivo
                  <select
                    onChange={(event) => {
                      const candidate = osmCandidates.find(
                        (item) => item.id === event.target.value,
                      );
                      if (candidate) applyOsmCandidate(candidate);
                    }}
                    value={selectedOsmId}
                  >
                    {osmCandidates.map((candidate) => (
                      <option key={candidate.id} value={candidate.id}>
                        {candidate.label} — {candidate.detail}
                      </option>
                    ))}
                  </select>
                  <small>
                    Confira o contorno no mapa. Se o arquivo tiver mais de uma
                    área, selecione a correta antes de salvar.
                  </small>
                </label>
              )}
              <small aria-live="polite" role="status">
                {draftGeometry
                  ? `${draftGeometry.type} pronto para validação${editableVertexCount ? ` com ${editableVertexCount} vértice(s) editável(is)` : ""}${addingVertex ? " · clique no mapa para inserir o novo ponto" : ""}`
                  : draftPoints.length
                    ? `${draftPoints.length} ponto(s) marcado(s)`
                    : "Clique no mapa para iniciar o desenho"}
              </small>
            </div>
            {error && <div className="territory-message territory-error" role="alert">{error}</div>}
            {notice && <div className="territory-message" role="status">{notice}</div>}
            {impact && <TerritoryImpactPanel impact={impact} />}
            <p className="territory-warning">
              Não inclua nomes de moradores, dados pessoais ou informações
              clínicas.
            </p>
            <footer>
              <button onClick={cancel} type="button">
                Cancelar
              </button>
              {editor === "microregion" && (
                <button
                  disabled={loading}
                  onClick={(event) => {
                    const form = event.currentTarget.form;
                    if (form)
                      void submit(
                        {
                          preventDefault: () => {},
                          currentTarget: form,
                        } as unknown as React.FormEvent<HTMLFormElement>,
                        true,
                      );
                  }}
                  type="button"
                >
                  Validar impacto
                </button>
              )}
              <button
                className="territory-primary"
                disabled={loading || (editor === "microregion" && !previewed)}
                type="submit"
              >
                {loading ? "Salvando…" : "Salvar"}
              </button>
            </footer>
          </form>
        </div>
      )}
    </section>
  );
}

function visitCoordinates(
  value: unknown,
  visitor: (coordinate: [number, number]) => void,
) {
  if (!Array.isArray(value)) return;
  if (
    value.length >= 2 &&
    typeof value[0] === "number" &&
    typeof value[1] === "number"
  ) {
    visitor([value[0], value[1]]);
    return;
  }
  for (const child of value) visitCoordinates(child, visitor);
}

function translateAction(action: string) {
  return action === "Create"
    ? "criação"
    : action === "Update"
      ? "edição"
      : action === "Archive"
        ? "arquivamento"
        : "reativação";
}

function coverageColor() {
  return [
    "match",
    ["get", "coverageStatus"],
    "overdue",
    "#c85b36",
    "neverVisited",
    "#d18a2f",
    "covered",
    "#207764",
    "#71817c",
  ] as maplibregl.ExpressionSpecification;
}

function territoryColorExpression(fallback: string) {
  return [
    "coalesce",
    ["get", "color"],
    fallback,
  ] as maplibregl.ExpressionSpecification;
}

export function polygonFromPoints(points: [number, number][]): Geometry {
  if (points.length < 3)
    throw new Error("Marque ao menos três pontos no mapa.");
  return { type: "Polygon", coordinates: [[...points, points[0]]] };
}

export function buildDraftFeatures(
  draftGeometry: Geometry | null,
  draftPoints: [number, number][],
  showVertices = true,
) {
  const features: object[] = [];
  if (draftGeometry)
    features.push({
      type: "Feature",
      properties: {},
      geometry: draftGeometry,
    });
  if (draftGeometry && showVertices)
    editableVerticesFromGeometry(draftGeometry).forEach((vertex) =>
      features.push({
        type: "Feature",
        properties: { vertex: vertex.number, vertexPath: vertex.path.join(".") },
        geometry: { type: "Point", coordinates: vertex.coordinate },
      }),
    );
  if (!draftGeometry && draftPoints.length > 1)
    features.push({
      type: "Feature",
      properties: {},
      geometry: { type: "LineString", coordinates: draftPoints },
    });
  if (!draftGeometry) draftPoints.forEach((coordinates, index) =>
    features.push({
      type: "Feature",
      properties: { vertex: index + 1 },
      geometry: { type: "Point", coordinates },
    }),
  );
  return features;
}

export function setGeoJsonSourceData(
  map: Pick<MapLibreMap, "getSource" | "once">,
  sourceId: string,
  data: object,
) {
  const update = () =>
    (map.getSource(sourceId) as GeoJSONSource | undefined)?.setData(
      data as never,
    );
  if (map.getSource(sourceId)) update();
  else map.once("load", update);
}
