# Requirements — โมดูล Settings (ตั้งค่าระบบ)

> อ้างอิง: `System-Analysis.md` section 5.2, 9
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Settings รับผิดชอบการตั้งค่าทั้งหมดของระบบ จัดเก็บใน `config/system.ini` ตามที่ระบุใน System-Analysis.md section 5.2, 9

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

### 2.1 Terminal Settings

#### FR-SET-001: ตั้งค่า Terminal
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-001 |
| **ชื่อ** | ตั้งค่า Terminal |
| **Description** | ระบบต้องรองรับการตั้งค่า Terminal ID และ POS ID |
| **Input** | TerminalNo, PosId |
| **Output** | Terminal settings saved |
| **Config** | `terminalno`, `posid` |
| **Priority** | High |

#### FR-SET-002: ตั้งค่า Cash Drawer
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-002 |
| **ชื่อ** | ตั้งค่า Cash Drawer |
| **Description** | ระบบต้องรองรับการตั้งค่า Cash Drawer |
| **Input** | COM Port, USB Port, Send Type |
| **Output** | Cash Drawer settings saved |
| **Config** | `cash_drawer_com_port`, `cash_drawer_usb_port_id`, `cash_drawer_send_type` |
| **Priority** | High |

#### FR-SET-003: ตั้งค่า Receipt Printer
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-003 |
| **ชื่อ** | ตั้งค่า Receipt Printer |
| **Description** | ระบบต้องรองรับการตั้งค่า Receipt Printer |
| **Input** | Model, COM Port, Language |
| **Output** | Printer settings saved |
| **Config** | `tmu`, `tmu_com_port`, `tmu_language_code` |
| **Priority** | High |

#### FR-SET-004: ตั้งค่า Customer Display
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-004 |
| **ชื่อ** | ตั้งค่า Customer Display |
| **Description** | ระบบต้องรองรับการตั้งค่า Customer Display |
| **Input** | On/Off, Model, Welcome Text |
| **Output** | Display settings saved |
| **Config** | `cus_dis_on`, `cus_disp_model`, `cus_disp_text_upper` |
| **Priority** | Medium |

#### FR-SET-005: ตั้งค่า EDC
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-005 |
| **ชื่อ** | ตั้งค่า EDC |
| **Description** | ระบบต้องรองรับการตั้งค่า EDC (Payment Terminal) |
| **Input** | Model, COM Port |
| **Output** | EDC settings saved |
| **Config** | `edc_name`, `edc_port` |
| **Priority** | Medium |

---

### 2.2 System Settings

#### FR-SET-006: เปลี่ยนภาษา
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-006 |
| **ชื่อ** | เปลี่ยนภาษา |
| **Description** | ระบบต้องรองรับการเปลี่ยนภาษา (15+ ภาษา) |
| **Input** | Language code (TH/EN/...) |
| **Output** | UI language changed |
| **Config** | `language`, `language_on_employee` |
| **Priority** | High |

#### FR-SET-007: เปิด/ปิด Auto Backup
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-007 |
| **ชื่อ** | เปิด/ปิด Auto Backup |
| **Description** | ระบบต้องรองรับการเปิด/ปิด Auto Backup |
| **Input** | AutoBackup (0/1) |
| **Output** | Auto Backup enabled/disabled |
| **Config** | `autobackup` |
| **Priority** | High |

#### FR-SET-008: เปิด/ปิดระบบส่งอีเมล
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-008 |
| **ชื่อ** | เปิด/ปิดระบบส่งอีเมล |
| **Description** | ระบบต้องรองรับการเปิด/ปิดระบบส่งอีเมล |
| **Input** | SendMail (YES/NO) |
| **Output** | Email system enabled/disabled |
| **Config** | `send_mail` |
| **Priority** | Medium |

#### FR-SET-009: เปลี่ยนทศนิยมสกุลเงิน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-009 |
| **ชื่อ** | เปลี่ยนทศนิยมสกุลเงิน |
| **Description** | ระบบต้องรองรับการเปลี่ยนจำนวนทศนิยม |
| **Input** | Decimal points (2-4) |
| **Output** | Currency display changed |
| **Config** | `currency_decimal_point` |
| **Priority** | Medium |

---

### 2.3 Database Settings

#### FR-SET-010: เปลี่ยนฐานข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-010 |
| **ชื่อ** | เปลี่ยนฐานข้อมูล |
| **Description** | ระบบต้องรองรับการเปลี่ยนฐานข้อมูล (Firebird/SQL/PostgreSQL) |
| **Input** | Database type, Connection settings |
| **Output** | Database connection changed |
| **Config** | `conn_mode`, `db_type` |
| **Priority** | High |

#### FR-SET-011: ตั้งค่า Firebird
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-011 |
| **ชื่อ** | ตั้งค่า Firebird |
| **Description** | ระบบต้องรองรับการตั้งค่า Firebird SQL |
| **Input** | Local/LAN path, Username, Password |
| **Output** | Firebird settings saved |
| **Config** | `fb_local_database`, `fb_lan_host`, `fb_lan_port` |
| **Priority** | High |

#### FR-SET-012: ตั้งค่า SQL Server
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-012 |
| **ชื่อ** | ตั้งค่า SQL Server |
| **Description** | ระบบต้องรองรับการตั้งค่า SQL Server |
| **Input** | Instance, Database, Auth |
| **Output** | SQL Server settings saved |
| **Config** | `sql_local_instance`, `sql_local_database` |
| **Priority** | High |

