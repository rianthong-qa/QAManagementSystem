# Requirements — โมดูล Utility

> อ้างอิง: `System-Analysis.md` section 3.8, 6.4, 6.7
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Utility รับผิดชอบเครื่องมือเสริมต่างๆ ของระบบ ตามที่ระบุใน System-Analysis.md section 3.8, 6.4, 6.7

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

### 2.1 Barcode Generator

#### FR-UTL-001: พิมพ์บาร์โค้ดสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-UTL-001 |
| **ชื่อ** | พิมพ์บาร์โค้ดสินค้า |
| **Description** | ระบบต้องรองรับการพิมพ์บาร์โค้ดสินค้า |
| **Input** | ItemId, Quantity, Label format |
| **Output** | Barcode printed |
| **Barcode Type** | Code 128 |
| **Priority** | High |

#### FR-UTL-002: เลือกรูปแบบ Label
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-UTL-002 |
| **ชื่อ** | เลือกรูปแบบ Label |
| **Description** | ระบบต้องรองรับหลายรูปแบบ Label |
| **Input** | Label format |
| **Output** | Label format selected |
| **Formats** | BP01001 (3.4x2.0), BP01002 (2.2x1.2), BP01003 (5x5) |
| **Priority** | High |

#### FR-UTL-003: ตั้งค่าความเข้ม/ความเร็ว
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-UTL-003 |
| **ชื่อ** | ตั้งค่าความเข้ม/ความเร็ว |
| **Description** | ระบบต้องรองรับการตั้งค่าความเข้มและความเร็วพิมพ์ |
| **Input** | Darkness, Speed |
| **Output** | Print settings saved |
| **Config** | `darkness`, `speed` |
| **Priority** | Low |

---

### 2.2 Data Export/Import

#### FR-UTL-004: ส่งออกข้อมูลเป็น Excel
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-UTL-004 |
| **ชื่อ** | ส่งออกข้อมูลเป็น Excel |
| **Description** | ระบบต้องรองรับการส่งออกข้อมูลเป็น Excel |
| **Input** | Data type, Filters |
| **Output** | Excel file |
| **Library** | ClosedXML |
| **Priority** | High |

#### FR-UTL-005: นำเข้าข้อมูลจาก Excel
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-UTL-005 |
| **ชื่อ** | นำเข้าข้อมูลจาก Excel |
| **Description** | ระบบต้องรองรับการนำเข้าข้อมูลจาก Excel |
| **Input** | Excel file |
| **Output** | Data imported |
| **Library** | ExcelDataReader |
| **Priority** | High |

---

### 2.3 Data Maintenance

#### FR-UTL-006: Backup ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-UTL-006 |
| **ชื่อ** | Backup ข้อมูล |
| **Description** | ระบบต้องรองรับการ Backup ข้อมูล |
| **Input** | Backup path |
| **Output** | Backup file created |
| **Priority** | High |

#### FR-UTL-007: Restore ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-UTL-007 |
| **ชื่อ** | Restore ข้อมูล |
| **Description** | ระบบต้องรองรับการ Restore ข้อมูล |
| **Input** | Backup file |
| **Output** | Data restored |
| **Priority** | High |

#### FR-UTL-008: Reindex ฐานข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-UTL-008 |
| **ชื่อ** | Reindex ฐานข้อมูล |
| **Description** | ระบบต้องรองรับการ Reindex ฐานข้อมูล |
| **Input** | None |
| **Output** | Index rebuilt |
| **Priority** | Medium |

---

## 3. Non-Functional Requirements

### NFR-UTL-001: Barcode Print Speed
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-UTL-001 |
| **ชื่อ** | ความเร็วพิมพ์บาร์โค้ด |
| **Description** | การพิมพ์บาร์โค้ดต้องเสร็จสิ้นภายใน 5 วินาที |
| **Measurement** | Print time < 5s |

### NFR-UTL-002: Data Export Accuracy
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-UTL-002 |
| **ชื่อ** | ความถูกต้องของข้อมูลส่งออก |
| **Description** | ข้อมูลส่งออกต้องตรงกับข้อมูลจริง 100% |
| **Measurement** | Data accuracy = 100% |

### NFR-UTL-003: Backup File Integrity
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-UTL-003 |
| **ชื่อ** | ความสมบูรณ์ของไฟล์ Backup |
| **Description** | ไฟล์ Backup ต้องสามารถ Restore ได้สำเร็จ |
| **Measurement** | Restore success rate = 100% |

---

## 4. Data Requirements

### DR-UTL-001: Barcode Configuration
| Field | Type | Description |
|-------|------|-------------|
| barcode_type | INT | ประเภทบาร์โค้ด |
| bar_type | INT | โค้ด (128) |
| width | INT | ความกว้าง |
| height | INT | ความสูง |
| speed | DECIMAL | ความเร็วพิมพ์ |
| darkness | INT | ความเข้ม |
| font_size | INT | ขนาดตัวอักษร |
| font_name | VARCHAR | ฟอนต์ |
| form_name | VARCHAR | รูปแบบ Label |

### DR-UTL-002: Label Formats
| Format | ขนาด | ดวง/แถว |
|--------|------|---------|
| BP01001 | 3.4 x 2.0 ซม. | 3 ดวง |
| BP01002 | 2.2 x 1.2 ซม. | 4 ดวง |
| BP01003 | 5 x 5 ซม. | 2 ดวง |

---

## 5. Business Rules

### BR-UTL-001: Barcode Generation
```
BarcodeData = ItemId + Price + Weight (optional)
BarcodeFormat = Code128
```

### BR-UTL-002: Label Layout
```
FOR EACH label format:
  Calculate X,Y position for each field
  Apply font size and spacing
```

### BR-UTL-003: Data Import Validation
```
FOR EACH row IN Excel file:
  Validate required fields
  Check data types
  Check for duplicates
  IF validation fails THEN
    Skip row and log error
```

---

## 6. Configuration Reference

### barcode.ini

| Section | Key | ค่า Default | คำอธิบาย |
|---------|-----|------------|----------|
| [Type] | barcode_type | 2 | ประเภทบาร์โค้ด |
| [Type] | bar_type | 128 | โค้ด 128 |
| [Type] | width_3 | 102 | ความกว้าง 3.4x2.0 |
| [Type] | height_3 | 20 | ความสูง 3.4x2.0 |
| [Type] | speed | 3.0 | ความเร็วพิมพ์ |
| [Type] | darkness | 10 | ความเข้ม |
| [Type] | font_size | 20 | ขนาดตัวอักษร |
| [Type] | font_name | AngsanaUPC | ฟอนต์ |
| [Type] | form_name | BP01001 | รูปแบบ Label |
| [Code] | select_item | 3 | เลือกสินค้า |
| [Printer] | printer_type | 0 | ประเภทเครื่องพิมพ์ |
