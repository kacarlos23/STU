import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { ApiError, changePassword, getCurrentSession, login, logout } from './api'
import type { AuthenticatedContext, Portal, Session } from './types'
import './auth.css'

type AuthGateProps = {
  portal: Portal
  requiredRole?: string
  children: (context: AuthenticatedContext) => ReactNode
}

function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const validationMessage = Object.values(error.validationErrors).flat()[0]
    return validationMessage ?? error.message
  }
  return 'Não foi possível acessar o servidor. Verifique sua conexão e tente novamente.'
}

export function AuthGate({ portal, requiredRole, children }: AuthGateProps) {
  const [session, setSession] = useState<Session | null>(null)
  const [checking, setChecking] = useState(true)
  const [initialError, setInitialError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    getCurrentSession()
      .then((current) => {
        if (active) setSession(current)
      })
      .catch((error: unknown) => {
        if (active) setInitialError(errorMessage(error))
      })
      .finally(() => {
        if (active) setChecking(false)
      })
    return () => { active = false }
  }, [])

  async function handleLogout() {
    await logout()
    setSession(null)
  }

  if (checking) {
    return (
      <main className="stu-auth-loading" aria-live="polite">
        <span className="stu-auth-spinner" aria-hidden="true" />
        <strong>Verificando acesso seguro…</strong>
      </main>
    )
  }

  if (!session) {
    return (
      <LoginScreen
        portal={portal}
        initialError={initialError}
        onAuthenticated={setSession}
      />
    )
  }

  if (session.mustChangePassword) {
    return <PasswordChangeScreen session={session} onChanged={setSession} />
  }

  if (requiredRole && !session.roles.some((role) => role.name === requiredRole)) {
    return (
      <main className="stu-auth-page">
        <section className="stu-auth-card stu-auth-card--compact">
          <div className="stu-auth-mark" aria-hidden="true">STU</div>
          <span className="stu-auth-kicker">Acesso restrito</span>
          <h1>Esta conta não acessa este painel.</h1>
          <p>Entre com uma conta de administrador global ou retorne ao sistema principal.</p>
          <button className="stu-auth-primary" type="button" onClick={() => void handleLogout()}>
            Sair desta conta
          </button>
        </section>
      </main>
    )
  }

  return children({ session, logout: handleLogout })
}

type LoginScreenProps = {
  portal: Portal
  initialError: string | null
  onAuthenticated: (session: Session) => void
}

function LoginScreen({ portal, initialError, onAuthenticated }: LoginScreenProps) {
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(initialError)
  const isAdmin = portal === 'admin'

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      onAuthenticated(await login(userName, password, portal))
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className={`stu-auth-page ${isAdmin ? 'stu-auth-page--admin' : ''}`}>
      <section className="stu-auth-story" aria-label="Apresentação do STU">
        <div className="stu-auth-brand">
          <span className="stu-auth-brand-mark">STU</span>
          <span>Sistema Territorial das UBS</span>
        </div>
        <div className="stu-auth-story-copy">
          <span className="stu-auth-kicker">{isAdmin ? 'Administração global' : 'Território em uma só visão'}</span>
          <h1>{isAdmin ? 'Gestão integral, com cada ação protegida.' : 'Cuidar do território começa por enxergá-lo.'}</h1>
          <p>
            {isAdmin
              ? 'Acesso exclusivo para configuração, supervisão e operação técnica de toda a plataforma.'
              : 'Acesse imóveis, visitas e cobertura da sua UBS em um ambiente simples e seguro.'}
          </p>
        </div>
        <div className="stu-auth-territory" aria-hidden="true">
          <span /><span /><span /><span /><span />
        </div>
      </section>

      <section className="stu-auth-form-panel">
        <form className="stu-auth-card" onSubmit={(event) => void submit(event)}>
          <div className="stu-auth-mobile-brand">STU</div>
          <span className="stu-auth-kicker">Acesso seguro</span>
          <h2>Entre na sua conta</h2>
          <p className="stu-auth-help">
            Use o usuário e a senha individual fornecidos pelo responsável do sistema.
          </p>

          <label className="stu-auth-field">
            <span>Usuário</span>
            <input
              autoComplete="username"
              autoFocus
              maxLength={120}
              name="username"
              onChange={(event) => setUserName(event.target.value)}
              placeholder="Digite seu usuário"
              required
              value={userName}
            />
          </label>

          <label className="stu-auth-field">
            <span>Senha</span>
            <div className="stu-auth-password-field">
              <input
                autoComplete="current-password"
                maxLength={200}
                name="password"
                onChange={(event) => setPassword(event.target.value)}
                placeholder="Digite sua senha"
                required
                type={showPassword ? 'text' : 'password'}
                value={password}
              />
              <button type="button" onClick={() => setShowPassword((visible) => !visible)}>
                {showPassword ? 'Ocultar' : 'Mostrar'}
              </button>
            </div>
          </label>

          {error && <div className="stu-auth-error" role="alert">{error}</div>}

          <button className="stu-auth-primary" disabled={submitting} type="submit">
            {submitting ? 'Entrando…' : 'Entrar no STU'}
          </button>
          <p className="stu-auth-footnote">O acesso é individual. Não compartilhe sua senha.</p>
        </form>
      </section>
    </main>
  )
}

type PasswordChangeScreenProps = {
  session: Session
  onChanged: (session: Session) => void
}

function PasswordChangeScreen({ session, onChanged }: PasswordChangeScreenProps) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [showPasswords, setShowPasswords] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      onChanged(await changePassword(currentPassword, newPassword, confirmPassword))
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="stu-auth-page stu-auth-page--password">
      <section className="stu-auth-card stu-auth-card--password">
        <div className="stu-auth-mark" aria-hidden="true">STU</div>
        <span className="stu-auth-kicker">Primeiro acesso</span>
        <h1>Crie sua senha definitiva</h1>
        <p>Olá, {session.displayName}. A senha temporária precisa ser substituída antes de continuar.</p>

        <form onSubmit={(event) => void submit(event)}>
          <label className="stu-auth-field">
            <span>Senha temporária</span>
            <input
              autoComplete="current-password"
              onChange={(event) => setCurrentPassword(event.target.value)}
              required
              type={showPasswords ? 'text' : 'password'}
              value={currentPassword}
            />
          </label>
          <label className="stu-auth-field">
            <span>Nova senha</span>
            <input
              autoComplete="new-password"
              minLength={12}
              onChange={(event) => setNewPassword(event.target.value)}
              required
              type={showPasswords ? 'text' : 'password'}
              value={newPassword}
            />
          </label>
          <label className="stu-auth-field">
            <span>Confirme a nova senha</span>
            <input
              autoComplete="new-password"
              minLength={12}
              onChange={(event) => setConfirmPassword(event.target.value)}
              required
              type={showPasswords ? 'text' : 'password'}
              value={confirmPassword}
            />
          </label>
          <label className="stu-auth-check">
            <input
              checked={showPasswords}
              onChange={(event) => setShowPasswords(event.target.checked)}
              type="checkbox"
            />
            Mostrar senhas
          </label>
          <div className="stu-auth-rules">
            Use pelo menos 12 caracteres, com letra maiúscula, minúscula, número e símbolo.
          </div>
          {error && <div className="stu-auth-error" role="alert">{error}</div>}
          <button className="stu-auth-primary" disabled={submitting} type="submit">
            {submitting ? 'Salvando…' : 'Salvar nova senha e continuar'}
          </button>
        </form>
      </section>
    </main>
  )
}

