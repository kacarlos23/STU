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
type View = "properties" | "visits" | "settings";

const rasterTilesUrl =
  import.meta.env.VITE_STU_RASTER_TILES_URL ||
  "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
const emptyPoint: [number, number] = [-39.7419, -17.5394];

export function PropertyWorkspace({
  session,
  global = false,
  initialView = "properties",
  onOpenTerritory,
}: {
  session: Session;
  global?: boolean;
  initialView?: "properties" | "visits";
  onOpenTerritory?: () => void;
}) {
  const [view, setView] = useState<View>(initialView);
  const [units, setUnits] = useState<Unit[]>(
    session.healthUnit ? [session.healthUnit] : [],
  );
  const [unitId, setUnitId] = useState(session.healthUnit?.id ?? "");
  const [reference, setReference] = useState<ReferenceData | null>(null);
  const [properties, setProperties] = useState<PropertyItem[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [visits, setVisits] = useState<Visit[]>([]);
  const [versions, setVersions] = useState<Version[]>([]);
  const [query, setQuery] = useState("");
  const [queryInput, setQueryInput] = useState("");
  const [microregionId, setMicroregionId] = useState("");
  const [coverage, setCoverage] = useState("");
  const [page, setPage] = useState(1);
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

  const load = useCallback(
    async (selectedUnit: string) => {
      if (!selectedUnit) return;
      setLoading(true);
      setError(null);
      try {
        const params = new URLSearchParams({
          healthUnitId: selectedUnit,
          includeArchived: "true",
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
        };
        setProperties(result.items);
        setTotal(result.total);
        setReference((await referenceResponse.json()) as ReferenceData);
        setSelectedId((current) =>
          result.items.some((item) => item.id === current)
            ? current
            : (result.items[0]?.id ?? ""),
        );
      } catch (caught) {
        setError(messageOf(caught, "Falha ao carregar os imóveis."));
      } finally {
        setLoading(false);
      }
    },
    [coverage, microregionId, page, query],
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
  }, [load, unitId]);
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setPage(1);
      setQuery(queryInput.trim());
    }, 300);
    return () => window.clearTimeout(timer);
  }, [queryInput]);
  useEffect(() => {
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
        setVisits((await visitResponse.json()) as Visit[]);
        setVersions((await versionResponse.json()) as Version[]);
      })
      .catch(() =>
        setError("Não foi possível carregar o histórico do imóvel."),
      );
  }, [selectedId]);

  const filtered = properties;
  const selected = properties.find((item) => item.id === selectedId) ?? null;

  async function archiveProperty(item: PropertyItem) {
    const action = item.archivedAtUtc ? "reativar" : "arquivar";
    if (
      !confirm(
        `Deseja ${action} o imóvel ${item.houseNumber}? O histórico será preservado.`,
      )
    )
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
    try {
      await mutate(
        `/api/properties/${selectedId}/visits/${visit.id}/${visit.archivedAtUtc ? "restore" : "archive"}`,
        "POST",
      );
      const response = await fetch(`/api/properties/${selectedId}/visits`, {
        credentials: "include",
      });
      if (response.ok) setVisits((await response.json()) as Visit[]);
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
          <span>Operação territorial</span>
          <h2>
            {view === "visits"
              ? "Visitas aos imóveis"
              : view === "settings"
                ? "Cobertura e rótulos"
                : "Cadastro de imóveis"}
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
          <button
            aria-pressed={view === "properties"}
            className={view === "properties" ? "active" : ""}
            onClick={() => setView("properties")}
            type="button"
          >
            Imóveis
          </button>
          <button
            aria-pressed={view === "visits"}
            className={view === "visits" ? "active" : ""}
            onClick={() => setView("visits")}
            type="button"
          >
            Visitas
          </button>
          {canManageSettings && (
            <button
              aria-pressed={view === "settings"}
              className={view === "settings" ? "active" : ""}
              onClick={() => setView("settings")}
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
          <button onClick={() => setError(null)} type="button">
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
          unitId={unitId}
          reference={reference}
          reload={() => load(unitId)}
        />
      ) : (
        <>
          <section className="property-stats">
            <span>
              <strong>{total}</strong> resultados
            </span>
            <span>
              <strong>
                {
                  properties.filter((item) => item.coverageStatus === "overdue")
                    .length
                }
              </strong>{" "}
              fora do prazo nesta página
            </span>
            <span>
              <strong>
                {
                  properties.filter(
                    (item) =>
                      item.registrationStatus === "Draft" &&
                      !item.archivedAtUtc,
                  ).length
                }
              </strong>{" "}
              rascunhos nesta página
            </span>
            <span>
              <strong>
                {visits.filter((item) => !item.archivedAtUtc).length}
              </strong>{" "}
              visitas na ficha atual
            </span>
          </section>
          {reference && reference.microregions.length === 0 && (
            <section className="property-prerequisite" role="status">
              <div><span>Etapa necessária</span><h3>Cadastre uma microrregião antes do primeiro imóvel</h3><p>Todo imóvel precisa pertencer a uma microrregião ativa. Desenhe ou importe o limite no mapa territorial e depois volte a esta tela.</p></div>
              {onOpenTerritory && <button onClick={onOpenTerritory} type="button">Ir para o mapa territorial →</button>}
            </section>
          )}
          <div className="property-filters">
            <input
              aria-label="Buscar imóvel"
              onChange={(event) => setQueryInput(event.target.value)}
              placeholder="Buscar por rua, casa ou família"
              value={queryInput}
            />
            <select
              aria-label="Filtrar microrregião"
              onChange={(event) => {
                setPage(1);
                setMicroregionId(event.target.value);
              }}
              value={microregionId}
            >
              <option value="">Todas as microrregiões</option>
              {reference?.microregions.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.code} — {item.name}
                </option>
              ))}
            </select>
            <select
              aria-label="Filtrar cobertura"
              onChange={(event) => {
                setPage(1);
                setCoverage(event.target.value);
              }}
              value={coverage}
            >
              <option value="">Toda cobertura</option>
              <option value="overdue">Fora do prazo</option>
              <option value="neverVisited">Nunca visitado</option>
              <option value="covered">Em dia</option>
              <option value="notConfigured">Sem regra</option>
            </select>
          </div>
          <div className="property-layout">
            <aside aria-label="Imóveis encontrados" className="property-list">
              {loading && <p role="status">Atualizando…</p>}
              {filtered.length === 0 && !loading && (
                <p>Nenhum imóvel encontrado nesta UBS.</p>
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
                        : translateSituation(item.situation)}
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
                      {canManageVisits && !selected.archivedAtUtc && (
                        <button
                          className="property-primary"
                          onClick={() => setVisitEditor("new")}
                          type="button"
                        >
                          ＋ Registrar visita
                        </button>
                      )}
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
  const dialogRef = useAccessibleDialog<HTMLFormElement>(true, onCancel);
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

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
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
        if (event.currentTarget === event.target) onCancel();
      }}
    >
      <form
        aria-labelledby={titleId}
        aria-modal="true"
        className="property-modal"
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
            onClick={onCancel}
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
                  defaultValue={editing?.street}
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
                  defaultValue={editing?.postalCode ?? ""}
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
              <button disabled={saving} onClick={onCancel} type="button">
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
  const dialogRef = useAccessibleDialog<HTMLFormElement>(true, onCancel);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
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
      setError(messageOf(caught, "Não foi possível salvar a visita."));
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
        if (event.currentTarget === event.target) onCancel();
      }}
      role="presentation"
    >
      <form
        aria-labelledby={titleId}
        aria-modal="true"
        className="property-modal visit-modal"
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
            onClick={onCancel}
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
        <footer>
          <button onClick={onCancel} type="button">
            Cancelar
          </button>
          <button className="property-primary" type="submit">
            Salvar visita
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
  const [tags, setTags] = useState<Tag[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  useEffect(() => {
    if (!unitId) return;
    fetch(`/api/property-settings/tags?healthUnitId=${unitId}`, {
      credentials: "include",
    })
      .then((response) => response.json())
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
      form.reset();
      setNotice("Rótulo criado.");
      await reload();
    } catch (caught) {
      setError(messageOf(caught, "Não foi possível criar o rótulo."));
    }
  }
  async function toggleTag(tag: Tag) {
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
    } catch (caught) {
      setError(messageOf(caught, "Não foi possível salvar o prazo."));
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
        <form className="tag-create" onSubmit={(event) => void addTag(event)}>
          <input
            aria-label="Nome do rótulo operacional"
            maxLength={80}
            name="name"
            placeholder="Ex.: Difícil acesso"
            required
          />
          <input
            aria-label="Cor do rótulo"
            defaultValue="#4f9a7d"
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
  save: (id: string, days: number) => Promise<void>;
}) {
  const [days, setDays] = useState(initial);
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
          max={730}
          min={1}
          onChange={(event) => setDays(Number(event.target.value))}
          type="number"
          value={days}
        />{" "}
        dias
      </label>
      <button onClick={() => void save(microregion.id, days)} type="button">
        Salvar
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
        paint: { "fill-color": "#4f9a7d", "fill-opacity": 0.18 },
      });
      map.addLayer({
        id: "micro-line",
        source: "micro",
        type: "line",
        paint: { "line-color": "#26725f", "line-width": 2 },
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
