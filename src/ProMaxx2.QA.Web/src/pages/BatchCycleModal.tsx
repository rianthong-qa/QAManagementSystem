import { useEffect, useMemo, useState } from "react";
import { ApiError, apiFetch, isAbortError } from "../api";
import { Badge } from "../components/Badge";
import { DateField } from "../components/DateField";
import { ModalShell } from "../components/ModalShell";
import type { BuildItem, ReleaseItem } from "../shared/types";
import { type TestSuiteItem, useMasterOptions } from "../shared/appShared";

// สร้าง Test Cycle จากหลาย Test Suite พร้อมกัน (หน้า Test Suite) — Test Cycle ผูกได้ 1 Suite จึงสร้าง Suite ละ 1 Cycle
// โดยใช้ Release / Build / Environment / ประเภท / วันที่ ชุดเดียวกัน; Cycle Code ให้ API สร้างเอง (ส่งค่าว่าง)
// และยิงทีละรายการตามลำดับ เพื่อให้เลขรันไม่ชนกันและรายงานผลราย Suite ได้

type Environment = { testEnvironmentId: string; projectId: string; environmentName: string; isActive: boolean };
type CreatedCycle = { testCycleId: string; cycleCode: string; cycleName: string; caseCount: number };
type RowResult = { status: "pending" | "running" | "done" | "error"; cycle?: CreatedCycle; error?: string };

