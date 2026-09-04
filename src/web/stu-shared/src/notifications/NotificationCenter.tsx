import { useEffect, useId, useRef, useState } from "react";
import "./notifications.css";

type Notification = {
  id: string;
  title: string;
  message: string;
  readAtUtc: string | null;
  createdAtUtc: string;
};
type NotificationResponse = { items: Notification[]; unreadCount: number };

export function NotificationCenter() {
  const [data, setData] = useState<NotificationResponse>({
    items: [],
    unreadCount: 0,
  });
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const panelId = useId();

  async function load() {
    const response = await fetch("/api/notifications?unreadOnly=false", {
      credentials: "include",
    });
    if (response.ok) setData((await response.json()) as NotificationResponse);
  }
  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), 30000);
    return () => window.clearInterval(timer);
  }, []);
  useEffect(() => {
    if (!open) return;
    function closeWithEscape(event: KeyboardEvent) {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setOpen(false);
      trigger.current?.focus();
    }
    document.addEventListener("keydown", closeWithEscape);
    return () => document.removeEventListener("keydown", closeWithEscape);
  }, [open]);
  useEffect(() => {
    function close(event: MouseEvent) {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, []);

  async function mark(path: string) {
    const csrf = await fetch("/api/auth/csrf", { credentials: "include" });
    if (!csrf.ok) return;
    const { token } = (await csrf.json()) as { token: string };
    const response = await fetch(path, {
      method: "POST",
      credentials: "include",
      headers: { "X-STU-CSRF": token },
    });
    if (response.ok) await load();
  }

  return (
    <div className="notification-center" ref={root}>
      <button
        aria-controls={panelId}
        aria-expanded={open}
        aria-label={`Notificações${data.unreadCount ? `, ${data.unreadCount} não lidas` : ""}`}
        className="notification-trigger"
        onClick={() => setOpen((value) => !value)}
        ref={trigger}
        type="button"
      >
        <span aria-hidden="true">◉</span>
        {data.unreadCount > 0 && (
          <b>{data.unreadCount > 9 ? "9+" : data.unreadCount}</b>
        )}
      </button>
      {open && (
        <aside
          aria-label="Central de notificações"
          className="notification-panel"
          id={panelId}
        >
          <header>
            <div>
              <strong>Notificações</strong>
              <small>{data.unreadCount} não lida(s)</small>
            </div>
            {data.unreadCount > 0 && (
              <button
                onClick={() => void mark("/api/notifications/read-all")}
                type="button"
              >
                Marcar todas
              </button>
            )}
          </header>
          <div>
            {data.items.length === 0 ? (
              <p className="notification-empty">
                Você ainda não recebeu notificações.
              </p>
            ) : (
              data.items.map((item) => (
                <button
                  className={
                    item.readAtUtc
                      ? "notification-item"
                      : "notification-item notification-item--unread"
                  }
                  key={item.id}
                  onClick={() => {
                    if (!item.readAtUtc)
                      void mark(`/api/notifications/${item.id}/read`);
                  }}
                  type="button"
                >
                  <strong>{item.title}</strong>
                  <span>{item.message}</span>
                  <small>
                    {new Date(item.createdAtUtc).toLocaleString("pt-BR")}
                  </small>
                </button>
              ))
            )}
          </div>
        </aside>
      )}
    </div>
  );
}
