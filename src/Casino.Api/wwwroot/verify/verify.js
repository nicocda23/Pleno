// Verificacion de jugadas "provably fair": segunda implementacion INDEPENDIENTE del algoritmo de docs/provably-fair.md,
// escrita en JavaScript para correr en el navegador del jugador (WebCrypto) y en Node. No depende de nada del servidor.

const encoder = new TextEncoder();

const toHex = (bytes) => Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");

/** SHA-256 en hexadecimal de un texto UTF-8. Es el "compromiso" que se publica antes de jugar. */
export async function sha256Hex(text) {
  const digest = await crypto.subtle.digest("SHA-256", encoder.encode(text));
  return toHex(new Uint8Array(digest));
}

/** Bloque `cursor` del flujo: HMAC-SHA256(clave = serverSeed, mensaje = "clientSeed:nonce:cursor"), 32 bytes. */
async function block(serverSeed, clientSeed, nonce, cursor) {
  const key = await crypto.subtle.importKey("raw", encoder.encode(serverSeed), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const message = encoder.encode(`${clientSeed}:${nonce}:${cursor}`);
  return new Uint8Array(await crypto.subtle.sign("HMAC", key, message));
}

/** Flujo determinista de enteros uniformes: los bytes se leen de a 4 (uint32 big-endian) con rejection sampling. */
export class FairStream {
  constructor(serverSeed, clientSeed, nonce) {
    this.serverSeed = serverSeed;
    this.clientSeed = clientSeed;
    this.nonce = nonce;
    this.cursor = 0;
    this.bytes = new Uint8Array(0);
    this.offset = 0;
  }

  async nextUInt32() {
    if (this.offset + 4 > this.bytes.length) {
      this.bytes = await block(this.serverSeed, this.clientSeed, this.nonce, this.cursor);
      this.cursor += 1;
      this.offset = 0;
    }
    const view = new DataView(this.bytes.buffer, this.bytes.byteOffset + this.offset, 4);
    this.offset += 4;
    return view.getUint32(0, false);
  }

  /** Entero uniforme en [0, max). Descarta los valores de la "cola" para no sesgar el resultado. */
  async nextInt(max) {
    if (!Number.isInteger(max) || max < 1 || max > 2 ** 31 - 1) {
      throw new RangeError("max debe ser un entero entre 1 y 2^31 - 1");
    }
    const limit = Math.floor(2 ** 32 / max) * max;
    for (;;) {
      const value = await this.nextUInt32();
      if (value < limit) {
        return value % max;
      }
    }
  }
}

/** Numero que sale en la ruleta europea (0 a 36) para una apuesta: el primer entero del flujo. */
export async function rouletteNumber(serverSeed, clientSeed, nonce) {
  return new FairStream(serverSeed, clientSeed, nonce).nextInt(37);
}

/**
 * Lee los datos de una jugada desde la URL (?commitment=&serverSeed=&clientSeed=&nonce=&claimed=). Es lo que arma el historial
 * del casino para abrir esta pagina ya completa. Devuelve null si falta algo o algo no es valido: nunca se rellena a medias.
 */
export function parsePrefill(search) {
  const params = new URLSearchParams(search);
  const text = (name) => (params.get(name) ?? "").trim();
  const whole = (name) => (/^\d+$/.test(text(name)) ? Number(text(name)) : null);

  const commitment = text("commitment").toLowerCase();
  const serverSeed = text("serverSeed");
  const clientSeed = params.get("clientSeed") ?? "";
  const nonce = whole("nonce");
  const claimed = whole("claimed");

  const valid =
    /^[0-9a-f]{64}$/.test(commitment) &&
    /^[0-9a-f]{64}$/.test(serverSeed) &&
    clientSeed.length >= 1 &&
    clientSeed.length <= 64 &&
    nonce !== null &&
    claimed !== null &&
    claimed <= 36;

  return valid ? { commitment, serverSeed, clientSeed, nonce, claimedNumber: claimed } : null;
}

/**
 * Verifica una jugada con datos publicos. Devuelve cada paso por separado para poder mostrarlo.
 * @param {{ commitment: string, serverSeed: string, clientSeed: string, nonce: number, claimedNumber: number }} round
 */
export async function verifyRound({ commitment, serverSeed, clientSeed, nonce, claimedNumber }) {
  const computedCommitment = await sha256Hex(serverSeed);
  const commitmentOk = computedCommitment === String(commitment).trim().toLowerCase();
  const recomputed = await rouletteNumber(serverSeed, clientSeed, nonce);
  return {
    commitmentOk,
    computedCommitment,
    recomputed,
    numberOk: recomputed === Number(claimedNumber),
    ok: commitmentOk && recomputed === Number(claimedNumber),
  };
}
