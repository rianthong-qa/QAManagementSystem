# QA Test Plan — โมดูล Settings (ตั้งค่าระบบ)

> ProMaxx 21.0.0-beta.1
> วันที่สร้าง: 17 สิงหาคม 2569

---

## 1. ภาพรวมโมดูล

โมดูล Settings รับผิดชอบการตั้งค่าทั้งหมดของระบบ จัดเก็บใน `config/system.ini`

### 1.1 โครงสร้างโมดูล

```
Promaxxs.Modules.Settings
├── Terminal Settings     ─── ตั้งค่า Terminal/POS
├── System Settings       ─── ตั้งค่าทั่วไป
├── Database Settings     ─── ตั้งค่าฐานข้อมูล
├── UI Settings           ─── ตั้งค่า UI
├── Service Settings      ─── ตั้งค่า Service อัตโนมัติ
└── Feature Settings      ─── ตั้งค่าฟีเจอร์เสริม
```

---

## 2. โมดูลย่อย: Terminal Settings

### 2.1 Configuration Keys

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

### 2.2 Test Cases

#### TC-SET-TRM-001: เปลี่ยน Terminal Number
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน terminalno = P02<br>2. บันทึก<br>3. รีสตาร์ทระบบ |
| **Expected Result** | Terminal ใหม่แสดง P02 |
| **Priority** | High |

#### TC-SET-TRM-002: เปลี่ยน POS ID
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน posid<br>2. บันทึก |
| **Expected Result** | POS ID เปลี่ยนสำเร็จ |
| **Priority** | High |

#### TC-SET-TRM-003: ตั้งค่า Cash Drawer Port
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน cash_drawer_com_port<br>2. บันทึก |
| **Expected Result** | Cash Drawer เชื่อมต่อผ่าน Port ที่กำหนด |
| **Priority** | Medium |

#### TC-SET-TRM-004: ตั้งค่า Printer TMU
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน tmu_com_port<br>2. บันทึก |
| **Expected Result** | Printer เชื่อมต่อผ่าน Port ที่กำหนด |
| **Priority** | Medium |

---

## 3. โมดูลย่อย: System Settings

### 3.1 Configuration Keys

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `profile_string` | Firebird | ฐานข้อมูลที่ใช้ |
| `user_id` | sa | ผู้ใช้ |
| `language` | TH | ภาษาหลัก |
| `language_on_employee` | EN | ภาษาสำหรับพนักงาน |
| `autobackup` | 1 | Auto Backup |
| `send_mail` | NO | ส่งอีเมลอัตโนมัติ |
| `point` | NO | ระบบแต้ม |
| `update_price` | NO | อัพเดทราคาอัตโนมัติ |
| `currency_decimal_point` | 2 | ทศนิยมสกุลเงิน |

### 3.2 Test Cases

#### TC-SET-SYS-001: เปลี่ยนภาษาหลัก
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน language = EN<br>2. รีสตาร์ทระบบ |
| **Expected Result** | UI แสดงเป็นภาษาอังกฤษ |
| **Priority** | High |

#### TC-SET-SYS-002: เปลี่ยนฐานข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน profile_string = SQL<br>2. ตั้งค่า SQL connection<br>3. รีสตาร์ทระบบ |
| **Expected Result** | ระบบเชื่อมต่อ SQL Server สำเร็จ |
| **Priority** | High |

#### TC-SET-SYS-003: เปิด Auto Backup
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง autobackup = 1<br>2. ทำธุรกรรม<br>3. ตรวจสอบ Backup |
| **Expected Result** | Backup ถูกสร้างอัตโนมัติ |
| **Priority** | High |

#### TC-SET-SYS-004: เปิดระบบส่งอีเมล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง send_mail = YES<br>2. ทำธุรกรรม |
| **Expected Result** | อีเมลถูกส่งอัตโนมัติ |
| **Priority** | Medium |

#### TC-SET-SYS-005: เปลี่ยนทศนิยมสกุลเงิน
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน currency_decimal_point = 3<br>2. แสดงราคาสินค้า |
| **Expected Result** | ราคาแสดงทศนิยม 3 ตำแหน่ง |
| **Priority** | Medium |

#### TC-SET-SYS-006: ตั้งค่า OSK (On-Screen Keyboard)
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง osk = 1<br>2. คลิกช่องกรอกข้อมูล |
| **Expected Result** | Keyboard บนหน้าจอแสดงขึ้นมา |
| **Priority** | Low |

---

## 4. โมดูลย่อย: Database Settings

### 4.1 Configuration Keys

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `conn_mode` | Local | โหมดเชื่อมต่อ |
| `db_type` | Firebird | ประเภทฐานข้อมูล |
| `is_64bit` | 1 | 64-bit mode |
| `fb_local_database` | C:\...\FBMAXX.FDB | Firebird Local Path |
| `fb_lan_host` | localhost | Firebird LAN Host |
| `fb_lan_port` | 3053 | Firebird LAN Port |
| `sql_local_instance` | .\SQLEXPRESS | SQL Server Instance |
| `sql_local_database` | PROMAXXS | SQL Database Name |
| `pg_local_host` | localhost | PostgreSQL Host |
| `pg_local_port` | 5432 | PostgreSQL Port |
| `pg_local_database` | promaxxs | PostgreSQL DB |

