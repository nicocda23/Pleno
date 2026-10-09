import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useApi } from "./ApiProvider";
import { ApiError } from "./client";
import type { Account, BlackjackBet, BlackjackTable, BlackjackTableState, CrashBet, CrashCashOut, CrashState, GameInfo, SlotsSettings, SlotsSettingsBody, SlotsSettingsHistoryItem, SlotsSettingsPreview, CreditFilter, CreditHistory, MovementsPage, AuditEntry, CreditResult, FairnessInfo, Me, Paytable, PlaceBetBody, PlacedBet, Round, Spin, UserSummary } from "./types";

export const queryKeys = {
  spinsAll: ["spins"] as const,
  spins: (limit: number) => ["spins", limit] as const,
  paytable: ["paytable"] as const,
  me: ["me"] as const,
  account: ["account"] as const,
  rounds: (limit: number) => ["rounds", limit] as const,
  roundsAll: ["rounds"] as const,
  fairness: ["fairness"] as const,
  adminUsers: ["admin", "users"] as const,
  adminAudit: ["admin", "audit"] as const,
  adminCredits: (filter: CreditFilter) => ["admin", "credits", filter] as const,
  adminCreditsAll: ["admin", "credits"] as const,
  movements: ["movements"] as const,
  games: ["games"] as const,
  crashState: ["crash", "state"] as const,
  crashBets: ["crash", "bets"] as const,
  blackjackTables: ["blackjack", "tables"] as const,
  blackjackTable: (tableId: string) => ["blackjack", "table", tableId] as const,
  slotsSettings: ["admin", "slots-settings"] as const,
  slotsSettingsHistory: ["admin", "slots-settings-history"] as const,
  adminAccount: (userId: string) => ["admin", "account", userId] as const,
};

export const BACKOFFICE_ROLE = "backoffice";

/** Datos del usuario autenticado. Llamarlo tambien da de alta al jugador la primera vez. */
export function useMe() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.me, queryFn: () => api.get<Me>("/me"), staleTime: 60_000 });
}

/**
 * Cuenta de fichas. La cuenta de un jugador nuevo se abre de forma asincrona (por mensajes): durante un instante
 * puede responder 404. Se reintenta hasta 30 veces, una por segundo, antes de rendirse.
 */
export function useAccount() {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.account,
    queryFn: () => api.get<Account>("/wallet/me"),
    retry: (count, error) => error instanceof ApiError && error.status === 404 && count < 30,
    retryDelay: 1_000,
  });
}

export function useRounds(limit = 20) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.rounds(limit), queryFn: () => api.get<Round[]>(`/games/roulette/rounds?limit=${limit}`) });
}

export function useFairness() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.fairness, queryFn: () => api.get<FairnessInfo>("/fairness/me") });
}

/** Rota las seeds: revela la server seed activa (para poder verificar jugadas) y empieza un par nuevo. */
export function useRotateSeeds() {
  const api = useApi();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<FairnessInfo>("/fairness/me/rotate"),
    onSuccess: (info) => queryClient.setQueryData(queryKeys.fairness, info),
  });
}

export function usePlaceBet() {
  const api = useApi();
  return useMutation({
    mutationFn: ({ body, idempotencyKey }: { body: PlaceBetBody; idempotencyKey: string }) =>
      api.post<PlacedBet>("/games/roulette/bets", body, { "Idempotency-Key": idempotencyKey }),
  });
}

/** Una ronda concreta. Si `enabled`, se consulta cada 3 s hasta que cierre: es el respaldo por si se pierde el aviso en vivo. */
export function useRound(betId: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: ["round", betId],
    queryFn: () => api.get<Round>(`/games/roulette/rounds/${betId}`),
    enabled: betId !== null,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === "Settled" || status === "Rejected" || status === "Voided" ? false : 3_000;
    },
  });
}

/** Jugadores registrados. Solo responde a quien tenga el rol de administrador. */
export function useAdminUsers(enabled: boolean) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.adminUsers, queryFn: () => api.get<UserSummary[]>("/backoffice/users"), enabled });
}

/** Cuenta de cualquier jugador (solo administrador). */
export function useAdminAccount(userId: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.adminAccount(userId ?? ""),
    queryFn: () => api.get<Account>(`/backoffice/wallet/users/${userId}`),
    enabled: userId !== null,
  });
}

