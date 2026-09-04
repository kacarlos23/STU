import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { TerritoryColorPicker } from "@stu/shared/territory";

afterEach(cleanup);

describe("seletor de cor territorial", () => {
  it("abre e fecha a paleta ao clicar novamente no mesmo controle", () => {
    render(<TerritoryColorPicker onChange={vi.fn()} value="#2e8b72" />);
    const trigger = screen.getByRole("button", { name: /Escolher cor da área/i });

    fireEvent.click(trigger);
    expect(screen.getByRole("group", { name: "Cores disponíveis" })).not.toBeNull();

    fireEvent.click(trigger);
    expect(screen.queryByRole("group", { name: "Cores disponíveis" })).toBeNull();
  });

  it("fecha ao clicar fora e após escolher uma cor", () => {
    const onChange = vi.fn();
    render(<TerritoryColorPicker onChange={onChange} value="#2e8b72" />);
    const trigger = screen.getByRole("button", { name: /Escolher cor da área/i });

    fireEvent.click(trigger);
    fireEvent.pointerDown(document.body);
    expect(screen.queryByRole("group", { name: "Cores disponíveis" })).toBeNull();

    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("button", { name: "Usar cor #2563eb" }));
    expect(onChange).toHaveBeenCalledWith("#2563eb");
    expect(screen.queryByRole("group", { name: "Cores disponíveis" })).toBeNull();
  });
});
