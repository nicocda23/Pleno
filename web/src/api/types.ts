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
  /** Total apostado en la tirada. */
  stake: number;
  bets: BetLine[];
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

/** Una apuesta dentro de una tirada. */
export interface BetLine {
  betType: RouletteBetType;
  selection: number[];
  stake: number;
}

/** Una tirada: una o varias apuestas que comparten el mismo numero sorteado. */
export interface PlaceBetBody {
  bets: BetLine[];
}

export interface PlacedBet {
  betId: string;
  status: RoundStatus;
  nonce: number;
  commitment: string;
  clientSeed: string;
  alreadyPlaced: boolean;
}

/** Un giro de tragamonedas. */
export interface Spin {
  betId: string;
  status: RoundStatus;
  stake: number;
  /** Nombre del simbolo de cada rodillo (vacio hasta que se sortea). */
  reels: string[];
  multiplier: number | null;
  payout: number | null;
  pairId: string;
  nonce: number;
  failureReason: string | null;
  placedAt: string;
}

export interface PaytableSymbol {
  name: string;
  weight: number;
  triplePayout: number;
}

export interface LeadingPay {
  symbol: string;
  count: number;
  payout: number;
}

/** Tabla de pagos publica de la tragamonedas. */
export interface Paytable {
  reels: number;
  symbols: PaytableSymbol[];
  leadingPays: LeadingPay[];
  minStake: number;
  maxStake: number;
  totalWeight: number;
  returnToPlayerPercent: number;
  hitRatePercent: number;
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
