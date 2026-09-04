/* oxlint-disable react/set-state-in-effect -- effects synchronize state with the monitoring API */
import { useCallback, useEffect, useState } from 'react'
import { adminApi, type MonitoringStatusItem } from './adminApi'

const statusLabels = { Healthy: 'Normal', Warning: 'Atenção', Critical: 'Crítico' }

function formatDate(value: string | null) {
  return value
    ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'medium' }).format(new Date(value))
    : 'Ainda não observado'
}

export function MonitoringSection() {
  const [snapshot, setSnapshot] = useState<MonitoringStatusItem | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)

  const load = useCallback(async () => {
    setRefreshing(true)
    try {
      setSnapshot(await adminApi.monitoringStatus()); setError(null)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Não foi possível consultar a saúde do sistema.')
    } finally { setRefreshing(false) }
  }, [])

  useEffect(() => {
    void load()
    const timer = window.setInterval(() => void load(), 30000)
    return () => window.clearInterval(timer)
  }, [load])

  return <section className="management-page monitoring-page">
    <header className="management-heading"><div><span>Disponibilidade</span><h2>Monitoramento</h2><p>Saúde dos serviços essenciais e alertas que exigem atenção.</p></div><button className="management-primary" disabled={refreshing} onClick={() => void load()} type="button">{refreshing ? 'Verificando…' : 'Verificar agora'}</button></header>
    {error && <div className="admin-error" role="alert">{error}</div>}
    {snapshot && <>
      <div className={`monitoring-overall monitoring-overall--${snapshot.overallStatus.toLowerCase()}`} role="status"><div><span>Situação geral</span><strong>{statusLabels[snapshot.overallStatus]}</strong></div><p>Última verificação: {formatDate(snapshot.checkedAtUtc)}. Atualização automática a cada 30 segundos.</p><a href="/health/ready" rel="noreferrer" target="_blank">Abrir teste de prontidão</a></div>
      <section className="monitoring-checks" aria-label="Componentes monitorados">{snapshot.checks.map((check) => <article key={check.id}><header><span className={`monitoring-dot monitoring-dot--${check.status.toLowerCase()}`} /><small>{statusLabels[check.status]}</small></header><h3>{check.label}</h3><p>{check.detail}</p><time>{formatDate(check.lastObservedAtUtc)}</time></article>)}</section>
      <section className="monitoring-alerts"><header><div><span>Ocorrências ativas</span><h3>Alertas operacionais</h3></div><strong>{snapshot.alerts.length}</strong></header>
        {snapshot.alerts.length === 0 ? <div className="monitoring-empty"><span>✓</span><div><strong>Nenhum alerta ativo</strong><p>Os componentes monitorados estão dentro dos limites esperados.</p></div></div> : <div className="monitoring-alert-list">{snapshot.alerts.map((alert) => <article className={`monitoring-alert monitoring-alert--${alert.severity.toLowerCase()}`} key={alert.code}><span>{alert.severity === 'Critical' ? '!' : 'i'}</span><div><strong>{alert.title}</strong><p>{alert.detail}</p></div><small>{statusLabels[alert.severity]}</small></article>)}</div>}
      </section>
      <p className="monitoring-note">“Crítico” indica interrupção de uma função essencial. “Atenção” indica atraso ou falha operacional que deve ser analisada, mas não torna o sistema inteiro indisponível.</p>
    </>}
  </section>
}
