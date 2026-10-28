# QA Test Plan — โมดูล Utility

> ProMaxx 21.0.0-beta.1
> วันที่สร้าง: 17 สิงหาคม 2569

---

## 1. ภาพรวมโมดูล

โมดูล Utility รับผิดชอบเครื่องมือเสริมต่างๆ ของระบบ

### 1.1 โครงสร้างโมดูล

```
Promaxxs.Modules.Utility
├── Barcode Generator     ─── สร้างบาร์โค้ด
├── Data Export/Import    ─── ส่งออก/นำเข้าข้อมูล
├── Data Maintenance      ─── บำรุงรักษาข้อมูล
└── System Tools          ─── เครื่องมือระบบ
```

---

## 2. Barcode Generator

### 2.1 Configuration (`barcode.ini`)

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `barcode_type` | 2 | ประเภทบาร์โค้ด |
| `bar_type` | 128 | โค้ด 128 |
| `width_3` | 102 | ความกว้าง 3.4x2.0 |
| `height_3` | 20 | ความสูง 3.4x2.0 |
| `speed` | 3.0 | ความเร็วพิมพ์ |
| `darkness` | 10 | ความเข้ม |
| `font_size` | 20 | ขนาดตัวอักษร |
| `font_name` | AngsanaUPC | ฟอนต์ |
| `form_name` | BP01001 | รูปแบบ Label |

### 2.2 Label Formats

| Format | ขนาด | ดวง/แถว |
|--------|------|---------|
| BP01001 | 3.4 x 2.0 ซม. | 3 ดวง |
| BP01002 | 2.2 x 1.2 ซม. | 4 ดวง |
| BP01003 | 5 x 5 ซม. | 2 ดวง |

### 2.3 Test Cases

#### TC-UTL-BC-001: พิมพ์บาร์โค้ดสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้า<br>2. เลือกรูปแบบ Label<br>3. ระบุจำนวน<br>4. พิมพ์ |
| **Expected Result** | บาร์โค้ดพิมพ์ออกครบถ้วน |
| **Priority** | High |

#### TC-UTL-BC-002: พิมพ์บาร์โค้ดหลายสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้าหลายรายการ<br>2. พิมพ์บาร์โค้ด |
| **Expected Result** | บาร์โค้ดทุกรายการถูกพิมพ์ |
| **Priority** | High |

#### TC-UTL-BC-003: เปลี่ยนรูปแบบ Label
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน form_name = BP01002<br>2. พิมพ์บาร์โค้ด |
| **Expected Result** | บาร์โค้ดพิมพ์ในรูปแบบ 2.2x1.2 |
| **Priority** | Medium |

#### TC-UTL-BC-004: ตั้งค่าความเข้ม/ความเร็ว
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน darkness = 15<br>2. เปลี่ยน speed = 2.0<br>3. พิมพ์ |
| **Expected Result** | บาร์โค้ดพิมพ์เข้มขึ้น ช้าลง |
| **Priority** | Low |

---

## 3. Data Export/Import

### 3.1 Libraries

- **ClosedXML** - Excel file operations
- **ExcelDataReader** - Read Excel files
- **DocumentFormat.OpenXml** - Office document format

### 3.2 Test Cases

#### TC-UTL-EX-001: ส่งออกข้อมูลสินค้าเป็น Excel
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกส่งออกข้อมูลสินค้า<br>2. เลือก Excel format<br>3. บันทึกไฟล์ |
| **Expected Result** | ไฟล์ Excel ถูกสร้าง ข้อมูลครบถ้วน |
| **Priority** | High |

#### TC-UTL-EX-002: ส่งออกข้อมูลลูกค้าเป็น Excel
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกส่งออกข้อมูลลูกค้า<br>2. เลือก Excel format<br>3. บันทึกไฟล์ |
| **Expected Result** | ไฟล์ Excel ถูกสร้าง ข้อมูลครบถ้วน |
| **Priority** | High |

#### TC-UTL-EX-003: นำเข้าข้อมูลสินค้าจาก Excel
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกนำเข้าข้อมูลสินค้า<br>2. เลือกไฟล์ Excel<br>3. ตรวจสอบข้อมูล<br>4. ยืนยันนำเข้า |
| **Expected Result** | ข้อมูลสินค้าถูกนำเข้าสำเร็จ |
| **Priority** | High |

#### TC-UTL-EX-004: นำเข้าข้อมูลจากไฟล์ที่มีข้อมูลผิด format
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกไฟล์ที่ format ผิด<br>2. พยายามนำเข้า |
| **Expected Result** | แสดง Error Message ระบุข้อมูลผิด |
| **Priority** | Medium |

---

## 4. Data Maintenance

### 4.1 Test Cases

#### TC-UTL-DM-001: Backup ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด Data Maintenance<br>2. กด Backup<br>3. เลือกที่เก็บ<br>4. ยืนยัน |
| **Expected Result** | ไฟล์ Backup ถูกสร้าง |
| **Priority** | High |

#### TC-UTL-DM-002: Restore ข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกไฟล์ Backup<br>2. กด Restore<br>3. ยืนยัน |
| **Expected Result** | ข้อมูลถูก Restore สำเร็จ |
| **Priority** | High |

#### TC-UTL-DM-003: Reindex ฐานข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด Data Maintenance<br>2. กด Reindex |
| **Expected Result** | Index ถูกสร้างใหม่ ระบบทำงานเร็วขึ้น |
| **Priority** | Medium |

#### TC-UTL-DM-004: ตรวจสอบความสมบูรณ์ของข้อมูล
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด Data Maintenance<br>2. ตรวจสอบ Data Integrity |
| **Expected Result** | แสดงผลการตรวจสอบ ไม่มี Error |
| **Priority** | Medium |

---

## 5. System Tools

### 5.1 Test Cases

#### TC-UTL-ST-001: ตรวจสอบ Version ระบบ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด About/Version |
| **Expected Result** | แสดง ProMaxx 21.0.0-beta.1 |
| **Priority** | Low |

#### TC-UTL-ST-002: ตรวจสอบ License
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ดูข้อมูล License |
| **Expected Result** | แสดงข้อมูล License ถูกต้อง |
| **Priority** | Low |

---

## 6. Regression Test Checklist

- [ ] พิมพ์บาร์โค้ดได้ถูกต้องทุกรูปแบบ
- [ ] บาร์โค้ดสแกนได้ถูกต้อง
- [ ] ส่งออก Excel ได้ถูกต้อง
- [ ] นำเข้า Excel ได้ถูกต้อง
- [ ] Backup/Restore ทำงานถูกต้อง
- [ ] Reindex ทำงานถูกต้อง
- [ ] ความเข้ม/ความเร็วพิมพ์ถูกต้อง
