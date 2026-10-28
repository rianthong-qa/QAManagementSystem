# QA Test Plan — โมดูล Data & Database

> ProMaxx 21.0.0-beta.1
> วันที่สร้าง: 17 สิงหาคม 2569

---

## 1. ภาพรวมโมดูล

โมดูล Data & Database รับผิดชอบการจัดการฐานข้อมูลทั้งหมดของระบบ

### 1.1 โครงสร้างโมดูล

```
Promaxxs.Data
├── Database Connection     ─── การเชื่อมต่อฐานข้อมูล
├── Repository Layer        ─── Data Access Layer
├── Migration               ─── การอัพเกรด Schema
├── Encryption              ─── การเข้ารหัสข้อมูล
└── Cache Layer             ─── การแคชข้อมูล
```

---

## 2. Database Support

### 2.1 ฐานข้อมูลที่รองรับ

| ฐานข้อมูล | ไดรเวอร์ | สถานะ |
|-----------|---------|-------|
| Firebird SQL | FirebirdSql.Data.FirebirdClient | **หลัก** |
| SQL Server | Microsoft.Data.SqlClient | รองรับ |
| PostgreSQL | (ผ่าน Npgsql) | รองรับ |

### 2.2 Firebird SQL Details

| รายการ | ข้อมูล |
|--------|--------|
| Driver | FirebirdSql.Data.FirebirdClient |
| Local DB | `C:\SeniorSoft ProMaxx\FBMAXX.FDB` |
| LAN DB | `C:\SeniorSoft ProMaxx\FBMAXX2.FDB` |
| LAN Port | 3053 |
| Local User | seniorsoft |
| LAN User | SYSDBA |
| Library | dbup-core.dll, dbup-firebird.dll |

### 2.3 SQL Server Details

| รายการ | ข้อมูล |
|--------|--------|
| Driver | Microsoft.Data.SqlClient |
| Instance | `.\SQLEXPRESS` |
| Database | PROMAXXS |
| Port | 1433 |
| Auth | SQL Authentication |

### 2.4 PostgreSQL Details

| รายการ | ข้อมูล |
|--------|--------|
| Host | localhost |
| Port | 5432 |
| Database | promaxxs |
| User | postgres |

---

## 3. Test Cases

### 3.1 Database Connection

#### TC-DB-CONN-001: เชื่อมต่อ Firebird Local
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง conn_mode = Local<br>2. ตั้ง db_type = Firebird<br>3. รีสตาร์ทระบบ |
| **Expected Result** | เชื่อมต่อสำเร็จ แสดงหน้าหลัก |
| **Priority** | High |

#### TC-DB-CONN-002: เชื่อมต่อ Firebird LAN
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง conn_mode = LAN<br>2. ตั้ง fb_lan_host, port, database<br>3. รีสตาร์ทระบบ |
| **Expected Result** | เชื่อมต่อสำเร็จ |
| **Priority** | High |

#### TC-DB-CONN-003: เชื่อมต่อ SQL Server
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง db_type = SQL<br>2. ตั้ง sql instance, database<br>3. รีสตาร์ทระบบ |
| **Expected Result** | เชื่อมต่อสำเร็จ |
| **Priority** | High |

#### TC-DB-CONN-004: เชื่อมต่อ PostgreSQL
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง pg host, port, database<br>2. รีสตาร์ทระบบ |
| **Expected Result** | เชื่อมต่อสำเร็จ |
| **Priority** | High |

#### TC-DB-CONN-005: เชื่อมต่อด้วยรหัสผ่านผิด
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ป้อนรหัสผ่านผิด |
| **Expected Result** | แสดง Error "Invalid username or password" |
| **Priority** | High |

#### TC-DB-CONN-006: เชื่อมต่อฐานข้อมูลที่ไม่มีอยู่
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ป้อน database path ผิด |
| **Expected Result** | แสดง Error "Database not found" |
| **Priority** | High |

---

### 3.2 Password Encryption

#### TC-DB-ENC-001: ตรวจสอบการเข้ารหัสรหัสผ่าน
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ดูไฟล์ system.ini<br>2. ตรวจสอบค่ารหัสผ่าน |
| **Expected Result** | รหัสผ่านถูกเข้ารหัส (AES) ไม่ใช่ Plain Text |
| **Priority** | High |

#### TC-DB-ENC-002: เปลี่ยนรหัสผ่านฐานข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยนรหัสผ่านในระบบ<br>2. รีสตาร์ท<br>3. ล็อกอินใหม่ |
| **Expected Result** | ใช้รหัสผ่านใหม่ได้สำเร็จ |
| **Priority** | High |

---

### 3.3 Data Operations

#### TC-DB-DATA-001: INSERT ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เพิ่มสินค้าใหม่<br>2. ตรวจสอบใน DB |
| **Expected Result** | ข้อมูลถูก INSERT ลงตารางถูกต้อง |
| **Priority** | High |

#### TC-DB-DATA-002: SELECT ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ค้นหาสินค้า<br>2. ตรวจสอบผลลัพธ์ |
| **Expected Result** | ข้อมูลที่ SELECT มาถูกต้อง |
| **Priority** | High |

#### TC-DB-DATA-003: UPDATE ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. แก้ไขข้อมูลสินค้า<br>2. บันทึก<br>3. ตรวจสอบใน DB |
| **Expected Result** | ข้อมูลถูก UPDATE ตรงกัน |
| **Priority** | High |

#### TC-DB-DATA-004: DELETE ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ลบสินค้า<br>2. ตรวจสอบใน DB |
| **Expected Result** | ข้อมูลถูกลบ (Soft/Hard Delete ตาม design) |
| **Priority** | High |

