import { useState, useMemo, useEffect, useRef } from "react";
import type { ProjectItem, UserLookup } from "../shared/types";
import { apiUrl, getJson, isAbortError } from "../api";
import { notify, confirmDialog } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { SortHeader, type SortState } from "../components/SortHeader";
import "../components/ListPage.css";
import { ListPager } from "../components/ListPager";
import { useUserPageSize } from "../components/usePageSize";
import { BatchCycleModal } from "./BatchCycleModal";
import { type ModuleItem, type SessionUser, type TestSuiteItem, fmtDateTimeBE, masterOptionElements, nextBusinessCode, priorityTone, renderModuleSelectOptions, useMasterOptions } from "../shared/appShared";

/** แถวจาก GET /test-cases/picker — ข้อมูลย่อพอสำหรับตัวเลือกในฟอร์มและ AI Generate */
type PickerCase = { testCaseId: string; projectId: string; moduleId: string; testCaseCode: string; title: string; priority: string; testType?: string; status: string };
type GeneratedTestSuiteDraft={suiteName:string;suiteType:string;description:string;riskTier:string;testCases:{testCaseId:string;isRequired:boolean;reason:string}[];selectionSummary:string};
// เรียงรายการ Test Suite ด้วยหัวคอลัมน์ — จำไว้ต่อเครื่อง (อ่าน/เขียนไม่ได้ใช้ค่าเริ่มต้น "สร้างล่าสุดก่อน" ตามลำดับเดิมของ API)
type SuiteSortKey = "code" | "module" | "risk" | "cases" | "cycles" | "status" | "createdAt";
const suiteSortStorageKey = "qa.testSuites.listSort";
const defaultSuiteSort: SortState<SuiteSortKey> = { key: "createdAt", dir: "desc" };
const suiteSortKeys: SuiteSortKey[] = ["code", "module", "risk", "cases", "cycles", "status", "createdAt"];
const readSuiteSort = (): SortState<SuiteSortKey> => {
  try {
    const saved = JSON.parse(localStorage.getItem(suiteSortStorageKey) ?? "null") as SortState<SuiteSortKey> | null;
    return saved && suiteSortKeys.includes(saved.key) && (saved.dir === "asc" || saved.dir === "desc") ? saved : defaultSuiteSort;
  } catch { return defaultSuiteSort; }
};
// ตัวเลือก "เรียงตาม" บนมือถือ (หัวตารางถูกซ่อนในโหมด card)
const suiteSortOptions: { value: string; label: string }[] = [
  { value: "createdAt:desc", label: "สร้างล่าสุดก่อน" }, { value: "createdAt:asc", label: "สร้างเก่าสุดก่อน" },
  { value: "code:asc", label: "รหัส Suite น้อย → มาก" }, { value: "code:desc", label: "รหัส Suite มาก → น้อย" },
  { value: "module:asc", label: "Module ก → ฮ" }, { value: "risk:asc", label: "Risk Tier สูงสุดก่อน (P0)" },
  { value: "cases:desc", label: "Test Case มากสุดก่อน" }, { value: "cycles:desc", label: "Test Cycle มากสุดก่อน" },
  { value: "cycles:asc", label: "Test Cycle น้อยสุดก่อน" }, { value: "status:asc", label: "ใช้งานก่อน" },
];
const suiteCaseCount = (x: TestSuiteItem) => x.cases?.length || x.caseCount || 0;
const codeCompare = (a: string, b: string) => a.localeCompare(b, undefined, { numeric: true, sensitivity: "base" });
const suiteSortValue: Record<SuiteSortKey, (a: TestSuiteItem, b: TestSuiteItem) => number> = {
  code: (a, b) => codeCompare(a.suiteCode, b.suiteCode),
  // Suite ที่ไม่มี Module ไปท้ายเสมอเมื่อเรียงน้อย → มาก
  module: (a, b) => Number(!a.modules?.length) - Number(!b.modules?.length) || (a.modules?.[0]?.moduleName ?? "").localeCompare(b.modules?.[0]?.moduleName ?? "", "th"),
  risk: (a, b) => codeCompare(a.riskTier ?? "~", b.riskTier ?? "~"),
  cases: (a, b) => suiteCaseCount(a) - suiteCaseCount(b),
  cycles: (a, b) => a.cycleCount - b.cycleCount,
  status: (a, b) => Number(b.isActive) - Number(a.isActive),
  createdAt: (a, b) => (a.createdAt ?? "").localeCompare(b.createdAt ?? ""),
};
export function TestSuitesPage({
  search,
  onClearSearch,
  canEdit,
  contextProjectId,
  onOpenCycle,
  onCreateCycle,
  canCreateCycle = false,
  onOpenCycleList,
}: {
  search: string;
  /** ล้างคำค้นในช่องค้นหาที่หัวหน้า (state อยู่ใน App) — ใช้กับปุ่ม "ล้างตัวกรอง" */
  onClearSearch?: () => void;
  canEdit: boolean;
  contextProjectId?: string;
  onOpenCycle?: (page: "test-cycles" | "execution", cycleId: string) => void;
  onCreateCycle?: (projectId: string, testSuiteId: string) => void;
  /** มีสิทธิ์สร้าง Test Cycle (EXECUTION.RUN) — เปิดการเลือกหลาย Suite เพื่อสร้าง Cycle พร้อมกัน */
  canCreateCycle?: boolean;
  onOpenCycleList?: () => void;
}) {
  const masterOptions = useMasterOptions(), suiteTypes = masterOptions("TestSuiteType"), riskTiers = masterOptions("TestSuiteRiskTier");
  const [items, setItems] = useState<TestSuiteItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [modules, setModules] = useState<ModuleItem[]>([]),
    [testCases, setTestCases] = useState<PickerCase[]>([]),
    [users, setUsers] = useState<UserLookup[]>([]),
    [reload, setReload] = useState(0),
    [form, setForm] = useState(false),
    [editing, setEditing] = useState<TestSuiteItem | null>(null),
    [managing, setManaging] = useState<TestSuiteItem | null>(null),
    [detail, setDetail] = useState<TestSuiteItem | null>(null),
    // ตัวค้นหา/กรอง Test Case ในหน้ารายละเอียด Suite (แสดงครบทุกเคส ไม่ตัดที่ 5 รายการแบบเดิม)
    [detailCaseSearch, setDetailCaseSearch] = useState(""),
    [detailCaseFilter, setDetailCaseFilter] = useState<"all" | "required" | "optional">("all"),
    // แก้ไข Suite ในหน้าต่างรายละเอียด — editBaseline = ค่าตอนเริ่มแก้ ใช้ตรวจว่ามีข้อมูล Suite ที่ยังไม่บันทึก
    [detailEditing, setDetailEditing] = useState(false),
    [editBaseline, setEditBaseline] = useState<{ name: string; type: string; risk: string; description: string; active: boolean } | null>(null),
    [checked, setChecked] = useState<string[]>([]),
    [saving, setSaving] = useState(false),
    [error, setError] = useState(""),
    // สถานะของรายการ Test Suite แยกจาก error ของฟอร์ม — เดิมใช้ตัวเดียวกัน error ตอนโหลดรายการจึงไปขึ้นแค่ใน modal แก้ไข
    // หน้ารายการเห็นเป็นตารางว่างเฉยๆ
    [listLoading, setListLoading] = useState(true),
    [listError, setListError] = useState(""),
    [projectFilter, setProjectFilter] = useState(contextProjectId ?? ""),
    [suiteModuleFilter, setSuiteModuleFilter] = useState(""),
    [typeFilter, setTypeFilter] = useState(""),
    [riskFilter, setRiskFilter] = useState(""),
    [createdByFilter, setCreatedByFilter] = useState(""),
    [activeFilter, setActiveFilter] = useState("active"),
    [noCycleOnly, setNoCycleOnly] = useState(false),
    [caseSearch, setCaseSearch] = useState(""),
    [casePriorityFilter, setCasePriorityFilter] = useState(""),
    [caseTypeFilter, setCaseTypeFilter] = useState(""),
    // เฉพาะตอนแก้ไข Suite ที่มีอยู่แล้ว — ตอนสร้างใหม่ใช้ formModuleId (ช่อง Module ที่บังคับเลือก) กรองอยู่แล้ว
    [caseModuleFilter, setCaseModuleFilter] = useState(""),
    [addRequired, setAddRequired] = useState(true),
    [suiteAiModal,setSuiteAiModal]=useState(false),[suiteAiGenerating,setSuiteAiGenerating]=useState(false),[suiteAiError,setSuiteAiError]=useState(""),
    [suiteAiProjectId,setSuiteAiProjectId]=useState(""),[suiteAiModuleId,setSuiteAiModuleId]=useState(""),[suiteAiModules,setSuiteAiModules]=useState<ModuleItem[]>([]),
    [suiteAiDrafts,setSuiteAiDrafts]=useState<GeneratedTestSuiteDraft[]>([]),[suiteAiExpanded,setSuiteAiExpanded]=useState<number|undefined>(undefined),
    [suiteAiCaseSearch,setSuiteAiCaseSearch]=useState(""),[suiteAiPriorityFilter,setSuiteAiPriorityFilter]=useState(""),[suiteAiTypeFilter,setSuiteAiTypeFilter]=useState("");
  const [suitePage, setSuitePage] = useState(1), [suitePageSize, setSuitePageSize] = useUserPageSize("qa.testSuites.pageSize");
  const [listSort, setListSortState] = useState<SortState<SuiteSortKey>>(readSuiteSort);
  const setListSort = (next: SortState<SuiteSortKey>) => { setListSortState(next); try { localStorage.setItem(suiteSortStorageKey, JSON.stringify(next)); } catch { /* ไม่จำก็ได้ */ } };
  // เลือกหลาย Suite เพื่อสร้าง Test Cycle พร้อมกัน — Release/Build/Environment เป็นของ Project จึงเลือกได้ทีละ Project
  const [selectedSuiteIds, setSelectedSuiteIds] = useState<string[]>([]), [batchCycleOpen, setBatchCycleOpen] = useState(false);
  const [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [formModuleId, setFormModuleId] = useState(""),
    [type, setType] = useState(""),
    [risk, setRisk] = useState(""),
    [description, setDescription] = useState(""),
    [projectId, setProjectId] = useState(""),
    [active, setActive] = useState(true);
  const headers = useMemo(() => ({
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  }), []);
  // ปุ่ม "ลบถาวร" (พ่วงลบ Test Cycle + ประวัติ Execution ทั้งหมด) จำกัดเฉพาะ SYS_ADMIN —
  // สิทธิ์อื่นกดปุ่มเดียวกันแล้วจะเป็นแค่ "ปิดใช้งาน" (ย้อนกลับได้ ไม่ลบข้อมูลจริง)
  const isSysAdmin = useMemo(() => {
    try { return (JSON.parse(localStorage.getItem("qa.user") ?? "{}") as SessionUser).roles?.includes("SYS_ADMIN") ?? false; }
    catch { return false; }
  }, []);
  useEffect(() => { if (contextProjectId) setProjectFilter(contextProjectId); }, [contextProjectId]);
  useEffect(() => {
    const requestHeaders = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    const fetchAllSuites = async () => {
      const collected: TestSuiteItem[] = [];
      let pageNo = 1;
      let total = Number.POSITIVE_INFINITY;
      while (collected.length < total) {
        // 500 = ขนาดหน้าสูงสุดของ API — Suite ทั้งหมดมักจบในคำขอเดียว (ทุกคำขอผ่าน Cloudflare เสีย ~0.2–1 วินาที)
        const response = await fetch(`${apiUrl}/test-suites?page=${pageNo}&size=500`, { headers: requestHeaders });
        if (!response.ok) throw new Error(`โหลด Test Suite ไม่สำเร็จ (${response.status})`);
        const data = await response.json();
        const pageRows = Array.isArray(data) ? data : data?.rows ?? [];
        if (!pageRows.length) break;
        collected.push(...pageRows);
        total = Number(data?.total ?? pageRows.length);
        pageNo += 1;
      }
      return collected;
    };
    setListLoading(true);
    setListError("");
    Promise.all([
      fetchAllSuites(),
      getJson<ProjectItem[]>(`${apiUrl}/projects`),
      getJson<unknown[]>(`${apiUrl}/lookups/users`).catch(() => []),
    ]).then(([s, p, u]) => {
      setItems(Array.isArray(s) ? s : (s as any)?.rows ?? []);
      const activeProjects = p.filter((x) => x.isActive);
      setProjects(activeProjects);
      setUsers(Array.isArray(u) ? u as typeof users : []);
      setProjectId((current) => current || activeProjects[0]?.projectId || "");
    }).catch((e) => setListError(`โหลดรายการ Test Suite ไม่สำเร็จ (${e instanceof Error ? e.message : "ไม่ทราบสาเหตุ"})`))
      .finally(() => setListLoading(false));
  }, [reload]);
  useEffect(() => {
    const target = managing?.projectId ?? (form && !editing ? projectId : null) ?? projectFilter ?? contextProjectId;
    if (!target) { setModules([]); return; }
    getJson<ModuleItem[]>(`${apiUrl}/projects/${target}/modules`).then((rows) => setModules(rows.filter(x => x.isActive))).catch(() => setError("โหลดรายการ Module ไม่สำเร็จ"));
  }, [managing?.projectId, form, editing, projectId, projectFilter, contextProjectId]);
  // Test Case ใช้แค่ในฟอร์มสร้าง/แก้ไข Suite และ AI Generate — เดิมโหลดทันทีที่เปิดหน้ารายการ และไล่ดึง /test-cases ทีละ 100
  // (Project 6,500 เคส = 66 คำขอเรียงกันทุกครั้งที่เปิดหน้า) ตอนนี้โหลดเฉพาะตอนเปิดฟอร์ม/AI ด้วย /test-cases/picker ครั้งเดียว
  // และจำไว้ราย Project (ล้างเมื่อ reload หลังบันทึก) — AI Generate ใช้ Project ที่เลือกใน modal (เดิมใช้ Project ของตัวกรองหน้ารายการ)
  const pickerCache = useRef(new Map<string, PickerCase[]>());
  useEffect(() => { pickerCache.current.clear(); }, [reload]);
  const pickerProjectId = form ? projectId : detailEditing && detail ? detail.projectId : suiteAiModal ? suiteAiProjectId : "";
  useEffect(() => {
    if (!pickerProjectId) return;
    const cached = pickerCache.current.get(pickerProjectId);
    if (cached) { setTestCases(cached); return; }
    setTestCases([]);
    const controller = new AbortController();
    getJson<PickerCase[]>(`${apiUrl}/test-cases/picker?projectId=${pickerProjectId}`, controller.signal)
      .then((rows) => { pickerCache.current.set(pickerProjectId, rows); if (!controller.signal.aborted) setTestCases(rows); })
      .catch((e) => { if (!controller.signal.aborted && !isAbortError(e)) { if (form) setError("โหลดรายการ Test Case ไม่สำเร็จ"); else setSuiteAiError("โหลดรายการ Test Case ไม่สำเร็จ"); } });
    return () => controller.abort();
    // form แยกไว้ใน deps ไม่ได้ — ต้องการให้โหลดใหม่เฉพาะตอน Project ที่ต้องใช้เปลี่ยน ไม่ใช่ทุกครั้งที่เปิด/ปิดฟอร์มของ Project เดิม
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pickerProjectId]);
  useEffect(()=>{
    if(!suiteAiProjectId){setSuiteAiModules([]);setSuiteAiModuleId("");return;}
    fetch(`${apiUrl}/projects/${suiteAiProjectId}/modules`,{headers}).then(async response=>response.ok?response.json():Promise.reject(new Error("โหลด Module ไม่สำเร็จ"))).then((rows:ModuleItem[])=>{const active=rows.filter(x=>x.isActive);setSuiteAiModules(active);setSuiteAiModuleId(current=>active.some(x=>x.moduleId===current)?current:(active[0]?.moduleId??""));}).catch(error=>setSuiteAiError(error instanceof Error?error.message:"โหลด Module ไม่สำเร็จ"));
  },[suiteAiProjectId,headers]);
  useEffect(() => {
    if (!form || editing || !projectId) return;
    const project = projects.find((x) => x.projectId === projectId);
    setCode(
      nextBusinessCode(
        `${project?.projectCode ?? "PRJ"}-TS`,
        items.map((x) => x.suiteCode),
      ),
    );
  }, [form, editing, projectId, projects, items]);
  useEffect(() => {
    // Switching the target project mid-creation invalidates any staged picks from the old project —
    // clear them so a stale test case ID can never ride along into the wrong suite's project.
    if (form && !editing) setChecked([]);
  }, [form, editing, projectId]);
  // ฟอร์มนี้ใช้สร้าง Suite ใหม่เท่านั้น — การแก้ไขทำในหน้าต่างรายละเอียด (startDetailEdit) ให้จบในหน้าเดียว
  const openForm = () => {
    setEditing(null);
    setManaging(null);
    setChecked([]);
    setCaseSearch("");
    setCasePriorityFilter("");
    setCaseTypeFilter("");
    setCaseModuleFilter("");
    setAddRequired(true);
    setError("");
    const project = projects.find((x) => x.projectId === (projects[0]?.projectId ?? ""));
    setCode(nextBusinessCode(`${project?.projectCode ?? "PRJ"}-TS`, items.map((x) => x.suiteCode)));
    setName("");
    setFormModuleId("");
    setType(suiteTypes[0]?.value ?? "");
    setRisk(riskTiers[0]?.value ?? "");
    setDescription("");
    setProjectId(contextProjectId ?? projects[0]?.projectId ?? "");
    setActive(true);
    setForm(true);
  };
  const openSuiteAi=()=>{const targetProject=contextProjectId||projectFilter||projects[0]?.projectId||"";setSuiteAiProjectId(targetProject);setSuiteAiModuleId("");setSuiteAiError("");setSuiteAiDrafts([]);setSuiteAiExpanded(undefined);setSuiteAiCaseSearch("");setSuiteAiPriorityFilter("");setSuiteAiTypeFilter("");setSuiteAiModal(true);};
  const generateSuiteWithAi=async()=>{if(!suiteAiProjectId||!suiteAiModuleId)return;setSuiteAiGenerating(true);setSuiteAiError("");try{const response=await fetch(`${apiUrl}/test-suites/generate-ai`,{method:"POST",headers,body:JSON.stringify({projectId:suiteAiProjectId,moduleId:suiteAiModuleId,suiteTypes:suiteTypes.map(x=>x.value),riskTiers:riskTiers.map(x=>x.value)})});if(!response.ok){const problem=await response.json().catch(()=>null);throw new Error(problem?.detail??"AI Generate Test Suite ไม่สำเร็จ");}const drafts:GeneratedTestSuiteDraft[]=await response.json();if(!Array.isArray(drafts)||!drafts.length)throw new Error("AI ไม่ได้สร้าง Test Suite กลับมา");setSuiteAiDrafts(drafts);setSuiteAiExpanded(0);}catch(error){if(error instanceof SyntaxError)setSuiteAiError("AI ส่งข้อมูลกลับมาในรูปแบบที่ไม่ถูกต้อง กรุณาลองใหม่");else setSuiteAiError(error instanceof Error?error.message:"AI Generate Test Suite ไม่สำเร็จ");}finally{setSuiteAiGenerating(false);}};
  const removeSuiteAiDraft=(index:number)=>setSuiteAiDrafts(drafts=>{const next=drafts.filter((_,i)=>i!==index);if(next.length===0){setSuiteAiModal(false);}return next;});
  const saveAllSuiteDrafts=async()=>{if(!suiteAiDrafts.length)return;setSuiteAiGenerating(true);setSuiteAiError("");try{let created=0;for(const draft of suiteAiDrafts){const body={projectId:suiteAiProjectId,suiteCode:"",suiteName:draft.suiteName,suiteType:draft.suiteType,riskTier:draft.riskTier,description:draft.description,isActive:true};const res=await fetch(`${apiUrl}/test-suites`,{method:"POST",headers,body:JSON.stringify(body)});if(!res.ok){const problem=await res.json().catch(()=>null);throw new Error(`สร้าง Suite "${draft.suiteName}" ไม่สำเร็จ: ${problem?.detail??""}`);}const saved:TestSuiteItem=await res.json();const required=draft.testCases.filter(x=>x.isRequired).map(x=>x.testCaseId),optional=draft.testCases.filter(x=>!x.isRequired).map(x=>x.testCaseId);for(const [ids,isRequired] of [[required,true],[optional,false]] as const){if(!ids.length)continue;const ar=await fetch(`${apiUrl}/test-suites/${saved.testSuiteId}/cases`,{method:"POST",headers,body:JSON.stringify({testCaseIds:ids,isRequired})});if(!ar.ok)throw new Error(`สร้าง "${draft.suiteName}" แล้ว แต่กำหนด Test Case ไม่สำเร็จ`);}created++;}setSuiteAiDrafts([]);setSuiteAiModal(false);setReload(x=>x+1);}catch(error){setSuiteAiError(error instanceof Error?error.message:"บันทึก Test Suite ไม่สำเร็จ");}finally{setSuiteAiGenerating(false);}};
  const save = async () => {
    setSaving(true);
    try {
      const response = await fetch(
        `${apiUrl}/test-suites${editing ? `/${editing.testSuiteId}` : ""}`,
        {
          method: editing ? "PUT" : "POST",
          headers,
          body: JSON.stringify({
            projectId,
            suiteCode: editing ? code : "",
            suiteName: name,
            suiteType: type,
            description: description || null,
            riskTier: risk,
            isActive: active,
          }),
        },
      );
      if (!response.ok) {
        const problem = await response.json();
        throw new Error(problem.detail ?? "บันทึกไม่สำเร็จ");
      }
      const saved: TestSuiteItem = await response.json();
      if (editing) {
        // แก้ไขจากหน้าต่างรายละเอียด: อยู่ในโหมดแก้ไขต่อ อัปเดตหัว/รายการทันที
        applyFresh(saved);
        setEditBaseline({ name: saved.suiteName, type: saved.suiteType ?? "", risk: saved.riskTier ?? "", description: saved.description ?? "", active: saved.isActive });
        setReload((x) => x + 1);
        notify("บันทึกข้อมูล Suite แล้ว", "success");
        return;
      }
      if (checked.length) {
        // Cases staged before the suite existed get added in one batch right after creation.
        const caseResponse = await fetch(`${apiUrl}/test-suites/${saved.testSuiteId}/cases`, {
          method: "POST",
          headers,
          body: JSON.stringify({ testCaseIds: checked, isRequired: addRequired }),
        });
        if (!caseResponse.ok) throw new Error("สร้าง Test Suite แล้ว แต่เพิ่ม Test Case ที่เลือกไว้ไม่สำเร็จ กรุณาเพิ่มอีกครั้งจากหน้าจัดการ");
      }
      setForm(false);
      setManaging(null);
      setChecked([]);
      setReload((x) => x + 1);
    } catch (e) {
      notify(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ", "error");
    } finally {
      setSaving(false);
    }
  };
  // ผลล่าสุดของ Suite (หลังเพิ่ม/นำออก/สลับ Required/บันทึกข้อมูล) — ซิงก์ทั้งหน้าต่างรายละเอียด ตัวจัดการ และแถวในรายการ
  // (GET /test-suites/{id} ไม่มี modules จึง merge กับค่าเดิม)
  const applyFresh = (fresh: TestSuiteItem) => {
    setManaging((m) => m && m.testSuiteId === fresh.testSuiteId ? { ...m, ...fresh } : m);
    setDetail((d) => d && d.testSuiteId === fresh.testSuiteId ? { ...d, ...fresh } : d);
    setItems((current) => current.map((x) => x.testSuiteId === fresh.testSuiteId ? { ...x, ...fresh } : x));
  };
  const addCases = async () => {
    if (!managing || !checked.length) return;
    setSaving(true);
    try {
      const response = await fetch(`${apiUrl}/test-suites/${managing.testSuiteId}/cases`, {
        method: "POST",
        headers,
        body: JSON.stringify({ testCaseIds: checked, isRequired: addRequired }),
      });
      if (!response.ok) throw new Error(await response.text() || "เพิ่ม Test Case ไม่สำเร็จ");
      const fresh = await fetch(`${apiUrl}/test-suites/${managing.testSuiteId}`, { headers }).then(r => r.json());
      setChecked([]);
      applyFresh(fresh);
      setReload((x) => x + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "เพิ่ม Test Case ไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const removeCase = async (suiteId: string, caseId: string) => {
    if (!await confirmDialog({ title: "นำ Test Case ออกจาก Suite", message: "นำ Test Case นี้ออกจาก Suite ใช่หรือไม่? (Test Case ไม่ถูกลบ)", confirmLabel: "นำออก", tone: "danger" })) return;
    const response = await fetch(`${apiUrl}/test-suites/${suiteId}/cases/${caseId}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) { setError("นำ Test Case ออกจาก Suite ไม่สำเร็จ"); return; }
    const fresh = await fetch(`${apiUrl}/test-suites/${suiteId}`, { headers }).then(r => r.json());
    applyFresh(fresh);
    setReload((x) => x + 1);
  };
  const updateCase = async (suite: TestSuiteItem, caseId: string, sortOrder: number, isRequired: boolean) => {
    setSaving(true); setError("");
    const response = await fetch(`${apiUrl}/test-suites/${suite.testSuiteId}/cases/${caseId}`, { method: "PUT", headers, body: JSON.stringify({ sortOrder, isRequired }) });
    if (!response.ok) { setError("ปรับ Test Case ไม่สำเร็จ"); setSaving(false); return; }
    const fresh = await fetch(`${apiUrl}/test-suites/${suite.testSuiteId}`, { headers }).then(r => r.json());
    applyFresh(fresh); setSaving(false);
  };
  const removeSuite = async (suite: TestSuiteItem) => {
    const confirmMessage = isSysAdmin
      ? `ยืนยันลบ ${suite.suiteCode} ถาวร? การลบนี้ไม่สามารถกู้คืนได้ Test Case ที่ผูกไว้จะถูกนำออก และถ้ามี Test Cycle ผูกอยู่ ${suite.cycleCount > 0 ? `(${suite.cycleCount} รายการ) ` : ""}จะถูกลบถาวรพร้อมผล Execution/ประวัติการทดสอบทั้งหมดของ Cycle นั้นไปด้วย`
      : `ยืนยันปิดใช้งาน ${suite.suiteCode}? Suite นี้จะไม่แสดงในรายการที่ใช้งานอยู่ (เปิดกลับมาใช้งานได้ภายหลังผ่านหน้าแก้ไข)`;
    if (!await confirmDialog(confirmMessage)) return;
    const response = await fetch(`${apiUrl}/test-suites/${suite.testSuiteId}${isSysAdmin ? "/hard" : ""}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      notify(problem?.detail || problem?.title || `${isSysAdmin ? "ลบ" : "ปิดใช้งาน"} Test Suite ไม่สำเร็จ (${response.status})`, "error");
      return;
    }
    setReload((x) => x + 1);
  };
  // โหลดไม่สำเร็จคืน null — เดิมคืน cases: [] ทำให้หน้า detail/รายงาน/editor แสดง 0 case และถ้าบันทึกใน editor อาจทำให้ case หาย
  const fetchFullSuite = async (item: TestSuiteItem): Promise<TestSuiteItem | null> => {
    try {
      return { ...item, ...(await getJson<Partial<TestSuiteItem>>(`${apiUrl}/test-suites/${item.testSuiteId}`)) };
    } catch (e) {
      notify(`โหลดรายละเอียด ${item.suiteCode} ไม่สำเร็จ (${e instanceof Error ? e.message : "ไม่ทราบสาเหตุ"})`, "error");
      return null;
    }
  };
  const openSuiteDetail = async (item: TestSuiteItem) => { const full = await fetchFullSuite(item); if (!full) return; setDetailCaseSearch(""); setDetailCaseFilter("all"); setDetail(full); };
  // แก้ไขจบในหน้าต่างรายละเอียด: ข้อมูล Suite บันทึกด้วยปุ่ม ส่วนการเพิ่ม/นำออก/สลับ Required ของ Test Case บันทึกทันที
  const detailInfoDirty = detailEditing && !!editBaseline && (name !== editBaseline.name || type !== editBaseline.type || risk !== editBaseline.risk || description !== editBaseline.description || active !== editBaseline.active);
  const startDetailEdit = (suite: TestSuiteItem) => {
    const baseline = { name: suite.suiteName, type: suite.suiteType ?? "", risk: suite.riskTier ?? "", description: suite.description ?? "", active: suite.isActive };
    setEditing(suite); setManaging(suite); setEditBaseline(baseline);
    setCode(suite.suiteCode); setProjectId(suite.projectId);
    setName(baseline.name); setType(baseline.type); setRisk(baseline.risk); setDescription(baseline.description); setActive(baseline.active);
    setChecked([]); setCaseSearch(""); setCasePriorityFilter(""); setCaseTypeFilter(""); setAddRequired(true); setError("");
    // เริ่มที่ Module ของ Suite (Module แรกของเคสในชุด) — Suite ที่ยังไม่มีเคสให้เลือกเอง
    setCaseModuleFilter(suite.modules?.[0]?.moduleId ?? "");
    setDetailEditing(true);
  };
  const leaveDetailEdit = () => { setDetailEditing(false); setEditing(null); setManaging(null); setEditBaseline(null); setChecked([]); setError(""); };
  const confirmDiscardEdit = () => !detailEditing || (!detailInfoDirty && !checked.length) ? Promise.resolve(true) : confirmDialog({
    title: "ทิ้งการแก้ไขที่ยังไม่บันทึก?",
    message: [detailInfoDirty && "ข้อมูล Suite ที่แก้ไว้ยังไม่ได้บันทึก", checked.length > 0 && `Test Case ที่ติ๊กไว้ ${checked.length} รายการยังไม่ได้เพิ่มเข้าชุด`].filter(Boolean).join(" และ ") + " — ต้องการทิ้งหรือไม่?",
    confirmLabel: "ทิ้งการแก้ไข", cancelLabel: "กลับไปแก้ไข", tone: "danger",
  });
  const finishDetailEdit = async () => { if (await confirmDiscardEdit()) leaveDetailEdit(); };
  const closeDetail = () => { leaveDetailEdit(); setDetail(null); };
  const requestCloseDetail = async () => { if (await confirmDiscardEdit()) closeDetail(); };
  const openSuiteEdit = async (item: TestSuiteItem) => { const full = await fetchFullSuite(item); if (!full) return; setDetailCaseSearch(""); setDetailCaseFilter("all"); setDetail(full); startDetailEdit(full); };
  const downloadSuiteReport = (suite: TestSuiteItem) => {
    const activeCycles = (suite.linkedCycles ?? []).filter(c => !c.isDeleted);
    const rows: (string | number)[][] = [
      ["Test Suite Report"],
      ["Suite Code", suite.suiteCode],
      ["Suite Name", suite.suiteName],
      ["Type", suite.suiteType ?? "-"],
      ["Risk Tier", suite.riskTier ?? "-"],
      ["Status", suite.isActive ? "ใช้งาน" : "ปิดใช้งาน"],
      ["Module", suite.modules?.length ? suite.modules.map(m => m.moduleName).join(", ") : "-"],
      ["สร้างโดย", suite.createdByName ?? "-"],
      ["สร้างเมื่อ", formatThaiDateTime(suite.createdAt, { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" })],
      [],
      ["Test Cases", suite.cases.length],
      ["#", "Test Case Code", "Title", "Priority", "Required"],
      ...suite.cases.map(c => [c.sortOrder, c.testCaseCode, c.title, c.priority, c.isRequired ? "Required" : "Optional"]),
      [],
      ["Test Cycles", activeCycles.length],
      ["Cycle Code", "Cycle Name", "Release", "Build", "Status", "Progress %"],
      ...activeCycles.map(c => [c.cycleCode, c.cycleName, [c.releaseCode, c.releaseVersion].filter(Boolean).join(" · ") || "-", c.buildNumber || "-", c.status, c.progressPercent]),
    ];
    const csv = "﻿" + rows.map(row => row.map(v => `"${String(v).replaceAll('"', '""')}"`).join(",")).join("\r\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const a = document.createElement("a"); a.href = url; a.download = `${suite.suiteCode}-report.csv`; a.click(); URL.revokeObjectURL(url);
  };
  // Split out the active/inactive condition so the status chips can each show "how many would match
  // with every other filter applied" — same approach as the Test Cycle page's status chips.
  const matchesOtherSuiteFilters = (x: TestSuiteItem) =>
    (!projectFilter || x.projectId === projectFilter) &&
    (!suiteModuleFilter || x.modules?.some((m) => m.moduleId === suiteModuleFilter)) &&
    (!typeFilter || x.suiteType === typeFilter) &&
    (!riskFilter || x.riskTier === riskFilter) &&
    (!createdByFilter || x.createdBy === createdByFilter) &&
    `${x.suiteCode} ${x.suiteName} ${x.suiteType ?? ""}`.toLowerCase().includes(search.trim().toLowerCase());
  // ไม่นับ Project (มาจาก context ของ topbar) และสถานะ ใช้งาน/ปิดใช้งาน (ค่าเริ่มต้นของหน้า) เป็นตัวกรองที่ต้องล้าง
  const hasExtraFilters = Boolean(search.trim() || suiteModuleFilter || typeFilter || riskFilter || createdByFilter || noCycleOnly);
  const extraFilterCount = [search.trim(), suiteModuleFilter, typeFilter, riskFilter, createdByFilter, noCycleOnly].filter(Boolean).length;
  const clearSuiteFilters = () => { onClearSearch?.(); setSuiteModuleFilter(""); setTypeFilter(""); setRiskFilter(""); setCreatedByFilter(""); setNoCycleOnly(false); };
  const baseRows = items.filter(matchesOtherSuiteFilters);
  const activeSuiteCount = baseRows.filter((x) => x.isActive).length;
  const inactiveSuiteCount = baseRows.length - activeSuiteCount;
  const statusRows = baseRows.filter(
    (x) => activeFilter === "all" || (activeFilter === "active" ? x.isActive : !x.isActive),
  );
  // "ยังไม่มี Test Cycle" is an orthogonal toggle (not another status partition), so its count is
  // taken from statusRows — how many would remain if this toggle were also applied to the current view.
  const noCycleCount = statusRows.filter((x) => x.cycleCount === 0).length;
  // เรียงก่อนแบ่งหน้า — ค่าเท่ากันเรียงต่อด้วยรหัส Suite ให้ลำดับคงที่
  const rows = statusRows.filter((x) => !noCycleOnly || x.cycleCount === 0)
    .sort((a, b) => (listSort.dir === "asc" ? 1 : -1) * suiteSortValue[listSort.key](a, b) || codeCompare(a.suiteCode, b.suiteCode));
  const suitePageCount = Math.max(1, Math.ceil(rows.length / suitePageSize));
  const pagedSuiteRows = rows.slice((suitePage - 1) * suitePageSize, suitePage * suitePageSize);
  useEffect(() => { setSuitePage(1); }, [projectFilter, suiteModuleFilter, typeFilter, riskFilter, createdByFilter, activeFilter, noCycleOnly, search, suitePageSize, listSort]);
  useEffect(() => { if (suitePage > suitePageCount) setSuitePage(suitePageCount); }, [suitePage, suitePageCount]);
  const selectableSuite = (x: TestSuiteItem) => canCreateCycle && x.isActive;
  const selectedSuites = selectedSuiteIds.map((id) => items.find((x) => x.testSuiteId === id)).filter((x): x is TestSuiteItem => Boolean(x && x.isActive));
  const selectedProjectId = selectedSuites[0]?.projectId;
  const selectedProject = projects.find((x) => x.projectId === selectedProjectId);
  const pageSelectable = pagedSuiteRows.filter((x) => selectableSuite(x) && (!selectedProjectId || x.projectId === selectedProjectId));
  const pageProjects = new Set(pagedSuiteRows.filter(selectableSuite).map((x) => x.projectId));
  // เลือกทั้งหน้าได้เมื่อแถวในหน้ามาจาก Project เดียว (หรือเลือก Project นั้นไว้แล้ว) — กันการผสม Suite ข้าม Project
  const canSelectPage = pageSelectable.length > 0 && (selectedProjectId ? true : pageProjects.size === 1);
  const pageAllSelected = pageSelectable.length > 0 && pageSelectable.every((x) => selectedSuiteIds.includes(x.testSuiteId));
  const toggleSuite = (x: TestSuiteItem) => {
    if (selectedSuiteIds.includes(x.testSuiteId)) { setSelectedSuiteIds((ids) => ids.filter((id) => id !== x.testSuiteId)); return; }
    if (selectedProjectId && x.projectId !== selectedProjectId) {
      notify(`เลือกได้เฉพาะ Suite ใน Project ${selectedProject?.projectCode ?? "เดียวกัน"} — Release/Build/Environment เป็นของแต่ละ Project`, "error");
      return;
    }
    setSelectedSuiteIds((ids) => [...ids, x.testSuiteId]);
  };
  const togglePage = () => {
    const ids = pageSelectable.map((x) => x.testSuiteId);
    setSelectedSuiteIds((current) => pageAllSelected ? current.filter((id) => !ids.includes(id)) : [...new Set([...current, ...ids])]);
  };
  // เปลี่ยน Project ที่กรอง หรือ Suite ที่เลือกถูกปิดใช้งาน/ลบ → ตัดออกจากรายการที่เลือก
  useEffect(() => { setSelectedSuiteIds((ids) => ids.filter((id) => items.some((x) => x.testSuiteId === id && x.isActive && (!projectFilter || x.projectId === projectFilter)))); }, [items, projectFilter]);
  // When editing, cases live against the already-saved suite (managing). When creating, there's no
  // suite yet — the project comes straight from the form, and picks are staged locally in `checked`
  // until the suite is actually created (then added in one batch right after).
  const caseProjectId = editing ? managing?.projectId : projectId;
  const existingCaseIds = new Set((managing?.cases ?? []).map((c) => c.testCaseId));
  const caseCodeSort = (a: { testCaseCode: string }, b: { testCaseCode: string }) => a.testCaseCode.localeCompare(b.testCaseCode, undefined, { numeric: true, sensitivity: "base" });
  // ยังไม่เลือก Module = ไม่แสดง Test Case เลย ทั้งตอนสร้าง (formModuleId) และแก้ไข (caseModuleFilter) — เดิมแสดงทั้ง Project หลายพันรายการ
  // และ "เลือกทั้งหมด" เลือกข้าม Module ได้
  const available = testCases.filter(
    (x) =>
      !!caseProjectId &&
      x.projectId === caseProjectId &&
      x.status === "Ready" && // only fully reviewed cases belong in a suite — Draft/Review/Deprecated aren't addable
      !existingCaseIds.has(x.testCaseId) &&
      (editing || !checked.includes(x.testCaseId)), // while creating, a staged pick moves out of "available" into the staged panel
  ).filter(x => (!caseSearch || `${x.testCaseCode} ${x.title}`.toLowerCase().includes(caseSearch.toLowerCase())) && (editing ? (!!caseModuleFilter && x.moduleId === caseModuleFilter) : (!!formModuleId && x.moduleId === formModuleId)) && (!casePriorityFilter || x.priority === casePriorityFilter) && (!caseTypeFilter || x.testType === caseTypeFilter)).sort(caseCodeSort);
  const projectCases = testCases.filter((x) => x.projectId === caseProjectId);
  const moduleCodeOf = (moduleId?: string) => modules.find((m) => m.moduleId === moduleId)?.moduleCode;
  const caseMeta = (moduleId?: string, testType?: string) => [moduleCodeOf(moduleId), testType].filter(Boolean).join(" · ");
  // แผง "Test Case ที่เพิ่มได้" ใช้ร่วมกันระหว่างฟอร์มสร้าง (editing = null → กรองด้วย Module ทางซ้าย, เพิ่มตอนกดสร้าง)
  // และโหมดแก้ไขในหน้าต่างรายละเอียด (editing = Suite → เลือก Module ในแผง, กด "เพิ่มเข้าชุด" แล้วบันทึกทันที)
  const renderAvailablePanel = () => (
    <section className="suite-panel">
      <div className="suite-panel-head">
        <h3><span className="material-symbols-outlined" aria-hidden="true">playlist_add</span> Test Case ที่เพิ่มได้</h3>
        <span className="suite-panel-count">{available.length}</span>
        <small className="tse-ready-note">เฉพาะสถานะ Ready</small>
      </div>
      <div className="tse-filters">
        <label className="suite-case-search">
          <span className="material-symbols-outlined" aria-hidden="true">search</span>
          <input value={caseSearch} onChange={(e) => setCaseSearch(e.target.value)} aria-label="ค้นหา Test Case ที่เพิ่มได้" placeholder="ค้นหา Code / ชื่อ" />
        </label>
        {editing && (
          <select className="testcase-module-filter" aria-label="Module ของ Test Case ที่จะเพิ่ม" value={caseModuleFilter} onChange={(e) => setCaseModuleFilter(e.target.value)}>
            <option value="">เลือก Module</option>
            {renderModuleSelectOptions(modules)}
          </select>
        )}
        <select aria-label="กรองตาม Priority" value={casePriorityFilter} onChange={(e) => setCasePriorityFilter(e.target.value)}><option value="">ทุก Priority</option>{[...new Set(projectCases.map((x) => x.priority))].sort().map((x) => <option key={x}>{x}</option>)}</select>
        <select aria-label="กรองตาม Type" value={caseTypeFilter} onChange={(e) => setCaseTypeFilter(e.target.value)}><option value="">ทุก Type</option>{[...new Set(projectCases.map((x) => x.testType).filter(Boolean))].sort().map((x) => <option key={x} value={x}>{x}</option>)}</select>
      </div>
      <div className="tse-select-bar">
        <button type="button" className="tse-select-btn" disabled={!available.length} onClick={() => setChecked((c) => [...new Set([...c, ...available.map((x) => x.testCaseId)])])}><span className="material-symbols-outlined" aria-hidden="true">done_all</span> เลือกทั้งหมด ({available.length})</button>
        <button type="button" className="tse-select-btn" disabled={!checked.length} onClick={() => setChecked([])}><span className="material-symbols-outlined" aria-hidden="true">close</span> ล้างที่เลือก</button>
      </div>
      <div className="suite-panel-body">
        {available.length ? available.map((x) => (
          <label className={`suite-case selectable tse-case${checked.includes(x.testCaseId) ? " is-checked" : ""}`} key={x.testCaseId}>
            <input type="checkbox" checked={checked.includes(x.testCaseId)} onChange={(e) => setChecked((c) => e.target.checked ? [...c, x.testCaseId] : c.filter((id) => id !== x.testCaseId))} />
            <span className="suite-case-info"><b>{x.testCaseCode}</b><small>{x.title}</small>{caseMeta(x.moduleId, x.testType) && <small className="tse-case-meta">{caseMeta(x.moduleId, x.testType)}</small>}</span>
            <Badge tone={priorityTone(x.priority)}>{x.priority}</Badge>
          </label>
        )) : (
          <div className="suite-panel-empty"><span className="material-symbols-outlined" aria-hidden="true">search_off</span><p>{!caseProjectId ? "เลือก Project ก่อนเพื่อดู Test Case ที่เพิ่มได้" : !editing && !formModuleId ? "เลือก Module ทางซ้ายก่อน" : editing && !caseModuleFilter ? "เลือก Module ในช่องด้านบนก่อน" : "ไม่พบ Test Case ที่ตรงกับตัวกรอง"}</p></div>
        )}
      </div>
      <div className="tse-add-bar">
        <label className="suite-required-choice"><input type="checkbox" checked={addRequired} onChange={(e) => setAddRequired(e.target.checked)} /> เพิ่มเป็น Required</label>
        {editing ? (
          <button className="btn primary" onClick={addCases} disabled={saving || !checked.length}>
            {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">add</span> เพิ่ม {checked.length} รายการเข้าชุด</>}
          </button>
        ) : <small className="tse-hint">{checked.length ? `เลือกแล้ว ${checked.length} รายการ — เพิ่มให้ตอนกด "สร้าง Test Suite"` : "ติ๊กเลือก Test Case ที่ต้องการ"}</small>}
      </div>
    </section>
  );
  const stagedCases = checked.map((id) => testCases.find((x) => x.testCaseId === id)).filter((x): x is PickerCase => Boolean(x));
  // ตัวเลขที่ส่งให้ AI วิเคราะห์จริง (ทั้ง Module ไม่ผ่าน filter ตัวอย่างด้านล่าง) เทียบกับรายการที่กรองแล้วซึ่งไว้ preview ก่อนกด Generate
  const suiteAiModuleCases = testCases.filter(x => x.moduleId === suiteAiModuleId && x.status !== "Deprecated");
  const suiteAiCandidates = suiteAiModuleCases.filter(x => (!suiteAiCaseSearch || `${x.testCaseCode} ${x.title}`.toLowerCase().includes(suiteAiCaseSearch.toLowerCase())) && (!suiteAiPriorityFilter || x.priority === suiteAiPriorityFilter) && (!suiteAiTypeFilter || x.testType === suiteAiTypeFilter));
  return (
    <>
      <article className="card lp-card">
        {listError && <div className="inline-alert error" role="alert"><span>{listError}</span><button onClick={() => setReload((value) => value + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
        <div className="lp-head">
          <div className="lp-title">
            <div><strong>{rows.length.toLocaleString()}</strong><span>Test Suite{extraFilterCount ? ` · กรอง ${extraFilterCount} เงื่อนไข` : ""}</span></div>
            {/* แทนแถบ "ขอบเขตการกรอง" เดิม — Suite ผูกกับ Project ส่วน Release/Build เลือกตอนสร้าง Test Cycle */}
            <small>Suite ผูกกับ Project · เลือก Release/Build ตอนนำไปสร้าง Test Cycle</small>
          </div>
          {canEdit && <div className="lp-actions">
            <button className="btn ai-button" type="button" onClick={openSuiteAi}><span className="material-symbols-outlined" aria-hidden="true">auto_awesome</span>AI Generate</button>
            <button className="btn primary" type="button" onClick={() => openForm()}><span className="material-symbols-outlined" aria-hidden="true">add</span>Test Suite</button>
          </div>}
        </div>
        <label className="lp-sort-mobile">เรียงตาม<select value={`${listSort.key}:${listSort.dir}`} onChange={(e) => { const [key, dir] = e.target.value.split(":"); setListSort({ key: key as SuiteSortKey, dir: dir as "asc" | "desc" }); }}>
          {!suiteSortOptions.some((o) => o.value === `${listSort.key}:${listSort.dir}`) && <option value={`${listSort.key}:${listSort.dir}`}>ตามหัวคอลัมน์ที่เลือกไว้</option>}
          {suiteSortOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select></label>
        <div className="lp-chips" role="group" aria-label="กรองตามสถานะ">
          <button type="button" aria-pressed={activeFilter === "all"} onClick={() => setActiveFilter("all")}>ทั้งหมด <b>{(activeSuiteCount + inactiveSuiteCount).toLocaleString()}</b></button>
          <button type="button" aria-pressed={activeFilter === "active"} onClick={() => setActiveFilter("active")}><span className="lp-dot green" aria-hidden="true" />ใช้งาน <b>{activeSuiteCount.toLocaleString()}</b></button>
          <button type="button" aria-pressed={activeFilter === "inactive"} onClick={() => setActiveFilter("inactive")}><span className="lp-dot gray" aria-hidden="true" />ปิดใช้งาน <b>{inactiveSuiteCount.toLocaleString()}</b></button>
          <span className="lp-chip-divider" aria-hidden="true" />
          <button type="button" aria-pressed={noCycleOnly} title="แสดงเฉพาะ Suite ที่ยังไม่ถูกนำไปสร้าง Test Cycle" onClick={() => setNoCycleOnly((v) => !v)}><span className="lp-dot yellow" aria-hidden="true" />ยังไม่มี Test Cycle <b>{noCycleCount.toLocaleString()}</b></button>
        </div>
        <div className="lp-filters" role="group" aria-label="ตัวกรอง Test Suite">
          <select aria-label="Project" value={projectFilter} onChange={e => { setProjectFilter(e.target.value); setSuiteModuleFilter(""); }}><option value="">ทุก Project</option>{projects.map(x => <option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select>
          <select aria-label="Module" className="testcase-module-filter" value={suiteModuleFilter} onChange={e => setSuiteModuleFilter(e.target.value)} disabled={!modules.length}><option value="">ทุก Module</option>{renderModuleSelectOptions(modules)}</select>
          <select aria-label="Type" value={typeFilter} onChange={e => setTypeFilter(e.target.value)}><option value="">ทุก Type</option>{suiteTypes.map(x => <option key={x.value} value={x.value}>{x.displayName}</option>)}</select>
          <select aria-label="Risk Tier" value={riskFilter} onChange={e => setRiskFilter(e.target.value)}><option value="">ทุก Risk Tier</option>{riskTiers.map(x => <option key={x.value} value={x.value}>{x.displayName}</option>)}</select>
          <select aria-label="ผู้สร้าง" value={createdByFilter} onChange={e => setCreatedByFilter(e.target.value)}><option value="">ผู้สร้างทั้งหมด</option>{users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select>
          {/* คำค้นมาจากช่องค้นหาที่หัวหน้า (App) — ปุ่มนี้ล้างตัวกรองทั้งหมดรวมคำค้นนั้น */}
          {hasExtraFilters && <button type="button" className="btn lp-clear" onClick={clearSuiteFilters}><span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span>ล้างตัวกรอง</button>}
        </div>
        {canCreateCycle && selectedSuites.length > 0 && (
          <div className="lp-bulk" role="region" aria-label="Suite ที่เลือก">
            <span className="lp-bulk-count"><b>{selectedSuites.length.toLocaleString()}</b> เลือกแล้ว{selectedProject ? ` · ${selectedProject.projectCode}` : ""}</span>
            <small className="lp-bulk-hint">{selectedSuites.reduce((sum, x) => sum + suiteCaseCount(x), 0).toLocaleString()} Test Case · สร้าง Suite ละ 1 Test Cycle</small>
            <span className="lp-bulk-end">
              <button className="btn primary" type="button" onClick={() => setBatchCycleOpen(true)}><span className="material-symbols-outlined" aria-hidden="true">playlist_add</span>สร้าง Test Cycle ({selectedSuites.length})</button>
              <button type="button" className="lp-bulk-clear" onClick={() => setSelectedSuiteIds([])} aria-label="ยกเลิกการเลือกทั้งหมด" title="ยกเลิกการเลือก"><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </span>
          </div>
        )}
        <ListPager position="top" page={suitePage} pageCount={suitePageCount} totalCount={rows.length} pageSize={suitePageSize} onPage={setSuitePage} onPageSize={setSuitePageSize} />
        <div className="lp-table-wrap">
          <table className={`lp-table${canCreateCycle ? " is-selectable" : ""}`}>
            <thead>
              <tr>
                {canCreateCycle && (
                  <th className="lp-col-select">
                    <input type="checkbox" checked={pageAllSelected} disabled={!canSelectPage} onChange={togglePage}
                      aria-label={pageAllSelected ? "ยกเลิกเลือก Suite ทั้งหน้า" : "เลือก Suite ที่ใช้งานอยู่ทั้งหน้า"}
                      title={canSelectPage ? undefined : "หน้านี้มี Suite หลาย Project — กรอง Project ก่อนจึงเลือกทั้งหน้าได้"} />
                  </th>
                )}
                <SortHeader label="Test Suite" sortKey="code" sort={listSort} onSort={setListSort} className="lp-col-item" />
                <SortHeader label="Module" sortKey="module" sort={listSort} onSort={setListSort} className="lp-col-module" />
                <SortHeader label="Risk Tier" sortKey="risk" sort={listSort} onSort={setListSort} className="lp-col-badge" />
                <SortHeader label="Test Case" sortKey="cases" sort={listSort} onSort={setListSort} className="lp-col-num lp-hide-md" defaultDir="desc" />
                <SortHeader label="Test Cycle" sortKey="cycles" sort={listSort} onSort={setListSort} className="lp-col-num" defaultDir="desc" />
                <SortHeader label="สถานะ" sortKey="status" sort={listSort} onSort={setListSort} className="lp-col-status" />
                <SortHeader label="สร้างเมื่อ" sortKey="createdAt" sort={listSort} onSort={setListSort} className="lp-col-date" defaultDir="desc" />
                {canEdit && <th className="lp-col-actions lp-actions-4"><span className="sr-only">จัดการ</span></th>}
              </tr>
            </thead>
            <tbody>
              {listLoading && <tr className="lp-empty-row"><td colSpan={(canCreateCycle ? 1 : 0) + (canEdit ? 8 : 7)}><div className="lp-empty" role="status"><span className="spinner" aria-hidden="true" /><b>กำลังโหลด Test Suite...</b></div></td></tr>}
              {!listLoading && !listError && rows.length === 0 && (
                <tr className="lp-empty-row"><td colSpan={(canCreateCycle ? 1 : 0) + (canEdit ? 8 : 7)}>
                  {items.length === 0
                    ? <div className="lp-empty"><span className="material-symbols-outlined" aria-hidden="true">inventory_2</span><b>ยังไม่มี Test Suite</b><small>{canEdit ? "กด \"Test Suite\" หรือ \"AI Generate\" เพื่อเริ่มจัดชุด Test Case" : "ยังไม่มีการสร้าง Test Suite ในระบบ"}</small></div>
                    : <div className="lp-empty"><span className="material-symbols-outlined" aria-hidden="true">search_off</span><b>ไม่พบ Test Suite ตามตัวกรองที่เลือก</b><small>ลองเปลี่ยนคำค้น สถานะ หรือตัวกรอง</small>{hasExtraFilters && <button type="button" className="btn" onClick={clearSuiteFilters}><span className="material-symbols-outlined" aria-hidden="true">filter_alt_off</span> ล้างตัวกรอง</button>}</div>}
                </td></tr>
              )}
              {!listLoading && pagedSuiteRows.map((x) => {
                const firstModule = x.modules?.[0];
                const requiredCount = x.cases?.length ? x.cases.filter((c) => c.isRequired).length : null;
                return (
                <tr key={x.testSuiteId} className={[selectedSuiteIds.includes(x.testSuiteId) && "is-selected", !x.isActive && "is-inactive"].filter(Boolean).join(" ") || undefined}>
                  {canCreateCycle && (
                    <td className="lp-col-select">
                      {selectableSuite(x) && (
                        <input type="checkbox" checked={selectedSuiteIds.includes(x.testSuiteId)} onChange={() => toggleSuite(x)}
                          disabled={!!selectedProjectId && x.projectId !== selectedProjectId}
                          aria-label={`เลือก ${x.suiteCode} เพื่อสร้าง Test Cycle`} />
                      )}
                    </td>
                  )}
                  <td className="lp-col-item">
                    <button type="button" className="lp-item" onClick={() => openSuiteDetail(x)} aria-label={`เปิด ${x.suiteCode} ${x.suiteName}`}>
                      <span className="lp-code">{x.suiteCode}<span className="lp-meta">{x.suiteType ?? "-"} · {suiteCaseCount(x).toLocaleString()} Test Case</span></span>
                      <span className="lp-name">{x.suiteName}</span>
                    </button>
                  </td>
                  <td className="lp-col-module">{firstModule ? <span className="lp-module" title={x.modules!.map((m) => `${m.moduleCode} · ${m.moduleName}`).join(", ")}><b>{firstModule.moduleName}{x.modules!.length > 1 ? ` +${x.modules!.length - 1}` : ""}</b><small>{firstModule.moduleCode}</small></span> : <span className="lp-muted">-</span>}</td>
                  <td className="lp-col-badge">{x.riskTier ? <Badge tone={x.riskTier === "P0" ? "red" : "yellow"}>{x.riskTier}</Badge> : <span className="lp-muted">-</span>}</td>
                  <td className="lp-col-num lp-hide-md lp-labeled" data-label="Test Case"><span className="lp-num"><b>{suiteCaseCount(x).toLocaleString()}</b>{requiredCount !== null && <small>Required {requiredCount}</small>}</span></td>
                  <td className="lp-col-num lp-labeled" data-label="Test Cycle"><span className="lp-num">{x.cycleCount === 0 ? <span className="is-warning"><span className="material-symbols-outlined" aria-hidden="true">warning</span>ยังไม่มี</span> : <b>{x.cycleCount.toLocaleString()}</b>}</span></td>
                  <td className="lp-col-status"><Badge tone={x.isActive ? "green" : "gray"}>{x.isActive ? "ใช้งาน" : "ปิดใช้งาน"}</Badge></td>
                  <td className="lp-col-date" title={fmtDateTimeBE(x.createdAt)}><span className="lp-date">{fmtDateTimeBE(x.createdAt).split(" ")[0]}{x.createdByName && <small>{x.createdByName}</small>}</span></td>
                  {canEdit && (
                    <td className="lp-col-actions lp-actions-4">
                      <div className="lp-row-actions">
                        <button type="button" title="รายละเอียด" aria-label={`ดูรายละเอียด ${x.suiteCode}`} onClick={() => openSuiteDetail(x)}><span className="material-symbols-outlined" aria-hidden="true">info</span></button>
                        <button type="button" title="แก้ไข / จัด Test Case" aria-label={`แก้ไขหรือจัดการ Test Case ของ ${x.suiteCode}`} onClick={() => void openSuiteEdit(x)}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button>
                        {onCreateCycle && x.isActive && <button type="button" title="สร้าง Test Cycle จาก Suite นี้" aria-label={`สร้าง Test Cycle จาก ${x.suiteCode}`} onClick={() => onCreateCycle(x.projectId, x.testSuiteId)}><span className="material-symbols-outlined" aria-hidden="true">playlist_add</span></button>}
                        <button type="button" className="danger" title={isSysAdmin ? "ลบถาวร" : "ปิดใช้งาน"} aria-label={`${isSysAdmin ? "ลบถาวร" : "ปิดใช้งาน"} ${x.suiteCode}`} onClick={() => removeSuite(x)}><span className="material-symbols-outlined" aria-hidden="true">{isSysAdmin ? "delete" : "block"}</span></button>
                      </div>
                    </td>
                  )}
                </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <ListPager position="bottom" page={suitePage} pageCount={suitePageCount} totalCount={rows.length} pageSize={suitePageSize} onPage={setSuitePage} onPageSize={setSuitePageSize} />
      </article>
      {batchCycleOpen && selectedProjectId && (
        <BatchCycleModal
          projectId={selectedProjectId}
          projectCode={selectedProject?.projectCode}
          suites={selectedSuites}
          onClose={() => setBatchCycleOpen(false)}
          onCreated={(ids) => { setSelectedSuiteIds((current) => current.filter((id) => !ids.includes(id))); setReload((x) => x + 1); }}
          onOpenCycles={onOpenCycleList ? () => { setBatchCycleOpen(false); onOpenCycleList(); } : undefined}
        />
      )}
      {suiteAiModal&&<ModalShell labelledBy="suite-ai-title" dirty={suiteAiDrafts.length > 0} className="requirement-ai-modal suite-ai-modal" boxStyle={{position:"relative"}} onDismiss={() => { if (!suiteAiGenerating) setSuiteAiModal(false); }}>{suiteAiGenerating&&<div className="ai-loading-overlay"><div className="ai-spinner"/>{suiteAiDrafts.length?<p>กำลังบันทึก Test Suite...</p>:<p>AI กำลังวิเคราะห์ Test Suite...</p>}<small>{suiteAiDrafts.length?"กรุณารอสักครู่ อย่าปิดหน้าต่างนี้":"รอสักครู่ ระบบกำลังประมวลผล Requirement และ Test Case"}</small></div>}<div className="modal-head"><div><h2 id="suite-ai-title">AI Generate Test Suite</h2><small>{suiteAiDrafts.length?`พบ ${suiteAiDrafts.length} Suite ที่ AI สร้าง — ตรวจสอบและบันทึก`:"วิเคราะห์ Requirement และ Test Case จาก Module ที่เลือก"}</small></div><button disabled={suiteAiGenerating} aria-label="ปิดหน้าต่าง AI Generate" onClick={()=>setSuiteAiModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>{suiteAiDrafts.length===0?(<section className="requirement-ai-panel"><div className="requirement-ai-head"><div><span className="ai-spark">AI</span><p><strong>ผู้ช่วยจัดกลุ่ม Test Case</strong><small>AI จะสร้าง Test Suite หลายชุดจาก Module ที่เลือก</small></p></div><span className="ai-review-badge">ตรวจสอบก่อนบันทึก</span></div>{suiteAiError&&<div className="inline-alert error"><span>{suiteAiError}</span></div>}{(!suiteTypes.length||!riskTiers.length)&&<div className="inline-alert error"><span>กรุณาเพิ่ม Test Suite Type และ Risk Tier ในการตั้งค่ากลางก่อนใช้งาน AI</span></div>}<div className="form-grid"><label>Project<select value={suiteAiProjectId} disabled={suiteAiGenerating} onChange={event=>{setSuiteAiProjectId(event.target.value);setSuiteAiModuleId("");setSuiteAiError("")}}><option value="">เลือก Project</option>{projects.map(project=><option key={project.projectId} value={project.projectId}>{project.projectCode} · {project.projectName}</option>)}</select></label><label>Module<select className="testcase-module-filter" value={suiteAiModuleId} disabled={suiteAiGenerating||!suiteAiProjectId} onChange={event=>setSuiteAiModuleId(event.target.value)}><option value="">เลือก Module</option>{renderModuleSelectOptions(suiteAiModules)}</select></label></div><div className="ai-draft-note"><span className="material-symbols-outlined" aria-hidden="true">info</span><p><strong>ใช้ข้อมูลที่มีอยู่ในระบบ</strong><small>ระบบส่งเฉพาะ Requirement และ Test Case ของ Module ที่เลือกให้ AI วิเคราะห์ ผลลัพธ์ยังไม่ถูกบันทึกจนกว่าจะตรวจ Draft และกดบันทึก</small></p></div>{suiteAiModuleId&&<><div className="suite-case-toolbar"><div className="suite-case-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input value={suiteAiCaseSearch} onChange={e=>setSuiteAiCaseSearch(e.target.value)} placeholder="ค้นหา Test Case..." /></div><select value={suiteAiPriorityFilter} onChange={e=>setSuiteAiPriorityFilter(e.target.value)}><option value="">ทุก Priority</option>{[...new Set(suiteAiModuleCases.map(x=>x.priority))].map(x=><option key={x}>{x}</option>)}</select><select value={suiteAiTypeFilter} onChange={e=>setSuiteAiTypeFilter(e.target.value)}><option value="">ทุก Type</option>{[...new Set(suiteAiModuleCases.map(x=>x.testType).filter(Boolean))].map(x=><option key={x} value={x}>{x}</option>)}</select></div><section className="suite-panel suite-ai-candidate-panel"><div className="suite-panel-head"><h3><span className="material-symbols-outlined" aria-hidden="true">description</span> Test Case ที่ AI จะวิเคราะห์</h3><span className="suite-panel-count">{suiteAiCandidates.length}</span></div><div className="suite-panel-body">{suiteAiCandidates.length?suiteAiCandidates.map(x=><div className="suite-case" key={x.testCaseId}><span className="suite-case-info"><b>{x.testCaseCode}</b><small>{x.title}</small></span><Badge tone={x.priority==="P0"||x.priority==="P1"?"red":"blue"}>{x.priority}</Badge></div>):<div className="suite-panel-empty"><span className="material-symbols-outlined" aria-hidden="true">search_off</span><p>ไม่พบ Test Case ที่ตรงกับตัวกรอง</p></div>}</div></section><div className="requirement-ai-actions"><small>{suiteAiModuleCases.length} Test Cases พร้อมวิเคราะห์{suiteAiCandidates.length!==suiteAiModuleCases.length?` (แสดง ${suiteAiCandidates.length} รายการตามตัวกรอง)`:""}</small><div className="row-actions"><button className="btn" disabled={suiteAiGenerating} onClick={()=>setSuiteAiModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={suiteAiGenerating||!suiteAiProjectId||!suiteAiModuleId||!suiteTypes.length||!riskTiers.length} onClick={generateSuiteWithAi}>{suiteAiGenerating?"AI กำลังวิเคราะห์...":"✦ สร้าง Test Suite"}</button></div></div></>}</section>):(<section className="requirement-ai-panel suite-ai-review"><div className="suite-ai-review-head"><div><h3>Suites ที่ AI สร้าง ({suiteAiDrafts.length})</h3><p>{suiteAiDrafts.reduce((sum,d)=>sum+d.testCases.length,0)} Test Cases ถูกจัดกลุ่มเป็น {suiteAiDrafts.length} Suite</p></div></div>{suiteAiError&&<div className="inline-alert error" style={{marginBottom:8}}><span>{suiteAiError}</span></div>}<div className="suite-ai-draft-list">{suiteAiDrafts.map((draft,index)=>{const isExpanded=suiteAiExpanded===index;return<div key={index} className={`suite-ai-draft-card${isExpanded?" expanded":""}`}><div className="suite-ai-draft-head" onClick={()=>setSuiteAiExpanded(isExpanded?undefined:index)}><div className="suite-ai-draft-title"><b>{draft.suiteName}</b><div className="suite-ai-draft-tags"><Badge tone="blue">{draft.suiteType}</Badge><Badge tone="yellow">{draft.riskTier}</Badge><span className="suite-ai-case-count">{draft.testCases.length} Cases</span></div></div><span className="suite-ai-expand-icon">{isExpanded?"▾":"▸"}</span></div>{isExpanded&&<div className="suite-ai-draft-body"><p className="suite-ai-draft-desc">{draft.description}</p><p className="suite-ai-draft-summary"><strong>สรุป:</strong> {draft.selectionSummary}</p><div className="suite-ai-case-list">{draft.testCases.map((tc,ci)=>{const testCase=testCases.find(x=>x.testCaseId===tc.testCaseId);return<div key={tc.testCaseId}><b>{ci+1}</b><span><strong>{testCase?.testCaseCode??tc.testCaseId}</strong><small>{testCase?.title??"ไม่พบรายละเอียด"}</small><small>{tc.reason}</small></span><Badge tone={tc.isRequired?"blue":"yellow"}>{tc.isRequired?"Required":"Optional"}</Badge></div>})}</div><button className="table-action danger-action" style={{marginTop:8}} onClick={()=>removeSuiteAiDraft(index)}>นำ Suite นี้ออก</button></div>}</div>})}</div><div className="requirement-ai-actions"><small>{suiteAiDrafts.length} Suite พร้อมบันทึก</small><div className="row-actions"><button className="btn" disabled={suiteAiGenerating} onClick={()=>setSuiteAiDrafts([])}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> สร้างใหม่</button><button className="btn primary" disabled={suiteAiGenerating||!suiteAiDrafts.length} onClick={saveAllSuiteDrafts}>{suiteAiGenerating?"กำลังบันทึก...":`✦ บันทึกทั้งหมด (${suiteAiDrafts.length} Suite)`}</button></div></div></section>)}</ModalShell>}
      {form && (() => {
        const formProjectName = projects.find((x) => x.projectId === projectId)?.projectName ?? "-";
        const missing = [!name.trim() && "Suite Name", !formModuleId && "Module"].filter(Boolean) as string[];
        const canSave = !saving && !!projectId && !!code.trim() && missing.length === 0;
        return (
        <ModalShell labelledBy="testsuite-form-title" className="testsuite-editor" onDismiss={() => setForm(false)}>
            <div className="modal-head">
              <div className="modal-head-title-group">
                <div>
                  <span className="cycle-detail-eyebrow">สร้าง TEST SUITE · {formProjectName}</span>
                  <h2 id="testsuite-form-title">{code || "Test Suite ใหม่"}</h2>
                  <small>{name.trim() || "ยังไม่ได้ตั้งชื่อ Test Suite"}</small>
                </div>
              </div>
              <button aria-label="ปิดหน้าต่าง" onClick={() => setForm(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            {error && <div className="inline-alert error tse-alert" role="alert"><span>{error}</span></div>}
            <div className="tse-body">
              <section className="tse-meta" aria-label="ข้อมูล Suite">
                <h3><span className="material-symbols-outlined" aria-hidden="true">info</span> ข้อมูล Suite</h3>
                <label className="tse-field">
                  <span>Suite Name <b className="required-mark">*</b></span>
                  <textarea rows={2} value={name} onChange={(e) => setName(e.target.value)} />
                </label>
                <label className="tse-field">
                  <span>Project</span>
                  <select value={projectId} onChange={(e) => setProjectId(e.target.value)}>
                    {projects.map((x) => <option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}
                  </select>
                </label>
                <label className="tse-field">
                  <span>Module <b className="required-mark">*</b></span>
                  <select
                    className="testcase-module-filter"
                    value={formModuleId}
                    disabled={!modules.length}
                    required
                    onChange={(e) => {
                      const nextModuleId = e.target.value;
                      setFormModuleId(nextModuleId);
                      const nextModule = modules.find((m) => m.moduleId === nextModuleId);
                      setName((current) => {
                        const stripped = modules.reduce((acc, m) => acc.startsWith(`${m.moduleName}-`) ? acc.slice(m.moduleName.length + 1) : acc, current);
                        return nextModule ? `${nextModule.moduleName}-${stripped}` : stripped;
                      });
                    }}
                  >
                    <option value="">เลือก Module</option>
                    {renderModuleSelectOptions(modules)}
                  </select>
                  <small className="tse-hint">ชื่อ Suite จะขึ้นต้นด้วยชื่อ Module และรายการ Test Case ที่เพิ่มได้จะกรองตาม Module นี้</small>
                </label>
                <div className="tse-row">
                  <label className="tse-field">
                    <span>Type</span>
                    <select value={type} onChange={(e) => setType(e.target.value)}>{masterOptionElements(suiteTypes, type)}</select>
                  </label>
                  <label className="tse-field">
                    <span>Risk Tier</span>
                    <select value={risk} onChange={(e) => setRisk(e.target.value)}>{masterOptionElements(riskTiers, risk)}</select>
                  </label>
                </div>
                <label className="tse-field">
                  <span>รายละเอียด</span>
                  <textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Suite นี้ใช้ทดสอบอะไร" />
                </label>
                <dl className="tse-stats">
                  <div><dt>จะเพิ่ม</dt><dd>{checked.length}</dd></div>
                  <div><dt>เพิ่มได้</dt><dd>{available.length}</dd></div>
                </dl>
              </section>
              <section className="tse-cases" aria-label="จัด Test Case ในชุด">
                <div className="tse-columns">
                  <section className="suite-panel">
                    <div className="suite-panel-head">
                      <h3><span className="material-symbols-outlined" aria-hidden="true">checklist</span> Test Case ที่จะเพิ่ม</h3>
                      <span className="suite-panel-count">{checked.length}</span>
                    </div>
                    <div className="suite-panel-body">
                      {stagedCases.length ? stagedCases.map((x, index) => (
                        <div className="suite-case tse-case" key={x.testCaseId}>
                          <span className="suite-case-order">{index + 1}</span>
                          <span className="suite-case-info"><b>{x.testCaseCode}</b><small>{x.title}</small>{caseMeta(x.moduleId, x.testType) && <small className="tse-case-meta">{caseMeta(x.moduleId, x.testType)}</small>}</span>
                          <Badge tone={priorityTone(x.priority)}>{x.priority}</Badge>
                          <div className="tse-case-actions">
                            <button type="button" disabled={index === 0} title="เลื่อนขึ้น" aria-label={`เลื่อน ${x.testCaseCode} ขึ้น`} onClick={() => setChecked(c => { const next = [...c]; [next[index - 1], next[index]] = [next[index], next[index - 1]]; return next; })}><span className="material-symbols-outlined" aria-hidden="true">arrow_upward</span></button>
                            <button type="button" disabled={index === stagedCases.length - 1} title="เลื่อนลง" aria-label={`เลื่อน ${x.testCaseCode} ลง`} onClick={() => setChecked(c => { const next = [...c]; [next[index], next[index + 1]] = [next[index + 1], next[index]]; return next; })}><span className="material-symbols-outlined" aria-hidden="true">arrow_downward</span></button>
                            <button type="button" className="tse-case-remove" title="เอาออกจากรายการที่จะเพิ่ม" aria-label={`เอา ${x.testCaseCode} ออกจากรายการที่จะเพิ่ม`} onClick={() => setChecked((c) => c.filter((id) => id !== x.testCaseId))}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
                          </div>
                        </div>
                      )) : (
                        <div className="suite-panel-empty"><span className="material-symbols-outlined" aria-hidden="true">inventory_2</span><p>{formModuleId ? "ยังไม่ได้เลือก Test Case — เลือกจากรายการด้านขวา" : "เลือก Module ทางซ้ายก่อน แล้วเลือก Test Case จากรายการด้านขวา"}</p></div>
                      )}
                    </div>
                  </section>
                  {renderAvailablePanel()}
                </div>
              </section>
            </div>
            <div className="tse-footer">
              <p className={missing.length ? "tse-missing" : "tse-ready"} role="status">
                <span className="material-symbols-outlined" aria-hidden="true">{missing.length ? "error" : "check_circle"}</span>
                {missing.length ? <>ยังขาด: {missing.join(" · ")}</> : `พร้อมสร้าง${checked.length ? ` พร้อม ${checked.length} Test Case` : ""}`}
              </p>
              <button className="btn" onClick={() => setForm(false)}>ยกเลิก</button>
              <button className="btn primary" disabled={!canSave} onClick={save}>
                {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : checked.length ? <><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Test Suite + {checked.length} Test Case</> : <><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Test Suite</>}
              </button>
            </div>
          </ModalShell>
        );
      })()}
      {detail && (() => {
        const activeLinkedCycles = (detail.linkedCycles ?? []).filter(c => !c.isDeleted);
        const deletedCycleCount = (detail.linkedCycles ?? []).length - activeLinkedCycles.length;
        const totalCycleCases = activeLinkedCycles.reduce((sum, c) => sum + c.caseCount, 0);
        const totalCycleExecuted = activeLinkedCycles.reduce((sum, c) => sum + c.executedCount, 0);
        const cyclesProgressPercent = totalCycleCases ? Math.round((totalCycleExecuted * 100) / totalCycleCases) : 0;
        const linkedReleaseBuilds = Array.from(new Map(activeLinkedCycles.map(c => [`${c.releaseCode ?? ""}|${c.buildNumber ?? ""}`, c])).values());
        const requiredCount = detail.cases.filter(c => c.isRequired).length;
        const caseQuery = detailCaseSearch.trim().toLowerCase();
        // เรียงตามรหัส Test Case น้อย → มาก (เลขลำดับในแถว = ตำแหน่งในชุดที่เรียงแล้ว ไม่ขึ้นกับคำค้น/ตัวกรอง)
        const sortedCases = [...detail.cases].sort(caseCodeSort);
        const caseNo = new Map(sortedCases.map((x, i) => [x.testCaseId, i + 1]));
        const visibleCases = sortedCases.filter(x =>
          (detailCaseFilter === "all" || (detailCaseFilter === "required" ? x.isRequired : !x.isRequired)) &&
          (!caseQuery || `${x.testCaseCode} ${x.title}`.toLowerCase().includes(caseQuery)));
        const cycleTone = (status: string) => status === "Completed" || status === "Closed" ? "green" : status === "Cancelled" ? "red" : "yellow";
        const shortDate = (value?: string) => value ? formatThaiDateTime(value, { day: "numeric", month: "short", year: "2-digit" }) : "-";
        return (
          <ModalShell labelledBy="suite-detail-title" className={`suite-detail${detailEditing ? " is-editing" : ""}`} dirty={detailInfoDirty || checked.length > 0} onDismiss={closeDetail}>
              <div className="modal-head">
                <div className="modal-head-title-group">
                  <button className="modal-back-btn" aria-label="ปิดรายละเอียด Test Suite" onClick={() => void requestCloseDetail()}><span className="material-symbols-outlined" aria-hidden="true">arrow_back</span></button>
                  <div>
                    <span className="cycle-detail-eyebrow">{detailEditing ? "แก้ไข TEST SUITE" : "TEST SUITE"} · {projects.find(x => x.projectId === detail.projectId)?.projectName ?? "-"}</span>
                    <h2 id="suite-detail-title">{detail.suiteCode}</h2>
                    <small>{detail.suiteName}</small>
                  </div>
                </div>
                <div className="suite-detail-head-badges">
                  <Badge tone={detail.isActive ? "green" : "red"}>{detail.isActive ? "ใช้งาน" : "ปิดใช้งาน"}</Badge>
                  {detail.suiteType && <Badge tone="blue">{detail.suiteType}</Badge>}
                  {detail.riskTier && <Badge tone={detail.riskTier === "P0" ? "red" : "yellow"}>{detail.riskTier}</Badge>}
                </div>
                <button aria-label="ปิดรายละเอียด Test Suite" onClick={() => void requestCloseDetail()}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
              </div>
              <div className="suite-detail-toolbar" role="toolbar" aria-label="การดำเนินการกับ Test Suite">
                {detailEditing ? <>
                  <button className="btn primary" disabled={saving || !detailInfoDirty || !name.trim()} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">save</span> บันทึกข้อมูล Suite</>}</button>
                  <button className="btn" onClick={() => void finishDetailEdit()}><span className="material-symbols-outlined" aria-hidden="true">done</span> เสร็จสิ้นการแก้ไข</button>
                  <span className={`suite-edit-status${detailInfoDirty ? " is-dirty" : ""}`} role="status">{!name.trim() ? "ยังขาด: Suite Name" : detailInfoDirty ? "ข้อมูล Suite มีการแก้ไขที่ยังไม่บันทึก" : "การเพิ่ม นำออก และสลับ Required/Optional ของ Test Case บันทึกทันที"}</span>
                </> : <>
                  {canEdit && <button className="btn primary" onClick={() => startDetailEdit(detail)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข / จัด Test Case</button>}
                  {onCreateCycle && detail.isActive && <button className="btn" onClick={() => onCreateCycle(detail.projectId, detail.testSuiteId)}><span className="material-symbols-outlined" aria-hidden="true">playlist_add</span> สร้าง Test Cycle</button>}
                  <button className="btn suite-detail-download" onClick={() => downloadSuiteReport(detail)}><span className="material-symbols-outlined" aria-hidden="true">download</span> ดาวน์โหลดรายงาน</button>
                </>}
              </div>
              <div className={`suite-detail-body${detailEditing ? " is-editing" : ""}`}>
                <div className="suite-detail-main">
                  {detailEditing && error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
                  {detailEditing ? <div className="suite-edit-form">
                    <label className="tse-field suite-edit-name"><span>Suite Name <b className="required-mark">*</b></span><input value={name} onChange={(e) => setName(e.target.value)} aria-invalid={!name.trim()} /></label>
                    <label className="tse-field"><span>Type</span><select value={type} onChange={(e) => setType(e.target.value)}>{masterOptionElements(suiteTypes, type)}</select></label>
                    <label className="tse-field"><span>Risk Tier</span><select value={risk} onChange={(e) => setRisk(e.target.value)}>{masterOptionElements(riskTiers, risk)}</select></label>
                    <label className="check-line tse-check suite-edit-active"><input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} /> เปิดใช้งาน</label>
                    <label className="tse-field suite-edit-desc"><span>รายละเอียด</span><textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Suite นี้ใช้ทดสอบอะไร" /></label>
                  </div> : <>
                  <dl className="suite-detail-summary">
                    <div><dt>Test Cases</dt><dd><b>{detail.cases.length}</b> <small>Required {requiredCount} · Optional {detail.cases.length - requiredCount}</small></dd></div>
                    <div><dt>Test Cycle</dt><dd><b>{activeLinkedCycles.length}</b>{deletedCycleCount > 0 && <small> (ลบแล้ว {deletedCycleCount})</small>}</dd></div>
                    <div className="suite-detail-summary-progress"><dt>ความคืบหน้ารวม</dt><dd><b>{cyclesProgressPercent}%</b><span className="suite-detail-progress-track" aria-hidden="true"><span style={{ width: `${Math.min(100, Math.max(0, cyclesProgressPercent))}%` }} /></span></dd></div>
                    <div><dt>Module</dt><dd>{detail.modules?.length ? detail.modules.map(m => m.moduleName).join(", ") : "-"}</dd></div>
                    <div><dt>สร้างโดย</dt><dd>{detail.createdByName || "-"} <small>{detail.createdAt ? formatThaiDateTime(detail.createdAt, { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }) : ""}</small></dd></div>
                  </dl>
                  {detail.description && <p className="suite-detail-description">{detail.description}</p>}
                  </>}
                  <section className="suite-detail-cases-panel" aria-labelledby="suite-detail-cases-title">
                    <div className="suite-detail-cases-head">
                      <h3 id="suite-detail-cases-title"><span className="material-symbols-outlined" aria-hidden="true">checklist</span> Test Cases <span className="suite-detail-count">{visibleCases.length === detail.cases.length ? detail.cases.length : `${visibleCases.length} / ${detail.cases.length}`}</span></h3>
                      <div className="suite-detail-cases-tools">
                        <label className="suite-case-search">
                          <span className="material-symbols-outlined" aria-hidden="true">search</span>
                          <input value={detailCaseSearch} onChange={(e) => setDetailCaseSearch(e.target.value)} aria-label="ค้นหา Test Case ใน Suite" placeholder="ค้นหา Code / ชื่อ Test Case" />
                        </label>
                        <div className="cycle-status-chips" role="group" aria-label="กรอง Required / Optional">
                          {([["all", "ทั้งหมด", detail.cases.length], ["required", "Required", requiredCount], ["optional", "Optional", detail.cases.length - requiredCount]] as const).map(([value, label, count]) => (
                            <button key={value} type="button" className={"status-chip" + (detailCaseFilter === value ? " active" : "")} aria-pressed={detailCaseFilter === value} onClick={() => setDetailCaseFilter(value)}>{label} <b>{count}</b></button>
                          ))}
                        </div>
                      </div>
                    </div>
                    <div className={`suite-detail-case-list${detailEditing ? " is-editing" : ""}`}>
                      {visibleCases.length ? visibleCases.map(x => (
                        <div className="suite-detail-case" key={x.testCaseId}>
                          <span className="suite-detail-case-no">{caseNo.get(x.testCaseId)}</span>
                          <span className="suite-detail-case-info"><b>{x.testCaseCode}</b><small>{x.title}</small></span>
                          {x.priority && <Badge tone={x.priority === "P0" || x.priority === "P1" ? "red" : "blue"}>{x.priority}</Badge>}
                          {detailEditing && managing ? <>
                            <button type="button" className={`tse-required-toggle${x.isRequired ? " is-required" : ""}`} disabled={saving} aria-pressed={x.isRequired} title="สลับ Required / Optional (บันทึกทันที)" onClick={() => updateCase(managing, x.testCaseId, x.sortOrder, !x.isRequired)}>{x.isRequired ? "Required" : "Optional"}</button>
                            <span className="tse-case-actions"><button type="button" className="tse-case-remove" disabled={saving} title="นำออกจาก Suite" aria-label={`นำ ${x.testCaseCode} ออกจาก Suite`} onClick={() => removeCase(managing.testSuiteId, x.testCaseId)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></span>
                          </> : <Badge tone={x.isRequired ? "blue" : "yellow"}>{x.isRequired ? "Required" : "Optional"}</Badge>}
                        </div>
                      )) : <p className="muted-text suite-detail-empty">{detail.cases.length ? "ไม่พบ Test Case ที่ตรงกับคำค้นหรือตัวกรอง" : detailEditing ? "ยังไม่มี Test Case ในชุดนี้ — เลือกจากแผงด้านขวาแล้วกด \"เพิ่มเข้าชุด\"" : "ยังไม่มี Test Case ในชุดนี้"}</p>}
                    </div>
                  </section>
                </div>
                {detailEditing ? <aside className="suite-detail-side suite-detail-add" aria-label="เพิ่ม Test Case เข้าชุด">{renderAvailablePanel()}</aside> : <aside className="suite-detail-side" aria-label="การใช้งานของ Test Suite">
                  <section>
                    <h3><span className="material-symbols-outlined" aria-hidden="true">inventory_2</span> Release / Build ที่ใช้งาน <span className="suite-detail-count">{linkedReleaseBuilds.length}</span></h3>
                    {linkedReleaseBuilds.length ? (
                      <div className="suite-detail-chips">
                        {linkedReleaseBuilds.map(c => <span key={`${c.releaseCode ?? ""}|${c.buildNumber ?? ""}`}><b>{c.releaseCode || "ไม่ระบุ Release"}{c.releaseVersion ? ` · ${c.releaseVersion}` : ""}</b> Build {c.buildNumber || "-"}</span>)}
                      </div>
                    ) : <p className="muted-text">ยังไม่มี Test Cycle ที่อ้างอิง Suite นี้</p>}
                  </section>
                  <section>
                    <h3><span className="material-symbols-outlined" aria-hidden="true">cycle</span> Test Cycles <span className="suite-detail-count">{activeLinkedCycles.length}</span></h3>
                    {activeLinkedCycles.length ? (
                      <div className="suite-detail-cycles">
                        {activeLinkedCycles.map(c => (
                          <div className="suite-detail-cycle" key={c.testCycleId}>
                            <div className="suite-detail-cycle-head">
                              {onOpenCycle
                                ? <button type="button" className="link-button" title="เปิดรายละเอียด Test Cycle" onClick={() => { closeDetail(); onOpenCycle("test-cycles", c.testCycleId); }}>{c.cycleCode} <span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></button>
                                : <b>{c.cycleCode}</b>}
                              <Badge tone={cycleTone(c.status)}>{c.status}</Badge>
                            </div>
                            <p>{c.cycleName}</p>
                            <small>{c.releaseCode || "ไม่ระบุ Release"}{c.releaseVersion ? ` · ${c.releaseVersion}` : ""} · Build {c.buildNumber || "-"} · {shortDate(c.startDate)} – {shortDate(c.endDate)}{c.ownerName ? ` · ${c.ownerName}` : ""}</small>
                            <div className="suite-detail-cycle-progress"><span className="suite-detail-progress-track" aria-hidden="true"><span style={{ width: `${Math.min(100, Math.max(0, c.progressPercent))}%` }} /></span><b>{c.progressPercent}%</b><small>{c.executedCount}/{c.caseCount}</small></div>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <div className="suite-detail-side-empty">
                        <p className="muted-text">ยังไม่มี Test Cycle ผูกกับ Suite นี้</p>
                        {onCreateCycle && detail.isActive && <button className="btn" onClick={() => onCreateCycle(detail.projectId, detail.testSuiteId)}><span className="material-symbols-outlined" aria-hidden="true">playlist_add</span> สร้าง Test Cycle แรก</button>}
                      </div>
                    )}
                  </section>
                </aside>}
              </div>
            </ModalShell>
        );
      })()}
    </>
  );
}
