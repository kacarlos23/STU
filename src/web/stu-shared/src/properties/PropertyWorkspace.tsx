import {
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
  type FormEvent,
} from "react";
import * as maplibregl from "maplibre-gl";
import type { GeoJSONSource, Map as MapLibreMap } from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
import "./properties.css";
import "./property-editor.css";
import "./property-prerequisite.css";
import "./pagination.css";
import "./workflow.css";
import "./properties-refinement.css";
import { useAddressSuggestion } from './useAddressSuggestion';
import './address-suggestion.css';
import { useUiActions, useUnsavedChanges, useSessionPreferences } from "../interaction/InteractionProvider";
import type { Session } from "../auth/types";
import { useAccessibleDialog } from "../accessibility/useAccessibleDialog";
import {
  centerOfGeometry,
  geometryBounds,
  geometryContainsPoint,
  type Coordinate,
  type PropertyGeometry,
} from "./propertyGeometry";

type Geometry = PropertyGeometry;
type Tag = {
  id: string;
  name: string;
  color: string;
  archivedAtUtc?: string | null;
};
type Microregion = {
  id: string;
  code: string;
  name: string;
  assignedAgentId: string | null;
  boundary: Geometry;
};
type ReferenceData = {
  selectedHealthUnitId: string;
  microregions: Microregion[];
  tags: Tag[];
  coverageRules: { microregionId: string; maxDaysWithoutVisit: number }[];
};
type PropertyItem = {
  id: string;
  healthUnitId: string;
  microregionId: string;
  street: string;
  houseNumber: string;
  familyNumber: string;
  postalCode: string | null;
  complement: string | null;
  geometry: Geometry;
  registrationStatus: string;
  situation: string;
  concurrencyToken: string;
  archivedAtUtc: string | null;
  lastVisitAtUtc: string | null;
  coverageStatus: string;
  tags: Tag[];
};
type Visit = {
  id: string;
  visitedAtUtc: string;
  type: string;
  outcome: string;
  observedSituation: string;
  accessDifficulty: boolean;
  note: string | null;
  archivedAtUtc: string | null;
  concurrencyToken: string;
  agentName: string;
};
type Version = {
  versionNumber: number;
  changeKind: string;
  changedAtUtc: string;
  houseNumber: string;
  familyNumber: string;
};
type Unit = { id: string; code: string; name: string };
type View = "properties" | "settings";
type RecordState = "active" | "draft" | "archived" | "all";
type CoverageSummary = { total: number; overdue: number; neverVisited: number; covered: number; notConfigured: number };
type Preferences = { unitId: string; view: View; query: string; microregionId: string; coverage: string; recordState: RecordState; page: number; selectedId: string; createHandled: number };
type PropertySearchRequest = { value: string; nonce: number };
type PropertyCoverageRequest = { value: string; nonce: number };

const rasterTilesUrl =
  import.meta.env.VITE_STU_RASTER_TILES_URL ||
  "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
const emptyPoint: [number, number] = [-39.7419, -17.5394];

