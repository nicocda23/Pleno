// Saldo en vivo. Dos fuentes dicen lo mismo: una consulta HTTP (estado completo) y avisos de SignalR (cambios).
// Los avisos pueden llegar repetidos o FUERA DE ORDEN entre instancias de la API, asi que cada saldo lleva una `version`
// (la cantidad de eventos de la cuenta) y solo se acepta lo que NO es mas viejo que lo que ya se muestra.

export interface BalanceState {
  available: number;
  reserved: number;
  version: number;
  /** Falso hasta tener el primer dato real: evita mostrar un 0 falso mientras carga. */
  ready: boolean;
}

export interface BalanceSnapshot {
  available: number;
  reserved: number;
  version: number;
}

export type BalanceAction =
  | { type: "seed"; account: BalanceSnapshot }
  | { type: "notice"; notice: BalanceSnapshot }
  | { type: "reset" };

export const initialBalance: BalanceState = { available: 0, reserved: 0, version: -1, ready: false };

export function balanceReducer(state: BalanceState, action: BalanceAction): BalanceState {
  switch (action.type) {
    case "seed":
      // Igual version = mismo estado: se acepta (idempotente). Menor = dato viejo: se descarta.
      return action.account.version >= state.version ? { ...pick(action.account), ready: true } : state;
    case "notice":
      return action.notice.version > state.version ? { ...pick(action.notice), ready: true } : state;
    case "reset":
      return initialBalance;
  }
}

const pick = ({ available, reserved, version }: BalanceSnapshot) => ({ available, reserved, version });
