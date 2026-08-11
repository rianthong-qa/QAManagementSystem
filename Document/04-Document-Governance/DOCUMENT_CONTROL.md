# ProMaxx2 QA Management System — DOCUMENT_CONTROL

> Master Development Package เป็น **Single Source of Truth (SSOT)** ของระบบ ProMaxx2 QA Management

## 1. Master Package

ชื่อชุดเอกสาร:
`ProMaxx2_QA_Master_Development_Package`

ห้ามสร้าง Blueprint แยกใหม่สำหรับ Requirement/API/Database/UI เดียวกันโดยไม่มีเหตุผลและ Version Control ชัดเจน

## 2. Document Register

| Document | Purpose | Primary Owner | Review By | Update Trigger |
|---|---|---|---|---|
| REQUIREMENTS.md | Functional/NFR/Business Rules | Product/QA | Dev/QA Lead | Requirement Change |
| WORKFLOW.md | Business/QA Workflow | QA Lead | Product/Dev | Workflow Change |
| DATABASE_DESIGN.md | Logical Data Design | Dev | QA/Architect | Entity/Relationship Change |
| ARCHITECTURE.md | System Architecture | Tech Lead | Dev/QA Lead | Architecture Change |
| PROJECT_STRUCTURE.md | Code/Repository Structure | Tech Lead | Dev | Structural Change |
| SQL_SERVER_SCHEMA.md | Physical DB Schema | Backend/DB | QA/Tech Lead | DB Change |
| API_SPECIFICATION.md | API Contract | Backend | Frontend/QA | API Change |
| SCREEN_SPECIFICATION.md | UI/UX Contract | Frontend/QA | Product | Screen Change |
| DEVELOPMENT_PLAN.md | Development/Rollout Plan | Lead | Team | Priority/Plan Change |
| TRACEABILITY_MATRIX.md | Cross-document Traceability | QA | Dev/Product | Requirement/Design Change |
| CHANGELOG.md | Change History | Document Owner | Lead | Every Controlled Change |
| DOCUMENT_CONTROL.md | Governance | QA Lead | Tech/Product Lead | Governance Change |

## 3. Document Status

ใช้สถานะ:
- `DRAFT`
- `IN REVIEW`
- `APPROVED`
- `SUPERSEDED`
- `ARCHIVED`

เฉพาะเอกสารสถานะ `APPROVED` เท่านั้นที่ควรใช้เป็น Baseline สำหรับ Release สำคัญ

## 4. Version Rules

### PATCH
แก้ typo, wording, description, non-breaking clarification

### MINOR
เพิ่ม Requirement/API/Table/Screen/Workflow โดยไม่ทำลาย Contract เดิม

### MAJOR
Breaking API, Major DB migration, Architecture change, Workflow breaking change หรือ Security model change

## 5. Change Workflow

```text
Change Requested
      ↓
Assign Change ID
      ↓
Impact Analysis
      ↓
Update Source Requirement
      ↓
Update Dependent Documents
      ↓
Update Traceability Matrix
      ↓
Review
      ↓
Approve
      ↓
Update CHANGELOG
      ↓
Implement / Test
```

## 6. Source-of-Truth Rule

ถ้ามีข้อมูลขัดกัน ให้ใช้ลำดับ:

1. Approved Requirement / Business Decision
2. Approved Workflow
3. Architecture Decision
4. Database/API/Screen Specification
5. Implementation

ถ้า Code ไม่ตรงกับ Approved Specification ต้องพิจารณาว่าเป็น:
- Defect ของ Code หรือ
- Specification ต้องเปลี่ยน

ห้ามแก้ Specification ย้อนหลังเพื่อให้ตรง Code โดยไม่มี Change Approval

## 7. Change ID

รูปแบบแนะนำ:
`CHG-PMX2QA-YYYY-NNN`

ตัวอย่าง:
`CHG-PMX2QA-2026-001`

ทุก Change สำคัญควรมี:
- Change ID
- Requester
- Date
- Reason
- Impact
- Owner
- Documents
- Approval
- Implementation Status
- Test Status

## 8. Baseline

ก่อนเริ่ม Sprint/Release สำคัญ ให้สร้าง Baseline Version เช่น:
`Master Blueprint v1.2.0`

Baseline ต้องระบุ:
- Document Version
- Approval Date
- Release/Iteration
- Known Exceptions

## 9. Review Responsibility

### QA Lead
Requirement traceability, workflow, test impact, release governance

### Developer/Tech Lead
Architecture, DB, API, technical feasibility, migration

### Product/Business Owner
Business requirement, acceptance criteria, scope, risk decision

### Frontend
Screen/API compatibility

## 10. Change Impact Checklist

ก่อน Approve:
- [ ] Requirement Updated
- [ ] Acceptance Criteria Updated
- [ ] Workflow Reviewed
- [ ] Database Impact Reviewed
- [ ] Migration Reviewed
- [ ] API Contract Reviewed
- [ ] UI Reviewed
- [ ] Permission/Security Reviewed
- [ ] Audit Impact Reviewed
- [ ] Test Cases Updated
- [ ] Regression Impact Identified
- [ ] Traceability Updated
- [ ] Changelog Updated

## 11. Naming

ห้ามใช้ชื่อ:
- `final-final.md`
- `latest2.md`
- `new-requirement-final.md`

ใช้ Git/Version/Changelog แทนการสร้างสำเนาหลายชุด

## 12. Archive

เอกสารเก่าที่ถูกแทน:
- Mark `SUPERSEDED`
- เก็บใน Version Control
- ไม่ควรอยู่ปะปนกับ Current Approved Document ใน Working Folder

## 13. Pull Request Rule

เมื่อ Code Change กระทบ Contract ต้อง Update Documentation ใน PR เดียวกัน เช่น:
- DB Change → Schema + Database Design
- API Change → API Specification
- UI Flow Change → Screen + Workflow
- Requirement Change → Requirement + Traceability + Tests
- Release Gate Change → Requirement + Workflow + Architecture/Test

## 14. QA Document Review Gate

QA ควร Reject Change ที่:
- ไม่มี Acceptance Criteria
- ไม่ระบุ Impact
- API/DB/UI ไม่สอดคล้อง
- ไม่มี Test Impact
- Breaking Change ไม่มี Migration/Compatibility Plan
- Permission/Audit ไม่ถูกพิจารณา

## 15. Recommended Header

Controlled document สามารถเพิ่ม Header:

```text
Document:
Version:
Status:
Owner:
Last Updated:
Approved By:
Related Change ID:
```
