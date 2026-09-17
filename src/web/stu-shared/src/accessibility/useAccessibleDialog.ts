import { useEffect, useRef } from 'react'

const dialogStack: symbol[] = []

const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

export function useAccessibleDialog<T extends HTMLElement = HTMLElement>(active: boolean, onClose: () => void) {
  const dialogRef = useRef<T>(null)
  const closeRef = useRef(onClose)

  useEffect(() => {
    closeRef.current = onClose
  }, [onClose])

  useEffect(() => {
    if (!active) return
    const dialogId = Symbol('dialog')
    dialogStack.push(dialogId)

    const previousFocus = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null
    const frame = window.requestAnimationFrame(() => {
      const first = dialogRef.current?.querySelector<HTMLElement>(focusableSelector)
      ;(first ?? dialogRef.current)?.focus()
    })

    function onKeyDown(event: KeyboardEvent) {
      if (dialogStack.at(-1) !== dialogId) return
      if (event.key === 'Escape') {
        event.preventDefault()
        closeRef.current()
        return
      }
      if (event.key !== 'Tab' || !dialogRef.current) return

      const focusable = Array.from(
        dialogRef.current.querySelectorAll<HTMLElement>(focusableSelector),
      ).filter((element) => element.getAttribute('aria-hidden') !== 'true')
      if (focusable.length === 0) {
        event.preventDefault()
        dialogRef.current.focus()
        return
      }

      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      window.cancelAnimationFrame(frame)
      const index = dialogStack.indexOf(dialogId)
      if (index >= 0) dialogStack.splice(index, 1)
      document.removeEventListener('keydown', onKeyDown)
      previousFocus?.focus()
    }
  }, [active])

  return dialogRef
}