/** Carga fichas a un jugador. La clave de idempotencia la pone quien llama: reintentar el mismo envio no duplica la carga. */
export function useCreditChips() {
  const api = useApi();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, amount, idempotencyKey }: { userId: string; amount: number; idempotencyKey: string }) =>
      api.post<CreditResult>(`/backoffice/wallet/users/${userId}/credit`, { amount }, { "Idempotency-Key": idempotencyKey }),
    onSuccess: (_result, { userId }) =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.adminAccount(userId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.adminAudit }),
        queryClient.invalidateQueries({ queryKey: queryKeys.adminCreditsAll }),
      ]),
  });
}

/** Registro de auditoria: quien cargo fichas, a quien y cuando (las mas recientes primero). */
export function useAdminAudit(enabled: boolean) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.adminAudit, queryFn: () => api.get<AuditEntry[]>("/backoffice/wallet/audit?limit=50"), enabled });
}

/** Tabla de pagos y retorno de la tragamonedas (informacion publica). */
export function usePaytable() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.paytable, queryFn: () => api.get<Paytable>("/games/slots/paytable"), staleTime: 5 * 60_000 });
}

export function useSpins(limit = 10) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.spins(limit), queryFn: () => api.get<Spin[]>(`/games/slots/spins?limit=${limit}`) });
}

export function usePlaceSpin() {
  const api = useApi();
  return useMutation({
    mutationFn: ({ stake, idempotencyKey }: { stake: number; idempotencyKey: string }) =>
      api.post<PlacedBet>("/games/slots/spins", { stake }, { "Idempotency-Key": idempotencyKey }),
  });
}

/** Un giro concreto. Se consulta cada segundo hasta que cierre: asi se ven los rodillos apenas se sortean y es el respaldo si se pierde el aviso en vivo. */
export function useSpin(betId: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: ["spin", betId],
    queryFn: () => api.get<Spin>(`/games/slots/spins/${betId}`),
    enabled: betId !== null,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === "Settled" || status === "Rejected" || status === "Voided" ? false : 1_000;
    },
  });
}

/** Extracto de movimientos del jugador, de a paginas (cursor). Se pide "cargar mas" para ver lo anterior. */
export function useMovements(pageSize = 30) {
  const api = useApi();
  return useInfiniteQuery({
    queryKey: queryKeys.movements,
    initialPageParam: null as number | null,
    queryFn: ({ pageParam }) => api.get<MovementsPage>(`/wallet/me/movements?limit=${pageSize}${pageParam === null ? "" : `&before=${pageParam}`}`),
    getNextPageParam: (last) => last.nextBefore ?? undefined,
  });
}

/** Historial general de cargas (solo administrador): filtros por jugador y fechas, paginado, con el total del filtro. */
export function useAdminCredits(filter: CreditFilter, enabled: boolean, pageSize = 25) {
  const api = useApi();
  return useInfiniteQuery({
    queryKey: queryKeys.adminCredits(filter),
    initialPageParam: null as string | null,
    enabled,
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams({ limit: String(pageSize) });
      if (filter.userId) params.set("userId", filter.userId);
      if (filter.from) params.set("from", filter.from);
      if (filter.to) params.set("to", filter.to);
      if (pageParam) params.set("before", pageParam);
      return api.get<CreditHistory>(`/backoffice/wallet/credits?${params.toString()}`);
    },
    getNextPageParam: (last) => last.nextBefore ?? undefined,
  });
}

/** Ajustes vigentes de la tragamonedas (solo administrador). */
export function useSlotsSettings(enabled: boolean) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.slotsSettings, queryFn: () => api.get<SlotsSettings>("/backoffice/games/slots/settings"), enabled });
}

/** Prueba una tabla SIN guardarla: devuelve el retorno y la frecuencia de premio, o por que no sirve. */
export function useSlotsPreview(body: SlotsSettingsBody | null) {
  const api = useApi();
  return useQuery({
    queryKey: ["admin", "slots-preview", body],
    queryFn: () => api.post<SlotsSettingsPreview>("/backoffice/games/slots/settings/preview", body),
    enabled: body !== null,
    staleTime: Infinity,
    placeholderData: (previous) => previous,
  });
}

