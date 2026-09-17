import { createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type ReactNode, type SetStateAction } from 'react'
import { createPortal } from 'react-dom'
import { useAccessibleDialog } from '../accessibility/useAccessibleDialog'
import './interaction.css'

type Confirmation = { title: string; message: string; confirmLabel: string }
type Actions = {
  confirm: (options: Confirmation) => Promise<boolean>
  guard: (action: () => void) => Promise<void>
  registerDirty: (id: string, dirty: boolean) => void
  preferences: Map<string, unknown>
}
const Context = createContext<Actions | null>(null)

// Session-only UI preferences. Never persisted to storage or a service worker.
export function InteractionProvider({ children }: { children: ReactNode }) {
  const dirty = useRef(new Set<string>())
  const preferences = useRef(new Map<string, unknown>())
  const resolver = useRef<((answer: boolean) => void) | null>(null)
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null)
  const confirm = useCallback((options: Confirmation) => {
    if (resolver.current) return Promise.resolve(false)
    return new Promise<boolean>(resolve => { resolver.current = resolve; setConfirmation(options) })
  }, [])
  const guard = useCallback(async (action: () => void) => {
    if (dirty.current.size && !await confirm({ title: 'Descartar alterações?', message: 'Há alterações não salvas. Você pode continuar editando ou descartá-las para sair.', confirmLabel: 'Descartar e sair' })) return
    action()
  }, [confirm])
  const registerDirty = useCallback((id: string, changed: boolean) => {
    if (changed) dirty.current.add(id)
    else dirty.current.delete(id)
  }, [])
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (!dirty.current.size) return
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', beforeUnload)
    return () => { window.removeEventListener('beforeunload', beforeUnload); resolver.current?.(false) }
  }, [])
  function answer(value: boolean) {
    const resolve = resolver.current
    resolver.current = null
    setConfirmation(null)
    resolve?.(value)
  }
  const actions = useMemo(() => ({ confirm, guard, registerDirty, preferences: preferences.current }), [confirm, guard, registerDirty])
  return <Context.Provider value={actions}>{children}{confirmation && createPortal(<ConfirmationDialog {...confirmation} answer={answer} />, document.body)}</Context.Provider>
}

function ConfirmationDialog({ title, message, confirmLabel, answer }: Confirmation & { answer: (value: boolean) => void }) {
  const titleId = useId()
  const descriptionId = useId()
  const ref = useAccessibleDialog<HTMLDivElement>(true, () => answer(false))
  return <div className="stu-confirm-backdrop"><div className="stu-confirm" role="alertdialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={descriptionId} tabIndex={-1} ref={ref}>
    <h2 id={titleId}>{title}</h2><p id={descriptionId}>{message}</p>
    <div><button onClick={() => answer(false)} type="button">Continuar aqui</button><button className="stu-confirm-primary" onClick={() => answer(true)} type="button">{confirmLabel}</button></div>
  </div></div>
}

export function useUiActions() {
  const actions = useContext(Context)
  if (!actions) throw new Error('InteractionProvider ausente')
  return actions
}

export function useUnsavedChanges(dirty: boolean) {
  const id = useId()
  const { registerDirty } = useUiActions()
  useEffect(() => { registerDirty(id, dirty); return () => registerDirty(id, false) }, [id, dirty, registerDirty])
}

export function useSessionPreferences<T>(key: string, initial: T) {
  const { preferences } = useUiActions()
  const [value, setValue] = useState<T>(() => preferences.has(key) ? preferences.get(key) as T : initial)
  useEffect(() => { preferences.set(key, value) }, [key, value, preferences])
  return [value, setValue] as [T, (next: SetStateAction<T>) => void]
}
