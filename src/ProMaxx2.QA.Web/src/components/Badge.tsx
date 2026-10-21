import type { ReactNode } from "react";

export function Badge({
  children,
  tone = "blue",
}: {
  children: ReactNode;
  tone?: string;
}) {
  const effectiveTone = children === "No Data" ? "blue" : tone;
  return <span className={`badge ${effectiveTone}`}>{children}</span>;
}
