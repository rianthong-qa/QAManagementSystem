# Requirements — โมดูล Reporting (รายงาน)

> อ้างอิง: `System-Analysis.md` section 3.5, 6.5, 6.6
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Reporting รับผิดชอบการสร้างและส่งรายงานทุกประเภท ใช้ FastReport Engine ตามที่ระบุใน System-Analysis.md section 3.5, 6.5, 6.6

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

### 2.1 รายงานหลัก

#### FR-RPT-001: รายงานยอดขายสูงสุด-ต่ำสุด ตามมูลค่า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-001 |
| **ชื่อ** | รายงานยอดขายสูงสุด-ต่ำสุด ตามมูลค่า |
| **Description** | ระบบต้องสร้างรายงานยอดขายเรียงตามมูลค่าขาย |
| **Input** | Date range, Branch |
| **Output** | Report (File format) |
| **ReportFile** | 1 |
| **Priority** | High |

#### FR-RPT-002: รายงานยอดขายสูงสุด-ต่ำสุด ตามจำนวน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-002 |
| **ชื่อ** | รายงานยอดขายสูงสุด-ต่ำสุด ตามจำนวน |
| **Description** | ระบบต้องสร้างรายงานยอดขายเรียงตามจำนวนสินค้า |
| **Input** | Date range, Branch |
| **Output** | Report (File format) |
| **ReportFile** | 2 |
| **Priority** | High |

#### FR-RPT-003: รายงานกำไรเบื้องต้น
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-003 |
| **ชื่อ** | รายงานกำไรเบื้องต้น |
| **Description** | ระบบต้องสร้างรายงานกำไรตามต้นทุนหลัก |
| **Input** | Date range, Cost type |
| **Output** | Report (File format) |
| **ReportFile** | 3 |
| **Priority** | High |

#### FR-RPT-004: สรุปยอดขายประจำวัน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-004 |
| **ชื่อ** | สรุปยอดขายประจำวัน |
| **Description** | ระบบต้องสร้างรายงานสรุปยอดขายรายวัน |
| **Input** | Date |
| **Output** | Report (File + Text format) |
| **ReportFile** | 4 |
| **ReportText** | 4 |
| **Priority** | Critical |

---

### 2.2 Email Report

#### FR-RPT-005: ส่งรายงานทางอีเมล
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-005 |
| **ชื่อ** | ส่งรายงานทางอีเมล |
| **Description** | ระบบต้องรองรับการส่งรายงานทางอีเมล |
| **Input** | Report type, Email recipient |
| **Output** | Email sent with report attachment |
| **Config** | `ui_amail.ini` [EmailConfig] |
| **Priority** | High |

#### FR-RPT-006: ส่งรายงานพร้อม CC
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-006 |
| **ชื่อ** | ส่งรายงานพร้อม CC |
| **Description** | ระบบต้องรองรับการส่งรายงานพร้อม CC |
| **Input** | CC Email |
| **Output** | CC recipient received email |
| **Config** | `cc_email` |
| **Priority** | Medium |

#### FR-RPT-007: Subject กำหนดเอง
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-007 |
| **ชื่อ** | Subject กำหนดเอง |
| **Description** | ระบบต้องรองรับการตั้งค่า Subject อีเมลกำหนดเอง |
| **Input** | Custom subject |
| **Output** | Email with custom subject |
| **Config** | `use_system_subject`, `custom_subject` |
| **Priority** | Low |

---

### 2.3 Report Engine

#### FR-RPT-008: Export PDF
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-008 |
| **ชื่อ** | Export PDF |
| **Description** | ระบบต้องรองรับการ export รายงานเป็น PDF |
| **Input** | Report data |
| **Output** | PDF file |
| **Library** | FastReport.OpenSource.Export.PdfSimple |
| **Priority** | High |

#### FR-RPT-009: Export Excel
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-009 |
| **ชื่อ** | Export Excel |
| **Description** | ระบบต้องรองรับการ export รายงานเป็น Excel |
| **Input** | Report data |
| **Output** | Excel file |
| **Library** | ClosedXML |
| **Priority** | High |

#### FR-RPT-010: พิมพ์ใบเสร็จ
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-RPT-010 |
| **ชื่อ** | พิมพ์ใบเสร็จ |
| **Description** | ระบบต้องพิมพ์ใบเสร็จจากเทมเพลต |
| **Input** | Transaction data |
| **Output** | Receipt printed |
| **Template** | Templates/Receipt.frx |
| **Priority** | High |

---

## 3. Non-Functional Requirements

