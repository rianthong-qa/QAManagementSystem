// UI รอบ 3: กล่องยืนยัน/กรอกข้อความ/แจ้งผลแบบกลางของทั้งแอป แทน window.confirm / window.prompt / window.alert
// (กล่องของ browser หน้าตาไม่ตรง design system, บล็อกทั้งหน้า, ไม่รองรับข้อความยาว/ภาษาไทยได้ดี และไม่แสดงใน artifact viewer)
// ใช้แบบ imperative ได้จากทุกที่: `if (!(await confirmDialog("ลบ?"))) return;` — แสดงผลโดย <DialogHost /> ที่ mount ครั้งเดียวใน App

export type DialogTone = "primary" | "danger";
export type ToastTone = "success" | "error" | "info";

export type ConfirmOptions = {
  title?: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: DialogTone;
};

export type PromptOptions = ConfirmOptions & {
  placeholder?: string;
  initialValue?: string;
  /** ต้องกรอกก่อนกดยืนยัน */
  required?: boolean;
  multiline?: boolean;
};

export type DialogRequest =
  | { id: number; kind: "confirm"; options: ConfirmOptions; resolve: (ok: boolean) => void }
  | { id: number; kind: "prompt"; options: PromptOptions; resolve: (value: string | null) => void };

export type Toast = { id: number; message: string; tone: ToastTone };

type State = { dialogs: DialogRequest[]; toasts: Toast[] };

let state: State = { dialogs: [], toasts: [] };
let nextId = 1;
const listeners = new Set<() => void>();

const emit = () => listeners.forEach((listener) => listener());
const setState = (patch: Partial<State>) => { state = { ...state, ...patch }; emit(); };

export const dialogStore = {
  subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
  getSnapshot: () => state,
  settle(id: number, value: boolean | string | null) {
    const request = state.dialogs.find((d) => d.id === id);
    if (!request) return;
    setState({ dialogs: state.dialogs.filter((d) => d.id !== id) });
    if (request.kind === "confirm") request.resolve(value === true);
    else request.resolve(typeof value === "string" ? value : null);
  },
  dismissToast(id: number) { setState({ toasts: state.toasts.filter((t) => t.id !== id) }); },
};

const normalize = <T extends ConfirmOptions>(input: string | T): T => (typeof input === "string" ? ({ message: input } as T) : input);

/** แทน window.confirm — resolve true เมื่อกดยืนยัน, false เมื่อยกเลิก/Escape */
export function confirmDialog(input: string | ConfirmOptions): Promise<boolean> {
  const options = normalize(input);
  return new Promise((resolve) => setState({ dialogs: [...state.dialogs, { id: nextId++, kind: "confirm", options, resolve }] }));
}

/** แทน window.prompt — resolve ข้อความที่กรอก หรือ null เมื่อยกเลิก */
export function promptDialog(input: string | PromptOptions): Promise<string | null> {
  const options = normalize(input);
  return new Promise((resolve) => setState({ dialogs: [...state.dialogs, { id: nextId++, kind: "prompt", options, resolve }] }));
}

/** แทน window.alert — แจ้งผลแบบ toast ไม่บล็อกหน้า (error ค้างนานกว่า) */
export function notify(message: string, tone: ToastTone = "info") {
  const id = nextId++;
  setState({ toasts: [...state.toasts, { id, message, tone }].slice(-4) });
  setTimeout(() => dialogStore.dismissToast(id), tone === "error" ? 9000 : 5000);
}
