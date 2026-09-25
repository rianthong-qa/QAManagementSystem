import { useState, useEffect } from "react";
import type { ProjectItem, UserLookup } from "../shared/types";
import { apiUrl } from "../api";
import { notify } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { formatThaiDateTime } from "../dateTime";
import { type ModuleItem, type TestCaseItem, buildModuleTree, fmtDateTimeBE, masterOptionElements, nextBusinessCode, renderModuleSelectOptions, useMasterOptions } from "../shared/appShared";

function contextualCode(projectCode: string, moduleCode: string, kind: string) {
  const project = projectCode.toUpperCase();
  const module = moduleCode.toUpperCase().startsWith(`${project}-`)
    ? moduleCode.slice(projectCode.length + 1)
    : moduleCode;
  return `${projectCode}-${module}-${kind}`;
}
type TestCaseRequirement = { requirementId:string; requirementCode:string; title:string; status:string; coverageType?:string };
type TestCaseRevision = { revisionNo:number; changeReason:string; changedBy?:string; changedByName?:string; changedAt:string; steps:TestCaseItem["steps"] };
type TestCaseListSort = { field: "testCaseCode" | "createdAt"; direction: "asc" | "desc" };
const testCaseStatusInfo = [
  {value:"Draft",label:"ฉบับร่าง",detail:"อยู่ระหว่างออกแบบและยังไม่นำไปนับ Coverage",impact:"แก้ไขได้ และยังไม่พร้อมสำหรับ Execution"},
  {value:"Review",label:"รอตรวจสอบ",detail:"ส่งให้ผู้เกี่ยวข้องตรวจความครบถ้วนของขั้นตอน",impact:"เชื่อม Requirement ได้ แต่ RTM จะแสดง Partial"},
  {value:"Ready",label:"พร้อมใช้งาน",detail:"ผ่านการตรวจและพร้อมนำไปจัด Suite หรือ Execution",impact:"Test Case ที่เชื่อมจะทำให้ Requirement เป็น Covered"},
  {value:"Deprecated",label:"เลิกใช้งาน",detail:"เก็บไว้เพื่อประวัติและไม่ควรนำไปใช้รอบใหม่",impact:"ไม่ควรเพิ่มเข้า Test Suite หรือ Cycle ใหม่"},
];
type GeneratedTestCase = { title:string; objective:string; preconditions:string; priority:string; testType:string; automationCandidate:boolean; steps:{ stepNo:number; action:string; testData?:string; expectedResult:string }[] };
export function TestCasesPage({
  search,
  canEdit,
  contextProjectId,
}: {
  search: string;
  canEdit: boolean;
  contextProjectId?: string;
}) {
  const masterOptions = useMasterOptions(), testCasePriorities = masterOptions("TestCasePriority"), testCaseTypes = masterOptions("TestCaseType");
  const [items, setItems] = useState<TestCaseItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [modules, setModules] = useState<ModuleItem[]>([]),
    [filterModules,setFilterModules]=useState<ModuleItem[]>([]),
    [loading, setLoading] = useState(true),
    [totalCount,setTotalCount]=useState(0),
    [reload, setReload] = useState(0),
    [form, setForm] = useState(false),
    [editing, setEditing] = useState<TestCaseItem | null>(null),
    [saving, setSaving] = useState(false),
    [statusFilter, setStatusFilter] = useState(""),
    [projectFilter,setProjectFilter]=useState(""),[moduleFilter,setModuleFilter]=useState(""),
    [automationFilter,setAutomationFilter]=useState(""),
    [createdByFilter,setCreatedByFilter]=useState(""),
    [users,setUsers]=useState<UserLookup[]>([]),[ownerUserId,setOwnerUserId]=useState(""),
    [error,setError]=useState(""),[notice,setNotice]=useState(""),[page,setPage]=useState(1),[pageSize,setPageSize]=useState(30),
    [listSort,setListSort]=useState<TestCaseListSort>(()=>{try{const saved=JSON.parse(localStorage.getItem("qa.testCases.listSort")??"");return(saved?.field==="testCaseCode"||saved?.field==="createdAt")&&(saved?.direction==="asc"||saved?.direction==="desc")?saved:{field:"createdAt",direction:"desc"};}catch{return{field:"createdAt",direction:"desc"};}}),
    [detail,setDetail]=useState<TestCaseItem|null>(null),[detailRequirements,setDetailRequirements]=useState<TestCaseRequirement[]>([]),
    [revisions,setRevisions]=useState<TestCaseRevision[]>([]),[confirmDelete,setConfirmDelete]=useState<TestCaseItem|null>(null),
    [importing,setImporting]=useState(false),[templateDownloading,setTemplateDownloading]=useState(false),
    [testCaseAiModal,setTestCaseAiModal]=useState(false),
    [testCaseAiPrompt,setTestCaseAiPrompt]=useState(""),
    [testCaseAiFiles,setTestCaseAiFiles]=useState<File[]>([]),
    [testCaseAiGenerating,setTestCaseAiGenerating]=useState(false),
    [testCaseAiError,setTestCaseAiError]=useState(""),
    [caseAiDrafts,setCaseAiDrafts]=useState<GeneratedTestCase[]>([]),
    [caseAiExpanded,setCaseAiExpanded]=useState<number|undefined>(undefined),
    [tcSelected,setTcSelected]=useState<Set<string>>(new Set()),[tcBulkStatus,setTcBulkStatus]=useState(""),[tcBulkAutomation,setTcBulkAutomation]=useState(""),[tcSaving,setTcSaving]=useState(""),[confirmBulkDelete,setConfirmBulkDelete]=useState(false);
  const [projectId, setProjectId] = useState(""),
    [moduleId, setModuleId] = useState(""),
    [code, setCode] = useState(""),
    [title, setTitle] = useState(""),
    [objective, setObjective] = useState(""),
    [preconditions, setPreconditions] = useState(""),
    [priority, setPriority] = useState(""),
    [testType, setTestType] = useState(""),
    [automation, setAutomation] = useState(false),
    [status, setStatus] = useState("Draft"),
    [changeReason, setChangeReason] = useState(""),
    [steps, setSteps] = useState([
      { stepNo: 1, action: "", testData: "", expectedResult: "" },
    ]);
  const [debouncedSearch, setDebouncedSearch] = useState(search);
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  useEffect(() => {
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    setLoading(true); setError("");
    const readJson=(url:string)=>fetch(url,{headers:h}).then(async r=>{if(!r.ok)throw new Error((await r.text())||`HTTP ${r.status}`);return r.json();});
    Promise.all([readJson(`${apiUrl}/projects`),readJson(`${apiUrl}/lookups/users`)])
      .then(([projectData, userData]) => {
        setUsers(userData);
        const activeProjects = (projectData as ProjectItem[]).filter(
          (x) => x.isActive,
        );
        setProjects(activeProjects);
        setProjectId(
          (current) => current || activeProjects[0]?.projectId || "",
        );
      }).catch(e=>setError(e instanceof Error?e.message:"โหลดข้อมูล Test Case ไม่สำเร็จ"))
      .finally(() => setLoading(false));
  }, [reload]);
  useEffect(() => {
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    let cancelled = false;
    setLoading(true);
    const params = new URLSearchParams({ page: String(page), size: String(pageSize) });
    if (projectFilter) params.set("projectId", projectFilter);
    if (moduleFilter) params.set("moduleId", moduleFilter);
    if (statusFilter) params.set("status", statusFilter);
    if (automationFilter) params.set("automation", automationFilter === "yes" ? "true" : "false");
    if (createdByFilter) params.set("createdBy", createdByFilter);
    if (debouncedSearch.trim()) params.set("search", debouncedSearch.trim());
    params.set("sortBy", `${listSort.field}_${listSort.direction}`);
    fetch(`${apiUrl}/test-cases?${params}`, { headers: h })
      .then(async r => { if (!r.ok) throw new Error((await r.text()) || `HTTP ${r.status}`); return r.json(); })
      .then((body) => {
        if (cancelled) return;
        setItems(Array.isArray(body) ? body : body?.rows ?? []);
        setTotalCount(Array.isArray(body) ? body.length : body?.total ?? 0);
      })
      .catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : "โหลดข้อมูล Test Case ไม่สำเร็จ"); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [reload, page, pageSize, projectFilter, moduleFilter, statusFilter, automationFilter, createdByFilter, debouncedSearch, listSort]);
  useEffect(()=>{localStorage.setItem("qa.testCases.listSort",JSON.stringify(listSort));},[listSort]);
  useEffect(() => {
    if (!projectId) {
      setModules([]);
      return;
    }
    fetch(`${apiUrl}/projects/${projectId}/modules`, {
      headers: {
        Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
      },
    })
      .then((r) => r.json())
      .then((data: ModuleItem[]) => {
        const active = data.filter((x) => x.isActive);
        setModules(active);
        setModuleId((current) =>
          active.some((x) => x.moduleId === current)
            ? current
            : active[0]?.moduleId || "",
        );
      });
  }, [projectId]);
  useEffect(()=>{
    if(!projects.length){setFilterModules([]);return;}
    let cancelled=false;
    const targets=projectFilter?projects.filter(x=>x.projectId===projectFilter):projects;
    const authHeaders={Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`};
    Promise.all(targets.map(project=>fetch(`${apiUrl}/projects/${project.projectId}/modules`,{headers:authHeaders}).then(async response=>response.ok?response.json():Promise.reject(new Error(`โหลด Module ของ ${project.projectName} ไม่สำเร็จ`)))))
      .then(groups=>{if(!cancelled)setFilterModules((groups.flat() as ModuleItem[]).filter(x=>x.isActive));})
      .catch(error=>{if(!cancelled){setFilterModules([]);setError(error instanceof Error?error.message:"โหลดข้อมูล Module ไม่สำเร็จ");}});
    return()=>{cancelled=true};
  },[projects,projectFilter,reload]);
  useEffect(() => { setProjectFilter(contextProjectId ?? ""); setModuleFilter(""); setPage(1); }, [contextProjectId]);
  useEffect(() => {
    if (!form || editing || !projectId || !moduleId) return;
    const project = projects.find((x) => x.projectId === projectId);
    const module = modules.find((x) => x.moduleId === moduleId);
    setCode(
      nextBusinessCode(
        contextualCode(
          project?.projectCode ?? "PRJ",
          module?.moduleCode ?? "MOD",
          "TC",
        ),
        items.map((x) => x.testCaseCode),
      ),
    );
  }, [form, editing, projectId, moduleId, projects, modules, items]);
  const openForm = async (item?: TestCaseItem) => {
    let source = item;
    if (item) {
      // ต้องได้รายละเอียดเต็ม (รวม steps) ก่อนเปิดฟอร์มแก้ไข — เดิมถ้าดึงไม่สำเร็จจะเปิดฟอร์มด้วยข้อมูลจากรายการ
      // ซึ่งไม่มี steps ทำให้ฟอร์มมี step ว่าง 1 แถว และถ้ากดบันทึกจะทับ steps จริงทั้งหมด
      try {
        const r = await fetch(`${apiUrl}/test-cases/${item.testCaseId}`, { headers });
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        source = { ...item, ...(await r.json()) };
      } catch (e) {
        setError(`เปิด ${item.testCaseCode} เพื่อแก้ไขไม่สำเร็จ (${e instanceof Error ? e.message : "ไม่ทราบสาเหตุ"}) — ลองใหม่อีกครั้ง`);
        return;
      }
    }
    const target = source;
    setEditing(target ?? null);
    setProjectId(target?.projectId ?? projects[0]?.projectId ?? "");
    setModuleId(target?.moduleId ?? "");
    const targetProjectId = target?.projectId ?? projects[0]?.projectId ?? "";
    const targetModuleId = target?.moduleId ?? modules[0]?.moduleId ?? "";
    const project = projects.find((x) => x.projectId === targetProjectId);
    const module = modules.find((x) => x.moduleId === targetModuleId);
    setCode(
      target?.testCaseCode ??
        nextBusinessCode(
          contextualCode(
            project?.projectCode ?? "PRJ",
            module?.moduleCode ?? "MOD",
            "TC",
          ),
          items.map((x) => x.testCaseCode),
        ),
    );
    setTitle(target?.title ?? "");
    setObjective(target?.objective ?? "");
    setPreconditions(target?.preconditions ?? "");
    setPriority(target?.priority ?? testCasePriorities[0]?.value ?? "");
    setTestType(target?.testType ?? testCaseTypes[0]?.value ?? "");
    setAutomation(target?.automationCandidate ?? false);
    setOwnerUserId(target?.ownerUserId ?? "");
    setStatus(target?.status ?? "Draft");
    setChangeReason(target ? "ปรับปรุงข้อมูล Test Case" : "");
    setSteps(
      target?.steps?.length
        ? target.steps.map((x) => ({ ...x, testData: x.testData ?? "" }))
        : [{ stepNo: 1, action: "", testData: "", expectedResult: "" }],
    );
    setForm(true);
  };
  const updateStep = (
    index: number,
    field: "action" | "testData" | "expectedResult",
    value: string,
  ) =>
    setSteps((current) =>
      current.map((x, i) => (i === index ? { ...x, [field]: value } : x)),
    );
  const save = async () => {
    setSaving(true);
    try {
      const body = editing
        ? {
            moduleId,
            title,
            objective: objective || null,
            preconditions: preconditions || null,
            priority,
            testType,
            automationCandidate: automation,
            ownerUserId: ownerUserId || null,
            changeReason: changeReason.trim(),
            steps,
          }
        : {
            projectId,
            moduleId,
            testCaseCode: "",
            title,
            objective: objective || null,
            preconditions: preconditions || null,
            priority,
            testType,
            automationCandidate: automation,
            ownerUserId: ownerUserId || null,
            steps,
          };
      const response = await fetch(
        `${apiUrl}/test-cases${editing ? `/${editing.testCaseId}` : ""}`,
        {
          method: editing ? "PUT" : "POST",
          headers,
          body: JSON.stringify(body),
        },
      );
      if (!response.ok) {
        const problem = await response.json();
        throw new Error(problem.detail ?? "บันทึก Test Case ไม่สำเร็จ");
      }
      if (editing && status !== editing.status) {
        const statusResponse = await fetch(
          `${apiUrl}/test-cases/${editing.testCaseId}/status`,
          { method: "POST", headers, body: JSON.stringify({ status }) },
        );
        if (!statusResponse.ok) {
          const problem = await statusResponse.json();
          throw new Error(problem.detail ?? "เปลี่ยนสถานะไม่สำเร็จ");
        }
      }
      setForm(false); setNotice(editing ? "บันทึกและสร้าง Revision ใหม่แล้ว" : "เพิ่ม Test Case แล้ว");
      setReload((x) => x + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const remove = async (item: TestCaseItem) => {
    const response = await fetch(`${apiUrl}/test-cases/${item.testCaseId}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) {
      setError("ลบ Test Case ไม่สำเร็จ");
      return;
    }
    setConfirmDelete(null);setNotice(`ลบ ${item.testCaseCode} แล้ว`);setReload((x) => x + 1);
  };
  const openDetail=async(item:TestCaseItem)=>{try{const h={Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`};const read=(url:string)=>fetch(url,{headers:h}).then(r=>r.ok?r.json():null);const [full,reqs,history]=await Promise.all([read(`${apiUrl}/test-cases/${item.testCaseId}`),read(`${apiUrl}/test-cases/${item.testCaseId}/requirements`),read(`${apiUrl}/test-cases/${item.testCaseId}/revisions`)]);setDetail(full?{...item,...full}:item);setDetailRequirements(Array.isArray(reqs)?reqs:[]);setRevisions(Array.isArray(history)?history:[]);if(!full||!Array.isArray(reqs)||!Array.isArray(history))notify(`โหลดรายละเอียด ${item.testCaseCode} ไม่ครบ — ข้อมูลที่แสดงอาจไม่ครบ (Steps / Requirement / Revision)`,"error");}catch{setDetail(item);notify(`โหลดรายละเอียด ${item.testCaseCode} ไม่สำเร็จ`,"error");}};
  // ปุ่ม "ดูรายละเอียด" บน Test Case ที่เชื่อมโยงกับ Defect ฝาก id ไว้ผ่าน localStorage แล้วพามาที่นี่ — เปิด
  // detail modal ให้ทันทีด้วย openDetail เดิม (stub เฉพาะ testCaseId พอ เพราะ openDetail ดึงข้อมูลเต็มมา merge ทับอยู่แล้ว)
  useEffect(() => {
    const target = localStorage.getItem("qa.targetTestCaseId");
    if (!target) return;
    localStorage.removeItem("qa.targetTestCaseId");
    void openDetail({ testCaseId: target } as unknown as TestCaseItem);
  }, []);
  const cloneCase=async(item:TestCaseItem)=>{try{const r=await fetch(`${apiUrl}/test-cases/${item.testCaseId}/clone`,{method:"POST",headers});if(!r.ok)throw new Error("คัดลอก Test Case ไม่สำเร็จ");setNotice(`สร้างสำเนาจาก ${item.testCaseCode} แล้ว`);setReload(x=>x+1);}catch(e){setError(e instanceof Error?e.message:"คัดลอกไม่สำเร็จ");}};
  const toggleTcSelect=(id:string)=>setTcSelected(prev=>{const next=new Set(prev);if(next.has(id))next.delete(id);else next.add(id);return next;});
  const toggleTcSelectPage=()=>setTcSelected(prev=>{const next=new Set(prev);const all=pagedRows.length>0&&pagedRows.every(x=>prev.has(x.testCaseId));if(all)pagedRows.forEach(x=>next.delete(x.testCaseId));else pagedRows.forEach(x=>next.add(x.testCaseId));return next;});
  const applyTcBulkStatus=async()=>{if(!tcBulkStatus||!tcSelected.size||!canEdit)return;setTcSaving("bulk");setError("");try{const ids=[...tcSelected];for(const id of ids){const r=await fetch(`${apiUrl}/test-cases/${id}/status`,{method:"POST",headers,body:JSON.stringify({status:tcBulkStatus})});if(!r.ok){const p=await r.json().catch(()=>null);throw new Error(p?.detail??"เปลี่ยนสถานะไม่สำเร็จ")}}setTcSelected(new Set());setTcBulkStatus("");setReload(x=>x+1);}catch(e){setError(e instanceof Error?e.message:"เปลี่ยนสถานะไม่สำเร็จ")}finally{setTcSaving("")}};
  const applyTcBulkAutomation=async()=>{if(tcBulkAutomation===""||!tcSelected.size||!canEdit)return;setTcSaving("bulk");setError("");try{const ids=[...tcSelected];const value=tcBulkAutomation==="yes";for(const id of ids){const full=await fetch(`${apiUrl}/test-cases/${id}`,{headers}).then(r=>r.ok?r.json():null);if(!full||!full.moduleId)throw new Error("โหลด Test Case ไม่สำเร็จ");const body={moduleId:full.moduleId,title:full.title,objective:full.objective||null,preconditions:full.preconditions||null,priority:full.priority,testType:full.testType,automationCandidate:value,ownerUserId:full.ownerUserId||null,changeReason:"กำหนด Automation Candidate แบบกลุ่ม",steps:Array.isArray(full.steps)?full.steps.map((s:any)=>({stepNo:s.stepNo,action:s.action,testData:s.testData??"",expectedResult:s.expectedResult})):[]};const r=await fetch(`${apiUrl}/test-cases/${id}`,{method:"PUT",headers,body:JSON.stringify(body)});if(!r.ok){const p=await r.json().catch(()=>null);throw new Error(p?.detail??"กำหนด Automation Candidate ไม่สำเร็จ")}}setTcSelected(new Set());setTcBulkAutomation("");setReload(x=>x+1);}catch(e){setError(e instanceof Error?e.message:"กำหนด Automation Candidate ไม่สำเร็จ")}finally{setTcSaving("")}};
  const removeBulkSelected=async()=>{if(!tcSelected.size||!canEdit)return;setTcSaving("bulk-delete");setError("");const ids=[...tcSelected];let deleted=0;const failed:string[]=[];for(const id of ids){const item=items.find(x=>x.testCaseId===id);try{const r=await fetch(`${apiUrl}/test-cases/${id}`,{method:"DELETE",headers});if(r.ok){deleted++;}else{failed.push(item?.testCaseCode??id);}}catch{failed.push(item?.testCaseCode??id);}}setConfirmBulkDelete(false);if(failed.length){setError(`ลบ Test Case ไม่สำเร็จ ${failed.length} รายการ (${failed.slice(0,5).join(", ")}${failed.length>5?" และอื่น ๆ":""})`);}if(deleted){setNotice(`ลบ Test Case แล้ว ${deleted} รายการ`);}if(deleted||failed.length){setTcSelected(new Set());setReload(x=>x+1);}setTcSaving("");};
  const importFile=async(file:File)=>{setImporting(true);setError("");try{const data=new FormData();data.append("file",file);data.append("projectId",projectFilter||projects[0]?.projectId||"");const r=await fetch(`${apiUrl}/test-cases/import`,{method:"POST",headers:{Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`},body:data});if(!r.ok)throw new Error((await r.text())||"นำเข้าข้อมูลไม่สำเร็จ");const result=await r.json();setNotice(`นำเข้าสำเร็จ ${result.imported} รายการ${result.failed?` ไม่สำเร็จ ${result.failed} รายการ`:""}`);setReload(x=>x+1);}catch(e){setError(e instanceof Error?e.message:"นำเข้าข้อมูลไม่สำเร็จ");}finally{setImporting(false);}};
  const downloadImportTemplate=async()=>{const selectedProjectId=projectFilter||projects[0]?.projectId||"";if(!selectedProjectId){setError("กรุณาเลือก Project ก่อนดาวน์โหลด Template");return;}setTemplateDownloading(true);setError("");try{const response=await fetch(`${apiUrl}/test-cases/import-template?projectId=${selectedProjectId}`,{headers:{Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`}});if(!response.ok){const problem=await response.json().catch(()=>null);throw new Error(problem?.detail??"ดาวน์โหลด Template ไม่สำเร็จ");}const blob=await response.blob();const url=URL.createObjectURL(blob);const link=document.createElement("a");link.href=url;link.download="TestCase_Import_Template.xlsx";link.click();URL.revokeObjectURL(url);}catch(e){setError(e instanceof Error?e.message:"ดาวน์โหลด Template ไม่สำเร็จ");}finally{setTemplateDownloading(false);}};
  const openTestCaseAi=()=>{setProjectId(projectFilter||projectId||projects[0]?.projectId||"");setModuleId(moduleFilter||moduleId||"");setTestCaseAiPrompt("");setTestCaseAiFiles([]);setTestCaseAiError("");setCaseAiDrafts([]);setCaseAiExpanded(undefined);setTestCaseAiModal(true);};
  const addTestCaseAiFiles=(selected:File[])=>{const next=[...testCaseAiFiles,...selected].slice(0,5);if(next.reduce((sum,file)=>sum+file.size,0)>20_000_000){setTestCaseAiError("ขนาดไฟล์รวมต้องไม่เกิน 20 MB");return;}setTestCaseAiError(selected.length+testCaseAiFiles.length>5?"แนบไฟล์ได้ไม่เกิน 5 ไฟล์":"");setTestCaseAiFiles(next);};
  const generateTestCaseWithAi=async()=>{
    if(!projectId||!moduleId||!testCaseAiPrompt.trim())return;
    setTestCaseAiGenerating(true);setTestCaseAiError("");
    try{
      const body=new FormData();body.append("prompt",testCaseAiPrompt.trim());body.append("projectName",projects.find(x=>x.projectId===projectId)?.projectName??"");body.append("moduleName",modules.find(x=>x.moduleId===moduleId)?.moduleName??"");body.append("moduleId",moduleId);body.append("projectId",projectId);testCaseAiFiles.forEach(file=>body.append("files",file));
      const response=await fetch(`${apiUrl}/test-cases/generate-ai`,{method:"POST",headers:{Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`},body});
      if(!response.ok){const problem=await response.json().catch(()=>null);throw new Error(problem?.detail??"AI Generate Test Case ไม่สำเร็จ");}
      const drafts:GeneratedTestCase[]=await response.json();
      if(!Array.isArray(drafts)||!drafts.length)throw new Error("AI ไม่ได้สร้าง Test Case กลับมา");
      setCaseAiDrafts(drafts);setCaseAiExpanded(0);
    }catch(e){setTestCaseAiError(e instanceof Error?e.message:"AI Generate Test Case ไม่สำเร็จ");}finally{setTestCaseAiGenerating(false);}
  };
  const removeCaseAiDraft=(index:number)=>setCaseAiDrafts(drafts=>{const next=drafts.filter((_,i)=>i!==index);if(next.length===0){setTestCaseAiModal(false);}return next;});
  const saveAllCaseDrafts=async()=>{if(!caseAiDrafts.length)return;setTestCaseAiGenerating(true);setTestCaseAiError("");try{let created=0;for(const draft of caseAiDrafts){const priority=testCasePriorities.some(x=>x.value===draft.priority)?draft.priority:(testCasePriorities[0]?.value??"");const testType=testCaseTypes.some(x=>x.value===draft.testType)?draft.testType:(testCaseTypes[0]?.value??"");const res=await fetch(`${apiUrl}/test-cases`,{method:"POST",headers,body:JSON.stringify({projectId,moduleId,testCaseCode:"",title:draft.title,objective:draft.objective,preconditions:draft.preconditions,priority,testType,automationCandidate:draft.automationCandidate,ownerUserId:null,steps:draft.steps.map((x,i)=>({stepNo:i+1,action:x.action,testData:x.testData??"",expectedResult:x.expectedResult}))})});if(!res.ok){const problem=await res.json().catch(()=>null);throw new Error(`สร้าง Test Case "${draft.title}" ไม่สำเร็จ: ${problem?.detail??""}`);}await res.json();created++;}setCaseAiDrafts([]);setTestCaseAiModal(false);setReload(x=>x+1);}catch(e){setTestCaseAiError(e instanceof Error?e.message:"บันทึก Test Case ไม่สำเร็จ");}finally{setTestCaseAiGenerating(false);}};
  if (loading && !items.length)
    return (
      <article className="card empty">
        <div className="spinner" />
        <p>กำลังโหลด Test Case...</p>
      </article>
    );
  const rows = [...items].sort((a,b)=>{
    const comparison=listSort.field==="testCaseCode"
      ? a.testCaseCode.localeCompare(b.testCaseCode,undefined,{numeric:true,sensitivity:"base"})
      : (new Date(a.createdAt??0).getTime()-new Date(b.createdAt??0).getTime());
    const stableComparison=comparison||a.testCaseId.localeCompare(b.testCaseId);
    return listSort.direction==="asc"?stableComparison:-stableComparison;
  });
  const pageCount=Math.max(1,Math.ceil(totalCount/pageSize));
  const pagedRows=rows;
  const toggleListSort=(field:TestCaseListSort["field"])=>{setListSort(current=>({field,direction:current.field===field&&current.direction==="asc"?"desc":"asc"}));setPage(1);};
  const moduleFilterGroups=projects.filter(project=>!projectFilter||project.projectId===projectFilter).map(project=>({
    project,
    ordered:buildModuleTree(filterModules.filter(module=>module.projectId===project.projectId)),
  })).filter(group=>group.ordered.length);
  return (
    <>
      <article className="card testcase-list-card">
        {error&&<div className="inline-alert error"><span>{error}</span><button onClick={()=>{setError("");setReload(x=>x+1)}}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
        {notice&&<div className="inline-alert success"><span>{notice}</span><button type="button" aria-label="ปิดข้อความ" onClick={()=>setNotice("")}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>}
        <div className="testcase-toolbar">
          <div className="testcase-toolbar-head">
            <div className="result-count"><strong>{totalCount.toLocaleString()}</strong><span>Test Cases</span></div>
            <div className="testcase-toolbar-actions">
              {canEdit&&<button className="btn ai-button" onClick={openTestCaseAi}><span className="material-symbols-outlined" aria-hidden="true">auto_awesome</span> AI Generate</button>}
              {canEdit&&<button className="btn" disabled={templateDownloading||projects.length===0} onClick={downloadImportTemplate}>{templateDownloading?"กำลังดาวน์โหลด...":<><span className="material-symbols-outlined" aria-hidden="true">download</span> Template</>}</button>}
              {canEdit&&<label className="btn import-button"><span className="material-symbols-outlined" aria-hidden="true">upload_file</span><span>{importing?"กำลังนำเข้า...":"Import"}<small>CSV/XLSX</small></span><input type="file" accept=".csv,.xlsx" disabled={importing} onChange={e=>{const file=e.target.files?.[0];if(file)void importFile(file);e.target.value=""}} /></label>}
              {canEdit&&<button className="btn primary" onClick={()=>openForm()}>+ Test Case</button>}
            </div>
          </div>
          <div className="testcase-filter-section">
            <span className="testcase-filter-label">ตัวกรอง</span>
            <div className="testcase-filters">
            <select value={projectFilter} onChange={e=>{setProjectFilter(e.target.value);setModuleFilter("");setPage(1)}}><option value="">ทุก Project</option>{projects.map(x=><option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select>
            <select className="testcase-module-filter" value={moduleFilter} onChange={e=>{setModuleFilter(e.target.value);setPage(1)}} disabled={!moduleFilterGroups.length}><option value="">ทุก Module</option>{moduleFilterGroups.map(({project,ordered})=><optgroup key={project.projectId} label={`${project.projectCode} · ${project.projectName}`}>{renderModuleSelectOptions(ordered.map(entry=>entry.module))}</optgroup>)}</select>
            <select value={automationFilter} onChange={e=>{setAutomationFilter(e.target.value);setPage(1)}}><option value="">ทุก Automation</option><option value="yes">Automation Candidate</option><option value="no">Manual</option></select>
            <select aria-label="กรองผู้สร้าง" value={createdByFilter} onChange={e=>{setCreatedByFilter(e.target.value);setPage(1)}}><option value="">ผู้สร้างทั้งหมด</option>{users.map(u=><option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select>
            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="">ทุกสถานะ</option>
              <option>Draft</option>
              <option>Review</option>
              <option>Ready</option>
              <option>Deprecated</option>
            </select>
            </div>
          </div>
        </div>
        {canEdit && tcSelected.size > 0 && (
          <div className="testcase-bulk-bar" role="region" aria-label="กำหนดข้อมูลกลุ่ม">
            <span className="bulk-count">{tcSelected.size} เลือกแล้ว</span>
            <label className="bulk-status">กำหนดสถานะ
              <select value={tcBulkStatus} onChange={e=>setTcBulkStatus(e.target.value)}>
                <option value="">เลือกสถานะ...</option>
                <option>Draft</option>
                <option>Review</option>
                <option>Ready</option>
                <option>Deprecated</option>
              </select>
            </label>
            <button type="button" className="btn primary" disabled={tcSaving!==""||!tcBulkStatus} onClick={applyTcBulkStatus}>{tcSaving==="bulk"?<><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</>:<><span className="material-symbols-outlined" aria-hidden="true">check</span> กำหนดสถานะ</>}</button>
            <label className="bulk-automation">Automation Candidate
              <select value={tcBulkAutomation} onChange={e=>setTcBulkAutomation(e.target.value)}>
                <option value="">เลือกค่า...</option>
                <option value="yes">เป็น Candidate</option>
                <option value="no">Manual</option>
              </select>
            </label>
            <button type="button" className="btn primary" disabled={tcSaving!==""||tcBulkAutomation===""} onClick={applyTcBulkAutomation}>{tcSaving==="bulk"?<><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</>:<><span className="material-symbols-outlined" aria-hidden="true">check</span> กำหนด Candidate</>}</button>
            <button type="button" className="btn danger" disabled={tcSaving!==""} onClick={()=>setConfirmBulkDelete(true)}>{tcSaving==="bulk-delete"?<><span className="spinner inline" aria-hidden="true" /> กำลังลบ...</>:<><span className="material-symbols-outlined" aria-hidden="true">close</span> ลบที่เลือก</>}</button>
            <button type="button" className="bulk-clear" disabled={tcSaving!==""} onClick={()=>setTcSelected(new Set())}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิกเลือก</button>
          </div>
        )}
        <div className="table-wrap">
          <table className="testcase-list-table">
            <thead>
              <tr>
                <th className="tc-select-col"><input type="checkbox" aria-label="เลือกทั้งหน้านี้" checked={pagedRows.length>0&&pagedRows.every(x=>tcSelected.has(x.testCaseId))} disabled={!canEdit} onChange={toggleTcSelectPage}/></th>
                <th aria-sort={listSort.field==="testCaseCode"?(listSort.direction==="asc"?"ascending":"descending"):"none"}><button type="button" className="table-sort-button" onClick={()=>toggleListSort("testCaseCode")}>Test Case ID <span className="material-symbols-outlined" aria-hidden="true">{listSort.field==="testCaseCode"?(listSort.direction==="asc"?"arrow_upward":"arrow_downward"):"unfold_more"}</span></button></th>
                <th>Title</th>
                <th>Priority</th>
                <th>Type</th>
                <th>Revision</th>
                <th>Steps</th>
                <th>Status</th>
                <th aria-sort={listSort.field==="createdAt"?(listSort.direction==="asc"?"ascending":"descending"):"none"}><button type="button" className="table-sort-button" onClick={()=>toggleListSort("createdAt")}>สร้างเมื่อ <span className="material-symbols-outlined" aria-hidden="true">{listSort.field==="createdAt"?(listSort.direction==="asc"?"arrow_upward":"arrow_downward"):"unfold_more"}</span></button></th>
                {canEdit && <th className="actions-col">จัดการ</th>}
              </tr>
            </thead>
            <tbody>
              {pagedRows.map((x) => (
                <tr key={x.testCaseId} className={tcSelected.has(x.testCaseId)?"is-selected":""}>
                  <td className="tc-select-col" data-label="เลือก"><input type="checkbox" aria-label={`เลือก ${x.testCaseCode}`} checked={tcSelected.has(x.testCaseId)} disabled={!canEdit} onChange={()=>toggleTcSelect(x.testCaseId)}/></td>
                  <td data-label="Test Case ID">
                    <button className="link-button" onClick={()=>openDetail(x)}>{x.testCaseCode}</button>
                  </td>
                  <td data-label="Title">{x.title}</td>
                  <td data-label="Priority">
                    <Badge
                      tone={
                        x.priority === "P0" || x.priority === "P1"
                          ? "red"
                          : "blue"
                      }
                    >
                      {x.priority}
                    </Badge>
                  </td>
                  <td data-label="Type">{x.testType ?? "-"}</td>
                  <td data-label="Revision">Rev. {x.revisionNo}</td>
                  <td data-label="Steps">{(x as any).steps?.length ?? (x as any).stepCount ?? 0}</td>
                  <td data-label="Status">
                    <Badge tone={x.status === "Ready" ? "green" : "yellow"}>
                      {x.status}
                    </Badge>
                  </td>
                  <td data-label="สร้างเมื่อ">{fmtDateTimeBE(x.createdAt)}</td>
                  {canEdit && (
                    <td data-label="จัดการ" className="actions-col">
                      <div className="row-actions">
                        <button
                          className="table-action icon-only"
                          title="แก้ไข"
                          aria-label={`แก้ไข ${x.testCaseCode}`}
                          onClick={() => openForm(x)}
                        >
                          <span className="material-symbols-outlined" aria-hidden="true">edit</span>
                        </button>
                        <button className="table-action icon-only" title="สำเนา" aria-label={`สำเนา ${x.testCaseCode}`} onClick={() => cloneCase(x)}><span aria-hidden="true">⧉</span></button>
                        <button
                          className="table-action danger-action icon-only"
                          title="ลบ"
                          aria-label={`ลบ ${x.testCaseCode}`}
                          onClick={() => setConfirmDelete(x)}
                        >
                          <span className="material-symbols-outlined" aria-hidden="true">close</span>
                        </button>
                      </div>
                    </td>
                  )}
                </tr>
              ))}
              {!pagedRows.length && !loading && <tr><td colSpan={canEdit ? 10 : 9}><div className="empty"><p>ไม่พบ Test Case ตามตัวกรองที่เลือก</p></div></td></tr>}
            </tbody>
          </table>
        </div>
        <div className="pagination suite-pagination"><label>แสดง<select value={pageSize} onChange={e=>{setPageSize(Number(e.target.value));setPage(1)}}><option value="30">30</option><option value="50">50</option><option value="100">100</option><option value="150">150</option></select> รายการ</label><span>หน้า {Math.min(page,pageCount)} / {pageCount} · ทั้งหมด {totalCount.toLocaleString()} รายการ</span><button className="btn" disabled={page<=1} onClick={()=>setPage(x=>x-1)}><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span> ก่อนหน้า</button><button className="btn" disabled={page>=pageCount} onClick={()=>setPage(x=>x+1)}>ถัดไป <span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button></div>
      </article>
      {testCaseAiModal&&<ModalShell labelledBy="testcase-ai-title" dirty={caseAiDrafts.length > 0} className="requirement-ai-modal" boxStyle={{position:"relative"}} onDismiss={() => {if(!testCaseAiGenerating)setTestCaseAiModal(false)}}>{testCaseAiGenerating&&<div className="ai-loading-overlay"><div className="ai-spinner"/>{caseAiDrafts.length?<p>กำลังบันทึก Test Cases...</p>:<p>AI กำลังออกแบบ Test Case...</p>}<small>{caseAiDrafts.length?"กรุณารอสักครู่ อย่าปิดหน้าต่างนี้":"รอสักครู่ ระบบกำลังสร้าง Test Steps และ Expected Results"}</small></div>}
        <div className="modal-head"><div><h2 id="testcase-ai-title">AI Generate Test Case</h2><small>{caseAiDrafts.length?`พบ ${caseAiDrafts.length} Test Cases ที่ AI สร้าง — ตรวจสอบและบันทึก`:"สร้าง Draft พร้อม Test Steps จากคำอธิบายและไฟล์อ้างอิง"}</small></div><button aria-label="ปิดหน้าต่าง AI Generate" disabled={testCaseAiGenerating} onClick={()=>setTestCaseAiModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
        {caseAiDrafts.length===0?(
        <section className="requirement-ai-panel">
          <div className="requirement-ai-head"><div><span className="ai-spark">AI</span><p><strong>ผู้ช่วยออกแบบ Test Case</strong><small>AI จะสร้าง Test Case หลายชุดจากคำอธิบายเดียว</small></p></div><span className="ai-review-badge">ต้องตรวจสอบก่อนบันทึก</span></div>
          {testCaseAiError&&<div className="inline-alert error"><span>{testCaseAiError}</span></div>}
          <div className="form-grid">
            <label>Project<select value={projectId} disabled={testCaseAiGenerating} onChange={e=>{setProjectId(e.target.value);setModuleId("")}}><option value="">เลือก Project</option>{projects.map(x=><option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select></label>
            <label>Module<select value={moduleId} disabled={testCaseAiGenerating||!projectId} onChange={e=>setModuleId(e.target.value)}><option value="">เลือก Module</option>{renderModuleSelectOptions(modules.filter(x=>x.isActive))}</select></label>
            <label className="full">อธิบายสิ่งที่ต้องการทดสอบ<textarea rows={5} value={testCaseAiPrompt} disabled={testCaseAiGenerating} onChange={e=>setTestCaseAiPrompt(e.target.value)} placeholder="เช่น ตรวจสอบ Dashboard หลัง Login โดยครอบคลุม KPI, กราฟ และกรณีไม่มีข้อมูล"/><small>{testCaseAiPrompt.length} ตัวอักษร</small></label>
          </div>
          <div className="ai-attachments"><div><strong>ไฟล์อ้างอิง (ไม่บังคับ)</strong><small>รองรับ PDF, Word, Excel, CSV, Text และรูปภาพ สูงสุด 5 ไฟล์ รวมไม่เกิน 20 MB</small></div><label className="ai-file-picker">+ เลือกไฟล์<input type="file" multiple accept=".pdf,.txt,.md,.csv,.docx,.xlsx,.png,.jpg,.jpeg,.webp" disabled={testCaseAiGenerating||testCaseAiFiles.length>=5} onChange={e=>{addTestCaseAiFiles(Array.from(e.target.files??[]));e.target.value=""}}/></label>{testCaseAiFiles.length>0&&<div className="ai-file-list">{testCaseAiFiles.map((file,index)=><div key={`${file.name}-${index}`}><span aria-hidden="true">▧</span><p><b>{file.name}</b><small>{(file.size/1024/1024).toFixed(2)} MB</small></p><button aria-label={`ลบไฟล์ ${file.name}`} disabled={testCaseAiGenerating} onClick={()=>setTestCaseAiFiles(current=>current.filter((_,i)=>i!==index))}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>)}</div>}</div>
          <div className="ai-draft-note"><span className="material-symbols-outlined" aria-hidden="true">info</span><p><strong>AI จะยังไม่บันทึกข้อมูล</strong><small>AI จะสร้าง Test Case หลายชุดให้ตรวจสอบก่อนบันทึก ผลลัพธ์ยังไม่ถูกบันทึกลงระบบ</small></p></div>
          <div className="requirement-ai-actions"><small>ไฟล์ใช้วิเคราะห์เฉพาะคำขอนี้และไม่บันทึกลงระบบ</small><div className="row-actions"><button className="btn" disabled={testCaseAiGenerating} onClick={()=>setTestCaseAiModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={testCaseAiGenerating||!projectId||!moduleId||!testCaseAiPrompt.trim()} onClick={generateTestCaseWithAi}>{testCaseAiGenerating?"AI กำลังวิเคราะห์...":"✦ สร้าง Test Cases"}</button></div></div>
        </section>
        ):(
        <section className="requirement-ai-panel case-ai-review">
          <div className="case-ai-review-head"><div><h3>Test Cases ที่ AI สร้าง ({caseAiDrafts.length})</h3></div></div>
          {testCaseAiError&&<div className="inline-alert error" style={{marginBottom:8}}><span>{testCaseAiError}</span></div>}
          <div className="case-ai-draft-list">{caseAiDrafts.map((draft,index)=>{const isExpanded=caseAiExpanded===index;return<div key={index} className={`case-ai-draft-card${isExpanded?" expanded":""}`}><div className="case-ai-draft-head" onClick={()=>setCaseAiExpanded(isExpanded?undefined:index)}><div className="case-ai-draft-title"><b>{draft.title}</b><div className="case-ai-draft-tags"><Badge tone={draft.priority==="P0"||draft.priority==="P1"?"red":"blue"}>{draft.priority}</Badge><Badge tone="yellow">{draft.testType}</Badge>{draft.automationCandidate&&<Badge tone="green">Auto</Badge>}<span className="case-ai-step-count">{draft.steps.length} Steps</span></div></div><span className="case-ai-expand-icon">{isExpanded?"▾":"▸"}</span></div>{isExpanded&&<div className="case-ai-draft-body"><p className="case-ai-draft-desc"><strong>Objective:</strong> {draft.objective}</p>{draft.preconditions&&<p className="case-ai-draft-desc"><strong>Preconditions:</strong> {draft.preconditions}</p>}<div className="case-ai-steps-list">{draft.steps.map(step=><div key={step.stepNo}><b>{step.stepNo}</b><span><strong>{step.action}</strong>{step.testData&&<small>Test Data: {step.testData}</small>}<small>Expected: {step.expectedResult}</small></span></div>)}</div><button className="table-action danger-action" style={{marginTop:8}} onClick={()=>removeCaseAiDraft(index)}><span className="material-symbols-outlined" aria-hidden="true">close</span> นำ Test Case นี้ออก</button></div>}</div>})}</div>
          <div className="requirement-ai-actions"><small>{caseAiDrafts.length} Test Cases พร้อมบันทึก</small><div className="row-actions"><button className="btn" disabled={testCaseAiGenerating} onClick={()=>setCaseAiDrafts([])}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> สร้างใหม่</button><button className="btn primary" disabled={testCaseAiGenerating||!caseAiDrafts.length} onClick={saveAllCaseDrafts}>{testCaseAiGenerating?"กำลังบันทึก...":`✦ บันทึกทั้งหมด (${caseAiDrafts.length} Cases)`}</button></div></div>
        </section>
        )}
      </ModalShell>}
      {form && (
        // ไม่ปิดเมื่อคลิกพื้นหลัง — ฟอร์มนี้มี step editor ข้อมูลที่กรอกจะหายทั้งหมด
        <ModalShell labelledBy="testcase-form-title" className="testcase-modal" onDismiss={() => setForm(false)}>
            <div className="modal-head">
              <h2 id="testcase-form-title">{editing ? "แก้ไข" : "เพิ่ม"} Test Case</h2>
              <button aria-label="ปิดฟอร์ม Test Case" onClick={() => setForm(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            <div className="form-grid">
              <label className="tc-span-2">
                Project
                <select
                  disabled
                  value={projectId}
                  onChange={(e) => setProjectId(e.target.value)}
                >
                  {projects.map((x) => (
                    <option key={x.projectId} value={x.projectId}>
                      {x.projectName}
                    </option>
                  ))}
                </select>
              </label>
              <label className="tc-span-2">
                Module
                <select
                  value={moduleId}
                  onChange={(e) => setModuleId(e.target.value)}
                >
                  {renderModuleSelectOptions(modules.filter((x) => x.isActive))}
                </select>
              </label>
              <label className="tc-span-2">
                Test Case Code
                <input
                  disabled
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </label>
              <label className="tc-span-2">
                Title
                <input
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                />
              </label>
              <label className="tc-compact-field">
                Priority
                <select
                  value={priority}
                  onChange={(e) => setPriority(e.target.value)}
                >
                  {masterOptionElements(testCasePriorities, priority)}
                </select>
              </label>
              <label className="tc-compact-field">
                Type
                <select
                  value={testType}
                  onChange={(e) => setTestType(e.target.value)}
                >
                  {masterOptionElements(testCaseTypes, testType)}
                </select>
              </label>
              {editing && (
                <label className="tc-compact-field">
                  สถานะ
                  <select
                    value={status}
                    onChange={(e) => setStatus(e.target.value)}
                  >
                    <option>Draft</option>
                    <option>Review</option>
                    <option>Ready</option>
                    <option>Deprecated</option>
                  </select>
                </label>
              )}
              <label className="check-line tc-automation">
                <input
                  type="checkbox"
                  checked={automation}
                  onChange={(e) => setAutomation(e.target.checked)}
                />{" "}
                Automation Candidate
              </label>
              <label className="tc-span-2">Owner<select value={ownerUserId} onChange={e=>setOwnerUserId(e.target.value)}><option value="">ไม่ระบุผู้รับผิดชอบ</option>{users.map(x=><option key={x.userId} value={x.userId}>{x.displayName}</option>)}</select></label>
              <div className="status-information tc-span-2"><strong>ข้อมูลสถานะ</strong><span>{testCaseStatusInfo.find(x=>x.value===status)?.label}</span><small>{testCaseStatusInfo.find(x=>x.value===status)?.detail}</small><small><b>ผลกระทบ:</b> {testCaseStatusInfo.find(x=>x.value===status)?.impact}</small></div>
              <label className="tc-span-2">
                Objective
                <textarea
                  rows={2}
                  value={objective}
                  onChange={(e) => setObjective(e.target.value)}
                />
              </label>
              <label className="tc-span-2">
                Preconditions
                <textarea
                  rows={2}
                  value={preconditions}
                  onChange={(e) => setPreconditions(e.target.value)}
                />
              </label>
              {editing && (
                <label className="full tc-span-4">
                  <span>
                    เหตุผลที่แก้ไข <b className="required-mark">*</b>
                  </span>
                  <input
                    value={changeReason}
                    onChange={(e) => setChangeReason(e.target.value)}
                    placeholder="ระบุเหตุผลเพื่อสร้าง Revision ใหม่"
                  />
                  {!changeReason.trim() && (
                    <small className="field-error">
                      กรุณาระบุเหตุผลก่อนบันทึก เพื่อสร้าง Revision ใหม่
                    </small>
                  )}
                </label>
              )}
            </div>
            <div className="testcase-steps">
              <div className="card-title">
                <div>
                  <h3>Test Steps</h3>
                  <p>{steps.length} ขั้นตอน</p>
                </div>
                <button
                  className="btn"
                  onClick={() =>
                    setSteps((current) => [
                      ...current,
                      {
                        stepNo: current.length + 1,
                        action: "",
                        testData: "",
                        expectedResult: "",
                      },
                    ])
                  }
                >
                  + Step
                </button>
              </div>
              <div className="testcase-step-head" aria-hidden="true">
                <span>#</span>
                <span>Action</span>
                <span>Test Data</span>
                <span>Expected Result</span>
                <span>จัดการ</span>
              </div>
              {steps.map((step, index) => (
                <div className="testcase-step" key={index}>
                  <b>{index + 1}</b>
                  <input
                    placeholder="Action"
                    value={step.action}
                    onChange={(e) =>
                      updateStep(index, "action", e.target.value)
                    }
                  />
                  <input
                    placeholder="Test Data"
                    value={step.testData ?? ""}
                    onChange={(e) =>
                      updateStep(index, "testData", e.target.value)
                    }
                  />
                  <input
                    placeholder="Expected Result"
                    value={step.expectedResult}
                    onChange={(e) =>
                      updateStep(index, "expectedResult", e.target.value)
                    }
                  />
                  {steps.length > 1 && (
                    <button
                      className="table-action danger-action"
                      onClick={() =>
                        setSteps((current) =>
                          current
                            .filter((_, i) => i !== index)
                            .map((x, i) => ({ ...x, stepNo: i + 1 })),
                        )
                      }
                    >
                      ลบ
                    </button>
                  )}
                </div>
              ))}
            </div>
            {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
            <div className="modal-actions">
              <button className="btn" onClick={() => setForm(false)}>
                ยกเลิก
              </button>
              <button
                className="btn primary"
                disabled={
                  saving ||
                  !projectId ||
                  !moduleId ||
                  !code.trim() ||
                  !title.trim() ||
                  (editing && !changeReason.trim()) ||
                  steps.some(
                    (x) => !x.action.trim() || !x.expectedResult.trim(),
                  )
                }
                onClick={save}
              >
                {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}
              </button>
            </div>
          </ModalShell>
      )}
 {detail && (() => {
        const ownerName = users.find(x=>x.userId===detail.ownerUserId)?.displayName || "ไม่ระบุ";
        const moduleCode = modules.find(x=>x.moduleId===detail.moduleId)?.moduleCode || "-";
        return (
        <ModalShell className="testcase-detail" onDismiss={() => setDetail(null)}>
            <div className="modal-head">
              <div><h2>{detail.testCaseCode}</h2><small>{modules.find(x=>x.moduleId===detail.moduleId)?.moduleName||"-"} · {projects.find(x=>x.projectId===detail.projectId)?.projectName||""}</small></div>
              <button aria-label="ปิดรายละเอียด Test Case" onClick={()=>setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            <section className="cycle-detail-hero">
              <div className="suite-detail-hero-text">
                <span className="suite-detail-hero-icon" aria-hidden="true">▤</span>
                <div><h3>{detail.title}</h3></div>
              </div>
              <div className="cycle-detail-badges">
                <Badge tone={detail.priority==="P0"||detail.priority==="P1"?"red":"blue"}>{detail.priority}</Badge>
                <Badge tone={detail.status==="Ready"?"green":detail.status==="Deprecated"?"yellow":"blue"}>{detail.status}</Badge>
                {detail.testType && <Badge tone="yellow">{detail.testType}</Badge>}
                <Badge tone={detail.automationCandidate?"green":"gray"}>{detail.automationCandidate?"Automation Candidate":"Manual"}</Badge>
              </div>
            </section>
            <div className="suite-info-cards">
              <div className="suite-info-card"><span className="suite-info-card-label"><span aria-hidden="true">U</span> Owner</span><b>{ownerName}</b></div>
              <div className="suite-info-card"><span className="suite-info-card-label"><span aria-hidden="true">D</span> Revision</span><b>Rev. {detail.revisionNo}</b></div>
              <div className="suite-info-card"><span className="suite-info-card-label"><span aria-hidden="true">M</span> Module</span><b>{moduleCode}</b></div>
            </div>
            <div className="defect-detail-split">
              <section className="cycle-detail-section">
                <h3><span className="material-symbols-outlined" aria-hidden="true">search_off</span> Objective</h3>
                <p className="defect-detail-text">{detail.objective||"ไม่ระบุวัตถุประสงค์"}</p>
              </section>
              <section className="cycle-detail-section">
                <h3><span className="material-symbols-outlined" aria-hidden="true">description</span> Preconditions</h3>
                <p className="defect-detail-text">{detail.preconditions||"ไม่มีเงื่อนไขก่อนเริ่ม"}</p>
              </section>
            </div>
            <section className="cycle-detail-section">
              <h3><span className="material-symbols-outlined" aria-hidden="true">description</span> Test Steps ({detail.steps.length})</h3>
              <div className="tc-detail-steps-v2">
                {detail.steps.map((x,i)=>(
                  <div className="tc-detail-step-row" key={x.stepNo}>
                    <div className="tc-detail-step-timeline">
                      <span className="tc-detail-step-dot">{x.stepNo}</span>
                      {i<detail.steps.length-1 && <i className="tc-detail-step-line" />}
                    </div>
                    <div className="tc-detail-step-card">
                      <div className="tc-detail-step-col"><span className="tc-detail-step-col-label"><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span> Action</span><b>{x.action}</b></div>
                      <div className="tc-detail-step-col"><span className="tc-detail-step-col-label"><span aria-hidden="true">●</span> Test Data</span>{x.testData ? <span className="tc-detail-step-pill amber">{x.testData}</span> : <span className="muted-text">-</span>}</div>
                      <div className="tc-detail-step-col"><span className="tc-detail-step-col-label"><span className="material-symbols-outlined" aria-hidden="true">check</span> Expected Result</span><span className="tc-detail-step-pill green">{x.expectedResult}</span></div>
                    </div>
                  </div>
                ))}
              </div>
            </section>
            <section className="cycle-detail-section">
              <h3>Requirements ที่เชื่อมโยง ({detailRequirements.length})</h3>
              {detailRequirements.length ? (
                <div className="defect-linked-cases">
                  {detailRequirements.map(x=>(
                    <div key={x.requirementId} className="defect-linked-case">
                      <div><b>{x.requirementCode}</b><small>{x.title}{x.coverageType ? ` · Coverage: ${x.coverageType}` : ""}</small></div>
                      <Badge tone={x.status==="Approved"?"green":x.status==="Draft"?"yellow":"blue"}>{x.status}</Badge>
                    </div>
                  ))}
                </div>
              ) : <p className="muted-text">ยังไม่มี Requirement ที่เชื่อมโยง — สามารถเชื่อมได้จากหน้า Requirement</p>}
            </section>
            {!!revisions.length && (
              <section className="cycle-detail-section">
                <h3>Revision History ({revisions.length})</h3>
                <div className="defect-activity-list">
                  {revisions.map(x=>(
                    <div key={x.revisionNo} className="defect-activity-row">
                      <Badge tone="blue">Rev. {x.revisionNo}</Badge>
                      <div><p>{x.changeReason||"-"}</p><small>{x.changedByName||"ไม่ระบุผู้แก้ไข"} · {formatThaiDateTime(x.changedAt)}</small></div>
                    </div>
                  ))}
                </div>
              </section>
            )}
            <div className="modal-actions">
              <button className="btn" onClick={()=>setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button>
              {canEdit && <button className="btn primary" onClick={()=>{const item=detail;setDetail(null);openForm(item);}}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button>}
            </div>
          </ModalShell>
        );
      })()}       {confirmDelete&&<ModalShell className="confirm-box" onDismiss={() => setConfirmDelete(null)}><div className="modal-head"><h2>ยืนยันการลบ Test Case</h2><button onClick={()=>setConfirmDelete(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div><p>ต้องการลบ <b>{confirmDelete.testCaseCode}</b> ใช่หรือไม่? ข้อมูลประวัติจะยังคงอยู่ในระบบ</p><div className="modal-actions"><button className="btn" onClick={()=>setConfirmDelete(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn danger" onClick={()=>remove(confirmDelete)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยืนยันลบ</button></div></ModalShell>}      {confirmBulkDelete&&<ModalShell className="confirm-box" onDismiss={() => {if(tcSaving!=="bulk-delete")setConfirmBulkDelete(false)}}><div className="modal-head"><h2>ยืนยันการลบ Test Case ที่เลือก</h2><button disabled={tcSaving==="bulk-delete"} onClick={()=>setConfirmBulkDelete(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div><p>ต้องการลบ <b>{tcSelected.size}</b> Test Case ที่เลือกใช่หรือไม่? ข้อมูลประวัติจะยังคงอยู่ในระบบ</p><div className="modal-actions"><button className="btn" disabled={tcSaving==="bulk-delete"} onClick={()=>setConfirmBulkDelete(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn danger" disabled={tcSaving==="bulk-delete"} onClick={removeBulkSelected}>{tcSaving==="bulk-delete"?<><span className="spinner inline" aria-hidden="true" /> กำลังลบ...</>:<><span className="material-symbols-outlined" aria-hidden="true">close</span> ยืนยันลบ</>}</button></div></ModalShell>}
    </>
  );
}
