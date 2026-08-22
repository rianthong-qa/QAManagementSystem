import { describe, it, expect, vi } from "vitest";
import { exportPlanAction, writeBackAction, type PlanRow, type WriteBackRun } from "./automationHelpers";

describe("exportPlanAction (integration)", () => {
  const rows: PlanRow[] = [
    { caseItem: { testCaseCode: "TC-01", title: "Login", priority: "High", status: "Ready", automationTarget: "pos", stepCount: 2 } },
    { caseItem: { testCaseCode: "TC-02", title: "No mod", priority: "Low", status: "Draft", stepCount: 0 } },
  ];

  it("creates a CSV download with the right filename and revokes the object URL", () => {
    const revokeObjectUrl = vi.fn();
    const clicks: string[] = [];
    const createAnchor = vi.fn(() => {
      const a: { href: string; download: string; click: () => void } = { href: "", download: "", click: () => clicks.push(a.download) };
      return a;
    });
    const captured: Blob[] = [];
    const co = vi.fn((b: Blob) => {
      captured.push(b);
      return "blob:fake";
    });
    exportPlanAction({ rows, projectId: "proj-9", onError: vi.fn(), createObjectUrl: co, revokeObjectUrl, createAnchor });

    expect(co).toHaveBeenCalledTimes(1);
    expect(revokeObjectUrl).toHaveBeenCalledWith("blob:fake");
    expect(clicks).toEqual(["automation-plan-proj-9.csv"]);
    expect(captured[0]).toBeInstanceOf(Blob);
  });

  it("calls onError with the Thai message when download setup throws", () => {
    const onError = vi.fn();
    exportPlanAction({
      rows,
      projectId: "p",
      onError,
      createObjectUrl: () => {
        throw new Error("boom");
      },
      revokeObjectUrl: vi.fn(),
      createAnchor: () => ({ href: "", download: "", click: () => {} }),
    });
    expect(onError).toHaveBeenCalledWith("Export Plan ไม่สำเร็จ");
  });
});

describe("writeBackAction (integration)", () => {
  const run: WriteBackRun = {
    targetApp: "pos",
    runnerName: "RunnerA",
    results: [
      { testCaseId: "c1", status: "Passed" },
      { testCaseId: "c2", status: "Failed", errorMessage: "boom" },
      { testCaseId: "c3", status: "Skipped", testExecutionId: "e3" },
      { status: "Blocked" },
    ],
  };

  it("POSTs one execution per unlinked result with mapped status, then onDone", async () => {
    const fetchImpl = vi.fn(async (url: string, init?: any) => {
      if (url.endsWith("/execution")) {
        return { ok: true, json: async () => ({ cases: [{ testCaseId: "c1", testCycleCaseId: "cc1" }, { testCaseId: "c2", testCycleCaseId: "cc2" }] }) };
      }
      if (init?.method === "POST") return { ok: true, json: async () => ({}) };
      return { ok: false, json: async () => null };
    });
    const onDone = vi.fn();
    const onError = vi.fn();
    await writeBackAction({ run, cycleId: "cyc1", apiUrl: "http://x", token: "tok", canRun: true, onError, onDone, fetchImpl: fetchImpl as any });

    const posts = fetchImpl.mock.calls.filter((c: any) => c[1]?.method === "POST");
    expect(posts.length).toBe(2);
    const bodies = posts.map((c: any) => JSON.parse(c[1].body));
    expect(bodies[0].status).toBe("Pass");
    expect(bodies[1].status).toBe("Fail");
    expect(bodies[1].actualResult).toBe("boom");
    expect(bodies[0].comment).toContain("RunnerA");
    expect(bodies[0].stepResults).toEqual([]);
    expect(onDone).toHaveBeenCalledWith(2);
    expect(onError).not.toHaveBeenCalled();
  });

  it("calls onError when the workspace fetch fails", async () => {
    const fetchImpl = vi.fn(async () => ({ ok: false, json: async () => null }));
    const onError = vi.fn();
    const onDone = vi.fn();
    await writeBackAction({ run, cycleId: "cyc1", apiUrl: "http://x", token: "tok", canRun: true, onError, onDone, fetchImpl: fetchImpl as any });

    expect(onError).toHaveBeenCalledWith("โหลด Test Cycle ไม่สำเร็จ");
    expect(onDone).not.toHaveBeenCalled();
  });

  it("does not call fetch when canRun is false or no cycle is chosen", async () => {
    const fetchImpl = vi.fn(async () => ({ ok: true, json: async () => ({ cases: [] }) }));
    const onDone = vi.fn();
    await writeBackAction({ run, cycleId: "", apiUrl: "http://x", token: "tok", canRun: true, onError: vi.fn(), onDone, fetchImpl: fetchImpl as any });
    await writeBackAction({ run, cycleId: "cyc1", apiUrl: "http://x", token: "tok", canRun: false, onError: vi.fn(), onDone, fetchImpl: fetchImpl as any });

    expect(fetchImpl).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
  });

  it("surfaces the backend detail message when a POST execution fails", async () => {
    const fetchImpl = vi.fn(async (url: string, init?: any) => {
      if (url.endsWith("/execution")) return { ok: true, json: async () => ({ cases: [{ testCaseId: "c1", testCycleCaseId: "cc1" }] }) };
      if (init?.method === "POST") return { ok: false, json: async () => ({ detail: "Cycle ปิดแล้ว" }) };
      return { ok: false, json: async () => null };
    });
    const onError = vi.fn();
    await writeBackAction({ run: { targetApp: "pos", results: [{ testCaseId: "c1", status: "Passed" }] }, cycleId: "cyc1", apiUrl: "http://x", token: "tok", canRun: true, onError, onDone: vi.fn(), fetchImpl: fetchImpl as any });

    expect(onError).toHaveBeenCalledWith("Cycle ปิดแล้ว");
  });
});
