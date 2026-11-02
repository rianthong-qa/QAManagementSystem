import { useState, useMemo, useEffect, useRef, type ReactNode } from "react";
import type { ProjectItem } from "../shared/types";
import { apiUrl } from "../api";
import { notify, confirmDialog } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import type { MasterOption } from "../shared/appShared";
import "./MasterSettingsPage.css";
import { CrmMirrorIntervalEditor } from "../components/CrmMirrorIntervalEditor";

const masterSettingGroups = [
  ["ReleaseType", "Release", "Release Type"],
  ["TestCasePriority", "Test Case", "Priority"],
  ["TestCaseType", "Test Case", "Type"],
  ["TestSuiteType", "Test Suite", "Type"],
  ["TestSuiteRiskTier", "Test Suite", "Risk Tier"],
  ["TestCycleType", "Test Cycle", "Cycle Type"],
  ["DefectRootCause", "Defect", "หมวดสาเหตุ (Root Cause)"],
] as const;
const masterSettingSections = [
  { name: "Release", icon: "rocket_launch", description: "ค่าที่ใช้ในหน้าจัดการ Release", groups: masterSettingGroups.filter((x) => x[1] === "Release") },
  { name: "Test Case", icon: "fact_check", description: "ค่าที่ใช้ในการออกแบบ Test Case", groups: masterSettingGroups.filter((x) => x[1] === "Test Case") },
  { name: "Test Suite", icon: "inventory_2", description: "ค่าที่ใช้จัดกลุ่ม Test Case", groups: masterSettingGroups.filter((x) => x[1] === "Test Suite") },
  { name: "Test Cycle", icon: "event_repeat", description: "ค่าที่ใช้ในการวางแผนรอบทดสอบ", groups: masterSettingGroups.filter((x) => x[1] === "Test Cycle") },
  { name: "Defect", icon: "bug_report", description: "หมวดสาเหตุที่ต้องเลือกตอนปิด Defect แบบแก้ไขแล้ว — ใช้วิเคราะห์และเป็นความรู้ให้ผู้ช่วย AI", groups: masterSettingGroups.filter((x) => x[1] === "Defect") },
];
// หมวดที่เปิดล่าสุดจำไว้ต่อเครื่อง (สะดวกเฉย ๆ — อ่าน/เขียนไม่ได้ก็เริ่มที่ AI)
const sectionStorageKey = "qa.settingsSection";
const readSavedSection = () => { try { return localStorage.getItem(sectionStorageKey) || "AI"; } catch { return "AI"; } };
type EnvironmentSetting = { testEnvironmentId: string; projectId: string; environmentName: string; baseUrl?: string; isActive: boolean };
type EmbeddingProvider = "None" | "OpenAI" | "OpenRouter" | "Local";
type AiProvider = "OpenAI" | "Google" | "Anthropic" | "OpenRouter" | "Local" | "opencode";
type AiConfiguration = { provider: AiProvider; model: string; baseUrl?: string; isEnabled: boolean; hasApiKey: boolean; apiKeyHint?: string; updatedAt?: string; embeddingProvider?: EmbeddingProvider; embeddingModel?: string | null; embeddingBaseUrl?: string | null; hasEmbeddingApiKey?: boolean; embeddingApiKeyHint?: string | null; allowCrmDataToExternalAi?: boolean; mcpEnabled?: boolean };
// คลังความรู้ (AI Chat) — Embedding แยกจาก Chat เพราะ Anthropic/Google ไม่มี embedding API ในระบบนี้
type KnowledgeSettings = { embeddingProvider: EmbeddingProvider; embeddingModel: string; embeddingBaseUrl: string; hasEmbeddingApiKey: boolean; embeddingApiKeyHint?: string | null; allowCrmDataToExternalAi: boolean; mcpEnabled: boolean };
const embeddingProviderModels: Record<EmbeddingProvider, string[]> = { None: [], OpenAI: ["text-embedding-3-small", "text-embedding-3-large"], OpenRouter: ["qwen/qwen3-embedding-8b", "baai/bge-m3", "google/gemini-embedding-001", "openai/text-embedding-3-small"], Local: ["bge-m3", "nomic-embed-text", "mxbai-embed-large"] };
const toKnowledgeSettings = (ai: AiConfiguration): KnowledgeSettings => ({ embeddingProvider: ai.embeddingProvider ?? "None", embeddingModel: ai.embeddingModel ?? "", embeddingBaseUrl: ai.embeddingBaseUrl ?? "", hasEmbeddingApiKey: !!ai.hasEmbeddingApiKey, embeddingApiKeyHint: ai.embeddingApiKeyHint, allowCrmDataToExternalAi: !!ai.allowCrmDataToExternalAi, mcpEnabled: !!ai.mcpEnabled });
// การ์ดเลือก Provider ของ AI หลัก — keyRequired ต้องตรงกับ SharedAiConfigurationService.GetRuntimeAsync (Local/opencode ไม่บังคับคีย์)
// models = ตัวเลือกด่วนใต้ช่อง Model (ตัวแรกเป็นค่าเริ่มต้นเมื่อเปลี่ยน Provider); รายการเต็มกด "โหลดรายการจาก Provider"
const aiProviders: { id: AiProvider; name: string; hint: string; keyRequired: boolean; baseUrl?: string; keyUrl?: string; models: string[] }[] = [
  { id: "Anthropic", name: "Anthropic Claude", hint: "Claude โดยตรง · ต้องมี API key", keyRequired: true, keyUrl: "https://console.anthropic.com/settings/keys", models: ["claude-sonnet-5-5", "claude-opus-5-5", "claude-haiku-5-5"] },
  { id: "OpenAI", name: "OpenAI", hint: "GPT · ต้องมี API key", keyRequired: true, keyUrl: "https://platform.openai.com/api-keys", models: ["gpt-5-mini", "gpt-5.4"] },
  { id: "Google", name: "Google Gemini", hint: "Gemini · ต้องมี API key", keyRequired: true, keyUrl: "https://aistudio.google.com/apikey", models: ["gemini-3.5-flash", "gemini-3.1-pro"] },
  { id: "OpenRouter", name: "OpenRouter", hint: "หลายค่ายในคีย์เดียว · ต้องมี API key", keyRequired: true, baseUrl: "https://openrouter.ai/api/v1", keyUrl: "https://openrouter.ai/keys", models: ["anthropic/claude-sonnet-4", "openai/gpt-4o", "google/gemini-2.5-flash", "meta-llama/llama-4-maverick", "nvidia/nemotron-3.5-lightning:free"] },
  { id: "opencode", name: "opencode", hint: "Gateway opencode · คีย์ไม่บังคับ", keyRequired: false, baseUrl: "https://opencode.ai/zen/go/v1", models: ["gpt-5-mini", "gpt-5", "claude-sonnet-4", "gemini-2.5-pro", "deepseek-v4-flash", "mimo-v2.5"] },
  { id: "Local", name: "AI Local", hint: "Ollama / LM Studio ในองค์กร · ไม่ต้องใช้คีย์", keyRequired: false, baseUrl: "http://localhost:11434/v1", models: ["qwen3", "llama3.3", "mistral-small"] },
];
const aiProviderInfo = (id: AiProvider) => aiProviders.find((x) => x.id === id) ?? aiProviders[0];
type AiModelOption = { id: string; displayName: string };
type CrmSyncSettingsView = { pollIntervalMinutes: number; mirrorIntervalMinutes?: number; updatedAt?: string | null };
type CrmProjectMapping = { crmProjectMappingId: string; projectId: string; crmProductId: string; crmVersionId?: string | null };
type EmailConfig = { smtpHost: string; smtpPort: number; senderEmail: string; senderDisplayName?: string | null; hasPassword: boolean; passwordHint?: string | null; isEnabled: boolean; updatedAt?: string | null };
export function MasterSettingsPage() {
  const [items, setItems] = useState<MasterOption[]>([]), [environments, setEnvironments] = useState<EnvironmentSetting[]>([]), [projects, setProjects] = useState<ProjectItem[]>([]), [reload, setReload] = useState(0);
  const [category, setCategory] = useState("ReleaseType"), [formCategory, setFormCategory] = useState<string | null>(null), [value, setValue] = useState(""), [displayName, setDisplayName] = useState(""), [sortOrder, setSortOrder] = useState(10), [editing, setEditing] = useState<MasterOption | null>(null);
  const [environment, setEnvironment] = useState<EnvironmentSetting | null>(null), [environmentFormOpen, setEnvironmentFormOpen] = useState(false), [environmentProjectId, setEnvironmentProjectId] = useState(""), [environmentName, setEnvironmentName] = useState(""), [baseUrl, setBaseUrl] = useState("");
  const [aiConfiguration, setAiConfiguration] = useState<AiConfiguration>({ provider: "OpenAI", model: "gpt-5-mini", isEnabled: true, hasApiKey: false }), [aiApiKey, setAiApiKey] = useState(""), [savingAi, setSavingAi] = useState(false);
  const [knowledge, setKnowledge] = useState<KnowledgeSettings>({ embeddingProvider: "None", embeddingModel: "", embeddingBaseUrl: "", hasEmbeddingApiKey: false, allowCrmDataToExternalAi: false, mcpEnabled: false }), [embeddingApiKey, setEmbeddingApiKey] = useState(""), [savingKnowledge, setSavingKnowledge] = useState(false);
  const [testingEmbedding, setTestingEmbedding] = useState(false), [embeddingTest, setEmbeddingTest] = useState<{ ok: boolean; message: string } | null>(null);
  const [savedAi, setSavedAi] = useState<AiConfiguration | null>(null), [testingAi, setTestingAi] = useState(false), [aiTest, setAiTest] = useState<{ ok: boolean; message: string } | null>(null);
  const [aiModels, setAiModels] = useState<AiModelOption[]>([]), [loadingAiModels, setLoadingAiModels] = useState(false), [aiModelsError, setAiModelsError] = useState("");
  const [crmSyncSettings, setCrmSyncSettings] = useState<CrmSyncSettingsView>({ pollIntervalMinutes: 2 }), [savingCrmSync, setSavingCrmSync] = useState(false);
  const [crmMappings, setCrmMappings] = useState<CrmProjectMapping[]>([]);
  const [crmMappingEditing, setCrmMappingEditing] = useState<CrmProjectMapping | null>(null), [crmMappingFormOpen, setCrmMappingFormOpen] = useState(false), [crmMappingProjectId, setCrmMappingProjectId] = useState(""), [crmMappingProductId, setCrmMappingProductId] = useState(""), [crmMappingVersionId, setCrmMappingVersionId] = useState("");
  const [emailConfig, setEmailConfig] = useState<EmailConfig>({ smtpHost: "smtp.gmail.com", smtpPort: 587, senderEmail: "", hasPassword: false, isEnabled: true }), [emailPassword, setEmailPassword] = useState(""), [savingEmail, setSavingEmail] = useState(false);
  const [emailTestTo, setEmailTestTo] = useState(""), [sendingTestEmail, setSendingTestEmail] = useState(false), [testEmailResult, setTestEmailResult] = useState<{ ok: boolean; message: string } | null>(null);
  const [activeSection, setActiveSection] = useState(readSavedSection);
  const [loaded, setLoaded] = useState(false), [loadError, setLoadError] = useState("");
  const headers = useMemo(() => ({ "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }), []);
  useEffect(() => {
    const load = async () => {
      const read = async (url: string) => { const response = await fetch(url, { headers }); if (!response.ok) throw new Error(`โหลดข้อมูลไม่สำเร็จ (${response.status})`); return response.json(); };
      try { const [masterData, environmentData, projectData, aiData, crmSyncData, crmMappingData, emailData] = await Promise.all([read(`${apiUrl}/master-settings?includeInactive=true`), read(`${apiUrl}/master-settings/environments`), read(`${apiUrl}/projects`), read(`${apiUrl}/master-settings/ai`), read(`${apiUrl}/master-settings/crm-sync`), read(`${apiUrl}/master-settings/crm-mappings`), read(`${apiUrl}/master-settings/email`)]); setItems(masterData); setEnvironments(environmentData); setProjects(projectData); setAiConfiguration(aiData); setSavedAi(aiData); setKnowledge(toKnowledgeSettings(aiData)); setCrmSyncSettings(crmSyncData); setCrmMappings(crmMappingData); setEmailConfig(emailData); setEnvironmentProjectId((x) => x || projectData[0]?.projectId || ""); setLoadError(""); setLoaded(true); }
      // ห้ามแสดงรายการว่าง/0 แทนข้อผิดพลาด — แสดงแถบ error + ลองใหม่ และคงข้อมูลชุดก่อนไว้ถ้ามี
      catch (error) { setLoadError(error instanceof Error ? `${error.message} กรุณาตรวจสอบว่า API ใช้งานเวอร์ชันล่าสุด` : "โหลดการตั้งค่ากลางไม่สำเร็จ"); }
    };
    load();
  }, [reload, headers]);
  const selectSection = (id: string) => { setActiveSection(id); try { localStorage.setItem(sectionStorageKey, id); } catch { /* ไม่จำก็ได้ */ } };
  const resetOption = () => { setEditing(null); setFormCategory(null); setValue(""); setDisplayName(""); setSortOrder(10); };
  const saveOption = async () => {
    const response = await fetch(`${apiUrl}/master-settings${editing ? `/${editing.masterOptionId}` : ""}`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify({ category: editing?.category ?? category, value, displayName, sortOrder, isActive: editing?.isActive ?? true }) });
    if (!response.ok) { const p = await response.json(); notify(p.detail ?? "บันทึกข้อมูลไม่สำเร็จ", "error"); return; } resetOption(); setReload((x) => x + 1);
  };
  const toggleOption = async (item: MasterOption) => { await fetch(`${apiUrl}/master-settings/${item.masterOptionId}`, { method: "PUT", headers, body: JSON.stringify({ ...item, isActive: !item.isActive }) }); setReload((x) => x + 1); };
  const deleteOption = async (item: MasterOption) => { if (!await confirmDialog(`ยืนยันลบ ${item.displayName}?`)) return; const response = await fetch(`${apiUrl}/master-settings/${item.masterOptionId}`, { method: "DELETE", headers }); if (!response.ok) { const p = await response.json(); notify(p.detail ?? "ลบข้อมูลไม่สำเร็จ", "error"); return; } if (editing?.masterOptionId === item.masterOptionId) resetOption(); setReload((x) => x + 1); };
  const editEnvironment = (item?: EnvironmentSetting) => { setEnvironment(item ?? null); setEnvironmentFormOpen(true); setEnvironmentProjectId(item?.projectId ?? projects[0]?.projectId ?? ""); setEnvironmentName(item?.environmentName ?? ""); setBaseUrl(item?.baseUrl ?? ""); };
  const resetEnvironment = () => { setEnvironment(null); setEnvironmentFormOpen(false); setEnvironmentName(""); setBaseUrl(""); };
  const saveEnvironment = async () => { const response = await fetch(`${apiUrl}/master-settings/environments${environment ? `/${environment.testEnvironmentId}` : ""}`, { method: environment ? "PUT" : "POST", headers, body: JSON.stringify({ projectId: environmentProjectId, environmentName, baseUrl: baseUrl || null, isActive: environment?.isActive ?? true }) }); if (!response.ok) { const p = await response.json(); notify(p.detail ?? "บันทึก Environment ไม่สำเร็จ", "error"); return; } resetEnvironment(); setReload((x) => x + 1); };
  const toggleEnvironment = async (item: EnvironmentSetting) => { await fetch(`${apiUrl}/master-settings/environments/${item.testEnvironmentId}`, { method: "PUT", headers, body: JSON.stringify({ ...item, isActive: !item.isActive }) }); setReload((x) => x + 1); };
  const deleteEnvironment = async (item: EnvironmentSetting) => { if (!await confirmDialog(`ยืนยันลบ Environment ${item.environmentName}?`)) return; const response = await fetch(`${apiUrl}/master-settings/environments/${item.testEnvironmentId}`, { method: "DELETE", headers }); if (!response.ok) { const p = await response.json(); notify(p.detail ?? "ลบ Environment ไม่สำเร็จ", "error"); return; } if (environment?.testEnvironmentId === item.testEnvironmentId) resetEnvironment(); setReload((x) => x + 1); };
  const openOptionForm = (targetCategory: string, item?: MasterOption) => { setCategory(targetCategory); setFormCategory(targetCategory); setEditing(item ?? null); setValue(item?.value ?? ""); setDisplayName(item?.displayName ?? ""); setSortOrder(item?.sortOrder ?? 10); };
  const saveAiConfiguration = async () => { setSavingAi(true); try { const response = await fetch(`${apiUrl}/master-settings/ai`, { method: "PUT", headers, body: JSON.stringify({ provider: aiConfiguration.provider, model: aiConfiguration.model, baseUrl: aiConfiguration.baseUrl || null, apiKey: aiApiKey || null, isEnabled: aiConfiguration.isEnabled, clearApiKey: false }) }); if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? "บันทึกการตั้งค่า AI ไม่สำเร็จ"); } const saved = await response.json() as AiConfiguration; setAiConfiguration(saved); setSavedAi(saved); setAiApiKey(""); setAiTest(null); notify("บันทึกการตั้งค่า AI เรียบร้อยแล้ว", "success"); } catch (error) { notify(error instanceof Error ? error.message : "บันทึกการตั้งค่า AI ไม่สำเร็จ", "error"); } finally { setSavingAi(false); } };
  const saveKnowledge = async () => { setSavingKnowledge(true); try { const response = await fetch(`${apiUrl}/master-settings/ai/knowledge`, { method: "PUT", headers, body: JSON.stringify({ embeddingProvider: knowledge.embeddingProvider, embeddingModel: knowledge.embeddingModel.trim() || null, embeddingBaseUrl: knowledge.embeddingBaseUrl.trim() || null, embeddingApiKey: embeddingApiKey.trim() || null, clearEmbeddingApiKey: false, allowCrmDataToExternalAi: knowledge.allowCrmDataToExternalAi, mcpEnabled: knowledge.mcpEnabled }) }); if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? "บันทึกการตั้งค่าคลังความรู้ไม่สำเร็จ"); } const saved = await response.json() as AiConfiguration; setAiConfiguration((current) => ({ ...saved, provider: current.provider, model: current.model, baseUrl: current.baseUrl, isEnabled: current.isEnabled, hasApiKey: current.hasApiKey, apiKeyHint: current.apiKeyHint })); setSavedAi(saved); setKnowledge(toKnowledgeSettings(saved)); setEmbeddingApiKey(""); notify("บันทึกการตั้งค่าคลังความรู้เรียบร้อยแล้ว", "success"); } catch (error) { notify(error instanceof Error ? error.message : "บันทึกการตั้งค่าคลังความรู้ไม่สำเร็จ", "error"); } finally { setSavingKnowledge(false); } };
  // ทดสอบ Embedding ด้วยค่าที่บันทึกแล้ว (ไม่ใช่ค่าที่กำลังพิมพ์) — เหมือนปุ่มทดสอบอีเมล
  const testEmbedding = async () => { setTestingEmbedding(true); setEmbeddingTest(null); try { const response = await fetch(`${apiUrl}/master-settings/ai/knowledge/test-embedding`, { method: "POST", headers }); if (!response.ok) { const p = await response.json(); throw new Error(p.detail ?? "ทดสอบ Embedding ไม่สำเร็จ"); } const r = await response.json() as { provider: string; model: string; dimensions: number; elapsedMs: number }; setEmbeddingTest({ ok: true, message: `ใช้งานได้ — ${r.provider} ${r.model} ได้เวกเตอร์ ${r.dimensions.toLocaleString("th-TH")} มิติ ใน ${r.elapsedMs.toLocaleString("th-TH")} ms` }); } catch (error) { setEmbeddingTest({ ok: false, message: error instanceof Error ? error.message : "ทดสอบ Embedding ไม่สำเร็จ" }); } finally { setTestingEmbedding(false); } };
  const loadAiModels = async () => { setLoadingAiModels(true); setAiModelsError(""); try { const response = await fetch(`${apiUrl}/master-settings/ai/models`, { method: "POST", headers, body: JSON.stringify({ provider: aiConfiguration.provider, baseUrl: aiConfiguration.baseUrl || null, apiKey: aiApiKey || null }) }); if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? "โหลดรายการ Model ไม่สำเร็จ"); } const models = await response.json() as AiModelOption[]; setAiModels(models); if (!models.length) setAiModelsError("Provider ไม่ส่งรายการ Model กลับมา"); } catch (error) { setAiModelsError(error instanceof Error ? error.message : "โหลดรายการ Model ไม่สำเร็จ"); } finally { setLoadingAiModels(false); } };
  // ทดสอบด้วยค่าในฟอร์ม (ยังไม่ต้องบันทึก) — ต่างจากทดสอบ Embedding/อีเมลที่ใช้ค่าที่บันทึกแล้ว เพื่อให้ลองก่อนเปลี่ยนค่าที่ทุกระบบใช้อยู่
  const testAiConnection = async () => { setTestingAi(true); setAiTest(null); try { const response = await fetch(`${apiUrl}/master-settings/ai/test`, { method: "POST", headers, body: JSON.stringify({ provider: aiConfiguration.provider, model: aiConfiguration.model.trim(), baseUrl: aiConfiguration.baseUrl || null, apiKey: aiApiKey || null }) }); if (!response.ok) { const p = await response.json().catch(() => ({})); throw new Error(p.detail ?? `ทดสอบการเชื่อมต่อไม่สำเร็จ (${response.status})`); } const r = await response.json() as { provider: AiProvider; model: string; elapsedMs: number; reply: string }; setAiTest({ ok: true, message: `เชื่อมต่อได้ — ${aiProviderInfo(r.provider).name} · ${r.model} ตอบใน ${(r.elapsedMs / 1000).toLocaleString("th-TH", { maximumFractionDigits: 1 })} วินาที${r.reply ? `: "${r.reply}"` : " (ไม่มีข้อความตอบกลับ)"}` }); } catch (error) { setAiTest({ ok: false, message: error instanceof Error ? error.message : "ทดสอบการเชื่อมต่อไม่สำเร็จ" }); } finally { setTestingAi(false); } };
  // กลับมาเลือก Provider ที่บันทึกไว้ = คืน Model/Base URL/คีย์เดิม ไม่ต้องกรอกใหม่; Provider อื่นใช้ค่าเริ่มต้นของ Provider นั้น
  const selectAiProvider = (provider: AiProvider) => { const info = aiProviderInfo(provider); setAiApiKey(""); setAiModels([]); setAiModelsError(""); setAiTest(null); setAiConfiguration((current) => savedAi?.provider === provider ? { ...current, provider, model: savedAi.model, baseUrl: savedAi.baseUrl, hasApiKey: savedAi.hasApiKey, apiKeyHint: savedAi.apiKeyHint } : { ...current, provider, model: info.models[0] ?? "", baseUrl: info.baseUrl, hasApiKey: false, apiKeyHint: undefined }); };
  const updateAiForm = (patch: Partial<AiConfiguration>) => { setAiTest(null); setAiConfiguration((current) => ({ ...current, ...patch })); };
  const saveCrmSyncSettings = async () => { setSavingCrmSync(true); try { const response = await fetch(`${apiUrl}/master-settings/crm-sync`, { method: "PUT", headers, body: JSON.stringify({ pollIntervalMinutes: crmSyncSettings.pollIntervalMinutes }) }); if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? "บันทึกรอบ Poll ไม่สำเร็จ"); } setCrmSyncSettings(await response.json()); notify("บันทึกรอบ Poll เรียบร้อยแล้ว", "success"); } catch (error) { notify(error instanceof Error ? error.message : "บันทึกรอบ Poll ไม่สำเร็จ", "error"); } finally { setSavingCrmSync(false); } };
  // ค่าเริ่มต้น CRM Product Id = "34" — ตอนนี้ทุก Project ที่เจอใน CRM จริงใช้ Product เดียวกันนี้ (ยืนยันจากผู้ใช้)
  // ยังแก้ไขเป็นค่าอื่นได้ตามปกติ นี่แค่ prefill ให้ไม่ต้องพิมพ์ซ้ำทุกครั้งตอนเพิ่ม Mapping ใหม่
  const editCrmMapping = (item?: CrmProjectMapping) => { setCrmMappingEditing(item ?? null); setCrmMappingFormOpen(true); setCrmMappingProjectId(item?.projectId ?? projects.find((p) => !crmMappings.some((m) => m.projectId === p.projectId))?.projectId ?? projects[0]?.projectId ?? ""); setCrmMappingProductId(item?.crmProductId ?? "34"); setCrmMappingVersionId(item?.crmVersionId ?? ""); };
  const resetCrmMapping = () => { setCrmMappingEditing(null); setCrmMappingFormOpen(false); setCrmMappingProductId(""); setCrmMappingVersionId(""); };
  const saveCrmMapping = async () => { const response = await fetch(`${apiUrl}/master-settings/crm-mappings${crmMappingEditing ? `/${crmMappingEditing.crmProjectMappingId}` : ""}`, { method: crmMappingEditing ? "PUT" : "POST", headers, body: JSON.stringify({ projectId: crmMappingProjectId, crmProductId: crmMappingProductId, crmVersionId: crmMappingVersionId || null }) }); if (!response.ok) { const p = await response.json(); notify(p.detail ?? "บันทึก CRM Mapping ไม่สำเร็จ", "error"); return; } resetCrmMapping(); setReload((x) => x + 1); };
  const deleteCrmMapping = async (item: CrmProjectMapping) => { if (!await confirmDialog("ยืนยันลบ CRM Mapping นี้?")) return; const response = await fetch(`${apiUrl}/master-settings/crm-mappings/${item.crmProjectMappingId}`, { method: "DELETE", headers }); if (!response.ok) { const p = await response.json(); notify(p.detail ?? "ลบ CRM Mapping ไม่สำเร็จ", "error"); return; } if (crmMappingEditing?.crmProjectMappingId === item.crmProjectMappingId) resetCrmMapping(); setReload((x) => x + 1); };
  const saveEmailConfig = async () => { setSavingEmail(true); try { const response = await fetch(`${apiUrl}/master-settings/email`, { method: "PUT", headers, body: JSON.stringify({ smtpHost: emailConfig.smtpHost, smtpPort: emailConfig.smtpPort, senderEmail: emailConfig.senderEmail, senderDisplayName: emailConfig.senderDisplayName || null, password: emailPassword || null, isEnabled: emailConfig.isEnabled, clearPassword: false }) }); if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? "บันทึกการตั้งค่า Email ไม่สำเร็จ"); } setEmailConfig(await response.json()); setEmailPassword(""); notify("บันทึกการตั้งค่า Email เรียบร้อยแล้ว", "success"); } catch (error) { notify(error instanceof Error ? error.message : "บันทึกการตั้งค่า Email ไม่สำเร็จ", "error"); } finally { setSavingEmail(false); } };
  // ทดสอบด้วยค่าที่บันทึกไว้แล้วในฐานข้อมูล (ไม่ใช่ค่าที่กำลังพิมพ์ในฟอร์ม) — ต้องกด "บันทึกการตั้งค่า" ก่อนถึงจะทดสอบค่าล่าสุดได้
  const sendTestEmail = async () => { if (!emailTestTo.trim()) return; setSendingTestEmail(true); setTestEmailResult(null); try { const response = await fetch(`${apiUrl}/master-settings/email/test`, { method: "POST", headers, body: JSON.stringify({ toEmail: emailTestTo.trim() }) }); if (!response.ok) { const p = await response.json(); throw new Error(p.detail ?? "ส่งอีเมลทดสอบไม่สำเร็จ"); } setTestEmailResult({ ok: true, message: `ส่งอีเมลทดสอบไปที่ ${emailTestTo.trim()} สำเร็จ` }); } catch (error) { setTestEmailResult({ ok: false, message: error instanceof Error ? error.message : "ส่งอีเมลทดสอบไม่สำเร็จ" }); } finally { setSendingTestEmail(false); } };
  // ฟอร์มเพิ่ม/แก้ไขค่าแบบ inline ในกลุ่ม — Enter บันทึกได้เมื่อกรอกครบ
  const optionForm = (targetCategory: string) => formCategory === targetCategory && <form className="ms-inline-form" onSubmit={(e) => { e.preventDefault(); if (value.trim() && displayName.trim()) void saveOption(); }}>
    <p className="ms-inline-title">{editing ? `แก้ไข ${editing.displayName}` : "เพิ่มค่าใหม่"}</p>
    <div className="ms-inline-fields">
      <label className="ms-field">รหัสค่า<input autoFocus value={value} onChange={(e) => setValue(e.target.value)} placeholder="เช่น Major" /></label>
      <label className="ms-field">ชื่อที่แสดง<input value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="ชื่อที่ผู้ใช้เห็นในตัวเลือก" /></label>
      <label className="ms-field ms-order-field">ลำดับ<input type="number" value={sortOrder} onChange={(e) => setSortOrder(Number(e.target.value))} /></label>
    </div>
    <div className="ms-inline-actions"><button type="button" className="btn" onClick={resetOption}>ยกเลิก</button><button type="submit" className="btn primary" disabled={!value.trim() || !displayName.trim()}><span className="material-symbols-outlined" aria-hidden="true">check</span>{editing ? "บันทึกการแก้ไข" : "เพิ่มข้อมูล"}</button></div>
  </form>;
  // ป้ายพร้อมใช้งานคิดจากค่าที่บันทึกแล้ว (ค่าที่ระบบใช้จริง) ไม่ใช่ค่าที่กำลังแก้ในฟอร์ม
  const aiReady = !!savedAi && savedAi.isEnabled && (savedAi.hasApiKey || !aiProviderInfo(savedAi.provider).keyRequired);
  const aiInfo = aiProviderInfo(aiConfiguration.provider);
  const aiDirty = !!savedAi && (savedAi.provider !== aiConfiguration.provider || savedAi.model !== aiConfiguration.model || (savedAi.baseUrl ?? "") !== (aiConfiguration.baseUrl ?? "") || savedAi.isEnabled !== aiConfiguration.isEnabled || !!aiApiKey.trim());
  const aiMissing = [!aiConfiguration.model.trim() && "Model", aiInfo.baseUrl !== undefined && !aiConfiguration.baseUrl?.trim() && "Base URL", aiInfo.keyRequired && !aiConfiguration.hasApiKey && !aiApiKey.trim() && "API key"].filter((x): x is string => !!x);
  const knowledgeNeedsKey = (knowledge.embeddingProvider === "OpenAI" || knowledge.embeddingProvider === "OpenRouter") && !knowledge.hasEmbeddingApiKey && !embeddingApiKey.trim();
  const knowledgeIncomplete = knowledge.embeddingProvider !== "None" && (!knowledge.embeddingModel.trim() || (knowledge.embeddingProvider === "Local" && !knowledge.embeddingBaseUrl.trim()) || knowledgeNeedsKey);
  const emailReady = emailConfig.isEnabled && emailConfig.hasPassword && !!emailConfig.senderEmail;
  const sectionIds = ["AI", "Knowledge", "Email", "Crm", ...masterSettingSections.map((section) => section.name), "Environment"];
  const currentSection = sectionIds.includes(activeSection) ? activeSection : "AI";
  // ≤900px เมนูเป็นแถบเลื่อนแนวนอน — เลื่อนหมวดที่เลือกให้อยู่ในจอ (Desktop อยู่ในจออยู่แล้ว 'nearest' จึงไม่ขยับ)
  const navRef = useRef<HTMLElement>(null);
  useEffect(() => { navRef.current?.querySelector(".ms-nav-item.active")?.scrollIntoView({ block: "nearest", inline: "nearest" }); }, [currentSection, loaded]);
  const activeMasterSection = masterSettingSections.find((section) => section.name === currentSection);
  const activeOptionCount = (categories: readonly string[]) => items.filter((x) => categories.includes(x.category) && x.isActive).length;
  const environmentGroups = [...new Set(environments.map((x) => x.projectId))]
    .map((projectId) => ({ projectId, name: projects.find((x) => x.projectId === projectId)?.projectName ?? "ไม่พบ Project", items: environments.filter((x) => x.projectId === projectId) }))
    .sort((a, b) => a.name.localeCompare(b.name, "th"));
  const savedEmbedding = savedAi?.embeddingProvider && savedAi.embeddingProvider !== "None";
  const busyLabel = (busy: boolean, busyText: string, icon: string, text: string) => busy ? <><span className="spinner inline" aria-hidden="true" /> {busyText}</> : <><span className="material-symbols-outlined" aria-hidden="true">{icon}</span> {text}</>;
  const navButton = (id: string, icon: string, label: string, meta: string, tone?: "ok" | "warn") => <button type="button" key={id} className={`ms-nav-item${currentSection === id ? " active" : ""}`} aria-current={currentSection === id ? "true" : undefined} onClick={() => selectSection(id)}>
    <span className="ms-nav-icon material-symbols-outlined" aria-hidden="true">{icon}</span>
    <span className="ms-nav-text"><b>{label}</b><small>{loaded && tone && <span className={`ms-dot ${tone}`} aria-hidden="true" />}{loaded ? meta : loadError ? "—" : "กำลังโหลด..."}</small></span>
  </button>;
  const sectionHead = (icon: string, title: string, description: ReactNode, aside?: ReactNode) => <header className="ms-head">
    <span className="ms-head-icon material-symbols-outlined" aria-hidden="true">{icon}</span>
    <div className="ms-head-text"><h2 id="ms-section-title">{title}</h2><p>{description}</p></div>
    {aside && <div className="ms-head-aside">{aside}</div>}
  </header>;
  const rowActions = (name: string, onEdit: () => void, onDelete: () => void, toggle?: { active: boolean; onToggle: () => void }) => <span className="ms-row-actions">
    <button type="button" className="ms-icon-btn" title="แก้ไข" aria-label={`แก้ไข ${name}`} onClick={onEdit}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button>
    {toggle && <button type="button" className={`ms-icon-btn${toggle.active ? " is-on" : ""}`} title={toggle.active ? "ปิดใช้งาน" : "เปิดใช้งาน"} aria-label={`${toggle.active ? "ปิดใช้งาน" : "เปิดใช้งาน"} ${name}`} onClick={toggle.onToggle}><span className="material-symbols-outlined" aria-hidden="true">{toggle.active ? "toggle_on" : "toggle_off"}</span></button>}
    <button type="button" className="ms-icon-btn danger" title="ลบ" aria-label={`ลบ ${name}`} onClick={onDelete}><span className="material-symbols-outlined" aria-hidden="true">delete</span></button>
  </span>;
  const testResult = (result: { ok: boolean; message: string } | null) => result && <p className={`ms-result ${result.ok ? "ok" : "error"}`} role="status"><span className="material-symbols-outlined" aria-hidden="true">{result.ok ? "check_circle" : "error"}</span>{result.message}</p>;
  return <div className="ms-page">
    <nav className="ms-nav" ref={navRef} aria-label="หมวดการตั้งค่า">
      <p className="ms-nav-group">บริการ AI</p>
      {navButton("AI", "smart_toy", "AI Provider", aiReady && savedAi ? `พร้อม · ${aiProviderInfo(savedAi.provider).name}` : "ยังไม่พร้อมใช้งาน", aiReady ? "ok" : "warn")}
      {navButton("Knowledge", "local_library", "คลังความรู้ / MCP", `${savedEmbedding ? "ค้นด้วยคำ + ความหมาย" : "ค้นด้วยคำ"}${savedAi?.mcpEnabled ? " · MCP เปิด" : ""}`)}
      <p className="ms-nav-group">การเชื่อมต่อ</p>
      {navButton("Email", "mail", "Email / SMTP", emailReady ? `พร้อม · ${emailConfig.senderEmail}` : "ยังไม่พร้อมใช้งาน", emailReady ? "ok" : "warn")}
      {navButton("Crm", "sync_alt", "CRM", `ทุก ${crmSyncSettings.pollIntervalMinutes} นาที · ${crmMappings.length} Mapping`)}
      <p className="ms-nav-group">ข้อมูลหลัก (Master Data)</p>
      {masterSettingSections.map((section) => navButton(section.name, section.icon, section.name, `${activeOptionCount(section.groups.map((g) => g[0]))} ค่าที่ใช้งาน`))}
      {navButton("Environment", "dns", "Environment", `${environments.filter((x) => x.isActive).length} Environment ที่ใช้งาน`)}
    </nav>
    <div className="ms-main">
      {loadError && <div className="ms-alert" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{loadError}{loaded && " — แสดงข้อมูลชุดก่อนหน้า"}</span><button type="button" className="btn" onClick={() => setReload((x) => x + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
      {!loaded ? !loadError && <div className="ms-card ms-loading" role="status"><span className="spinner" aria-hidden="true" /> กำลังโหลดการตั้งค่า...</div> : <>
        {currentSection === "AI" && <section className="ms-card" aria-labelledby="ms-section-title">
          {sectionHead("smart_toy", "AI Provider", "AI ที่ผู้ช่วย AI (คลังความรู้) และ AI Generate ของ Requirement, Test Case, Test Suite, Automation ใช้ร่วมกัน", <Badge tone={aiReady ? "green" : "yellow"}>{aiReady ? "พร้อมใช้งาน" : "ยังไม่พร้อมใช้งาน"}</Badge>)}
          {savedAi && <div className="ms-current"><span className="material-symbols-outlined" aria-hidden="true">{savedAi.isEnabled ? "smart_toy" : "block"}</span><span>ใช้อยู่ตอนนี้: <b>{aiProviderInfo(savedAi.provider).name} · {savedAi.model}</b>{!savedAi.isEnabled && " (ปิดใช้งาน)"}</span>{aiDirty && <Badge tone="yellow">มีการแก้ไขที่ยังไม่บันทึก</Badge>}</div>}
          <div className="ms-steps">
            <fieldset className="ms-step">
              <legend><span className="ms-step-no" aria-hidden="true">1</span>เลือก Provider</legend>
              <div className="ms-providers" role="radiogroup" aria-label="Provider">
                {aiProviders.map((p) => <label key={p.id} className={`ms-provider${aiConfiguration.provider === p.id ? " selected" : ""}`}><input type="radio" name="ai-provider" value={p.id} checked={aiConfiguration.provider === p.id} onChange={() => selectAiProvider(p.id)} /><span className="ms-provider-text"><b>{p.name}</b><small>{p.hint}</small></span>{savedAi?.provider === p.id && <span className="ms-pill">ใช้อยู่</span>}</label>)}
              </div>
            </fieldset>
            <fieldset className="ms-step">
              <legend><span className="ms-step-no" aria-hidden="true">2</span>ตั้งค่า {aiInfo.name}</legend>
              <div className="ms-fields">
                {aiInfo.baseUrl !== undefined && <label className="ms-field"><span>Base URL</span><input value={aiConfiguration.baseUrl ?? ""} onChange={(e) => updateAiForm({ baseUrl: e.target.value })} placeholder={aiInfo.baseUrl} /></label>}
                <label className="ms-field"><span className="ms-label-row"><span>API key {!aiInfo.keyRequired && <small>(ไม่บังคับ)</small>}</span>{aiInfo.keyUrl && <a href={aiInfo.keyUrl} target="_blank" rel="noopener noreferrer">ขอ API key<span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></a>}</span><input type="password" autoComplete="new-password" value={aiApiKey} onChange={(e) => { setAiApiKey(e.target.value); setAiTest(null); }} placeholder={aiConfiguration.hasApiKey ? `ตั้งค่าแล้ว ${aiConfiguration.apiKeyHint ?? ""} — เว้นว่างเพื่อใช้คีย์เดิม` : aiInfo.keyRequired ? `วาง API key ของ ${aiInfo.name}` : "เว้นว่างได้ หาก Server ไม่ใช้คีย์"} /></label>
                <label className="ms-field"><span className="ms-label-row"><span>Model</span><button type="button" className="ms-link-button" onClick={loadAiModels} disabled={loadingAiModels}>{loadingAiModels ? <><span className="spinner inline" aria-hidden="true" /> กำลังโหลด...</> : aiModels.length ? `โหลดแล้ว ${aiModels.length} Models` : "โหลดรายการจาก Provider"}</button></span><input list="ai-model-options" value={aiConfiguration.model} onChange={(e) => updateAiForm({ model: e.target.value })} placeholder="เลือกด้านล่างหรือพิมพ์ Model ID" /><datalist id="ai-model-options">{(aiModels.length ? aiModels : aiInfo.models.map((id) => ({ id, displayName: "" }))).map((model) => <option key={model.id} value={model.id}>{model.displayName}</option>)}</datalist>{aiModelsError && <small className="ms-error">{aiModelsError}</small>}</label>
              </div>
              <div className="ms-chips" role="group" aria-label="Model แนะนำ"><span>แนะนำ:</span>{aiInfo.models.map((id) => <button type="button" key={id} aria-pressed={aiConfiguration.model === id} onClick={() => updateAiForm({ model: id })}>{id}</button>)}</div>
            </fieldset>
            <fieldset className="ms-step">
              <legend><span className="ms-step-no" aria-hidden="true">3</span>ทดสอบแล้วบันทึก</legend>
              <label className="ms-check"><input type="checkbox" checked={aiConfiguration.isEnabled} onChange={(e) => updateAiForm({ isEnabled: e.target.checked })} /><span><b>เปิดใช้งาน AI ร่วมกันทุกระบบ</b></span></label>
              <div className="ms-actions"><small className={aiMissing.length ? "ms-error" : undefined} role="status">{aiMissing.length ? `ยังขาด: ${aiMissing.join(", ")}` : "ทดสอบได้ก่อนบันทึก (ใช้ค่าในฟอร์ม) · API key เข้ารหัสและเก็บเฉพาะฝั่ง Server"}</small><button type="button" className="btn" disabled={testingAi || aiMissing.length > 0} onClick={testAiConnection}>{busyLabel(testingAi, "กำลังทดสอบ...", "network_check", "ทดสอบการเชื่อมต่อ")}</button><button type="button" className="btn primary" disabled={savingAi || aiMissing.length > 0} onClick={saveAiConfiguration}>{busyLabel(savingAi, "กำลังบันทึก...", "check", "บันทึกการตั้งค่า")}</button></div>
              {testResult(aiTest)}
            </fieldset>
          </div>
        </section>}
        {currentSection === "Knowledge" && <section className="ms-card" aria-labelledby="ms-section-title">
          {sectionHead("local_library", "คลังความรู้ / MCP", "การค้นหาความรู้จาก Defect, Test Case, CRM และเอกสาร เพื่อให้ผู้ช่วย AI ตอบพร้อมอ้างอิง และการเชื่อมต่อจาก Claude", <Badge tone={knowledge.embeddingProvider === "None" ? "blue" : "green"}>{knowledge.embeddingProvider === "None" ? "ค้นด้วยคำ (Full-Text)" : "ค้นด้วยคำ + ความหมาย"}</Badge>)}
          <div className="ms-block">
            <div className="ms-block-head"><h3>Embedding — ค้นตามความหมาย</h3><p>ใช้ร่วมกับ Full-Text ภาษาไทย — เลือก "ไม่ใช้" ระบบจะค้นด้วยคำอย่างเดียว; Anthropic และ Google ไม่มี Embedding จึงต้องเลือก OpenAI, OpenRouter หรือ AI Local แยกจาก AI Provider</p></div>
            <div className="ms-fields ms-fields-3">
              <label className="ms-field">Embedding Provider<select value={knowledge.embeddingProvider} onChange={(e) => { const embeddingProvider = e.target.value as EmbeddingProvider; setEmbeddingApiKey(""); setKnowledge((current) => ({ ...current, embeddingProvider, embeddingModel: embeddingProviderModels[embeddingProvider][0] ?? "", embeddingBaseUrl: embeddingProvider === "Local" ? (current.embeddingBaseUrl || (aiConfiguration.provider === "Local" ? aiConfiguration.baseUrl ?? "" : "") || "http://localhost:11434/v1") : "", hasEmbeddingApiKey: embeddingProvider === current.embeddingProvider && current.hasEmbeddingApiKey })); }}><option value="None">ไม่ใช้ (Full-Text อย่างเดียว)</option><option value="Local">AI Local</option><option value="OpenAI">OpenAI</option><option value="OpenRouter">OpenRouter</option></select></label>
              {knowledge.embeddingProvider !== "None" && <label className="ms-field">Embedding Model<input list="embedding-model-options" value={knowledge.embeddingModel} onChange={(e) => setKnowledge((current) => ({ ...current, embeddingModel: e.target.value }))} placeholder="ระบุ Model ID" /><datalist id="embedding-model-options">{embeddingProviderModels[knowledge.embeddingProvider].map((id) => <option key={id} value={id} />)}</datalist>{knowledge.embeddingProvider === "Local" && <small>แนะนำ bge-m3 (รองรับภาษาไทย)</small>}{knowledge.embeddingProvider === "OpenRouter" && <small>แนะนำ qwen/qwen3-embedding-8b หรือ baai/bge-m3 (รองรับภาษาไทย)</small>}</label>}
              {knowledge.embeddingProvider === "Local" && <label className="ms-field">Base URL<input value={knowledge.embeddingBaseUrl} onChange={(e) => setKnowledge((current) => ({ ...current, embeddingBaseUrl: e.target.value }))} placeholder="http://localhost:11434/v1" /></label>}
              {(knowledge.embeddingProvider === "OpenAI" || knowledge.embeddingProvider === "OpenRouter") && <label className="ms-field">API key สำหรับ Embedding<input type="password" autoComplete="new-password" value={embeddingApiKey} onChange={(e) => setEmbeddingApiKey(e.target.value)} placeholder={knowledge.hasEmbeddingApiKey ? `ตั้งค่าแล้ว ${knowledge.embeddingApiKeyHint ?? ""} — เว้นว่างเพื่อใช้ค่าเดิม` : `กรอก API key ของ ${knowledge.embeddingProvider}`} /></label>}
            </div>
          </div>
          <div className="ms-block">
            <div className="ms-block-head"><h3>ข้อมูลที่ส่งออกนอกองค์กร</h3><p>ข้อมูลทุกชนิดถูก mask ก่อนเข้าคลังความรู้แล้ว — สวิตช์ด้านล่างกำหนดว่าส่งออกไปที่ใดได้บ้าง</p></div>
            <div className="ms-switches">
              <label className="ms-check"><input type="checkbox" checked={knowledge.allowCrmDataToExternalAi} onChange={(e) => setKnowledge((current) => ({ ...current, allowCrmDataToExternalAi: e.target.checked }))} /><span><b>อนุญาตส่งข้อมูล CRM (mask แล้ว) ให้ AI ภายนอก</b><small>มีผลกับ AI Provider ที่ไม่ใช่ AI Local, Claude (MCP) และ Embedding ของ OpenAI/OpenRouter — ปิดไว้ เนื้อหา CRM จะไม่ถูกส่งออกนอกองค์กร</small></span></label>
              <label className="ms-check"><input type="checkbox" checked={knowledge.mcpEnabled} onChange={(e) => setKnowledge((current) => ({ ...current, mcpEnabled: e.target.checked }))} /><span><b>เปิดให้เชื่อมต่อจาก Claude (MCP)</b><small>ผู้มีสิทธิ์ KNOWLEDGE.MCP เชื่อม Claude (claude.ai / Claude Desktop / Claude Code) ค้นคลังความรู้ได้ (อ่านอย่างเดียว, ข้อมูลที่ mask แล้วจะถูกส่งเข้า Claude ของผู้ใช้, CRM เฉพาะเมื่อเปิดสวิตช์ด้านบน) — ปิดแล้ว Token ทุกใบใช้ไม่ได้ทันที</small></span></label>
            </div>
          </div>
          <div className="ms-actions"><small className={knowledgeIncomplete ? "ms-error" : undefined} role="status">{knowledgeIncomplete ? (knowledgeNeedsKey ? "ยังขาด: API key สำหรับ Embedding" : "ยังขาด: Embedding Model / Base URL") : "API key ถูกเข้ารหัสและเก็บเฉพาะฝั่ง Server · เปลี่ยน Provider ต้องกรอกคีย์ใหม่"}</small>{aiConfiguration.embeddingProvider && aiConfiguration.embeddingProvider !== "None" && <button type="button" className="btn" disabled={testingEmbedding} onClick={testEmbedding}>{busyLabel(testingEmbedding, "กำลังทดสอบ...", "network_check", "ทดสอบ Embedding")}</button>}<button type="button" className="btn primary" disabled={savingKnowledge || knowledgeIncomplete} onClick={saveKnowledge}>{busyLabel(savingKnowledge, "กำลังบันทึก...", "check", "บันทึกการตั้งค่าคลังความรู้")}</button></div>
          {testResult(embeddingTest)}
        </section>}
        {currentSection === "Email" && <section className="ms-card" aria-labelledby="ms-section-title">
          {sectionHead("mail", "Email / SMTP", "ค่ากลางสำหรับส่งอีเมลแจ้งเตือน (เช่น ตอนมอบหมายงานผ่าน CRM หรือ CRM ส่งเคสกลับมาหาเจ้าของเรื่อง) — ใช้ Gmail App Password", <Badge tone={emailReady ? "green" : "yellow"}>{emailReady ? "พร้อมใช้งาน" : "ยังไม่พร้อมใช้งาน"}</Badge>)}
          <div className="ms-split">
            <div className="ms-split-main">
              <div className="ms-block">
                <div className="ms-block-head"><h3>เซิร์ฟเวอร์ SMTP</h3></div>
                <div className="ms-fields ms-fields-smtp">
                  <label className="ms-field">SMTP Host<input value={emailConfig.smtpHost} onChange={(e) => setEmailConfig((current) => ({ ...current, smtpHost: e.target.value }))} placeholder="smtp.gmail.com" /></label>
                  <label className="ms-field">SMTP Port<input type="number" value={emailConfig.smtpPort} onChange={(e) => setEmailConfig((current) => ({ ...current, smtpPort: Number(e.target.value) }))} placeholder="587" /></label>
                </div>
              </div>
              <div className="ms-block">
                <div className="ms-block-head"><h3>บัญชีผู้ส่ง</h3></div>
                <div className="ms-fields">
                  <label className="ms-field">อีเมลผู้ส่ง (Gmail)<input type="email" value={emailConfig.senderEmail} onChange={(e) => setEmailConfig((current) => ({ ...current, senderEmail: e.target.value }))} placeholder="เช่น qahub@yourcompany.com" /></label>
                  <label className="ms-field"><span>ชื่อผู้ส่งที่แสดง <small>(ไม่บังคับ)</small></span><input value={emailConfig.senderDisplayName ?? ""} onChange={(e) => setEmailConfig((current) => ({ ...current, senderDisplayName: e.target.value }))} placeholder="เช่น QA Hub" /></label>
                  <label className="ms-field">App Password<input type="password" autoComplete="new-password" value={emailPassword} onChange={(e) => setEmailPassword(e.target.value)} placeholder={emailConfig.hasPassword ? "ตั้งค่าแล้ว — เว้นว่างเพื่อใช้ค่าเดิม" : "กรอก App Password 16 หลักของ Gmail"} /></label>
                </div>
              </div>
              <label className="ms-check"><input type="checkbox" checked={emailConfig.isEnabled} onChange={(e) => setEmailConfig((current) => ({ ...current, isEnabled: e.target.checked }))} /><span><b>เปิดใช้งานการส่งอีเมล</b></span></label>
              <div className="ms-actions"><small>App Password ถูกเข้ารหัสและเก็บเฉพาะฝั่ง Server</small><button type="button" className="btn primary" disabled={savingEmail || !emailConfig.smtpHost.trim() || !emailConfig.senderEmail.trim() || (!emailConfig.hasPassword && !emailPassword.trim())} onClick={saveEmailConfig}>{busyLabel(savingEmail, "กำลังบันทึก...", "check", "บันทึกการตั้งค่า")}</button></div>
            </div>
            <aside className="ms-split-side">
              <div className="ms-note"><span className="material-symbols-outlined" aria-hidden="true">lock</span><div><b>การจัดเก็บที่ปลอดภัย</b><p>App Password ถูกเข้ารหัสและเก็บเฉพาะฝั่ง Server เว้นว่างไว้เพื่อคงค่าเดิม — สร้างได้ที่ Google Account &gt; Security &gt; App passwords (ต้องเปิด 2-Step Verification ก่อน)</p></div></div>
              <form className="ms-side-box" onSubmit={(e) => { e.preventDefault(); void sendTestEmail(); }}>
                <h3>ทดสอบส่งอีเมล</h3>
                <p>ใช้ค่าที่บันทึกไว้แล้ว — ถ้าเพิ่งแก้ไข กด "บันทึกการตั้งค่า" ก่อน</p>
                <label className="ms-field">ส่งถึง<input type="email" value={emailTestTo} onChange={(e) => setEmailTestTo(e.target.value)} placeholder="อีเมลปลายทางสำหรับทดสอบ" /></label>
                <button type="submit" className="btn" disabled={sendingTestEmail || !emailTestTo.trim()}>{busyLabel(sendingTestEmail, "กำลังส่ง...", "send", "ส่งอีเมลทดสอบ")}</button>
                {testResult(testEmailResult)}
              </form>
            </aside>
          </div>
        </section>}
        {currentSection === "Crm" && <section className="ms-card" aria-labelledby="ms-section-title">
          {sectionHead("sync_alt", "CRM", "ค่ากลางของการเชื่อมต่อ CRM ทั้งระบบ — Login เข้า CRM เป็นแบบรายบุคคล ตั้งได้ที่ปุ่ม \"บัญชี CRM ของฉัน\" มุมขวาบน")}
          <div className="ms-block">
            <div className="ms-block-head"><h3>รอบตรวจการเปลี่ยนแปลง (Poll)</h3><p>รอบเวลาที่ Background Worker เช็คว่า Ticket ที่ผูกกับ CRM มีการเปลี่ยน Status/ผู้รับผิดชอบไหม — มีผลตั้งแต่รอบถัดไป ไม่ต้อง restart</p></div>
            <form className="ms-poll" onSubmit={(e) => { e.preventDefault(); void saveCrmSyncSettings(); }}>
              <label className="ms-field">รอบ Poll (นาที)<input type="number" min={1} max={60} value={crmSyncSettings.pollIntervalMinutes} onChange={(e) => setCrmSyncSettings((current) => ({ ...current, pollIntervalMinutes: Number(e.target.value) }))} /></label>
              <button type="submit" className="btn primary" disabled={savingCrmSync || crmSyncSettings.pollIntervalMinutes < 1 || crmSyncSettings.pollIntervalMinutes > 60}>{busyLabel(savingCrmSync, "กำลังบันทึก...", "check", "บันทึก")}</button>
              <small className={crmSyncSettings.pollIntervalMinutes < 1 || crmSyncSettings.pollIntervalMinutes > 60 ? "ms-error" : undefined}>กำหนดได้ 1–60 นาที</small>
            </form>
          </div>
          <div className="ms-block">
            <div className="ms-block-head"><h3>รอบดึงข้อมูล CRM ทั้งทีม</h3><p>ดึงสำเนา CRM Ticket ทั้งทีม (ติดต่อใน 90 วันล่าสุด) มาใช้วัดผลงาน CRM และเป็นความรู้ให้ผู้ช่วย AI — รอบใหญ่ย้อนหลัง 1 ปียังคงวันละครั้ง</p></div>
            <CrmMirrorIntervalEditor minutes={crmSyncSettings.mirrorIntervalMinutes ?? 15} prefix="ดึงทุก" onSaved={(minutes) => setCrmSyncSettings((current) => ({ ...current, mirrorIntervalMinutes: minutes }))} />
          </div>
          <div className="ms-block">
            <div className="ms-block-head has-action"><div><h3>Project Mapping</h3><p>ผูก Project ของ QA Hub เข้ากับ SysProductId/SysVersionId จริงของ CRM สำหรับใช้ตอนสร้าง Ticket</p></div><button type="button" className="btn ms-add" disabled={crmMappings.length >= projects.length} title={crmMappings.length >= projects.length ? "ทุก Project มี Mapping แล้ว" : undefined} onClick={() => editCrmMapping()}><span className="material-symbols-outlined" aria-hidden="true">add</span> เพิ่ม Mapping</button></div>
            {crmMappingFormOpen && <form className="ms-form-panel" onSubmit={(e) => { e.preventDefault(); if (crmMappingProjectId && crmMappingProductId.trim()) void saveCrmMapping(); }}>
              <p className="ms-inline-title">{crmMappingEditing ? "แก้ไข Mapping" : "เพิ่ม Mapping"}</p>
              <div className="ms-form-grid">
                <label className="ms-field">Project<select disabled={!!crmMappingEditing} value={crmMappingProjectId} onChange={(e) => setCrmMappingProjectId(e.target.value)}>{projects.filter((x) => crmMappingEditing || !crmMappings.some((m) => m.projectId === x.projectId)).map((x) => <option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select></label>
                <label className="ms-field">CRM Product Id<input autoFocus value={crmMappingProductId} onChange={(e) => setCrmMappingProductId(e.target.value)} /></label>
                <label className="ms-field"><span>CRM Version Id <small>(ไม่บังคับ)</small></span><input value={crmMappingVersionId} onChange={(e) => setCrmMappingVersionId(e.target.value)} /></label>
              </div>
              <div className="ms-inline-actions"><button type="button" className="btn" onClick={resetCrmMapping}>ยกเลิก</button><button type="submit" className="btn primary" disabled={!crmMappingProjectId || !crmMappingProductId.trim()}><span className="material-symbols-outlined" aria-hidden="true">check</span>{crmMappingEditing ? "บันทึกการแก้ไข" : "เพิ่มข้อมูล"}</button></div>
            </form>}
            {crmMappings.length ? <ul className="ms-list">{crmMappings.map((item) => { const name = projects.find((x) => x.projectId === item.projectId)?.projectName ?? "-"; return <li key={item.crmProjectMappingId} className="ms-row">
              <span className="ms-row-icon material-symbols-outlined" aria-hidden="true">link</span>
              <span className="ms-row-text"><b>{name}</b><small>Product <code>{item.crmProductId}</code>{item.crmVersionId && <> · Version <code>{item.crmVersionId}</code></>}</small></span>
              {rowActions(`CRM Mapping ของ ${name}`, () => editCrmMapping(item), () => deleteCrmMapping(item))}
            </li>; })}</ul> : <p className="ms-empty">ยังไม่มี Mapping — Project ที่ไม่มี Mapping จะสร้าง Ticket ไป CRM ไม่ได้</p>}
          </div>
        </section>}
        {activeMasterSection && <section className="ms-card" aria-labelledby="ms-section-title">
          {sectionHead(activeMasterSection.icon, activeMasterSection.name, activeMasterSection.description)}
          <div className="ms-groups">{activeMasterSection.groups.map((group) => {
            const list = items.filter((x) => x.category === group[0]).sort((a, b) => a.sortOrder - b.sortOrder);
            const active = list.filter((x) => x.isActive).length;
            return <section className="ms-group" key={group[0]} aria-labelledby={`ms-group-${group[0]}`}>
              <div className="ms-group-head"><div><h3 id={`ms-group-${group[0]}`}>{group[2]}</h3><small>{active} ใช้งาน{list.length > active && ` · ${list.length - active} ปิดใช้งาน`}</small></div><button type="button" className="btn ms-add" disabled={formCategory === group[0] && !editing} onClick={() => openOptionForm(group[0])}><span className="material-symbols-outlined" aria-hidden="true">add</span> เพิ่ม</button></div>
              {optionForm(group[0])}
              {list.length ? <ul className="ms-list">{list.map((item) => <li key={item.masterOptionId} className={`ms-row${item.isActive ? "" : " is-inactive"}`}>
                <span className="ms-row-order" title="ลำดับการแสดง">{item.sortOrder}</span>
                <span className="ms-row-text"><b>{item.displayName}</b><small><code>{item.value}</code>{!item.isActive && <span className="ms-inactive-note"> · ปิดใช้งาน</span>}</small></span>
                {!item.isActive && <Badge tone="gray">ปิดใช้งาน</Badge>}
                {rowActions(item.displayName, () => openOptionForm(group[0], item), () => deleteOption(item), { active: item.isActive, onToggle: () => toggleOption(item) })}
              </li>)}</ul> : <p className="ms-empty">ยังไม่มีค่า — กด "เพิ่ม" เพื่อสร้างค่าแรก</p>}
            </section>;
          })}</div>
        </section>}
        {currentSection === "Environment" && <section className="ms-card" aria-labelledby="ms-section-title">
          {sectionHead("dns", "Environment", "URL ของแต่ละ Project ที่ใช้อ้างอิงตอนวางแผน Test Cycle และ Execution", <button type="button" className="btn ms-add" onClick={() => editEnvironment()}><span className="material-symbols-outlined" aria-hidden="true">add</span> เพิ่ม Environment</button>)}
          {environmentFormOpen && <form className="ms-form-panel" onSubmit={(e) => { e.preventDefault(); if (environmentProjectId && environmentName.trim()) void saveEnvironment(); }}>
            <p className="ms-inline-title">{environment ? `แก้ไข ${environment.environmentName}` : "เพิ่ม Environment"}</p>
            <div className="ms-form-grid">
              <label className="ms-field">Project<select disabled={!!environment} value={environmentProjectId} onChange={(e) => setEnvironmentProjectId(e.target.value)}>{projects.map((x) => <option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select></label>
              <label className="ms-field">ชื่อ Environment<input autoFocus value={environmentName} onChange={(e) => setEnvironmentName(e.target.value)} placeholder="เช่น UAT" /></label>
              <label className="ms-field">Base URL<input value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="https://..." /></label>
            </div>
            <div className="ms-inline-actions"><button type="button" className="btn" onClick={resetEnvironment}>ยกเลิก</button><button type="submit" className="btn primary" disabled={!environmentProjectId || !environmentName.trim()}><span className="material-symbols-outlined" aria-hidden="true">check</span>{environment ? "บันทึกการแก้ไข" : "เพิ่มข้อมูล"}</button></div>
          </form>}
          {environmentGroups.length ? <div className="ms-groups">{environmentGroups.map((group) => <section className="ms-group" key={group.projectId} aria-labelledby={`ms-env-${group.projectId}`}>
            <div className="ms-group-head"><div><h3 id={`ms-env-${group.projectId}`}>{group.name}</h3><small>{group.items.length} Environment</small></div></div>
            <ul className="ms-list">{group.items.map((item) => <li key={item.testEnvironmentId} className={`ms-row${item.isActive ? "" : " is-inactive"}`}>
              <span className="ms-row-icon material-symbols-outlined" aria-hidden="true">dns</span>
              <span className="ms-row-text"><b>{item.environmentName}</b><small className="ms-url" title={item.baseUrl || undefined}>{item.baseUrl || "ไม่ระบุ URL"}{!item.isActive && <span className="ms-inactive-note"> · ปิดใช้งาน</span>}</small></span>
              {!item.isActive && <Badge tone="gray">ปิดใช้งาน</Badge>}
              {rowActions(`Environment ${item.environmentName}`, () => editEnvironment(item), () => deleteEnvironment(item), { active: item.isActive, onToggle: () => toggleEnvironment(item) })}
            </li>)}</ul>
          </section>)}</div> : <p className="ms-empty">ยังไม่มี Environment — กด "เพิ่ม Environment" เพื่อเริ่ม</p>}
        </section>}
      </>}
    </div>
  </div>;
}