/** Publica una version nueva de la tabla. Falla con 409 si otra persona publico antes. */
export function usePublishSlotsSettings() {
  const api = useApi();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SlotsSettingsBody) => api.post<SlotsSettings>("/backoffice/games/slots/settings", body),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.slotsSettings }),
        queryClient.invalidateQueries({ queryKey: queryKeys.slotsSettingsHistory }),
        queryClient.invalidateQueries({ queryKey: queryKeys.paytable }),
      ]),
  });
}

export function useSlotsSettingsHistory(enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.slotsSettingsHistory,
    queryFn: () => api.get<SlotsSettingsHistoryItem[]>("/backoffice/games/slots/settings/history?limit=25"),
    enabled,
  });
}

/** El catalogo de juegos habilitados en el servidor. El lobby lista esto: agregar o quitar un juego no toca el front. */
export function useGames() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.games, queryFn: () => api.get<GameInfo[]>("/games"), staleTime: 60_000 });
}

/**
 * El estado de Crash: la ronda en curso, mi apuesta y las ultimas explosiones. Se consulta cada segundo (respaldo) y los hechos en vivo que
 * llegan por SignalR lo refrescan al instante. `offsetMs` es cuanto adelanta el reloj del servidor al del navegador, para dibujar el
 * multiplicador con la hora del servidor.
 */
export function useCrashState() {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.crashState,
    queryFn: async () => {
      const sentAt = Date.now();
      const state = await api.get<CrashState>("/games/crash/state");
      const receivedAt = Date.now();
      // Se supone que la respuesta tardo lo mismo en ir que en volver: la hora del servidor corresponde a la mitad del viaje.
      return { ...state, offsetMs: Date.parse(state.serverNow) - (sentAt + receivedAt) / 2 };
    },
    refetchInterval: 1_000,
  });
}

export function usePlaceCrashBet() {
  const api = useApi();
  return useMutation({
    mutationFn: ({ stake, autoCashOut, idempotencyKey }: { stake: number; autoCashOut: number | null; idempotencyKey: string }) =>
      api.post<{ betId: string; roundId: string }>("/games/crash/bets", { stake, autoCashOut }, { "Idempotency-Key": idempotencyKey }),
  });
}

export function useCrashCashOut() {
  const api = useApi();
  return useMutation({ mutationFn: (betId: string) => api.post<CrashCashOut>(`/games/crash/bets/${betId}/cashout`) });
}

export function useCrashBets(limit = 10) {
  const api = useApi();
  return useQuery({ queryKey: [...queryKeys.crashBets, limit], queryFn: () => api.get<CrashBet[]>(`/games/crash/bets?limit=${limit}`) });
}

/** Las mesas de Blackjack y como esta cada una. Se refresca cada 3 s mientras se mira la lista. */
export function useBlackjackTables() {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.blackjackTables, queryFn: () => api.get<BlackjackTable[]>("/games/blackjack/tables"), refetchInterval: 3_000 });
}

/**
 * El estado de una mesa de Blackjack: la mano en curso y los asientos. Como en Crash, se consulta cada segundo (respaldo) y los hechos en vivo
 * lo refrescan al instante. `offsetMs` es cuanto adelanta el reloj del servidor al del navegador, para las cuentas regresivas.
 */
export function useBlackjackTable(tableId: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: queryKeys.blackjackTable(tableId ?? ""),
    enabled: tableId !== null,
    queryFn: async () => {
      const sentAt = Date.now();
      const state = await api.get<BlackjackTableState>(`/games/blackjack/tables/${tableId}`);
      const receivedAt = Date.now();
      return { ...state, offsetMs: Date.parse(state.serverNow) - (sentAt + receivedAt) / 2 };
    },
    refetchInterval: 1_000,
  });
}

export function usePlaceBlackjackBet(tableId: string) {
  const api = useApi();
  return useMutation({
    mutationFn: ({ stake, idempotencyKey }: { stake: number; idempotencyKey: string }) =>
      api.post<{ betId: string; roundId: string; alreadyPlaced: boolean }>(`/games/blackjack/tables/${tableId}/bets`, { stake }, { "Idempotency-Key": idempotencyKey }),
  });
}

/** Pedir carta o plantarse con la apuesta propia. */
export function useBlackjackAction() {
  const api = useApi();
  return useMutation({ mutationFn: ({ betId, action }: { betId: string; action: "hit" | "stand" }) => api.post<BlackjackBet>(`/games/blackjack/bets/${betId}/${action}`) });
}
