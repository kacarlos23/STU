export async function mutate(path: string, method: "POST" | "PUT", body?: unknown) {
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