### 4.2 Test Cases

#### TC-SET-DB-001: เชื่อมต่อ Firebird Local
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง conn_mode = Local<br>2. ตั้ง db_type = Firebird<br>3. รีสตาร์ทระบบ |
| **Expected Result** | เชื่อมต่อ Firebird Local สำเร็จ |
| **Priority** | High |

#### TC-SET-DB-002: เชื่อมต่อ Firebird LAN
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง conn_mode = LAN<br>2. ตั้ง fb_lan_host, port<br>3. รีสตาร์ทระบบ |
| **Expected Result** | เชื่อมต่อ Firebird LAN สำเร็จ |
| **Priority** | High |

#### TC-SET-DB-003: เชื่อมต่อ SQL Server
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง db_type = SQL<br>2. ตั้ง sql_local_instance, database<br>3. รีสตาร์ทระบบ |
| **Expected Result** | เชื่อมต่อ SQL Server สำเร็จ |
| **Priority** | High |

#### TC-SET-DB-004: เชื่อมต่อ PostgreSQL
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง pg_local_host, port, database<br>2. รีสตาร์ทระบบ |
| **Expected Result** | เชื่อมต่อ PostgreSQL สำเร็จ |
| **Priority** | High |

#### TC-SET-DB-005: เชื่อมต่อด้วยรหัสผ่านผิด
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ป้อนรหัสผ่านผิด<br>2. พยายามเชื่อมต่อ |
| **Expected Result** | แสดง Error Message |
| **Priority** | High |

---

## 5. โมดูลย่อย: UI Settings

### 5.1 Configuration Keys

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `LANGUAGE` | TH | ภาษา UI |
| `DARKMODE` | 0 | Dark Mode (0=Light, 1=Dark) |

### 5.2 Test Cases

#### TC-SET-UI-001: เปลี่ยนเป็น Dark Mode
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง DARKMODE = 1<br>2. รีสตาร์ทระบบ |
| **Expected Result** | UI เปลี่ยนเป็น Dark Mode |
| **Priority** | Medium |

#### TC-SET-UI-002: เปลี่ยนกลับเป็น Light Mode
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง DARKMODE = 0<br>2. รีสตาร์ทระบบ |
| **Expected Result** | UI เปลี่ยนกลับเป็น Light Mode |
| **Priority** | Medium |

#### TC-SET-UI-003: เปลี่ยนภาษา UI
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน LANGUAGE = EN<br>2. รีสตาร์ทระบบ |
| **Expected Result** | UI แสดงเป็นภาษาอังกฤษ |
| **Priority** | Medium |

---

## 6. โมดูลย่อย: Service Settings

### 6.1 Configuration Keys

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `auto_start_service1` | NO | เริ่ม Service1 อัตโนมัติ |
| `type` | 1 | ประเภท Service |
| `leniency` | 10 | ค่าความยืดหยุ่น |
| `interval` | 300 | ช่วงเวลา (วินาที) |

### 6.2 Test Cases

#### TC-SET-SVC-001: เปิด Auto Start Service
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง auto_start_service1 = YES<br>2. รีสตาร์ทระบบ |
| **Expected Result** | Service เริ่มทำงานอัตโนมัติ |
| **Priority** | Medium |

#### TC-SET-SVC-002: ตั้งค่า Interval Service
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน interval<br>2. บันทึก |
| **Expected Result** | Service ทำงานตาม interval ที่กำหนด |
| **Priority** | Low |

---

## 7. Feature Settings

### 7.1 Configuration Keys

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `color_ink` | 1 | หมึกสี |
| `gb_qr_code_due_date` | 1 | QR Code Due Date |
| `display_open_vdo` | 0 | เปิด Video Display |
| `select_pos_tview` | 0 | เลือก POS View |
| `showquantity` | 1 | แสดงจำนวน |
| `ka920_on_off` | 0 | KA920 Scale |

### 7.2 Test Cases

#### TC-SET-FTR-001: เปิด QR Code Due Date
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง gb_qr_code_due_date = 1<br>2. พิมพ์ใบเสร็จ |
| **Expected Result** | ใบเสร็จมี QR Code พร้อม Due Date |
| **Priority** | Medium |

#### TC-SET-FTR-002: ตั้งค่า Video Display
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง display_open_vdo = 1<br>2. ตั้ง path vdo<br>3. เปิด POS |
| **Expected Result** | Video Display ทำงาน |
| **Priority** | Low |

---

## 8. Regression Test Checklist

- [ ] เปลี่ยน Terminal ID ได้ถูกต้อง
- [ ] เปลี่ยน POS ID ได้ถูกต้อง
- [ ] เปลี่ยนภาษาได้ทั้ง TH/EN
- [ ] Dark Mode ทำงานถูกต้อง
- [ ] Auto Backup ทำงานหลังทำธุรกรรม
- [ ] เชื่อมต่อ Firebird ได้ทั้ง Local/LAN
- [ ] เชื่อมต่อ SQL Server ได้
- [ ] เชื่อมต่อ PostgreSQL ได้
- [ ] แสดง Error เมื่อเชื่อมต่อผิด
- [ ] Service Settings ทำงานตาม configuration
- [ ] QR Code Due Date ปรากฏบนใบเสร็จ
- [ ] OSK แสดงเมื่อตั้งค่า osk = 1
