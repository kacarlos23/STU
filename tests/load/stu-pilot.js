import http from "k6/http";
import { check, fail, sleep } from "k6";
import { vu } from "k6/execution";

const baseUrl = __ENV.STU_BASE_URL || "http://host.docker.internal:8092";
const password = __ENV.STU_PASSWORD;
const profile = (__ENV.STU_PROFILE || "smoke").toLowerCase();
const resultPath = __ENV.STU_RESULT_PATH || "summary.json";

if (!password) {
  throw new Error("STU_PASSWORD is required.");
}

http.setResponseCallback(http.expectedStatuses({ min: 200, max: 399 }, 403));

export const options = {
  scenarios: scenariosFor(profile),
  thresholds: {
    checks: ["rate>0.99"],
    http_req_failed: ["rate<0.01"],
    "http_req_duration{class:login}": ["p(95)<2000"],
    "http_req_duration{class:ordinary}": ["p(95)<2000"],
    "http_req_duration{class:map}": ["p(95)<3000"],
    "http_req_duration{class:write}": ["p(95)<2000"],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "p(99)", "max"],
  batchPerHost: 6,
  noCookiesReset: true,
  userAgent: `STU-pilot-k6/${profile}`,
};

let activeSession;

export function mapRead() {
  const userName = readUserName(vu.idInTest);
  authenticate(userName);

  const maps = http.batch([
    request("GET", "/api/territories/map", "map", "territory_map", true),
    request("GET", "/api/properties/map", "map", "property_map", true),
  ]);
  verify(maps[0], { "territory map returned 200": response => response.status === 200 }, "territory map");
  verify(maps[1], { "property map returned 200": response => response.status === 200 }, "property map");

  const dashboard = http.get(`${baseUrl}/api/dashboard/summary`, tags("ordinary", "dashboard"));
  const notifications = http.get(`${baseUrl}/api/notifications?unreadOnly=false`, tags("ordinary", "notifications"));
  verify(dashboard, { "dashboard returned 200": response => response.status === 200 }, "dashboard");
  verify(notifications, { "notifications returned 200": response => response.status === 200 }, "notifications");
  sleep(2 + (vu.idInTest % 3));
}

export function propertySearch() {
  authenticate(searchUserName(vu.idInTest));
  const page = (vu.idInTest % 10) + 1;
  const list = http.get(
    `${baseUrl}/api/properties?query=Rua&includeArchived=false&page=${page}&pageSize=20`,
    tags("ordinary", "property_search"),
  );
  const body = safeJson(list);
  const items = body?.items || [];
  verify(list, {
    "property search returned 200": response => response.status === 200,
    "property search returned items": () => items.length > 0,
  }, "property search");
  if (items.length === 0) {
    sleep(1);
    return;
  }

  const propertyId = items[vu.idInTest % items.length].id;
  const details = http.batch([
    request("GET", `/api/properties/${propertyId}`, "ordinary", "property_detail"),
    request("GET", `/api/properties/${propertyId}/visits`, "ordinary", "visit_history"),
  ]);
  verify(details[0], { "property detail returned 200": response => response.status === 200 }, "property detail");
  verify(details[1], { "visit history returned 200": response => response.status === 200 }, "visit history");
  sleep(1 + (vu.idInTest % 2));
}

export function visitWrite() {
  authenticate(`pilot.agent.${pad(((vu.idInTest - 1) % 20) + 1)}`);
  const page = (vu.idInTest % 10) + 1;
  const list = http.get(
    `${baseUrl}/api/properties?includeArchived=false&page=${page}&pageSize=10`,
    tags("ordinary", "agent_property_list"),
  );
  const items = safeJson(list)?.items || [];
  verify(list, {
    "agent list returned 200": response => response.status === 200,
    "agent has assigned properties": () => items.length > 0,
  }, "agent property list");
  if (items.length === 0) {
    sleep(2);
    return;
  }

  const propertyId = items[vu.idInTest % items.length].id;
  activeSession.csrfToken = refreshCsrf("visit creation");
  const response = http.post(
    `${baseUrl}/api/properties/${propertyId}/visits`,
    JSON.stringify({
      visitedAtUtc: new Date().toISOString(),
      type: "Routine",
      outcome: "Completed",
      observedSituation: "Occupied",
      accessDifficulty: false,
      note: "Registro sintético do ensaio de carga.",
      expectedVersion: null,
    }),
    writeTags("visit_create"),
  );
  verify(response, { "visit creation returned 201": value => value.status === 201 }, "visit creation");
  sleep(4);
}

