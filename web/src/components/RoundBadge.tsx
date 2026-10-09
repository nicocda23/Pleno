import type { RoundStatus } from "../api/types";
import { statusLabel } from "../lib/messages";

export function RoundBadge({ status }: { status: RoundStatus }) {
  return <span className={`badge badge--${status.toLowerCase()}`}>{statusLabel(status)}</span>;
}