export function PropertyWorkspace({
  session,
  global = false,
  createRequest = 0,
  coverageRequest,
  onOpenTerritory,
  searchRequest,
}: {
  session: Session;
  global?: boolean;
  createRequest?: number;
  coverageRequest?: PropertyCoverageRequest;
  onOpenTerritory?: () => void;
  searchRequest?: PropertySearchRequest;
}) {
  const { guard, confirm } = useUiActions();
  const [preferences, setPreferences] = useSessionPreferences<Preferences>("properties:unified-coverage", {
    unitId: session.healthUnit?.id ?? "", view: "properties", query: "", microregionId: "",
    coverage: "", recordState: "active", page: 1, selectedId: "", createHandled: 0,
  });
  const [view, setView] = useState<View>(preferences.view);
  const [recordState, setRecordState] = useState<RecordState>(preferences.recordState);
  const [coverageSummary, setCoverageSummary] = useState<CoverageSummary | null>(null);
  const [createHandled, setCreateHandled] = useState(preferences.createHandled);
  const searchHandled = useRef(0);
  const coverageHandled = useRef(0);
  const loadSequence = useRef(0);
  const [units, setUnits] = useState<Unit[]>(
    session.healthUnit ? [session.healthUnit] : [],
  );
  const [unitId, setUnitId] = useState(preferences.unitId);
  const [reference, setReference] = useState<ReferenceData | null>(null);
  const [properties, setProperties] = useState<PropertyItem[]>([]);
  const [selectedId, setSelectedId] = useState(preferences.selectedId);
  const [visits, setVisits] = useState<Visit[]>([]);
  const [versions, setVersions] = useState<Version[]>([]);
  const [query, setQuery] = useState(preferences.query);
  const [queryInput, setQueryInput] = useState(preferences.query);
  const [microregionId, setMicroregionId] = useState(preferences.microregionId);
  const [coverage, setCoverage] = useState(preferences.coverage);
  const [page, setPage] = useState(preferences.page);
  const [total, setTotal] = useState(0);
  const [editing, setEditing] = useState<PropertyItem | "new" | null>(null);
  const [visitEditor, setVisitEditor] = useState<Visit | "new" | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const canManageProperties =
    global ||
    session.permissions.includes("*") ||
    session.permissions.includes("properties.manage");
  const canManageVisits =
    global ||
    session.permissions.includes("*") ||
    session.permissions.includes("visits.manage");
  const canManageSettings =
    global ||
    session.permissions.includes("*") ||
    session.permissions.includes("territory.manage");

  useEffect(() => {
    setPreferences({ unitId, view, query: queryInput, microregionId, coverage, recordState, page, selectedId, createHandled });
  }, [unitId, view, queryInput, microregionId, coverage, recordState, page, selectedId, createHandled, setPreferences]);

  useEffect(() => {
    if (!searchRequest || searchRequest.nonce === searchHandled.current) return;
    searchHandled.current = searchRequest.nonce;
    const nextQuery = searchRequest.value.trim();
    setQueryInput(nextQuery);
    setQuery(nextQuery);
    setSelectedId("");
    setPage(1);
    setView("properties");
  }, [searchRequest]);

  useEffect(() => {
    if (!coverageRequest || coverageRequest.nonce === coverageHandled.current) return;
    coverageHandled.current = coverageRequest.nonce;
    setCoverage(coverageRequest.value);
    setRecordState("active");
    setSelectedId("");
    setPage(1);
    setView("properties");
  }, [coverageRequest]);

  const load = useCallback(
    async (selectedUnit: string) => {
      if (!selectedUnit) return;
      const sequence = ++loadSequence.current;
      setLoading(true);
      setError(null);
      try {
        const params = new URLSearchParams({
          healthUnitId: selectedUnit,
          recordState,
          pageSize: "100",
          page: String(page),
        });
        if (query) params.set("query", query);
        if (microregionId) params.set("microregionId", microregionId);
        if (coverage) params.set("coverage", coverage);
        const suffix = params.toString();
        const [propertyResponse, referenceResponse] = await Promise.all([
          fetch(`/api/properties?${suffix}`, {
            credentials: "include",
          }),
          fetch(`/api/properties/reference-data?${suffix}`, {
            credentials: "include",
          }),
        ]);
        if (!propertyResponse.ok || !referenceResponse.ok)
          throw new Error("Não foi possível carregar os imóveis autorizados.");
        const result = (await propertyResponse.json()) as {
          items: PropertyItem[];
          total: number;
          coverageSummary?: CoverageSummary;
        };
        const referenceData = (await referenceResponse.json()) as ReferenceData;
        if (sequence !== loadSequence.current) return;
        setProperties(result.items);
        setTotal(result.total);
        setReference(referenceData);
        setCoverageSummary(result.coverageSummary ?? null);
        if (page > Math.max(1, Math.ceil(result.total / 100))) setPage(Math.max(1, Math.ceil(result.total / 100)));
        setSelectedId((current) =>
          result.items.some((item) => item.id === current)
            ? current
            : (result.items[0]?.id ?? ""),
        );
      } catch (caught) {
        if (sequence === loadSequence.current) setError(messageOf(caught, "Falha ao carregar os imóveis."));
      } finally {
        if (sequence === loadSequence.current) setLoading(false);
      }
    },
    [coverage, microregionId, page, query, recordState],
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
      .catch((caught) => setError(messageOf(caught, "Falha ao listar UBS.")));
  }, [global]);

  useEffect(() => {
    void load(unitId);
    return () => { loadSequence.current++; };
  }, [load, unitId]);
  useEffect(() => {
    if (!createRequest || createRequest <= createHandled || !reference || loading) return;
    setCreateHandled(createRequest);
    if (canManageProperties && reference.microregions.length) { setView("properties"); setEditing("new"); }
    else if (canManageProperties) setNotice("Cadastre uma microrregião antes de incluir o primeiro imóvel.");
  }, [createRequest, createHandled, reference, loading, canManageProperties]);
  useEffect(() => {
    if (queryInput.trim() === query) return;
    const timer = window.setTimeout(() => {
      setPage(1);
      setQuery(queryInput.trim());
    }, 300);
    return () => window.clearTimeout(timer);
  }, [queryInput, query]);
  useEffect(() => {
    let active = true;
    setVisits([]);
    setVersions([]);
    if (!selectedId) {
      setVisits([]);
      setVersions([]);
      return;
    }
    Promise.all([
      fetch(`/api/properties/${selectedId}/visits`, { credentials: "include" }),
      fetch(`/api/properties/${selectedId}/versions`, {
        credentials: "include",
      }),
    ])
      .then(async ([visitResponse, versionResponse]) => {
        if (!visitResponse.ok || !versionResponse.ok) throw new Error();
        const visitData = (await visitResponse.json()) as Visit[];
        const versionData = (await versionResponse.json()) as Version[];
        if (active) { setVisits(visitData); setVersions(versionData); }
      })
      .catch(() => { if (active) setError("Não foi possível carregar o histórico do imóvel."); });
    return () => { active = false; };
  }, [selectedId]);

  const filtered = properties;
  const selected = properties.find((item) => item.id === selectedId) ?? null;
  const hasActiveFilters = recordState !== "active" || Boolean(queryInput.trim()) || Boolean(microregionId) || Boolean(coverage);

  function clearFilters() {
    setRecordState("active");
    setQueryInput("");
    setQuery("");
    setMicroregionId("");
    setCoverage("");
    setSelectedId("");
    setPage(1);
  }

  async function archiveProperty(item: PropertyItem) {
    const action = item.archivedAtUtc ? "reativar" : "arquivar";
    const visitImpact = item.id === selectedId ? `${visits.length} visita${visits.length === 1 ? "" : "s"} e todo o histórico serão preservados.` : "As visitas e todo o histórico serão preservados.";
    const consequence = item.archivedAtUtc
      ? "O imóvel voltará às listas ativas e poderá gerar alertas de cobertura."
      : "O imóvel sairá das listas ativas e deixará de gerar alertas de cobertura.";
    if (!await confirm({ title: `${item.archivedAtUtc ? "Reativar" : "Arquivar"} imóvel?`, message: `Imóvel ${item.houseNumber}, família ${item.familyNumber}. ${consequence} ${visitImpact}`, confirmLabel: item.archivedAtUtc ? "Reativar" : "Arquivar" }))
      return;
    try {
      setLoading(true);
      await mutate(
        `/api/properties/${item.id}/${item.archivedAtUtc ? "restore" : "archive"}`,
        "POST",
      );
      setNotice(`Imóvel ${item.archivedAtUtc ? "reativado" : "arquivado"}.`);
      await load(unitId);
    } catch (caught) {
      setError(messageOf(caught, `Não foi possível ${action} o imóvel.`));
    } finally {
      setLoading(false);
    }
  }

  async function toggleVisit(visit: Visit) {
    if (!await confirm({ title: `${visit.archivedAtUtc ? "Reativar" : "Arquivar"} visita?`, message: "O registro continuará disponível no histórico do imóvel.", confirmLabel: visit.archivedAtUtc ? "Reativar" : "Arquivar" })) return;
    try {
      await mutate(
        `/api/properties/${selectedId}/visits/${visit.id}/${visit.archivedAtUtc ? "restore" : "archive"}`,
        "POST",
      );
      const response = await fetch(`/api/properties/${selectedId}/visits`, {
        credentials: "include",
      });
      if (!response.ok) throw new Error("Não foi possível atualizar o histórico da visita.");
      setVisits((await response.json()) as Visit[]);
      await load(unitId);
      setNotice(
        visit.archivedAtUtc ? "Visita reativada." : "Visita arquivada.",
      );
    } catch (caught) {
      setError(messageOf(caught, "Não foi possível alterar a visita."));
    }
  }

  return (
    <section className="property-workspace">
      <header className="property-heading">
        <div>
          <h2>
            {view === "settings"
                ? "Cobertura e rótulos"
                : "Imóveis e cobertura"}
          </h2>
          <p>
            Somente dados do imóvel e da operação — sem moradores ou informações
            clínicas.
          </p>
        </div>
        <div className="property-actions">
          {global && (
            <label>
              UBS
              <select
                value={unitId}
                onChange={(event) => {
                  const nextUnit = event.target.value;
                  void guard(() => {
                    loadSequence.current++;
                    setProperties([]); setReference(null); setCoverageSummary(null);
                    setSelectedId(""); setMicroregionId(""); setQueryInput(""); setQuery(""); setPage(1);
                    setUnitId(nextUnit);
                  });
                }}
              >
                {units.map((unit) => (
                  <option key={unit.id} value={unit.id}>
                    {unit.code} — {unit.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          <button
            aria-pressed={view === "properties"}
            className={view === "properties" ? "active" : ""}
            onClick={() => { if (view !== "properties") void guard(() => setView("properties")); }}
            type="button"
          >
            Cobertura
          </button>
          {canManageSettings && (
            <button
              aria-pressed={view === "settings"}
              className={view === "settings" ? "active" : ""}
              onClick={() => { if (view !== "settings") void guard(() => setView("settings")); }}
              type="button"
            >
              Configurar
            </button>
          )}
          {canManageProperties && view !== "settings" && (
            <button
              className="property-primary"
              disabled={loading || !reference?.microregions.length}
              onClick={() => setEditing("new")}
              title={!reference?.microregions.length ? "Cadastre uma microrregião antes do primeiro imóvel." : undefined}
              type="button"
            >
              ＋ Cadastrar imóvel
            </button>
          )}
        </div>
      </header>
      {error && (
        <div className="property-message property-error" role="alert">
          {error}
          <button aria-label="Dispensar erro" onClick={() => setError(null)} type="button">
            ×
          </button>
        </div>
      )}
      {notice && (
        <div
          aria-live="polite"
          className="property-message property-success"
          role="status"
        >
          {notice}
          <button
            aria-label="Dispensar aviso"
            onClick={() => setNotice(null)}
            type="button"
          >
            ×
          </button>
        </div>
      )}
      {view === "settings" ? (
        <Settings
          key={unitId}
          unitId={unitId}
          reference={reference}
          reload={() => load(unitId)}
        />
      ) : (
        <>
          <section className="coverage-summary" aria-label="Resumo da cobertura">
            <div>{([
              ["", "Total de imóveis", coverageSummary?.total ?? total, `${pluralize(coverageSummary?.total ?? total, "imóvel ativo", "imóveis ativos")} na UBS`, "total", "building"],
              ["pending", "Precisam de atenção", (coverageSummary?.overdue ?? 0) + (coverageSummary?.neverVisited ?? 0), `${coveragePercent((coverageSummary?.overdue ?? 0) + (coverageSummary?.neverVisited ?? 0), coverageSummary?.total ?? 0)}% · sem visita ou fora do prazo`, "attention", "warning"],
              ["neverVisited", "Nunca visitados", coverageSummary?.neverVisited, `${coveragePercent(coverageSummary?.neverVisited ?? 0, coverageSummary?.total ?? 0)}% · ainda sem primeira visita`, "never", "calendar"],
              ["covered", "Em dia", coverageSummary?.covered, `${coveragePercent(coverageSummary?.covered ?? 0, coverageSummary?.total ?? 0)}% · dentro do prazo de cobertura`, "covered", "check"],
            ] as const).map(([filter, label, count, helper, tone, icon]) => <button className={`coverage-summary-card coverage-summary-card--${tone}`} key={label} type="button" aria-label={`${coverageSummary ? count : "—"} ${label}`} aria-pressed={coverage === filter} onClick={() => { setCoverage(filter); setPage(1); }}><span className="coverage-summary-icon"><PropertySummaryIcon name={icon} /></span><span className="coverage-summary-copy"><span>{label}</span><strong>{coverageSummary ? count : "—"}</strong><small>{helper}</small></span></button>)}</div>
          </section>
          {reference && reference.microregions.length === 0 && (
            <section className="property-prerequisite" role="status">
              <div><span>Etapa necessária</span><h3>Cadastre uma microrregião antes do primeiro imóvel</h3><p>Todo imóvel precisa pertencer a uma microrregião ativa. Desenhe ou importe o limite no mapa territorial e depois volte a esta tela.</p></div>
              {onOpenTerritory && <button onClick={onOpenTerritory} type="button">Ir para o mapa territorial →</button>}
            </section>
          )}
          <section className="property-filter-panel" aria-labelledby="property-filter-title">
            <div className="property-filter-heading"><div><h3 id="property-filter-title">Filtrar imóveis</h3><p>Refine a lista por cadastro, endereço, microrregião ou situação de cobertura.</p></div>{hasActiveFilters && <button onClick={clearFilters} type="button">Limpar filtros</button>}</div>
            <div className="property-filters">
              <label>Situação do cadastro<select aria-label="Situação do cadastro" value={recordState} onChange={event => { setRecordState(event.target.value as RecordState); setPage(1); }}><option value="active">Ativos</option><option value="draft">Rascunhos</option><option value="archived">Arquivados</option><option value="all">Todos os cadastros</option></select></label>
              <label>Buscar imóvel<input aria-label="Buscar imóvel" onChange={(event) => setQueryInput(event.target.value)} placeholder="Rua, número ou família" type="search" value={queryInput} /></label>
              <label>Microrregião<select aria-label="Filtrar microrregião" onChange={(event) => { setPage(1); setMicroregionId(event.target.value); }} value={microregionId}><option value="">Todas as microrregiões</option>{reference?.microregions.map((item) => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}</select></label>
              <label>Situação da cobertura<select aria-label="Filtrar cobertura" onChange={(event) => { setPage(1); setCoverage(event.target.value); }} value={coverage}><option value="">Todas as situações</option><option value="pending">Precisam de atenção</option><option value="overdue">Fora do prazo</option><option value="neverVisited">Nunca visitado</option><option value="covered">Em dia</option><option value="notConfigured">Sem regra configurada</option></select></label>
            </div>
            <p className="property-result-count" aria-live="polite"><strong>{total}</strong> {pluralize(total, "imóvel encontrado", "imóveis encontrados")}</p>
          </section>
          <div className="property-layout">
            <aside aria-label="Imóveis encontrados" className="property-list">
              {loading && <p role="status">Atualizando…</p>}
              {filtered.length === 0 && !loading && (
                <div className="property-list-empty"><strong>Nenhum imóvel encontrado</strong><p>Revise os filtros ou cadastre um novo imóvel.</p>{hasActiveFilters && <button onClick={clearFilters} type="button">Limpar filtros</button>}</div>
              )}
              {filtered.map((item) => (
                <button
                  aria-pressed={item.id === selectedId}
                  className={item.id === selectedId ? "selected" : ""}
                  key={item.id}
                  onClick={() => setSelectedId(item.id)}
                  type="button"
                >
                  <i
                    aria-hidden="true"
                    className={`coverage-dot coverage-${item.coverageStatus}`}
                  />
                  <span>
                    <strong>
                      {item.street}, {item.houseNumber}
                    </strong>
                    <small>
                      Família {item.familyNumber} ·{" "}
                      {microName(reference, item.microregionId)}
                    </small>
                    <em>
                      {item.archivedAtUtc
                        ? "Arquivado"
                        : `${translateCoverage(item.coverageStatus)} · ${translateSituation(item.situation)}`}
                    </em>
                  </span>
                </button>
              ))}
            </aside>
            <article className="property-detail">
              {selected ? (
                <>
                  <header>
                    <div>
                      <span>
                        Imóvel{" "}
                        {selected.archivedAtUtc
                          ? "arquivado"
                          : selected.registrationStatus === "Draft"
                            ? "em rascunho"
                            : "ativo"}
                      </span>
                      <h3>
                        {selected.street}, {selected.houseNumber}
                      </h3>
                      <p>
                        Família <strong>{selected.familyNumber}</strong> ·{" "}
                        {microName(reference, selected.microregionId)}
                      </p>
                    </div>
                    <div>
                      {canManageProperties && (
                        <>
                          <button
                            disabled={Boolean(selected.archivedAtUtc)}
                            onClick={() => setEditing(selected)}
                            type="button"
                          >
                            Editar
                          </button>
                          <button
                            onClick={() => void archiveProperty(selected)}
                            type="button"
                          >
                            {selected.archivedAtUtc ? "Reativar" : "Arquivar"}
                          </button>
                        </>
                      )}
                    </div>
                  </header>
                  <section className="property-last-visit" aria-label="Última visita do imóvel">
                    <div><span>Última visita</span><strong>{selected.lastVisitAtUtc ? new Date(selected.lastVisitAtUtc).toLocaleString("pt-BR") : "Nenhuma visita registrada"}</strong><small>{translateCoverage(selected.coverageStatus)}</small></div>
                    {canManageVisits && !selected.archivedAtUtc && <button className="property-primary" type="button" onClick={() => setVisitEditor("new")}>Registrar visita</button>}
                  </section>
                  <div className="property-facts">
                    <span>
                      <small>Situação</small>
                      <strong>{translateSituation(selected.situation)}</strong>
                    </span>
                    <span>
                      <small>Última visita</small>
                      <strong>
                        {selected.lastVisitAtUtc
                          ? new Date(
                              selected.lastVisitAtUtc,
                            ).toLocaleDateString("pt-BR")
                          : "Nunca visitado"}
                      </strong>
                    </span>
                    <span>
                      <small>Cobertura</small>
                      <strong>
                        {translateCoverage(selected.coverageStatus)}
                      </strong>
                    </span>
                    <span>
                      <small>CEP</small>
                      <strong>{selected.postalCode || "Não informado"}</strong>
                    </span>
                  </div>
                  {selected.tags.length > 0 && (
                    <div className="property-tags">
                      {selected.tags.map((tag) => (
                        <span
                          key={tag.id}
                          style={{ borderColor: tag.color, color: tag.color }}
                        >
                          {tag.name}
                        </span>
                      ))}
                    </div>
                  )}
                  <section className="visit-section">
                    <div>
                      <div>
                        <span>Histórico operacional</span>
                        <h4>Visitas registradas</h4>
                      </div>

                    </div>
                    {visits.length === 0 && (
                      <p>Nenhuma visita registrada para este imóvel.</p>
                    )}
                    {visits.map((visit) => (
                      <article
                        className={visit.archivedAtUtc ? "visit-archived" : ""}
                        key={visit.id}
                      >
                        <i />
                        <div>
                          <strong>
                            {translateVisitType(visit.type)} ·{" "}
                            {translateOutcome(visit.outcome)}
                          </strong>
                          <span>
                            {new Date(visit.visitedAtUtc).toLocaleString(
                              "pt-BR",
                            )}{" "}
                            por {visit.agentName}
                          </span>
                          <p>
                            {translateSituation(visit.observedSituation)}
                            {visit.accessDifficulty
                              ? " · dificuldade de acesso"
                              : ""}
                            {visit.note ? ` · ${visit.note}` : ""}
                          </p>
                          {canManageVisits && (
                            <div className="visit-actions">
                              <button
                                disabled={Boolean(visit.archivedAtUtc)}
                                onClick={() => setVisitEditor(visit)}
                                type="button"
                              >
                                Editar
                              </button>
                              <button
                                onClick={() => void toggleVisit(visit)}
                                type="button"
                              >
                                {visit.archivedAtUtc ? "Reativar" : "Arquivar"}
                              </button>
                            </div>
                          )}
                        </div>
                      </article>
                    ))}
                  </section>
                  <details className="property-versions">
                    <summary>
                      Ver histórico de números e edições ({versions.length})
                    </summary>
                    {versions.map((version) => (
                      <p key={version.versionNumber}>
                        <b>v{version.versionNumber}</b> Casa{" "}
                        {version.houseNumber} · Família {version.familyNumber}
                        <small>
                          {translateChange(version.changeKind)} em{" "}
                          {new Date(version.changedAtUtc).toLocaleString(
                            "pt-BR",
                          )}
                        </small>
                      </p>
                    ))}
                  </details>
                </>
              ) : (
                <div className="property-empty">
                  <strong>Selecione um imóvel</strong>
                  <p>A ficha e as visitas aparecerão aqui.</p>
                </div>
              )}
            </article>
          </div>
          {total > 100 && (
            <nav
              className="property-pagination"
              aria-label="Paginação de imóveis"
            >
              <button
                disabled={page === 1}
                onClick={() => setPage((value) => Math.max(1, value - 1))}
                type="button"
              >
                ← Anterior
              </button>
              <span>
                Página {page} de {Math.ceil(total / 100)}
              </span>
              <button
                disabled={page >= Math.ceil(total / 100)}
                onClick={() => setPage((value) => value + 1)}
                type="button"
              >
                Próxima →
              </button>
            </nav>
          )}
        </>
      )}
      {editing && reference && (
        <PropertyEditor
          item={editing}
          reference={reference}
          onCancel={() => setEditing(null)}
          onSaved={async (savedId) => {
            setEditing(null);
            setNotice("Imóvel salvo, auditado e versionado.");
            await load(unitId);
            if (savedId) setSelectedId(savedId);
          }}
          setError={setError}
        />
      )}
      {visitEditor && selected && (
        <VisitEditor
          item={visitEditor}
          property={selected}
          onCancel={() => setVisitEditor(null)}
          onSaved={async () => {
            setVisitEditor(null);
            setNotice("Visita operacional salva e auditada.");
            const response = await fetch(
              `/api/properties/${selected.id}/visits`,
              { credentials: "include" },
            );
            if (response.ok) setVisits((await response.json()) as Visit[]);
            await load(unitId);
          }}
          setError={setError}
        />
      )}
    </section>
  );
}

function PropertyEditor({
  item,
  reference,
  onCancel,
  onSaved,
  setError,
}: {
  item: PropertyItem | "new";
  reference: ReferenceData;
  onCancel: () => void;
  onSaved: (savedId?: string) => Promise<void>;
  setError: (message: string | null) => void;
}) {
  const editing = item === "new" ? null : item;
  const titleId = useId();
  const { guard, confirm } = useUiActions();
  const [dirty, setDirty] = useState(false);
  function requestClose() { if (!saving) void guard(onCancel); }
  const dialogRef = useAccessibleDialog<HTMLFormElement>(true, requestClose);
  const firstMicroregion =
    reference.microregions.find(
      (microregion) => microregion.id === editing?.microregionId,
    ) ?? reference.microregions[0];
  const initialPoint =
    editing?.geometry.type === "Point"
      ? (editing.geometry.coordinates as [number, number])
      : null;
  const [selectedMicroregionId, setSelectedMicroregionId] = useState(
    firstMicroregion?.id ?? "",
  );
  const [point, setPoint] = useState<Coordinate | null>(initialPoint);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const selectedBoundary = reference.microregions.find(
    (microregion) => microregion.id === selectedMicroregionId,
  )?.boundary;
  const pointIsInside =
    point !== null && geometryContainsPoint(selectedBoundary, point);

  const [street, setStreet] = useState(editing?.street ?? '');
  const [postalCode, setPostalCode] = useState(editing?.postalCode ?? '');
  const [addressEnabled, setAddressEnabled] = useState(true);
  const [addressRevision, setAddressRevision] = useState(0);
  const address = useAddressSuggestion(point, selectedMicroregionId, addressRevision, addressEnabled && pointIsInside && !saving);
  useEffect(() => {
    if (!address.result?.found) return;
    const suggestion = address.result;
    setStreet(current => current.trim() ? current : suggestion.street ?? '');
    setPostalCode(current => current.trim() ? current : suggestion.postalCode ?? '');
    setDirty(true);
  }, [address.result]);
  async function applyAddress() {
    const suggestion = address.result;
    if (!suggestion?.found) return;
    if (!await confirm({ title: 'Usar endereço sugerido?', message: 'O logradouro e o CEP retornados pela consulta substituirão os respectivos campos. Confira a sugestão antes de salvar o imóvel.', confirmLabel: 'Usar sugestão' })) return;
    if (suggestion.street) setStreet(suggestion.street);
    if (suggestion.postalCode) setPostalCode(suggestion.postalCode);
    setDirty(true);
  }

  useUnsavedChanges(dirty || selectedMicroregionId !== (firstMicroregion?.id ?? "") || JSON.stringify(point) !== JSON.stringify(initialPoint));

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (saving) return;
    setError(null);
    setFormError(null);
    if (!point) {
      setFormError("Marque a localização exata do imóvel no mapa.");
      return;
    }
    if (!pointIsInside) {
      setFormError(
        "A localização marcada precisa ficar dentro da microrregião selecionada.",
      );
      return;
    }
    const fields = new FormData(event.currentTarget);
    const body = {
      microregionId: String(fields.get("microregionId")),
      street: String(fields.get("street")),
      houseNumber: String(fields.get("houseNumber")),
      familyNumber: String(fields.get("familyNumber")),
      postalCode: String(fields.get("postalCode")) || null,
      complement: String(fields.get("complement")) || null,
      geometry: { type: "Point", coordinates: point },
      registrationStatus: String(fields.get("registrationStatus")),
      situation: String(fields.get("situation")),
      tagIds: fields.getAll("tagIds").map(String),
      expectedVersion: editing?.concurrencyToken ?? null,
    };
    try {
      setSaving(true);
      const result = (await mutate(
        editing ? `/api/properties/${editing.id}` : "/api/properties",
        editing ? "PUT" : "POST",
        body,
      )) as { id?: string } | null;
      await onSaved(result?.id ?? editing?.id);
    } catch (caught) {
      const message = messageOf(caught, "Não foi possível salvar o imóvel.");
      setFormError(message);
      setError(message);
    } finally {
      setSaving(false);
    }
  }
  return (
    <div
      className="property-modal-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.currentTarget === event.target) requestClose();
      }}
    >
      <form
        aria-labelledby={titleId}
        aria-modal="true"
        className="property-modal"
        onChangeCapture={() => setDirty(true)}
        onSubmit={(event) => void submit(event)}
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <header>
          <div>
            <span>{editing ? "Editar e versionar" : "Novo cadastro"}</span>
            <h3 id={titleId}>Imóvel e número de família</h3>
          </div>
          <button
            aria-label="Fechar cadastro de imóvel"
            onClick={requestClose}
            type="button"
          >
            ×
          </button>
        </header>
        {reference.microregions.length === 0 ? (
          <p>Cadastre uma microrregião antes de incluir imóveis.</p>
        ) : (
          <>
            <div className="property-form-grid">
              <label className="wide">
                Microrregião
                <select
                  name="microregionId"
                  required
                  value={selectedMicroregionId}
                  onChange={(event) => {
                    const id = event.target.value;
                    setSelectedMicroregionId(id);
                    setPoint(null);
                    setFormError(null);
                  }}
                >
                  {reference.microregions.map((micro) => (
                    <option key={micro.id} value={micro.id}>
                      {micro.code} — {micro.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="wide">
                Logradouro
                <input
                  value={street}
                  onChange={event => setStreet(event.target.value)}
                  maxLength={180}
                  name="street"
                  required
                />
              </label>
              <label>
                Número da casa
                <input
                  defaultValue={editing?.houseNumber}
                  maxLength={32}
                  name="houseNumber"
                  required
                />
              </label>
              <label>
                Número da família
                <input
                  defaultValue={editing?.familyNumber}
                  maxLength={32}
                  name="familyNumber"
                  required
                />
              </label>
              <label>
                CEP
                <input
                  value={postalCode}
                  onChange={event => setPostalCode(event.target.value)}
                  maxLength={16}
                  name="postalCode"
                />
              </label>
              <label>
                Complemento operacional
                <input
                  defaultValue={editing?.complement ?? ""}
                  maxLength={120}
                  name="complement"
                />
              </label>
              <label>
                Situação cadastral
                <select
                  defaultValue={editing?.registrationStatus ?? "Active"}
                  name="registrationStatus"
                >
                  <option value="Active">Ativo</option>
                  <option value="Draft">Rascunho</option>
                </select>
                {!editing && <small className="property-default-hint">Padrão recomendado: ativo. Altere somente se o cadastro ainda estiver incompleto.</small>}
              </label>
              <label>
                Situação do imóvel
                <select
                  defaultValue={editing?.situation ?? "Occupied"}
                  name="situation"
                >
                  <option value="Occupied">Ocupado</option>
                  <option value="Vacant">Vago</option>
                  <option value="Abandoned">Abandonado</option>
                  <option value="Commercial">Comercial</option>
                  <option value="Other">Outro</option>
                </select>
                {!editing && <small className="property-default-hint">Preenchido como ocupado para reduzir etapas no cadastro mais comum.</small>}
              </label>
            </div>
            <div className="property-map-picker">
              <div>
                <strong>Localização no mapa</strong>
                <span>
                  Clique dentro da área ou arraste o marcador para ajustar a
                  posição exata.
                </span>
              </div>
              <MapPicker
                boundary={selectedBoundary}
                onPoint={(nextPoint) => {
                  setPoint(nextPoint);
                  setAddressRevision(current => current + 1);
                  setFormError(null);
                }}
                point={point}
              />
              <div
                aria-live="polite"
                className={`property-location-status ${
                  point === null
                    ? "location-pending"
                    : pointIsInside
                      ? "location-valid"
                      : "location-invalid"
                }`}
                id="property-location-status"
              >
                {point === null
                  ? "Localização pendente — clique no mapa para marcar a casa."
                  : pointIsInside
                    ? `Localização confirmada · ${point[1].toFixed(6)}, ${point[0].toFixed(6)}`
                    : "O ponto está fora da microrregião. Reposicione o marcador."}
              </div>
            </div>
            <section className="property-address-suggestion" aria-label="Endereço sugerido pelo mapa">
              <label><input type="checkbox" checked={addressEnabled} onChange={event => setAddressEnabled(event.target.checked)} /> Buscar endereço ao marcar no mapa</label>
              <p>A consulta envia somente as coordenadas ao provedor. Número da casa e da família continuam manuais. Bairros e microrregiões seguem os limites do STU.</p>
              <p>Endereços: © <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap contributors</a> · <a href="https://operations.osmfoundation.org/policies/nominatim/" target="_blank" rel="noreferrer">Regras de uso do Nominatim</a>.</p>
              {address.status && <p role="status" aria-live="polite">{address.status}</p>}
              {address.result?.found && <div><p><strong>Logradouro:</strong> {address.result.street || 'Não encontrado'}<br /><strong>CEP:</strong> {address.result.postalCode || 'Não encontrado'}</p><button type="button" disabled={saving} onClick={() => void applyAddress()}>Usar endereço sugerido</button></div>}
              {addressEnabled && pointIsInside && <button type="button" disabled={address.loading || saving} onClick={() => setAddressRevision(current => current + 1)}>{address.loading ? 'Consultando…' : 'Consultar endereço novamente'}</button>}
            </section>
            {reference.tags.length > 0 && (
              <fieldset className="property-tag-field">
                <legend>Rótulos operacionais</legend>
                {reference.tags.map((tag) => (
                  <label key={tag.id}>
                    <input
                      defaultChecked={editing?.tags.some(
                        (current) => current.id === tag.id,
                      )}
                      name="tagIds"
                      type="checkbox"
                      value={tag.id}
                    />
                    <i style={{ background: tag.color }} />
                    {tag.name}
                  </label>
                ))}
              </fieldset>
            )}
            <p className="property-warning">
              Não registre nomes, telefones, documentos ou qualquer informação
              clínica.
            </p>
            {formError && (
              <p aria-live="assertive" className="property-form-error" role="alert">
                {formError}
              </p>
            )}
            <footer>
              <button disabled={saving} onClick={requestClose} type="button">
                Cancelar
              </button>
              <button
                className="property-primary"
                disabled={saving || !pointIsInside}
                type="submit"
              >
                {saving ? "Salvando…" : "Salvar imóvel"}
              </button>
            </footer>
          </>
        )}
      </form>
    </div>
  );
}

function VisitEditor({
  property,
  item,
  onCancel,
  onSaved,
  setError,
}: {
  property: PropertyItem;
  item: Visit | "new";
  onCancel: () => void;
  onSaved: () => Promise<void>;
  setError: (message: string | null) => void;
}) {
  const editing = item === "new" ? null : item;
  const titleId = useId();
  const { guard } = useUiActions();
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  useUnsavedChanges(dirty);
  function requestClose() { if (!saving) void guard(onCancel); }
  const dialogRef = useAccessibleDialog<HTMLFormElement>(true, requestClose);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (saving) return;
    setSaving(true); setFormError(null); setError(null);
    const fields = new FormData(event.currentTarget);
    try {
      await mutate(
        editing
          ? `/api/properties/${property.id}/visits/${editing.id}`
          : `/api/properties/${property.id}/visits`,
        editing ? "PUT" : "POST",
        {
          visitedAtUtc: new Date(String(fields.get("visitedAt"))).toISOString(),
          type: String(fields.get("type")),
          outcome: String(fields.get("outcome")),
          observedSituation: String(fields.get("situation")),
          accessDifficulty: fields.get("accessDifficulty") === "on",
          note: String(fields.get("note")) || null,
          expectedVersion: editing?.concurrencyToken ?? null,
        },
      );
      await onSaved();
    } catch (caught) {
      setFormError(messageOf(caught, "Não foi possível salvar a visita."));
    } finally {
      setSaving(false);
    }
  }
  const date = editing ? new Date(editing.visitedAtUtc) : new Date();
  const localDate = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
    .toISOString()
    .slice(0, 16);
  return (
    <div
      className="property-modal-backdrop"
      onMouseDown={(event) => {
        if (event.currentTarget === event.target) requestClose();
      }}
      role="presentation"
    >
      <form
        aria-labelledby={titleId}
        aria-modal="true"
        className="property-modal visit-modal"
        onChangeCapture={() => setDirty(true)}
        onSubmit={(event) => void submit(event)}
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <header>
          <div>
            <span>
              Imóvel {property.houseNumber} · Família {property.familyNumber}
            </span>
            <h3 id={titleId}>
              {editing
                ? "Editar visita operacional"
                : "Registrar visita operacional"}
            </h3>
          </div>
          <button
            aria-label="Fechar registro de visita"
            onClick={requestClose}
            type="button"
          >
            ×
          </button>
        </header>
        <div className="property-form-grid">
          <label className="wide">
            Data e hora
            <input
              defaultValue={localDate}
              name="visitedAt"
              required
              type="datetime-local"
            />
          </label>
          <label>
            Tipo
            <select defaultValue={editing?.type ?? "Routine"} name="type">
              <option value="Routine">Rotina</option>
              <option value="Registration">Cadastro</option>
              <option value="FollowUp">Acompanhamento</option>
              <option value="Attempt">Tentativa</option>
            </select>
            {!editing && <small className="property-default-hint">Padrão: visita de rotina.</small>}
          </label>
          <label>
            Resultado
            <select
              defaultValue={editing?.outcome ?? "Completed"}
              name="outcome"
            >
              <option value="Completed">Concluída</option>
              <option value="NoAnswer">Sem resposta</option>
              <option value="Refused">Recusada</option>
              <option value="AccessBlocked">Acesso impedido</option>
              <option value="Rescheduled">Reagendada</option>
            </select>
            {!editing && <small className="property-default-hint">Padrão: concluída. Ajuste quando o atendimento não for finalizado.</small>}
          </label>
          <label>
            Situação observada
            <select
              defaultValue={editing?.observedSituation ?? property.situation}
              name="situation"
            >
              <option value="Occupied">Ocupado</option>
              <option value="Vacant">Vago</option>
              <option value="Abandoned">Abandonado</option>
              <option value="Commercial">Comercial</option>
              <option value="Other">Outro</option>
            </select>
            {!editing && <small className="property-default-hint">Começa com a situação já registrada para este imóvel.</small>}
          </label>
          <label className="check">
            <input
              defaultChecked={editing?.accessDifficulty}
              name="accessDifficulty"
              type="checkbox"
            />{" "}
            Houve dificuldade de acesso
          </label>
          <label className="wide">
            Observação curta e opcional
            <textarea
              defaultValue={editing?.note ?? ""}
              maxLength={240}
              name="note"
              placeholder="Somente informação operacional sobre o imóvel ou a visita"
              rows={3}
            />
          </label>
        </div>
        <p className="property-warning">
          Não registre nome, CPF, telefone, e-mail, condição de saúde ou
          qualquer dado de morador.
        </p>
        {formError && <p className="property-message property-error" role="alert">{formError}</p>}
        <footer>
          <button disabled={saving} onClick={requestClose} type="button">
            Cancelar
          </button>
          <button className="property-primary" disabled={saving} type="submit">
            {saving ? "Salvando…" : "Salvar visita"}
          </button>
        </footer>
      </form>
    </div>
  );
}

function Settings({
  unitId,
  reference,
  reload,
}: {
  unitId: string;
  reference: ReferenceData | null;
  reload: () => Promise<void>;
}) {
  const { confirm } = useUiActions();
  const [tagDirty, setTagDirty] = useState(false);
  useUnsavedChanges(tagDirty);
  const [tags, setTags] = useState<Tag[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  useEffect(() => {
    if (!unitId) return;
    fetch(`/api/property-settings/tags?healthUnitId=${unitId}`, {
      credentials: "include",
    })
      .then((response) => { if (!response.ok) throw new Error(); return response.json(); })
      .then(setTags)
      .catch(() => setError("Não foi possível carregar os rótulos."));
  }, [unitId, reference]);
  async function addTag(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const fields = new FormData(form);
    try {
      await mutate("/api/property-settings/tags", "POST", {
        healthUnitId: unitId,
        name: String(fields.get("name")),
        color: String(fields.get("color")),
      });
      form.reset(); setTagDirty(false);
      setNotice("Rótulo criado.");
      await reload();
    } catch (caught) {
      setError(messageOf(caught, "Não foi possível criar o rótulo."));
    }
  }
  async function toggleTag(tag: Tag) {
    if (!await confirm({ title: `${tag.archivedAtUtc ? "Reativar" : "Arquivar"} rótulo?`, message: `Rótulo ${tag.name}. Os vínculos históricos serão preservados.`, confirmLabel: tag.archivedAtUtc ? "Reativar" : "Arquivar" })) return;
    try {
      await mutate(
        `/api/property-settings/tags/${tag.id}/${tag.archivedAtUtc ? "restore" : "archive"}`,
        "POST",
      );
      setNotice(tag.archivedAtUtc ? "Rótulo reativado." : "Rótulo arquivado.");
      await reload();
    } catch (caught) {
      setError(messageOf(caught, "Não foi possível alterar o rótulo."));
    }
  }
  async function saveCoverage(microregionId: string, days: number) {
    try {
      await mutate(`/api/property-settings/coverage/${microregionId}`, "PUT", {
        healthUnitId: unitId,
        maxDaysWithoutVisit: days,
      });
      setNotice("Prazo de cobertura atualizado.");
      await reload();
      return true;
    } catch (caught) {
      setError(messageOf(caught, "Não foi possível salvar o prazo."));
      return false;
    }
  }
  return (
    <div className="property-settings">
      {error && (
        <div className="property-message property-error" role="alert">
          {error}
        </div>
      )}
      {notice && (
        <div className="property-message property-success" role="status">
          {notice}
        </div>
      )}
      <article>
        <header>
          <div>
            <span>Padronização</span>
            <h3>Rótulos operacionais</h3>
            <p>Use termos gerais, nunca dados pessoais.</p>
          </div>
        </header>
        <form className="tag-create" onChangeCapture={() => setTagDirty(true)} onSubmit={(event) => void addTag(event)}>
          <input
            aria-label="Nome do rótulo operacional"
            maxLength={80}
            name="name"
            placeholder="Ex.: Difícil acesso"
            required
          />
          <input
            aria-label="Cor do rótulo"
            defaultValue="#A98BFF"
            name="color"
            type="color"
          />
          <button className="property-primary" type="submit">
            Criar rótulo
          </button>
        </form>
        <div className="settings-list">
          {tags.map((tag) => (
            <div className={tag.archivedAtUtc ? "archived" : ""} key={tag.id}>
              <i style={{ background: tag.color }} />
              <strong>{tag.name}</strong>
              <small>{tag.archivedAtUtc ? "Arquivado" : "Ativo"}</small>
              <button onClick={() => void toggleTag(tag)} type="button">
                {tag.archivedAtUtc ? "Reativar" : "Arquivar"}
              </button>
            </div>
          ))}
        </div>
      </article>
      <article>
        <header>
          <div>
            <span>Gestão ativa</span>
            <h3>Prazo máximo sem visita</h3>
            <p>Imóveis vencidos ganham alerta no painel e no mapa.</p>
          </div>
        </header>
        <div className="settings-list">
          {reference?.microregions.map((micro) => (
            <CoverageRow
              key={micro.id}
              microregion={micro}
              initial={
                reference.coverageRules.find(
                  (rule) => rule.microregionId === micro.id,
                )?.maxDaysWithoutVisit ?? 90
              }
              save={saveCoverage}
            />
          ))}
        </div>
      </article>
    </div>
  );
}

function CoverageRow({
  microregion,
  initial,
  save,
}: {
  microregion: Microregion;
  initial: number;
  save: (id: string, days: number) => Promise<boolean>;
}) {
  const [days, setDays] = useState(initial);
  const [savedDays, setSavedDays] = useState(initial);
  const [saving, setSaving] = useState(false);
  useUnsavedChanges(days !== savedDays);
  async function submit() {
    if (saving || !Number.isInteger(days) || days < 1 || days > 730) return;
    setSaving(true);
    try { if (await save(microregion.id, days)) setSavedDays(days); }
    finally { setSaving(false); }
  }
  return (
    <div>
      <span>
        <strong>
          {microregion.code} — {microregion.name}
        </strong>
        <small>Alerta após o prazo</small>
      </span>
      <label>
        <input
          aria-label={`Prazo sem visita em ${microregion.name}`}
          max={730}
          min={1}
          onChange={(event) => setDays(Number(event.target.value))}
          type="number"
          value={days}
        />{" "}
        dias
      </label>
      <button disabled={saving || !Number.isInteger(days) || days < 1 || days > 730} onClick={() => void submit()} type="button">
        {saving ? "Salvando…" : "Salvar"}
      </button>
    </div>
  );
}

function MapPicker({
  boundary,
  point,
  onPoint,
}: {
  boundary?: Geometry;
  point: Coordinate | null;
  onPoint: (point: Coordinate) => void;
}) {
  const node = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibreMap | null>(null);
  const markerRef = useRef<maplibregl.Marker | null>(null);
  const onPointRef = useRef(onPoint);
  onPointRef.current = onPoint;

  useEffect(() => {
    if (!node.current) return;
    const map = new maplibregl.Map({
      container: node.current,
      style: {
        version: 8,
        sources: {
          osm: {
            type: "raster",
            tiles: [rasterTilesUrl],
            tileSize: 256,
            attribution: "© OpenStreetMap contributors",
          },
        },
        layers: [{ id: "osm", type: "raster", source: "osm" }],
      },
      center: point ?? centerOfGeometry(boundary) ?? emptyPoint,
      zoom: point ? 17 : 14,
      attributionControl: {},
    });
    map.addControl(
      new maplibregl.NavigationControl({ showCompass: false }),
      "top-right",
    );
    map.on("load", () => {
      map.addSource("micro", {
        type: "geojson",
        data: feature(boundary) as never,
      });
      map.addLayer({
        id: "micro-fill",
        source: "micro",
        type: "fill",
        paint: { "fill-color": "#A98BFF", "fill-opacity": 0.18 },
      });
      map.addLayer({
        id: "micro-line",
        source: "micro",
        type: "line",
        paint: { "line-color": "#6D4AFF", "line-width": 2 },
      });
      fitBoundary(map, boundary);
    });
    map.on("click", (event) =>
      onPointRef.current([event.lngLat.lng, event.lngLat.lat]),
    );
    mapRef.current = map;
    return () => {
      markerRef.current?.remove();
      markerRef.current = null;
      map.remove();
      mapRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const update = () => {
      (map.getSource("micro") as GeoJSONSource | undefined)?.setData(
        feature(boundary) as never,
      );
      fitBoundary(map, boundary);
    };
    map.loaded() ? update() : map.once("load", update);
  }, [boundary]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    if (!point) {
      markerRef.current?.remove();
      markerRef.current = null;
      return;
    }

    if (!markerRef.current) {
      const marker = new maplibregl.Marker({
        color: "#cf6e32",
        draggable: true,
      })
        .setLngLat(point)
        .addTo(map);
      marker.on("dragend", () => {
        const position = marker.getLngLat();
        onPointRef.current([position.lng, position.lat]);
      });
      markerRef.current = marker;
    } else {
      markerRef.current.setLngLat(point);
    }
  }, [point]);

  return (
    <div
      aria-describedby="property-location-status"
      aria-label="Mapa para marcar e ajustar a localização do imóvel"
      className="map-picker-node"
      ref={node}
      role="region"
    />
  );
}

function feature(geometry?: Geometry) {
  return {
    type: "FeatureCollection",
    features: geometry ? [{ type: "Feature", properties: {}, geometry }] : [],
  };
}
function fitBoundary(map: MapLibreMap, geometry?: Geometry) {
  const bounds = geometryBounds(geometry);
  if (!bounds) return;
  if (bounds[0][0] === bounds[1][0] && bounds[0][1] === bounds[1][1]) {
    map.easeTo({ center: bounds[0], zoom: 17, duration: 250 });
    return;
  }
  map.fitBounds(bounds, { padding: 36, maxZoom: 17, duration: 250 });
}
function PropertySummaryIcon({ name }: { name: "building" | "warning" | "calendar" | "check" }) {
  const paths = {
    building: <><path d="M4 21V5a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v16" /><path d="M9 21v-4h3v4M8 7h1m3 0h1M8 11h1m3 0h1M2 21h18" /></>,
    warning: <><path d="M10.3 3.8 2.4 18a2 2 0 0 0 1.8 3h15.6a2 2 0 0 0 1.8-3L13.7 3.8a2 2 0 0 0-3.4 0Z" /><path d="M12 9v4m0 4h.01" /></>,
    calendar: <><rect height="16" rx="2" width="18" x="3" y="5" /><path d="M8 3v4m8-4v4M3 10h18M8 14h.01m4 0h.01m4 0h.01" /></>,
    check: <><circle cx="12" cy="12" r="9" /><path d="m8 12 2.7 2.7L16.5 9" /></>,
  } as const;
  return <svg aria-hidden="true" fill="none" height="22" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8" viewBox="0 0 24 24" width="22">{paths[name]}</svg>;
}
function pluralize(value: number, singular: string, plural: string) {
  return value === 1 ? singular : plural;
}
function coveragePercent(value: number, total: number) {
  return total > 0 ? Math.round((value / total) * 100) : 0;
}
function microName(reference: ReferenceData | null, id: string) {
  const item = reference?.microregions.find((micro) => micro.id === id);
  return item ? `${item.code} — ${item.name}` : "Microrregião";
}
function translateSituation(value: string) {
  return (
    (
      {
        Occupied: "Ocupado",
        Vacant: "Vago",
        Abandoned: "Abandonado",
        Commercial: "Comercial",
        Other: "Outro",
      } as Record<string, string>
    )[value] ?? value
  );
}
function translateCoverage(value: string) {
  return (
    (
      {
        overdue: "Fora do prazo",
        neverVisited: "Nunca visitado",
        covered: "Em dia",
        notConfigured: "Sem prazo configurado",
      } as Record<string, string>
    )[value] ?? value
  );
}
function translateVisitType(value: string) {
  return (
    (
      {
        Registration: "Cadastro",
        Routine: "Rotina",
        FollowUp: "Acompanhamento",
        Attempt: "Tentativa",
      } as Record<string, string>
    )[value] ?? value
  );
}
function translateOutcome(value: string) {
  return (
    (
      {
        Completed: "Concluída",
        NoAnswer: "Sem resposta",
        Refused: "Recusada",
        AccessBlocked: "Acesso impedido",
        Rescheduled: "Reagendada",
      } as Record<string, string>
    )[value] ?? value
  );
}
function translateChange(value: string) {
  return (
    (
      {
        Create: "Criação",
        Update: "Edição",
        ReassignIdentifiers: "Reatribuição dos números",
        Archive: "Arquivamento",
        Restore: "Reativação",
      } as Record<string, string>
    )[value] ?? value
  );
}
function messageOf(value: unknown, fallback: string) {
  return value instanceof Error ? value.message : fallback;
}
async function mutate(path: string, method: "POST" | "PUT", body?: unknown) {
  if (!navigator.onLine)
    throw new Error("Sem conexão. Reconecte-se antes de salvar imóveis ou visitas.");
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