export function management() {
  authenticate(`pilot.manager.${pad(((vu.idInTest - 1) % 10) + 1)}`);
  const responses = http.batch([
    request("GET", "/api/dashboard/summary", "ordinary", "manager_dashboard"),
    request("GET", "/api/operations/indicators", "ordinary", "indicators"),
    request("GET", "/api/operations/jobs", "ordinary", "operation_jobs"),
    request("GET", "/api/properties/reference-data", "ordinary", "property_reference"),
  ]);
  verify(responses[0], { "manager dashboard returned 200": response => response.status === 200 }, "manager dashboard");
  verify(responses[1], { "indicators returned 200": response => response.status === 200 }, "indicators");
  verify(responses[2], { "operation jobs returned 200": response => response.status === 200 }, "operation jobs");
  verify(responses[3], { "property reference returned 200": response => response.status === 200 }, "property reference");
  sleep(2 + (vu.idInTest % 2));
}

export function isolation() {
  authenticate("pilot.isolation.001");
  const list = http.get(`${baseUrl}/api/properties?includeArchived=false&page=1&pageSize=5`, tags("ordinary", "isolation_list"));
  const map = http.get(`${baseUrl}/api/properties/map`, tags("map", "isolation_map"));
  const dashboard = http.get(`${baseUrl}/api/dashboard/summary`, tags("ordinary", "isolation_dashboard"));
  const listBody = safeJson(list);
  const mapBody = safeJson(map);
  const dashboardBody = safeJson(dashboard);

  verify(list, {
    "isolation list returned 200": response => response.status === 200,
    "isolation list is empty": () => listBody?.total === 0,
  }, "isolation list");
  verify(map, {
    "isolation map returned 200": response => response.status === 200,
    "isolation map is empty": () => (mapBody?.features || []).length === 0,
  }, "isolation map");
  verify(dashboard, {
    "isolation dashboard returned 200": response => response.status === 200,
    "isolation dashboard has no properties": () => dashboardBody?.activeProperties === 0,
  }, "isolation dashboard");
  sleep(2);
}

export function handleSummary(data) {
  const compact = {
    profile,
    generatedAtUtc: new Date().toISOString(),
    checks: data.metrics.checks?.values,
    failures: data.metrics.http_req_failed?.values,
    duration: data.metrics.http_req_duration?.values,
    ordinary: data.metrics["http_req_duration{class:ordinary}"]?.values,
    map: data.metrics["http_req_duration{class:map}"]?.values,
    write: data.metrics["http_req_duration{class:write}"]?.values,
    login: data.metrics["http_req_duration{class:login}"]?.values,
    iterations: data.metrics.iterations?.values,
    vusMax: data.metrics.vus_max?.values,
    receivedBytes: data.metrics.data_received?.values,
    thresholdsPassed: thresholdsPassed(data),
  };

  return {
    [resultPath]: JSON.stringify(data, null, 2),
    stdout: `${JSON.stringify(compact, null, 2)}\n`,
  };
}

function authenticate(userName) {
  if (activeSession?.userName === userName) {
    return activeSession;
  }

  http.cookieJar().clear(baseUrl);
  const csrfResponse = http.get(`${baseUrl}/api/auth/csrf`, tags("login", "csrf"));
  const token = safeJson(csrfResponse)?.token;
  verify(csrfResponse, {
    "csrf returned 200": response => response.status === 200,
    "csrf returned token": () => Boolean(token),
  }, `csrf for ${userName}`);
  if (!token) {
    fail(`CSRF token unavailable for ${userName}`);
  }

  const loginResponse = http.post(
    `${baseUrl}/api/auth/login`,
    JSON.stringify({ userName, password, portal: "main" }),
    {
      headers: { "Content-Type": "application/json", "X-STU-CSRF": token },
      tags: { class: "login", endpoint: "login" },
    },
  );
  const body = safeJson(loginResponse);
  verify(loginResponse, {
    "login returned 200": response => response.status === 200,
    "login returned requested user": () => body?.userName === userName,
    "login does not require password change": () => body?.mustChangePassword === false,
  }, `login for ${userName}`);
  if (loginResponse.status !== 200) {
    fail(`Login failed for ${userName}: ${loginResponse.status}`);
  }

  activeSession = { userName, csrfToken: token };
  return activeSession;
}

