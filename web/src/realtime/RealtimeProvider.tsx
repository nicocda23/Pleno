import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { createContext, useCallback, useContext, useEffect, useMemo, useReducer, useRef, useState, type ReactNode } from "react";
import { queryKeys, useAccount } from "../api/hooks";
import type { BalanceNotice, RoundClosedNotice } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { config } from "../config";
import { balanceReducer, initialBalance, type BalanceState } from "./balance";

export type ConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";

/** Lo minimo que se usa de una conexion SignalR. Permite sustituirla en las pruebas. */
export interface HubConnectionLike {
  on(method: string, handler: (payload: never) => void): void;
  onreconnecting(handler: () => void): void;
  onreconnected(handler: () => void): void;
  onclose(handler: () => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
}

export type ConnectionFactory = (getToken: () => Promise<string | null>) => HubConnectionLike;

/** Conexion real: WebSocket a /hubs/player. El token viaja en la query (?access_token=) porque un navegador no puede poner cabeceras en un WebSocket. */
export const defaultConnectionFactory: ConnectionFactory = (getToken) =>
  new HubConnectionBuilder()
    .withUrl(`${config.apiUrl}/hubs/player`, {
      accessTokenFactory: async () => (await getToken()) ?? "",
      // Se autentica con token, no con cookies: asi la API no necesita habilitar credenciales en CORS.
      withCredentials: false,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build() as unknown as HubConnectionLike;

export interface RealtimeApi {
  balance: BalanceState;
  connection: ConnectionState;
  /** Se llama cada vez que una ronda termina. Devuelve la funcion para dejar de escuchar. */
  onRoundClosed: (listener: (notice: RoundClosedNotice) => void) => () => void;
}

const RealtimeContext = createContext<RealtimeApi | null>(null);

export function RealtimeProvider({ children, connectionFactory = defaultConnectionFactory }: { children: ReactNode; connectionFactory?: ConnectionFactory }) {
  const auth = useAuth();
  const queryClient = useQueryClient();
  const account = useAccount();
  const [balance, dispatch] = useReducer(balanceReducer, initialBalance);
  const [connection, setConnection] = useState<ConnectionState>("connecting");
  const listeners = useRef(new Set<(notice: RoundClosedNotice) => void>());
  const { status, getAccessToken } = auth;

  // Estado completo por HTTP: es la base. Los avisos en vivo se aplican encima, respetando la version.
  useEffect(() => {
    if (account.data) dispatch({ type: "seed", account: account.data });
  }, [account.data]);

  useEffect(() => {
    if (status !== "authenticated") {
      dispatch({ type: "reset" });
      return;
    }

    let disposed = false;
    const hub = connectionFactory(getAccessToken);
    const refresh = () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.account });
      void queryClient.invalidateQueries({ queryKey: queryKeys.roundsAll });
    };

    hub.on("balanceChanged", ((notice: BalanceNotice) => dispatch({ type: "notice", notice })) as (payload: never) => void);
    hub.on("roundClosed", ((notice: RoundClosedNotice) => {
      listeners.current.forEach((listener) => listener(notice));
      void queryClient.invalidateQueries({ queryKey: queryKeys.roundsAll });
    }) as (payload: never) => void);
    hub.onreconnecting(() => !disposed && setConnection("reconnecting"));
    hub.onreconnected(() => {
      if (disposed) return;
      setConnection("connected");
      // Los avisos solo cubren lo que pasa MIENTRAS hay conexion: al volver se pide el estado completo.
      refresh();
    });
    hub.onclose(() => !disposed && setConnection("disconnected"));

    hub
      .start()
      .then(() => {
        if (disposed) return;
        setConnection("connected");
        refresh();
      })
      .catch(() => !disposed && setConnection("disconnected"));

    return () => {
      disposed = true;
      void hub.stop();
    };
  }, [status, getAccessToken, connectionFactory, queryClient]);

  const onRoundClosed = useCallback((listener: (notice: RoundClosedNotice) => void) => {
    listeners.current.add(listener);
    return () => {
      listeners.current.delete(listener);
    };
  }, []);

  const value = useMemo<RealtimeApi>(() => ({ balance, connection, onRoundClosed }), [balance, connection, onRoundClosed]);
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

export function useRealtime(): RealtimeApi {
  const context = useContext(RealtimeContext);
  if (!context) throw new Error("useRealtime debe usarse dentro de <RealtimeProvider>.");
  return context;
}
