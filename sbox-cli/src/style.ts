import type { Style } from "./api";

export function styleFor(enabled: boolean): Style {
  const sgr = (open: number, close: number) => (text: string) =>
    enabled ? `\x1b[${open}m${text}\x1b[${close}m` : text;
  return {
    enabled,
    bold: sgr(1, 22),
    dim: sgr(2, 22),
    red: sgr(31, 39),
    green: sgr(32, 39),
    yellow: sgr(33, 39),
    cyan: sgr(36, 39),
  };
}
