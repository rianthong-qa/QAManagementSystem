# Requirements — โมดูล Person (บุคคล/ลูกค้า/พนักงาน)

> อ้างอิง: `System-Analysis.md` section 3.3
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Person รับผิดชอบการจัดการข้อมูลบุคคลที่เกี่ยวข้องกับระบบ POS ประกอบด้วย 3 กลุ่มหลัก ตามที่ระบุใน System-Analysis.md section 3.3

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

### 2.1 Customer (ลูกค้า)

#### FR-PRSN-001: จัดการข้อมูลลูกค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-001 |
| **ชื่อ** | จัดการข้อมูลลูกค้า |
| **Description** | ระบบต้องรองรับการเพิ่ม/แก้ไข/ลบ/ค้นหาข้อมูลลูกค้า |
| **Input** | CustomerId, Name, Address, Phone, Email, Category |
| **Output** | ข้อมูลลูกค้าที่บันทึกสำเร็จ |
| **Business Rule** | CustomerId ต้องไม่ซ้ำกัน |
| **Priority** | Critical |

#### FR-PRSN-002: จัดกลุ่มลูกค้า (Category)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-002 |
| **ชื่อ** | จัดกลุ่มลูกค้า |
| **Description** | ระบบต้องรองรับการจัดกลุ่มลูกค้า |
| **Input** | CategoryName |
| **Output** | กลุ่มลูกค้าที่บันทึกสำเร็จ |
| **Grid** | IsSelected, Ordinary, Categoryname |
| **Priority** | High |

#### FR-PRSN-003: ผูกบัญชีลูกค้า (Account Chart)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-003 |
| **ชื่อ** | ผูกบัญชีลูกค้า |
| **Description** | ระบบต้องรองรับการผูกบัญชีลูกค้า |
| **Input** | CustomerId, AccountCode |
| **Output** | ลูกค้าผูกกับบัญชีสำเร็จ |
| **Grid** | Accountcode, Accountname |
| **Priority** | Medium |

#### FR-PRSN-004: ค้นหาลูกค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-004 |
| **ชื่อ** | ค้นหาลูกค้า |
| **Description** | ระบบต้องรองรับการค้นหาลูกค้าด้วย CustomerId, Name, Phone |
| **Input** | Search keyword |
| **Output** | รายการลูกค้าที่ตรงกัน |
| **Priority** | High |

#### FR-PRSN-005: ลงทะเบียนลูกค้า (Member Registration)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-005 |
| **ชื่อ** | ลงทะเบียนลูกค้า |
| **Description** | ระบบต้องรองรับการลงทะเบียนลูกค้าใหม่ |
| **Input** | Customer data |
| **Output** | ลูกค้าใหม่ที่ลงทะเบียนสำเร็จ |
| **Config** | `member_reg_mode`, `member_reg_keypad` |
| **Priority** | High |

---

### 2.2 Staff (พนักงาน)

#### FR-PRSN-006: จัดการข้อมูลพนักงาน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-006 |
| **ชื่อ** | จัดการข้อมูลพนักงาน |
| **Description** | ระบบต้องรองรับการเพิ่ม/แก้ไข/ลบข้อมูลพนักงาน |
| **Input** | StaffId, Name, Position, PIN |
| **Output** | ข้อมูลพนักงานที่บันทึกสำเร็จ |
| **Priority** | Critical |

#### FR-PRSN-007: กำหนด PIN พนักงาน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-007 |
| **ชื่อ** | กำหนด PIN พนักงาน |
| **Description** | ระบบต้องรองรับการกำหนด PIN สำหรับพนักงาน |
| **Input** | StaffId, PIN |
| **Output** | PIN ถูกบันทึก ใช้เข้า POS ได้ |
| **Priority** | High |

#### FR-PRSN-008: จัดกลุ่มพนักงาน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-008 |
| **ชื่อ** | จัดกลุ่มพนักงาน |
| **Description** | ระบบต้องรองรับการจัดกลุ่มพนักงาน |
| **Input** | GroupName |
| **Output** | กลุ่มพนักงานที่บันทึกสำเร็จ |
| **Config** | `sys_person_group` |
| **Priority** | Medium |

---

### 2.3 Supplier (ซัพพลายเออร์)

