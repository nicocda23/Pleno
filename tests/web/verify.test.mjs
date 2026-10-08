// Pruebas de la implementacion en JavaScript de la verificacion provably fair.
// Corren con: node --test tests/web
// Los vectores son los de docs/provably-fair.md; los generaron originalmente con otra implementacion (Node escrito aparte)
// y los comprueban tambien las pruebas de C#: tres implementaciones que tienen que coincidir.
import assert from "node:assert/strict";
import { test } from "node:test";
import { FairStream, rouletteNumber, sha256Hex, verifyRound } from "../../src/Casino.Api/wwwroot/verify/verify.js";

const SERVER_SEED = "9f2c4a7e1b3d58606a1f0e9d8c7b6a5f4e3d2c1b0a99887766554433221100ff";
const CLIENT_SEED = "pleno-lab";
const COMMITMENT = "52ca56a3d81d3be381d594a5bca342bb63f6a3597776fed89138c9244fcbd2c8";

const draw = async (nonce, max, count, clientSeed = CLIENT_SEED) => {
  const stream = new FairStream(SERVER_SEED, clientSeed, nonce);
  const values = [];
  for (let i = 0; i < count; i += 1) values.push(await stream.nextInt(max));
  return values;
};

test("el compromiso coincide con el vector de la especificacion", async () => {
  assert.equal(await sha256Hex(SERVER_SEED), COMMITMENT);
});

for (const [nonce, expected] of [
  [0, [26, 13, 21, 22, 9, 30, 11, 7, 5, 26, 27, 4]],
  [1, [32, 26, 19, 14, 34, 27, 23, 28, 23, 1, 27, 19]],
  [2, [23, 20, 24, 21, 5, 16, 14, 13, 7, 29, 29, 24]],
]) {
  test(`la ruleta con nonce ${nonce} coincide con el vector`, async () => {
    assert.deepEqual(await draw(nonce, 37, 12), expected);
  });
}

test("el flujo cruza varios bloques HMAC y sigue coincidiendo", async () => {
  assert.deepEqual(
    await draw(9, 37, 20),
    [13, 31, 31, 23, 14, 15, 18, 0, 25, 18, 3, 23, 24, 3, 23, 28, 1, 30, 5, 29],
  );
});

test("los rangos grandes coinciden (rejection sampling)", async () => {
  assert.deepEqual(await draw(7, 1_500_000_000, 4), [544190317, 69811089, 683837891, 298158533]);
});

test("rouletteNumber es el primer entero del flujo", async () => {
  assert.equal(await rouletteNumber(SERVER_SEED, CLIENT_SEED, 0), 26);
  assert.equal(await rouletteNumber(SERVER_SEED, CLIENT_SEED, 1), 32);
});

test("una jugada legitima se verifica", async () => {
  const check = await verifyRound({ commitment: COMMITMENT, serverSeed: SERVER_SEED, clientSeed: CLIENT_SEED, nonce: 0, claimedNumber: 26 });
  assert.equal(check.ok, true);
  assert.equal(check.commitmentOk, true);
  assert.equal(check.recomputed, 26);
});

test("el compromiso se compara sin importar mayusculas ni espacios", async () => {
  const check = await verifyRound({ commitment: `  ${COMMITMENT.toUpperCase()} `, serverSeed: SERVER_SEED, clientSeed: CLIENT_SEED, nonce: 0, claimedNumber: 26 });
  assert.equal(check.commitmentOk, true);
});

test("un numero adulterado no se verifica", async () => {
  const check = await verifyRound({ commitment: COMMITMENT, serverSeed: SERVER_SEED, clientSeed: CLIENT_SEED, nonce: 0, claimedNumber: 17 });
  assert.equal(check.commitmentOk, true);
  assert.equal(check.numberOk, false);
  assert.equal(check.ok, false);
});

test("una server seed distinta a la comprometida no se verifica", async () => {
  const check = await verifyRound({ commitment: COMMITMENT, serverSeed: `${SERVER_SEED.slice(0, -2)}00`, clientSeed: CLIENT_SEED, nonce: 0, claimedNumber: 26 });
  assert.equal(check.commitmentOk, false);
  assert.equal(check.ok, false);
});

test("otro nonce o client seed da otro resultado", async () => {
  assert.notEqual(await rouletteNumber(SERVER_SEED, CLIENT_SEED, 0), await rouletteNumber(SERVER_SEED, CLIENT_SEED, 1));
  assert.notDeepEqual(await draw(0, 1_000_000, 8), await draw(0, 1_000_000, 8, "otra-semilla"));
});

test("los valores siempre quedan dentro del rango", async () => {
  const stream = new FairStream(SERVER_SEED, CLIENT_SEED, 3);
  for (let i = 0; i < 500; i += 1) {
    const value = await stream.nextInt(37);
    assert.ok(value >= 0 && value < 37);
  }
});

test("la distribucion de la ruleta es uniforme (chi-cuadrado)", async () => {
  const counts = new Array(37).fill(0);
  const nonces = 1_500;
  for (let nonce = 0; nonce < nonces; nonce += 1) {
    const stream = new FairStream(SERVER_SEED, CLIENT_SEED, nonce);
    for (let i = 0; i < 37; i += 1) counts[await stream.nextInt(37)] += 1;
  }
  const expected = (nonces * 37) / 37;
  const chi = counts.reduce((sum, observed) => sum + (observed - expected) ** 2 / expected, 0);
  assert.ok(chi < 67.99, `chi-cuadrado = ${chi.toFixed(2)}`); // 36 grados de libertad, p = 0,001
});

test("un max invalido se rechaza", async () => {
  const stream = new FairStream(SERVER_SEED, CLIENT_SEED, 0);
  await assert.rejects(() => stream.nextInt(0), RangeError);
  await assert.rejects(() => stream.nextInt(2 ** 31), RangeError);
  await assert.rejects(() => stream.nextInt(1.5), RangeError);
});