export function BatchCycleModal({ projectId, projectCode, suites: initialSuites, onClose, onCreated, onOpenCycles }: {
  projectId: string; projectCode?: string; suites: TestSuiteItem[];
  onClose: () => void; onCreated: (createdSuiteIds: string[]) => void; onOpenCycles?: () => void;
}) {
  // ตรึงรายการตอนเปิด — หน้า Test Suite จะตัด Suite ที่สร้างสำเร็จออกจากรายการที่เลือกระหว่างที่หน้าต่างยังเปิดอยู่
  const [suites] = useState(initialSuites);
  const cycleTypes = useMasterOptions()("TestCycleType");
  const [releases, setReleases] = useState<ReleaseItem[]>([]);
  const [builds, setBuilds] = useState<BuildItem[]>([]);
  const [environments, setEnvironments] = useState<Environment[]>([]);
  const [loadError, setLoadError] = useState("");
  const [releaseId, setReleaseId] = useState(""), [buildId, setBuildId] = useState(""), [environmentId, setEnvironmentId] = useState("");
  const [cycleType, setCycleType] = useState(""), [startDate, setStartDate] = useState(""), [endDate, setEndDate] = useState("");
  const [requiredOnly, setRequiredOnly] = useState(false);
  const [names, setNames] = useState<Record<string, string>>({});
  const [namesEdited, setNamesEdited] = useState<Record<string, boolean>>({});
  const [results, setResults] = useState<Record<string, RowResult>>({});
  const [running, setRunning] = useState(false);
  // API รายการ Suite ส่งแค่จำนวน (caseCount) ไม่มี cases — โหลดรายละเอียดเพื่อนับ Required และเตือน Suite ว่าก่อนสร้าง
  const [caseDetails, setCaseDetails] = useState<Record<string, { isRequired: boolean }[] | null>>({});

  useEffect(() => {
    const controller = new AbortController();
    setLoadError("");
    Promise.all([
      apiFetch<ReleaseItem[]>("/releases", { signal: controller.signal }),
      apiFetch<Environment[]>(`/test-environments?projectId=${projectId}`, { signal: controller.signal }),
    ]).then(([r, e]) => {
      // เงื่อนไขเดียวกับฟอร์มสร้าง Test Cycle: Release ที่ยังไม่ Released/Cancelled
      setReleases(r.filter((x) => x.projectId === projectId && x.status !== "Released" && x.status !== "Cancelled"));
      const active = e.filter((x) => x.projectId === projectId && x.isActive);
      setEnvironments(active);
      if (active.length === 1) setEnvironmentId(active[0].testEnvironmentId);
    }).catch((err) => { if (!isAbortError(err)) setLoadError("โหลด Release / Environment ไม่สำเร็จ — ปิดหน้าต่างแล้วลองใหม่"); });
    return () => controller.abort();
  }, [projectId]);

  useEffect(() => { if (!cycleType && cycleTypes[0]) setCycleType(cycleTypes[0].value); }, [cycleType, cycleTypes]);

  useEffect(() => {
    const controller = new AbortController();
    suites.forEach((s) => {
      if (s.cases) { setCaseDetails((d) => ({ ...d, [s.testSuiteId]: s.cases })); return; }
      apiFetch<{ cases?: { isRequired: boolean }[] }>(`/test-suites/${s.testSuiteId}`, { signal: controller.signal })
        .then((full) => setCaseDetails((d) => ({ ...d, [s.testSuiteId]: full.cases ?? [] })))
        .catch((err) => { if (!isAbortError(err)) setCaseDetails((d) => ({ ...d, [s.testSuiteId]: null })); });
    });
    return () => controller.abort();
  }, [suites]);

  useEffect(() => {
    setBuildId("");
    if (!releaseId) { setBuilds([]); return; }
    const controller = new AbortController();
    apiFetch<BuildItem[]>(`/releases/${releaseId}/builds`, { signal: controller.signal })
      .then((rows) => { const active = rows.filter((x) => x.isActive); setBuilds(active); if (active.length) setBuildId(active[active.length - 1].buildId); })
      .catch((err) => { if (!isAbortError(err)) { setBuilds([]); setLoadError("โหลด Build ของ Release นี้ไม่สำเร็จ"); } });
    return () => controller.abort();
  }, [releaseId]);

  // ชื่อ Cycle อัตโนมัติแบบเดียวกับฟอร์มสร้าง Test Cycle: Module (ถ้า Suite มี Module เดียว) - Suite Code - ประเภท Release Build
  // — ช่องที่ผู้ใช้แก้เองแล้วจะไม่ถูกเขียนทับ
  const release = releases.find((x) => x.releaseId === releaseId);
  const build = builds.find((x) => x.buildId === buildId);
  const typeLabel = cycleTypes.find((x) => x.value === cycleType)?.displayName ?? cycleType;
  const autoName = useMemo(() => (s: TestSuiteItem) => {
    const module = s.modules?.length === 1 ? s.modules[0].moduleName : undefined;
    return [module, s.suiteCode, [typeLabel, release?.releaseCode, build?.buildNumber].filter(Boolean).join(" ")].filter(Boolean).join("-");
  }, [typeLabel, release?.releaseCode, build?.buildNumber]);
  const nameOf = (s: TestSuiteItem) => (namesEdited[s.testSuiteId] ? names[s.testSuiteId] ?? "" : autoName(s));

  /** จำนวน Test Case ที่จะถูกดึงเข้า Cycle — null = ยังไม่รู้ (กำลังโหลด หรือโหลดรายละเอียดไม่สำเร็จขณะเลือกเฉพาะ Required) */
  const caseCount = (s: TestSuiteItem): number | null => {
    const cases = caseDetails[s.testSuiteId];
    if (cases) return requiredOnly ? cases.filter((c) => c.isRequired).length : cases.length;
    return requiredOnly ? null : s.caseCount ?? null;
  };
  const caseCountText = (s: TestSuiteItem) => {
    const n = caseCount(s);
    if (n !== null) return `${n.toLocaleString()} Test Case${requiredOnly ? " (Required)" : ""}`;
    return caseDetails[s.testSuiteId] === null ? "นับ Test Case ไม่ได้" : "กำลังนับ Test Case...";
  };
  const emptySuites = suites.filter((s) => caseCount(s) === 0);
  const datesInvalid = !!startDate && !!endDate && endDate < startDate;
  const pending = suites.filter((s) => results[s.testSuiteId]?.status !== "done");
  const finished = suites.length > 0 && pending.length === 0;
  const valid = !!releaseId && !!buildId && !!environmentId && !datesInvalid && pending.every((s) => nameOf(s).trim().length > 0);
  const doneCount = suites.filter((s) => results[s.testSuiteId]?.status === "done").length;
  const errorCount = suites.filter((s) => results[s.testSuiteId]?.status === "error").length;

  const create = async () => {
    if (!valid || running) return;
    setRunning(true);
    const createdIds: string[] = [];
    for (const s of pending) {
      setResults((r) => ({ ...r, [s.testSuiteId]: { status: "running" } }));
      const body = {
        projectId, releaseId, buildId, environmentId, testSuiteId: s.testSuiteId, cycleCode: "", cycleName: nameOf(s).trim(), cycleType: cycleType || null,
        startDate: startDate || null, endDate: endDate || null, ownerUserId: null, notes: null, populateFromSuite: true, requiredOnly,
      };
      try {
        let cycle: CreatedCycle;
        try { cycle = await apiFetch<CreatedCycle>("/test-cycles", { method: "POST", json: body }); }
        catch (err) {
          // ผู้ใช้อื่นสร้าง Cycle พร้อมกันจนเลขรันชน (409) — API คำนวณเลขใหม่ให้เมื่อส่งซ้ำ
          if (err instanceof ApiError && err.status === 409) cycle = await apiFetch<CreatedCycle>("/test-cycles", { method: "POST", json: body });
          else throw err;
        }
        createdIds.push(s.testSuiteId);
        setResults((r) => ({ ...r, [s.testSuiteId]: { status: "done", cycle } }));
      } catch (err) {
        setResults((r) => ({ ...r, [s.testSuiteId]: { status: "error", error: err instanceof Error ? err.message : "สร้างไม่สำเร็จ" } }));
      }
    }
    setRunning(false);
    if (createdIds.length) onCreated(createdIds);
  };

  const anyStarted = Object.keys(results).length > 0;
  return (
    <ModalShell labelledBy="batch-cycle-title" className="cycle-modal batch-cycle-modal" dirty={!anyStarted && (!!releaseId || Object.values(namesEdited).some(Boolean))} backdropDismiss={!running} onDismiss={() => { if (!running) onClose(); }}>
      <div className="modal-head">
        <div>
          <h2 id="batch-cycle-title">สร้าง Test Cycle จาก {suites.length} Test Suite</h2>
          <small>{projectCode ? `${projectCode} · ` : ""}สร้าง Suite ละ 1 Test Cycle ด้วย Release / Build / Environment เดียวกัน</small>
        </div>
        <button type="button" aria-label="ปิดหน้าต่าง" disabled={running} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      {loadError && <div className="inline-alert error" role="alert"><span>{loadError}</span></div>}
      <div className="batch-cycle-body">
        <fieldset className="batch-cycle-common" disabled={running || finished}>
          <legend>ค่าที่ใช้กับทุก Cycle</legend>
          <div className="form-grid">
            <label>Release <span className="required">*</span>
              <select value={releaseId} onChange={(e) => setReleaseId(e.target.value)}>
                <option value="">เลือก Release</option>
                {releases.map((x) => <option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · {x.version}</option>)}
              </select>
            </label>
            <label>Build <span className="required">*</span>
              <select value={buildId} onChange={(e) => setBuildId(e.target.value)} disabled={!releaseId}>
                <option value="">{releaseId && !builds.length ? "Release นี้ยังไม่มี Build" : "เลือก Build"}</option>
                {builds.map((x) => <option key={x.buildId} value={x.buildId}>{x.buildNumber}{x.applicationVersion ? ` · ${x.applicationVersion}` : ""}</option>)}
              </select>
            </label>
            <label>Environment <span className="required">*</span>
              <select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)}>
                <option value="">{environments.length ? "เลือก Environment" : "Project นี้ยังไม่มี Environment"}</option>
                {environments.map((x) => <option key={x.testEnvironmentId} value={x.testEnvironmentId}>{x.environmentName}</option>)}
              </select>
            </label>
            <label>Cycle Type
              <select value={cycleType} onChange={(e) => setCycleType(e.target.value)}>
                {cycleTypes.map((x) => <option key={x.value} value={x.value}>{x.displayName}</option>)}
              </select>
            </label>
            <label>Start Date<DateField value={startDate} max={endDate || undefined} allowEmpty onChange={setStartDate} /></label>
            <label>End Date<DateField value={endDate} min={startDate || undefined} allowEmpty invalid={datesInvalid} onChange={setEndDate} />{datesInvalid && <small className="field-error">วันสิ้นสุดต้องไม่ก่อนวันเริ่ม</small>}</label>
            <label className="full batch-cycle-check"><input type="checkbox" checked={requiredOnly} onChange={(e) => setRequiredOnly(e.target.checked)} /> ดึงเฉพาะ Test Case ที่เป็น Required</label>
          </div>
        </fieldset>

        <section className="batch-cycle-list" aria-labelledby="batch-cycle-list-title">
          <div className="batch-cycle-list-head">
            <h3 id="batch-cycle-list-title">Test Cycle ที่จะสร้าง ({suites.length})</h3>
            {anyStarted && <span role="status">สำเร็จ {doneCount} · ไม่สำเร็จ {errorCount}{running ? " · กำลังสร้าง..." : ""}</span>}
          </div>
          {emptySuites.length > 0 && !finished && (
            <div className="batch-cycle-warning" role="note"><span className="material-symbols-outlined" aria-hidden="true">warning</span><span>{emptySuites.map((s) => s.suiteCode).join(", ")} ไม่มี Test Case{requiredOnly ? " ที่เป็น Required" : ""} — Cycle จะว่างจนกว่าจะเพิ่ม Test Case</span></div>
          )}
          <ol>
            {suites.map((s) => {
              const r = results[s.testSuiteId];
              return (
                <li key={s.testSuiteId} className={r ? `is-${r.status}` : ""}>
                  <div className="batch-cycle-suite">
                    <b>{s.suiteCode}</b><span>{s.suiteName}</span>
                    <small>{caseCountText(s)}</small>
                  </div>
                  {r?.status === "done" && r.cycle ? (
                    <div className="batch-cycle-result"><Badge tone="green">สร้างแล้ว</Badge><span><b>{r.cycle.cycleCode}</b> {r.cycle.cycleName} · {r.cycle.caseCount} Cases</span></div>
                  ) : (
                    <div className="batch-cycle-name">
                      <input aria-label={`Cycle Name ของ ${s.suiteCode}`} value={nameOf(s)} maxLength={200} disabled={running} aria-invalid={!nameOf(s).trim()}
                        onChange={(e) => { const v = e.target.value; setNames((n) => ({ ...n, [s.testSuiteId]: v })); setNamesEdited((m) => ({ ...m, [s.testSuiteId]: true })); }}
                        placeholder="Cycle Name" />
                      {r?.status === "running" && <small className="batch-cycle-progress"><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง...</small>}
                      {r?.status === "error" && <small className="field-error" role="alert">{r.error}</small>}
                    </div>
                  )}
                </li>
              );
            })}
          </ol>
        </section>
      </div>
      <div className="modal-actions">
        {finished ? (
          <>
            {onOpenCycles && <button className="btn" type="button" onClick={onOpenCycles}><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span> ไปหน้า Test Cycle</button>}
            <button className="btn primary" type="button" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">check</span> เสร็จสิ้น</button>
          </>
        ) : (
          <>
            <button className="btn" type="button" disabled={running} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> {anyStarted ? "ปิด" : "ยกเลิก"}</button>
            <button className="btn primary" type="button" disabled={!valid || running} onClick={() => void create()}>
              {running ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง...</>
                : <><span className="material-symbols-outlined" aria-hidden="true">playlist_add</span> {errorCount ? `ลองใหม่ ${pending.length} รายการ` : `สร้าง ${pending.length} Test Cycle`}</>}
            </button>
          </>
        )}
      </div>
    </ModalShell>
  );
}
