import { config } from "../config";
import type { RetiredPair, Round } from "../api/types";

/**
 * Enlace a la pagina publica de verificacion con los datos ya cargados. Solo se puede armar cuando la server seed del par de la
 * ronda ya fue revelada (despues de rotar): mientras esta en uso es secreta y ni siquiera el servidor la entrega.
 */
export function verifyUrl(round: Round, retired: RetiredPair[]): string | null {
  const pair = retired.find((p) => p.pairId === round.pairId);
  if (!pair || round.winningNumber === null) return null;

  const query = new URLSearchParams({
    commitment: pair.commitment,
    serverSeed: pair.serverSeed,
    clientSeed: pair.clientSeed,
    nonce: String(round.nonce),
    claimed: String(round.winningNumber),
  });
  return `${config.apiUrl}/verify/index.html?${query.toString()}`;
}
