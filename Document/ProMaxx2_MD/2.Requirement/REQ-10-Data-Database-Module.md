# Requirements — โมดูล Data & Database

> อ้างอิง: `System-Analysis.md` section 4, 4.1, 4.2, 4.3
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Data & Database รับผิดชอบการจัดการฐานข้อมูลทั้งหมดของระบบ ตามที่ระบุใน System-Analysis.md section 4, 4.1, 4.2, 4.3

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

### 2.1 Database Connection

#### FR-DB-001: เชื่อมต่อ Firebird SQL
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-001 |
| **ชื่อ** | เชื่อมต่อ Firebird SQL |
| **Description** | ระบบต้องเชื่อมต่อ Firebird SQL ได้ทั้ง Local และ LAN |
| **Input** | Database path, Username, Password |
| **Output** | Connection established |
| **Library** | FirebirdSql.Data.FirebirdClient |
| **Priority** | Critical |

#### FR-DB-002: เชื่อมต่อ SQL Server
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-002 |
| **ชื่อ** | เชื่อมต่อ SQL Server |
| **Description** | ระบบต้องเชื่อมต่อ SQL Server ได้ |
| **Input** | Instance, Database, Auth |
| **Output** | Connection established |
| **Library** | Microsoft.Data.SqlClient |
| **Priority** | High |

#### FR-DB-003: เชื่อมต่อ PostgreSQL
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-003 |
| **ชื่อ** | เชื่อมต่อ PostgreSQL |
| **Description** | ระบบต้องเชื่อมต่อ PostgreSQL ได้ |
| **Input** | Host, Port, Database |
| **Output** | Connection established |
| **Priority** | High |

---

### 2.2 Data Operations

#### FR-DB-004: CRUD Operations
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-004 |
| **ชื่อ** | CRUD Operations |
| **Description** | ระบบต้องรองรับ Create, Read, Update, Delete |
| **Input** | SQL operations |
| **Output** | Data operations completed |
| **Priority** | Critical |

#### FR-DB-005: Transaction Management
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-005 |
| **ชื่อ** | Transaction Management |
| **Description** | ระบบต้องรองรับ Database Transaction (Commit/Rollback) |
| **Input** | Transaction operations |
| **Output** | Transaction committed/rolled back |
| **Priority** | Critical |

---

### 2.3 Password Encryption

#### FR-DB-006: เข้ารหัสรหัสผ่าน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-006 |
| **ชื่อ** | เข้ารหัสรหัสผ่าน |
| **Description** | ระบบต้องเข้ารหัสรหัสผ่านฐานข้อมูลด้วย AES |
| **Input** | Plain text password |
| **Output** | Encrypted password |
| **Algorithm** | AES |
| **Priority** | Critical |

#### FR-DB-007: ถอดรหัสรหัสผ่าน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-007 |
| **ชื่อ** | ถอดรหัสรหัสผ่าน |
| **Description** | ระบบต้องถอดรหัสรหัสผ่านฐานข้อมูล |
| **Input** | Encrypted password |
| **Output** | Plain text password (in memory only) |
| **Priority** | Critical |

---

### 2.4 Backup & Restore

#### FR-DB-008: Backup ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-008 |
| **ชื่อ** | Backup ข้อมูล |
| **Description** | ระบบต้อง Backup ข้อมูลได้ |
| **Input** | Backup path |
| **Output** | Backup file created |
| **Priority** | High |

#### FR-DB-009: Restore ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-009 |
| **ชื่อ** | Restore ข้อมูล |
| **Description** | ระบบต้อง Restore ข้อมูลได้ |
| **Input** | Backup file |
| **Output** | Data restored |
| **Priority** | High |

#### FR-DB-010: Auto Backup
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-010 |
| **ชื่อ** | Auto Backup |
| **Description** | ระบบต้อง Backup อัตโนมัติหลังทำธุรกรรม |
| **Input** | autobackup = 1 |
| **Output** | Backup file created automatically |
| **Priority** | High |

---

### 2.5 Schema Management

#### FR-DB-011: Database Migration
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-011 |
| **ชื่อ** | Database Migration |
| **Description** | ระบบต้องรองรับการอัพเกรด Schema |
| **Input** | Migration scripts |
| **Output** | Schema upgraded |
| **Library** | dbup-core, dbup-firebird |
| **Priority** | High |

