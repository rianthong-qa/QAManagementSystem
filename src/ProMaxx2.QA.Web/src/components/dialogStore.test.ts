import { afterEach, describe, expect, it, vi } from "vitest";
import { confirmDialog, dialogStore, notify, promptDialog } from "./dialogStore";

const top = () => dialogStore.getSnapshot().dialogs[0];

describe("dialogStore", () => {
  afterEach(() => {
    vi.useRealTimers();
    dialogStore.getSnapshot().dialogs.forEach((d) => dialogStore.settle(d.id, null));
    dialogStore.getSnapshot().toasts.forEach((t) => dialogStore.dismissToast(t.id));
  });

  it("confirmDialog resolves true when accepted and false when cancelled", async () => {
    const accepted = confirmDialog("ลบ?");
    dialogStore.settle(top().id, true);
    await expect(accepted).resolves.toBe(true);

    const cancelled = confirmDialog({ message: "ลบ?", tone: "danger" });
    dialogStore.settle(top().id, false);
    await expect(cancelled).resolves.toBe(false);
  });

  it("queues dialogs and shows them one at a time in order", async () => {
    const first = confirmDialog("first");
    const second = confirmDialog("second");
    expect(top().options.message).toBe("first");
    dialogStore.settle(top().id, true);
    expect(top().options.message).toBe("second");
    dialogStore.settle(top().id, false);
    await expect(first).resolves.toBe(true);
    await expect(second).resolves.toBe(false);
    expect(dialogStore.getSnapshot().dialogs).toHaveLength(0);
  });

  it("promptDialog resolves the typed text, or null when cancelled", async () => {
    const typed = promptDialog({ message: "เหตุผล", required: true });
    dialogStore.settle(top().id, "ข้อมูลไม่ครบ");
    await expect(typed).resolves.toBe("ข้อมูลไม่ครบ");

    const cancelled = promptDialog("เหตุผล");
    dialogStore.settle(top().id, null);
    await expect(cancelled).resolves.toBeNull();
  });

  it("notify keeps at most 4 toasts and dismisses them automatically", () => {
    vi.useFakeTimers();
    for (let i = 0; i < 6; i++) notify(`m${i}`, "success");
    expect(dialogStore.getSnapshot().toasts.map((t) => t.message)).toEqual(["m2", "m3", "m4", "m5"]);
    vi.advanceTimersByTime(5000);
    expect(dialogStore.getSnapshot().toasts).toHaveLength(0);
  });

  it("error toasts stay longer than success toasts", () => {
    vi.useFakeTimers();
    notify("saved", "success");
    notify("failed", "error");
    vi.advanceTimersByTime(5000);
    expect(dialogStore.getSnapshot().toasts.map((t) => t.message)).toEqual(["failed"]);
    vi.advanceTimersByTime(4000);
    expect(dialogStore.getSnapshot().toasts).toHaveLength(0);
  });
});