### NFR-RPT-001: Report Generation Time
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-RPT-001 |
| **ชื่อ** | เวลาสร้างรายงาน |
| **Description** | รายงานต้องสร้างเสร็จภายใน 10 วินาที |
| **Measurement** | Report generation < 10s |

### NFR-RPT-002: Email Delivery
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-RPT-002 |
| **ชื่อ** | การส่งอีเมล |
| **Description** | อีเมลต้องถูกส่งสำเร็จภายใน 30 วินาที |
| **Measurement** | Email delivery < 30s |

### NFR-RPT-003: Report Accuracy
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-RPT-003 |
| **ชื่อ** | ความถูกต้องของรายงาน |
| **Description** | ข้อมูลในรายงานต้องตรงกับข้อมูลจริง 100% |
| **Measurement** | Data accuracy = 100% |

---

## 4. Data Requirements

### DR-RPT-001: Email Report Config
| Field | Type | Description |
|-------|------|-------------|
| email_from | VARCHAR | อีเมลผู้ส่ง |
| password | VARCHAR | รหัสผ่านอีเมล |
| provider | VARCHAR | ผู้ให้บริการ (Gmail) |
| smtp_host | VARCHAR | SMTP Host |
| smtp_port | INT | SMTP Port |
| use_system_subject | BOOLEAN | ใช้ Subject ระบบ |
| custom_subject | VARCHAR | Subject กำหนดเอง |
| cc_email | VARCHAR | CC Email |

### DR-RPT-002: Report Files
| Field | Type | Description |
|-------|------|-------------|
| ReportId | INT | รหัสรายงาน |
| ReportName | VARCHAR | ชื่อรายงาน |
| ReportType | VARCHAR | ประเภท (File/Text) |
| Template | VARCHAR | เทมเพลต |

---

## 5. Business Rules

### BR-RPT-001: Daily Summary Calculation
```
DailySummary = {
  TotalSales: SUM(TranOut.GrandTotal) WHERE TranDate = Today,
  TotalCost: SUM(Detail.Quantity × Detail.Cost),
  GrossProfit: TotalSales - TotalCost,
  VAT: SUM(Detail.TaxAmount),
  TransactionCount: COUNT(TranOut)
}
```

### BR-RPT-002: Email Report Selection
```
IF report_type IN (1,2,3) THEN
  Send as File attachment
ELSE IF report_type = 4 THEN
  Send as File AND Text
```

### BR-RPT-003: Auto Email Report
```
IF send_mail = YES THEN
  Auto send daily summary after end of day
```

---

## 6. Configuration Reference

### ui_amail.ini

| Section | Key | ค่า Default | คำอธิบาย |
|---------|-----|------------|----------|
| [ReportFiles] | 1-4 | ชื่อรายงาน | รายงานที่รองรับแบบ File |
| [ReportTexts] | 4 | ชื่อรายงาน | รายงานที่รองรับแบบ Text |
| [EmailConfig] | email_from | - | อีเมลผู้ส่ง |
| [EmailConfig] | password | - | รหัสผ่าน |
| [EmailConfig] | provider | Gmail | ผู้ให้บริการ |
| [EmailConfig] | smtp_host | - | SMTP Host |
| [EmailConfig] | smtp_port | 587 | SMTP Port |
| [EmailConfig] | use_system_subject | 1 | ใช้ Subject ระบบ |
| [EmailConfig] | custom_subject | - | Subject กำหนดเอง |
| [EmailConfig] | cc_email | - | CC Email |

### system.ini

| Section | Key | ค่า Default | คำอธิบาย |
|---------|-----|------------|----------|
| [System] | send_mail | NO | ส่งอีเมลอัตโนมัติ |
| [SummaryDaily] | sys_voucher_id | 40 | Voucher ID สำหรับ Daily Summary |

---

## 7. Report Template Reference

| Template | คำอธิบาย |
|----------|----------|
| Templates/Receipt.frx | เทมเพลตใบเสร็จ |

### Receipt Template Fields

| Field | Description |
|-------|-------------|
| TransactionNo | เลขที่ธุรกรรม |
| TransactionDate | วันที่ทำธุรกรรม |
| CustomerName | ชื่อลูกค้า |
| StaffName | ชื่อพนักงาน |
| Items[] | รายการสินค้า |
| SubTotal | ยอดรวมก่อน VAT |
| VAT | ภาษีมูลค่าเพิ่ม |
| ServiceCharge | ค่าบริการ |
| GrandTotal | ยอดรวมสุทธิ |
| PaymentMethod | วิธีชำระเงิน |
| AmountReceived | จำนวนเงินที่รับ |
| Change | เงินทอน |
