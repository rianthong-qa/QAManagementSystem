import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiFetch, apiUrl, escapeHtml, isApiRequest } from "./api";

afterEach(() => { vi.unstubAllGlobals(); });

describe("escapeHtml", () => {
  it("escapes markup characters and tolerates null/undefined", () => {
    expect(escapeHtml(`<b a="1">'x' & y</b>`)).toBe("&lt;b a=&quot;1&quot;&gt;&#39;x&#39; &amp; y&lt;/b&gt;");
    expect(escapeHtml(null)).toBe("");
    expect(escapeHtml(undefined)).toBe("");
  });
});

describe("isApiRequest", () => {
  it("only treats URLs under the QA Hub API base as API requests", () => {
    vi.stubGlobal("window", { location: { origin: "http://localhost:5173" } });
    expect(isApiRequest(`${apiUrl}/defects`)).toBe(true);
    expect(isApiRequest("https://bluesea.example.com/api/v1/defects")).toBe(false);
    expect(isApiRequest("/assets/logo.png")).toBe(false);
  });
});

describe("apiFetch", () => {
  it("throws ApiError carrying the ProblemDetails detail message", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ detail: "กรุณาเลือก Project" }), { status: 400 })));
    await expect(apiFetch("/dashboard/share", { method: "POST", json: {} })).rejects.toEqual(new ApiError(400, "กรุณาเลือก Project"));
  });

  it("returns undefined for 204 and parsed JSON otherwise", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 204 })));
    expect(await apiFetch("/x", { method: "DELETE" })).toBeUndefined();
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ ok: 1 }), { status: 200 })));
    expect(await apiFetch<{ ok: number }>("/x")).toEqual({ ok: 1 });
  });
});