#### TC-DB-DATA-005: Transaction (Commit)
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ทำธุรกรรมที่มีหลาย INSERT<br>2. สำเร็จทั้งหมด |
| **Expected Result** | ข้อมูลถูก COMMIT ทั้งหมด |
| **Priority** | High |

#### TC-DB-DATA-006: Transaction (Rollback)
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ทำธุรกรรมที่มี ERROR<br>2. System จับ error ได้ |
| **Expected Result** | ข้อมูลถูก ROLLBACK ไม่มีผลก่อนหน้า |
| **Priority** | High |

---

### 3.4 Concurrency

#### TC-DB-CONC-001: Concurrent Read
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด POS 2 เครื่อง<br>2. อ่านข้อมูลสินค้าพร้อมกัน |
| **Expected Result** | อ่านได้ทั้งคู่ ไม่ error |
| **Priority** | High |

#### TC-DB-CONC-002: Concurrent Write
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด POS 2 เครื่อง<br>2. ขายสินค้าตัวเดียวกันพร้อมกัน |
| **Expected Result** | Stock ลดถูกต้อง ไม่ negative |
| **Priority** | High |

#### TC-DB-CONC-003: Lock Conflict
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. POS A แก้ไขสินค้าค้างอยู่<br>2. POS B แก้ไขสินค้าตัวเดียวกัน |
| **Expected Result** | แสดง Warning "Record is being edited" |
| **Priority** | High |

---

### 3.5 Backup & Restore

#### TC-DB-BK-001: Backup ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. กด Backup<br>2. เลือกที่เก็บ<br>3. ยืนยัน |
| **Expected Result** | ไฟล์ Backup ถูกสร้าง |
| **Priority** | High |

#### TC-DB-BK-002: Restore ข้อมูล
| รายการ | รายhiênหด |
|--------|------------|
| **Steps** | 1. เลือกไฟล์ Backup<br>2. กด Restore<br>3. ยืนยัน |
| **Expected Result** | ข้อมูลถูก Restore สำเร็จ |
| **Priority** | High |

#### TC-DB-BK-003: Auto Backup
| รายการ | รายละเอียด |
|--------|------------|
| **Config** | `autobackup = 1` |
| **Steps** | 1. ทำธุรกรรมสำเร็จ<br>2. ตรวจสอบ Backup |
| **Expected Result** | Backup ถูกสร้างอัตโนมัติ |
| **Priority** | High |

#### TC-DB-BK-004: Restore จากไฟล์เสีย
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกไฟล์ Backup ที่เสียหาย<br>2. กด Restore |
| **Expected Result** | แสดง Error "Invalid backup file" |
| **Priority** | Medium |

---

### 3.6 Multi-Database Operations

#### TC-DB-MULTI-001: เปลี่ยนฐานข้อมูล Runtime
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ปิดระบบ<br>2. เปลี่ยน db_type<br>3. เปิดระบบใหม่ |
| **Expected Result** | ระบบเชื่อมต่อ DB ใหม่สำเร็จ |
| **Priority** | High |

#### TC-DB-MULTI-002: ข้อมูลตรงกันทุก Database
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เพิ่มสินค้า 1 รายการ<br>2. เปลี่ยนไป Firebird<br>3. เปลี่ยนไป SQL Server<br>4. ตรวจสอบ |
| **Expected Result** | ข้อมูลตรงกันทุก Database |
| **Priority** | Medium |

---

## 4. Encryption Details

### 4.1 Password Format

```
AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA... (Base64 encoded AES encrypted)
```

### 4.2 Test Cases

#### TC-DB-SEC-001: ตรวจสอบไม่มี Plain Text Password
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ค้นหาไฟล์ config ทั้งหมด<br>2. ค้นหาคำว่า password |
| **Expected Result** | ไม่พบ Plain Text Password ในไฟล์ config |
| **Priority** | High |

---

## 5. Database Schema (Tables Expected)

| Table | คำอธิบาย |
|-------|----------|
| ITEM | ข้อมูลสินค้า |
| PERSON | ข้อมูลบุคคล |
| TRANSACTION | ข้อมูลธุรกรรม |
| TRANDETAIL | รายละเอียดธุรกรรม |
| STOCK | ข้อมูล Stock |
| STOCKMOVEMENT | การเคลื่อนไหว Stock |
| PROMOTION | ข้อมูลโปรโมชัน |
| EMAILREPORT | ตั้งค่าอีเมลรายงาน |
| BRANCH | ข้อมูลสาขา |
| WAREHOUSE | ข้อมูลคลัง |

---

## 6. Regression Test Checklist

- [ ] เชื่อมต่อ Firebird Local/LAN ได้
- [ ] เชื่อมต่อ SQL Server ได้
- [ ] เชื่อมต่อ PostgreSQL ได้
- [ ] แสดง Error เมื่อเชื่อมต่อผิด
- [ ] รหัสผ่านถูกเข้ารหัส (ไม่ใช่ Plain Text)
- [ ] INSERT/SELECT/UPDATE/DELETE ทำงานถูกต้อง
- [ ] Transaction Commit ทำงานถูกต้อง
- [ ] Transaction Rollback ทำงานถูกต้อง
- [ ] Concurrent Read ทำงานได้
- [ ] Concurrent Write ไม่ทำให้ Stock ผิด
- [ ] Lock Conflict แสดง Warning ถูกต้อง
- [ ] Backup ไฟล์ถูกต้อง
- [ ] Restore ข้อมูลสำเร็จ
- [ ] Auto Backup ทำงานหลังทำธุรกรรม
- [ ] เปลี่ยน Database ได้ทั้ง 3 ประเภท