function request(method, path, requestClass, endpoint, discardBody = false) {
  return {
    method,
    url: `${baseUrl}${path}`,
    params: {
      responseType: discardBody ? "none" : "text",
      tags: { class: requestClass, endpoint },
    },
  };
}

function tags(requestClass, endpoint) {
  return { tags: { class: requestClass, endpoint } };
}

function writeTags(endpoint) {
  return {
    headers: { "Content-Type": "application/json", "X-STU-CSRF": activeSession.csrfToken },
    tags: { class: "write", endpoint },
  };
}

function refreshCsrf(label) {
  const response = http.get(`${baseUrl}/api/auth/csrf`, tags("write", `${label}_csrf`));
  const token = safeJson(response)?.token;
  verify(response, {
    "write csrf returned 200": value => value.status === 200,
    "write csrf returned token": () => Boolean(token),
  }, `${label} csrf`);
  if (!token) {
    fail(`CSRF token unavailable for ${label}`);
  }
  return token;
}

function safeJson(response) {
  try {
    return response.json();
  } catch (_) {
    return null;
  }
}

function verify(response, assertions, label) {
  const passed = check(response, assertions);
  if (!passed) {
    const responsePreview = String(response?.body || "").replace(/\s+/g, " ").slice(0, 240);
    console.error(`${label} failed: status=${response?.status} url=${response?.url} body=${responsePreview}`);
  }
  return passed;
}

function readUserName(identifier) {
  const bucket = identifier % 10;
  if (bucket < 5) return `pilot.agent.${pad(((identifier - 1) % 20) + 1)}`;
  if (bucket < 7) return `pilot.reception.${pad(((identifier - 1) % 19) + 1)}`;
  if (bucket < 9) return `pilot.doctor.${pad(((identifier - 1) % 20) + 1)}`;
  return `pilot.manager.${pad(((identifier - 1) % 10) + 1)}`;
}

function searchUserName(identifier) {
  return identifier % 2 === 0
    ? `pilot.reception.${pad(((identifier - 1) % 19) + 1)}`
    : `pilot.doctor.${pad(((identifier - 1) % 20) + 1)}`;
}

function pad(value) {
  return String(value).padStart(3, "0");
}

function scenariosFor(selectedProfile) {
  if (selectedProfile === "smoke") {
    return {
      map_read: iterations("mapRead", 5),
      property_search: iterations("propertySearch", 2),
      visit_write: iterations("visitWrite", 1),
      management: iterations("management", 1),
      isolation: iterations("isolation", 1),
    };
  }

  if (selectedProfile === "baseline") {
    return {
      map_read: constant("mapRead", 25),
      property_search: constant("propertySearch", 12),
      visit_write: constant("visitWrite", 8),
      management: constant("management", 4),
      isolation: constant("isolation", 1),
    };
  }

  if (selectedProfile === "gate") {
    return {
      map_read: ramp("mapRead", [38, 75, 150]),
      property_search: ramp("propertySearch", [19, 38, 75]),
      visit_write: ramp("visitWrite", [11, 23, 45]),
      management: ramp("management", [6, 14, 29]),
      isolation: ramp("isolation", [1, 1, 1]),
    };
  }

  throw new Error(`Unknown STU_PROFILE '${selectedProfile}'.`);
}

function iterations(exec, vus) {
  return { executor: "per-vu-iterations", exec, vus, iterations: 1, maxDuration: "1m" };
}

function constant(exec, vus) {
  return { executor: "constant-vus", exec, vus, duration: __ENV.STU_BASELINE_DURATION || "1m", gracefulStop: "15s" };
}

function ramp(exec, targets) {
  return {
    executor: "ramping-vus",
    exec,
    startVUs: 0,
    stages: [
      { duration: "30s", target: targets[0] },
      { duration: "30s", target: targets[1] },
      { duration: "1m", target: targets[2] },
      { duration: __ENV.STU_GATE_HOLD || "2m", target: targets[2] },
      { duration: "20s", target: 0 },
    ],
    gracefulRampDown: "15s",
  };
}

function thresholdsPassed(data) {
  return Object.values(data.metrics).every(metric =>
    Object.values(metric.thresholds || {}).every(threshold => threshold.ok),
  );
}
