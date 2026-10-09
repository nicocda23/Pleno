import { ApiError, createApiClient } from "./client";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function setup(responder: (url: string, init: RequestInit) => Response | Promise<Response>, token: string | null = "tok-123") {
  const calls: { url: string; init: RequestInit }[] = [];
  const onUnauthorized = vi.fn();
  const fetchImpl = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(input), init: init ?? {} });
    return responder(String(input), init ?? {});
  }) as unknown as typeof fetch;
  const client = createApiClient({ baseUrl: "http://api.test", getToken: async () => token, onUnauthorized, fetchImpl });
  return { client, calls, onUnauthorized };
}

describe("api client", () => {
  it("sends the bearer token and parses the JSON body", async () => {
    const { client, calls } = setup(() => jsonResponse({ available: 950 }));

    const account = await client.get<{ available: number }>("/wallet/me");

    expect(account.available).toBe(950);
    expect(calls[0]?.url).toBe("http://api.test/wallet/me");
    expect((calls[0]?.init.headers as Record<string, string>).Authorization).toBe("Bearer tok-123");
  });

  it("omits the Authorization header when there is no token", async () => {
    const { client, calls } = setup(() => jsonResponse({}), null);

    await client.get("/anything");

    expect((calls[0]?.init.headers as Record<string, string>).Authorization).toBeUndefined();
  });

  it("posts JSON and forwards custom headers such as the idempotency key", async () => {
    const { client, calls } = setup(() => jsonResponse({ betId: "b1" }, 202));

    await client.post("/games/roulette/bets", { stake: 10 }, { "Idempotency-Key": "key-1" });

    const headers = calls[0]?.init.headers as Record<string, string>;
    expect(calls[0]?.init.method).toBe("POST");
    expect(calls[0]?.init.body).toBe(JSON.stringify({ stake: 10 }));
    expect(headers["Content-Type"]).toBe("application/json");
    expect(headers["Idempotency-Key"]).toBe("key-1");
  });

  it("turns a problem-details error into an ApiError with the stable code", async () => {
    const { client } = setup(() => jsonResponse({ title: "InsufficientFunds", detail: "Saldo insuficiente" }, 422));

    const error = await client.post("/games/roulette/bets", {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(422);
    expect((error as ApiError).title).toBe("InsufficientFunds");
    expect((error as ApiError).message).toBe("Saldo insuficiente");
  });

  it("tolerates an error body that is not JSON", async () => {
    const { client } = setup(() => new Response("boom", { status: 500 }));

    const error = (await client.get("/x").catch((e: unknown) => e)) as ApiError;

    expect(error.status).toBe(500);
    expect(error.title).toBe("HTTP 500");
  });

  it("notifies on 401 so the app can send the player to log in", async () => {
    const { client, onUnauthorized } = setup(() => new Response(null, { status: 401 }));

    const error = (await client.get("/me").catch((e: unknown) => e)) as ApiError;

    expect(onUnauthorized).toHaveBeenCalledOnce();
    expect(error.status).toBe(401);
  });

  it("reports a network failure as a status-0 error", async () => {
    const onUnauthorized = vi.fn();
    const client = createApiClient({
      baseUrl: "http://api.test",
      getToken: async () => "t",
      onUnauthorized,
      fetchImpl: (async () => {
        throw new TypeError("Failed to fetch");
      }) as typeof fetch,
    });

    const error = (await client.get("/me").catch((e: unknown) => e)) as ApiError;

    expect(error.status).toBe(0);
    expect(error.title).toBe("NetworkError");
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it("returns undefined for a 204", async () => {
    const { client } = setup(() => new Response(null, { status: 204 }));

    expect(await client.post("/nothing")).toBeUndefined();
  });
});