#### FR-PRSN-009: จัดการข้อมูลซัพพลายเออร์
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-009 |
| **ชื่อ** | จัดการข้อมูลซัพพลายเออร์ |
| **Description** | ระบบต้องรองรับการเพิ่ม/แก้ไข/ลบข้อมูลซัพพลายเออร์ |
| **Input** | SupplierId, Name, Address, Phone, CreditLimit |
| **Output** | ข้อมูลซัพพลายเออร์ที่บันทึกสำเร็จ |
| **Priority** | High |

#### FR-PRSN-010: ผูกบัญชีซัพพลายเออร์
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PRSN-010 |
| **ชื่อ** | ผูกบัญชีซัพพลายเออร์ |
| **Description** | ระบบต้องรองรับการผูกบัญชีซัพพลายเออร์ |
| **Input** | SupplierId, AccountCode |
| **Output** | ซัพพลายเออร์ผูกกับบัญชีสำเร็จ |
| **Priority** | Medium |

---

## 3. Non-Functional Requirements

### NFR-PRSN-001: Data Privacy
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-PRSN-001 |
| **ชื่อ** | ความเป็นส่วนตัวของข้อมูล |
| **Description** | ข้อมูลลูกค้าต้องได้รับการคุ้มครอง |
| **Measurement** | Data encryption at rest |

### NFR-PRSN-002: Search Performance
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-PRSN-002 |
| **ชื่อ** | ประสิทธิภาพการค้นหา |
| **Description** | การค้นหาลูกค้าต้องเสร็จสิ้นภายใน 1 วินาที |
| **Measurement** | Search time < 1s |

---

## 4. Data Requirements

### DR-PRSN-001: Customer Master
| Field | Type | Description |
|-------|------|-------------|
| CustomerId | VARCHAR(PK) | รหัสลูกค้า |
| Name | VARCHAR | ชื่อลูกค้า |
| Address | TEXT | ที่อยู่ |
| Phone | VARCHAR | เบอร์โทร |
| Email | VARCHAR | อีเมล |
| CategoryId | VARCHAR(FK) | กลุ่มลูกค้า |
| AccountCode | VARCHAR(FK) | รหัสบัญชี |
| CreditLimit | DECIMAL | วงเงินเครดิต |
| Status | VARCHAR | สถานะ |

### DR-PRSN-002: Staff Master
| Field | Type | Description |
|-------|------|-------------|
| StaffId | VARCHAR(PK) | รหัสพนักงาน |
| Name | VARCHAR | ชื่อพนักงาน |
| Position | VARCHAR | ตำแหน่ง |
| PIN | VARCHAR | PIN สำหรับ POS |
| GroupId | VARCHAR(FK) | กลุ่มพนักงาน |
| Status | VARCHAR | สถานะ |

### DR-PRSN-003: Supplier Master
| Field | Type | Description |
|-------|------|-------------|
| SupplierId | VARCHAR(PK) | รหัสซัพพลายเออร์ |
| Name | VARCHAR | ชื่อซัพพลายเออร์ |
| Address | TEXT | ที่อยู่ |
| Phone | VARCHAR | เบอร์โทร |
| AccountCode | VARCHAR(FK) | รหัสบัญชี |
| CreditLimit | DECIMAL | วงเงินเครดิต |
| Status | VARCHAR | สถานะ |

---

## 5. Business Rules

### BR-PRSN-001: Customer Deletion
```
IF customer.HasTransactions THEN
  BLOCK deletion
  SHOW error "Cannot delete customer with transactions"
ELSE
  Soft delete customer
```

### BR-PRSN-002: Staff PIN Authentication
```
IF staff.PIN IS NOT NULL THEN
  Allow POS login with PIN
ELSE
  Block POS login
```

### BR-PRSN-003: Person ID for Cash Sale
```
transaction.PersonId = 999  // ค่า default สำหรับ Cash Sale
```

---

## 6. Configuration Reference

| Config Key | Section | ค่า Default | คำอธิบาย |
|------------|---------|------------|----------|
| `member_reg_mode` | System | 0 | โหมดลงทะเบียนลูกค้า |
| `member_reg_keypad` | System | 0 | ใช้ Keypad ลงทะเบียน |
| `sys_person_group` | System | 0 | กลุ่มพนักงาน |
| `person_id_cash` | TranOut | 999 | Person ID สำหรับ Cash Sale |