---

## 3. Non-Functional Requirements

### NFR-DB-001: Connection Pool
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-DB-001 |
| **ชื่อ** | Connection Pool |
| **Description** | ระบบต้องใช้ Connection Pool เพื่อ performance |
| **Measurement** | Connection pool enabled |

### NFR-DB-002: Connection Timeout
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-DB-002 |
| **ชื่อ** | Connection Timeout |
| **Description** | การเชื่อมต่อต้อง timeout ภายใน 30 วินาที |
| **Measurement** | Timeout = 30s |

### NFR-DB-003: Data Encryption
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-DB-003 |
| **ชื่อ** | Data Encryption |
| **Description** | ข้อมูล敏感ต้องถูกเข้ารหัส |
| **Measurement** | AES encryption |

### NFR-DB-004: Concurrent Access
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-DB-004 |
| **ชื่อ** | Concurrent Access |
| **Description** | ต้องรองรับการเข้าถึงพร้อมกันหลาย POS |
| **Measurement** | No data corruption |

---

## 4. Data Requirements

### DR-DB-001: Database Schema

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

### DR-DB-002: Connection Strings

| Database | Local | LAN |
|----------|-------|-----|
| Firebird | `C:\SeniorSoft ProMaxx\FBMAXX.FDB` | `FBMAXX2.FDB` (port 3053) |
| SQL Server | `.\SQLEXPRESS` / `PROMAXXS` | port 1433 |
| PostgreSQL | localhost / `promaxxs` | port 5432 |

---

## 5. Business Rules

### BR-DB-001: Database Selection
```
SWITCH db_type:
  CASE "Firebird":
    Use FirebirdSql.Data.FirebirdClient
  CASE "SQL":
    Use Microsoft.Data.SqlClient
  CASE "PostgreSQL":
    Use Npgsql
```

### BR-DB-002: Password Storage
```
encrypted_password = AES_Encrypt(plain_password)
Store encrypted_password IN config file
NEVER store plain_password
```

### BR-DB-003: Connection Mode
```
IF conn_mode = "Local" THEN
  Use local database path
ELSE IF conn_mode = "LAN" THEN
  Use LAN host, port, database
```

### BR-DB-004: Backup Naming
```
backup_filename = "ProMaxx_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".fdb"
```

---

## 6. Configuration Reference

### system.ini — [DatabaseConnection]

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `conn_mode` | Local | โหมดเชื่อมต่อ |
| `db_type` | Firebird | ประเภทฐานข้อมูล |
| `is_64bit` | 1 | 64-bit mode |
| `fb_local_database` | C:\SeniorSoft ProMaxx\FBMAXX.FDB | Firebird Local Path |
| `fb_local_username` | seniorsoft | Firebird Local User |
| `fb_local_password` | (encrypted) | Firebird Local Password |
| `fb_lan_host` | localhost | Firebird LAN Host |
| `fb_lan_port` | 3053 | Firebird LAN Port |
| `fb_lan_database` | C:\SeniorSoft ProMaxx\FBMAXX2.FDB | Firebird LAN Path |
| `fb_lan_username` | SYSDBA | Firebird LAN User |
| `fb_lan_password` | (encrypted) | Firebird LAN Password |
| `sql_local_instance` | .\SQLEXPRESS | SQL Server Instance |
| `sql_local_database` | PROMAXXS | SQL Database Name |
| `sql_local_auth` | sql | SQL Auth Type |
| `sql_local_username` | sa | SQL User |
| `sql_local_password` | (encrypted) | SQL Password |
| `pg_local_host` | localhost | PostgreSQL Host |
| `pg_local_port` | 5432 | PostgreSQL Port |
| `pg_local_database` | promaxxs | PostgreSQL DB |
| `pg_local_username` | postgres | PostgreSQL User |
| `pg_local_password` | (encrypted) | PostgreSQL Password |

---

## 7. Libraries

| Library | Version | วัตถุประสงค์ |
|---------|---------|-------------|
| FirebirdSql.Data.FirebirdClient | - | Firebird SQL Driver |
| Microsoft.Data.SqlClient | - | SQL Server Driver |
| dbup-core | - | Database Migration |
| dbup-firebird | - | Firebird Migration |
| Newtonsoft.Json | 13.0.3 | JSON Serialization |