#### FR-SET-013: ตั้งค่า PostgreSQL
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-013 |
| **ชื่อ** | ตั้งค่า PostgreSQL |
| **Description** | ระบบต้องรองรับการตั้งค่า PostgreSQL |
| **Input** | Host, Port, Database |
| **Output** | PostgreSQL settings saved |
| **Config** | `pg_local_host`, `pg_local_port`, `pg_local_database` |
| **Priority** | High |

---

### 2.4 UI Settings

#### FR-SET-014: Dark Mode
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-014 |
| **ชื่อ** | Dark Mode |
| **Description** | ระบบต้องรองรับ Dark Mode |
| **Input** | DarkMode (0/1) |
| **Output** | UI theme changed |
| **Config** | `DARKMODE` |
| **Priority** | Medium |

#### FR-SET-015: Grid Layout Configuration
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-015 |
| **ชื่อ** | Grid Layout Configuration |
| **Description** | ระบบต้องรองรับการจัด layout DataGrid แบบกำหนดเอง |
| **Input** | Grid layout settings |
| **Output** | Grid layout changed |
| **Config** | `GridLayouts.json` |
| **Priority** | Medium |

---

### 2.5 Feature Settings

#### FR-SET-016: QR Code Due Date
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-016 |
| **ชื่อ** | QR Code Due Date |
| **Description** | ระบบต้องรองรับ QR Code บนใบเสร็จ |
| **Input** | QR Code setting |
| **Output** | QR Code appears on receipt |
| **Config** | `gb_qr_code_due_date` |
| **Priority** | Low |

#### FR-SET-017: Video Display
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-SET-017 |
| **ชื่อ** | Video Display |
| **Description** | ระบบต้องรองรับ Video Display |
| **Input** | Video path, Display time |
| **Output** | Video plays on display |
| **Config** | `display_open_vdo`, `display_path_vdo` |
| **Priority** | Low |

---

## 3. Non-Functional Requirements

### NFR-SET-001: Settings Persistence
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-SET-001 |
| **ชื่อ** | การเก็บรักษาการตั้งค่า |
| **Description** | การตั้งค่าต้องถูกเก็บในไฟล์ config อย่างถาวร |
| **Measurement** | Settings persist after restart |

### NFR-SET-002: Settings Validation
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-SET-002 |
| **ชื่อ** | การตรวจสอบการตั้งค่า |
| **Description** | ระบบต้องตรวจสอบความถูกต้องของการตั้งค่าก่อนบันทึก |
| **Measurement** | Invalid settings rejected |

---

## 4. Configuration Reference

### system.ini — [Terminal]

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `terminalno` | P01 | รหัส Terminal |
| `posid` | 1110000AA | รหัส POS |
| `f6_active` | 1 | เปิด/ปิด F6 |
| `cash_drawer_com_port` | NONE | COM Port เงินสด |
| `cash_drawer_usb_port_id` | 7 | USB Port เงินสด |
| `cash_drawer_send_type` | PIN5 | ประเภทส่งสัญญาณ |
| `tmu_language_code` | KU | ภาษา TMU |
| `tmu` | TMU220A | รุ่น Printer |
| `tmu_com_port` | NONE | COM Port Printer |
| `edc_name` | HYPERCOM | รุ่น EDC |
| `edc_port` | NONE | COM Port EDC |

### system.ini — [System]

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `profile_string` | Firebird | ฐานข้อมูลที่ใช้ |
| `user_id` | sa | ผู้ใช้ |
| `language` | TH | ภาษาหลัก |
| `language_on_employee` | EN | ภาษาสำหรับพนักงาน |
| `autobackup` | 1 | Auto Backup |
| `send_mail` | NO | ส่งอีเมลอัตโนมัติ |
| `point` | NO | ระบบแต้ม |
| `currency_decimal_point` | 2 | ทศนิยมสกุลเงิน |

### system.ini — [DatabaseConnection]

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `conn_mode` | Local | โหมดเชื่อมต่อ |
| `db_type` | Firebird | ประเภทฐานข้อมูล |
| `fb_local_database` | C:\...\FBMAXX.FDB | Firebird Local Path |
| `fb_lan_host` | localhost | Firebird LAN Host |
| `fb_lan_port` | 3053 | Firebird LAN Port |
| `sql_local_instance` | .\SQLEXPRESS | SQL Server Instance |
| `sql_local_database` | PROMAXXS | SQL Database Name |
| `pg_local_host` | localhost | PostgreSQL Host |
| `pg_local_port` | 5432 | PostgreSQL Port |

### system.ini — [UI]

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `LANGUAGE` | TH | ภาษา UI |
| `DARKMODE` | 0 | Dark Mode (0=Light, 1=Dark) |

---

## 5. Business Rules

### BR-SET-001: Database Switch
```
IF db_type changed THEN
  Restart application required
  Test connection before apply
```

### BR-SET-002: Language Change
```
IF language changed THEN
  Restart application required
  Load resource file for new language
```

### BR-SET-003: Dark Mode Toggle
```
IF DARKMODE changed THEN
  Reload UI theme
  No restart required
```

### BR-SET-004: Password Encryption
```
ALL database passwords MUST be encrypted using AES
NEVER store plain text passwords
```
