import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useApi } from "./ApiProvider";
import { ApiError } from "./client";
import type { Account, FairnessInfo, Me, Paytable, PlaceBetBody, PlacedBet, Round, Spin } from "./types";

export const queryKeys = {
  spinsAll: ["spins"] as const,
  spins: (limit: number) => ["spins", limit] as const,
  paytable: ["paytable"] as const,
  me: ["me"] as const,
  account: ["account"] as const,
  rounds: (limit: number) => ["rounds", limit] as const,
  roundsAll: ["rounds"] as const,
  fairness: ["fairness"] as const,
};

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
