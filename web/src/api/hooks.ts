import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useApi } from "./ApiProvider";
import { ApiError } from "./client";
import type { Account, AuditEntry, CreditResult, FairnessInfo, Me, PlaceBetBody, PlacedBet, Round, UserSummary } from "./types";

export const queryKeys = {
  me: ["me"] as const,
  account: ["account"] as const,
  rounds: (limit: number) => ["rounds", limit] as const,
  roundsAll: ["rounds"] as const,
  fairness: ["fairness"] as const,
  adminUsers: ["admin", "users"] as const,
  adminAudit: ["admin", "audit"] as const,
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
      ]),
  });
}

/** Registro de auditoria: quien cargo fichas, a quien y cuando (las mas recientes primero). */
export function useAdminAudit(enabled: boolean) {
  const api = useApi();
  return useQuery({ queryKey: queryKeys.adminAudit, queryFn: () => api.get<AuditEntry[]>("/backoffice/wallet/audit?limit=50"), enabled });
}
