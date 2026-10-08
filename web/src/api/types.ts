// Contratos de la API (JSON en camelCase). Las fichas son siempre enteros: nunca decimales.

export interface Me {
  userId: string;
  displayName: string | null;
  roles: string[];
  accountId: string;
  registeredAt: string;
}

export interface Account {
  accountId: string;
  userId: string;
  available: number;
  reserved: number;
  version: number;
  openReservations: Record<string, number>;
}

export type RoundStatus = "Placed" | "Resolved" | "Settled" | "Rejected" | "Voided";

export interface Round {
  betId: string;
  status: RoundStatus;
  betType: RouletteBetType;
  selection: number[];
  stake: number;
  pairId: string;
  nonce: number;
  winningNumber: number | null;
  payout: number | null;
  failureReason: string | null;
  placedAt: string;
}

export type RouletteBetType =
  | "Straight"
  | "Split"
  | "Street"
  | "Trio"
  | "Corner"
  | "FirstFour"
  | "SixLine"
  | "Dozen"
  | "Column"
  | "Red"
  | "Black"
  | "Even"
  | "Odd"
  | "Low"
  | "High";

export interface PlaceBetBody {
  betType: RouletteBetType;
  selection: number[];
  stake: number;
}

export interface PlacedBet {
  betId: string;
  status: RoundStatus;
  nonce: number;
  commitment: string;
  clientSeed: string;
  alreadyPlaced: boolean;
}

export interface RetiredPair {
  pairId: string;
  commitment: string;
  serverSeed: string;
  clientSeed: string;
  betsPlayed: number;
}

export interface FairnessInfo {
  userId: string;
  active: { pairId: string; commitment: string; clientSeed: string; nextNonce: number };
  pendingBets: number;
  retired: RetiredPair[];
}

// Avisos en vivo (SignalR)
export interface BalanceNotice {
  available: number;
  reserved: number;
  version: number;
}

export interface RoundClosedNotice {
  betId: string;
  game: string;
  status: "Settled" | "Rejected" | "Voided";
  winningNumber: number | null;
  stake: number;
  payout: number;
  failureReason: string | null;
}
