// type ที่หลายหน้าใช้ร่วมกัน — ย้ายออกจาก App.tsx (UI รอบ 6) เพื่อให้แยกหน้าเป็นไฟล์ได้
export type ProjectItem = {
  projectId: string;
  projectCode: string;
  projectName: string;
  description?: string;
  status: string;
  isActive: boolean;
  createdAt: string;
};
export type ReleaseItem = {
  releaseId: string;
  projectId: string;
  releaseCode: string;
  version: string;
  releaseType?: string;
  scope?: string;
  plannedReleaseDate?: string;
  actualReleaseDate?: string;
  status: string;
  createdAt: string;
};
export type BuildItem = {
  buildId: string;
  releaseId: string;
  buildNumber: string;
  applicationVersion?: string;
  packageVersion?: string;
  commitReference?: string;
  buildDate?: string;
  changeNotes?: string;
  knownIssues?: string;
  isReleaseCandidate: boolean;
  isActive: boolean;
  status: string;
};
export type DefectItem = { defectId:string; defectCode:string; title:string; severity:string; status:string; createdAt:string; projectId?:string; releaseId?:string|null; buildId?:string|null; moduleId?:string|null; description?:string|null; stepsToReproduce?:string|null; expectedResult?:string|null; actualResult?:string|null; assigneeUserId?:string|null; updatedAt?:string|null; createdByName?:string|null; updatedByName?:string|null; releaseCode?:string|null; buildNumber?:string|null; assigneeName?:string|null; crmTicketId?:string|null; crmSyncStatus?:string; crmLastSyncedAt?:string|null };
export type UserLookup = { userId:string; displayName:string };
