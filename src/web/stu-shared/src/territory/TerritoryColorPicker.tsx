import { useEffect, useId, useRef, useState } from "react";

export const territoryColorOptions = [
  "#6D4AFF",
  "#A98BFF",
  "#C05A9D",
  "#7C5AC7",
  "#4F2C73",
  "#8B6FD6",
] as const;

export function TerritoryColorPicker({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const paletteId = useId();

  useEffect(() => {
    if (!open) return;
    const closeOnOutsideClick = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", closeOnOutsideClick);
    return () => document.removeEventListener("pointerdown", closeOnOutsideClick);
  }, [open]);

  return (
    <div className="territory-color-field" ref={rootRef}>
      <span>Cor da área</span>
      <button
        aria-label={`Escolher cor da área: ${value}`}
        aria-controls={paletteId}
        aria-expanded={open}
        className="territory-color-trigger"
        onClick={() => setOpen((current) => !current)}
        type="button"
      >
        <i aria-hidden="true" style={{ backgroundColor: value }} />
        <output>{value}</output>
        <b aria-hidden="true">{open ? "▲" : "▼"}</b>
      </button>
      {open && (
        <div aria-label="Cores disponíveis" className="territory-color-palette" id={paletteId} role="group">
          {territoryColorOptions.map((color) => (
            <button
              aria-label={`Usar cor ${color}`}
              aria-pressed={value.toLowerCase() === color}
              key={color}
              onClick={() => {
                onChange(color);
                setOpen(false);
              }}
              style={{ backgroundColor: color }}
              type="button"
            />
          ))}
          <label className="territory-custom-color">
            <span>Cor personalizada</span>
            <input
              aria-label="Escolher uma cor personalizada"
              onChange={(event) => onChange(event.currentTarget.value.toUpperCase())}
              type="color"
              value={value}
            />
          </label>
        </div>
      )}
    </div>
  );
}
