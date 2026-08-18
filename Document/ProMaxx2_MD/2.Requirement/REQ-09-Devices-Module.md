# Requirements — โมดูล Devices (Hardware Integration)

> อ้างอิง: `System-Analysis.md` section 5, 5.1
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Devices รับผิดชอบการเชื่อมต่อกับอุปกรณ์ POS ภายนอกทั้งหมด ตามที่ระบุใน System-Analysis.md section 5, 5.1

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

### 2.1 Receipt Printer (TMU)

#### FR-DEV-001: เชื่อมต่อ Receipt Printer
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-001 |
| **ชื่อ** | เชื่อมต่อ Receipt Printer |
| **Description** | ระบบต้องเชื่อมต่อ Receipt Printer ได้ |
| **Input** | COM Port, Model |
| **Output** | Printer connected |
| **Supported** | Epson TMU220A |
| **Priority** | Critical |

#### FR-DEV-002: พิมพ์ใบเสร็จ
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-002 |
| **ชื่อ** | พิมพ์ใบเสร็จ |
| **Description** | ระบบต้องพิมพ์ใบเสร็จจาก Receipt Printer |
| **Input** | Transaction data |
| **Output** | Receipt printed |
| **Template** | form_tmu |
| **Priority** | Critical |

#### FR-DEV-003: พิมพ์ภาษาไทย
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-003 |
| **ชื่อ** | พิมพ์ภาษาไทย |
| **Description** | ระบบต้องพิมพ์ภาษาไทยได้ถูกต้อง |
| **Input** | Thai text |
| **Output** | Thai text printed correctly |
| **Config** | `tmu_language_code = KU`, `thai_chk_tmu_termal = 1` |
| **Priority** | High |

---

### 2.2 Cash Drawer

#### FR-DEV-004: เชื่อมต่อ Cash Drawer
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-004 |
| **ชื่อ** | เชื่อมต่อ Cash Drawer |
| **Description** | ระบบต้องเชื่อมต่อ Cash Drawer ได้ |
| **Input** | USB Port, Send Type |
| **Output** | Cash Drawer connected |
| **Priority** | Critical |

#### FR-DEV-005: เปิด Cash Drawer อัตโนมัติ
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-005 |
| **ชื่อ** | เปิด Cash Drawer อัตโนมัติ |
| **Description** | ระบบต้องเปิด Cash Drawer อัตโนมัติหลัง Cash Sale |
| **Input** | Transaction completed |
| **Output** | Cash Drawer opened |
| **Priority** | Critical |

#### FR-DEV-006: เปิด Cash Drawer ด้วย PIN
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-006 |
| **ชื่อ** | เปิด Cash Drawer ด้วย PIN |
| **Description** | ระบบต้องเปิด Cash Drawer ด้วย PIN |
| **Input** | PIN |
| **Output** | Cash Drawer opened |
| **Config** | `cash_drawer_send_type = PIN5` |
| **Priority** | High |

---

### 2.3 Customer Display

#### FR-DEV-007: เชื่อมต่อ Customer Display
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-007 |
| **ชื่อ** | เชื่อมต่อ Customer Display |
| **Description** | ระบบต้องเชื่อมต่อ Customer Display ได้ |
| **Input** | COM Port, Model |
| **Output** | Display connected |
| **Priority** | Medium |

#### FR-DEV-008: แสดง Welcome Message
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-008 |
| **ชื่อ** | แสดง Welcome Message |
| **Description** | ระบบต้องแสดง Welcome Message บน Display |
| **Input** | Welcome text |
| **Output** | Text displayed |
| **Config** | `cus_disp_text_upper`, `cus_disp_text_lower` |
| **Priority** | Medium |

#### FR-DEV-009: แสดงราคาสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-009 |
| **ชื่อ** | แสดงราคาสินค้า |
| **Description** | ระบบต้องแสดงราคาสินค้าบน Display |
| **Input** | Item name, Price |
| **Output** | Price displayed |
| **Priority** | Medium |

---

### 2.4 EDC (Payment Terminal)

#### FR-DEV-010: เชื่อมต่อ EDC
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-010 |
| **ชื่อ** | เชื่อมต่อ EDC |
| **Description** | ระบบต้องเชื่อมต่อ EDC ได้ |
| **Input** | COM Port, Model |
| **Output** | EDC connected |
| **Supported** | Hypercom |
| **Priority** | High |

#### FR-DEV-011: รูดบัตรเครดิต
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-011 |
| **ชื่อ** | รูดบัตรเครดิต |
| **Description** | ระบบต้องรองรับการรูดบัตรเครดิต |
| **Input** | Card data |
| **Output** | Payment approved/rejected |
| **Priority** | High |

---

### 2.5 Scale (Weighing)

#### FR-DEV-012: เชื่อมต่อเครื่องชั่ง
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-012 |
| **ชื่อ** | เชื่อมต่อเครื่องชั่ง |
| **Description** | ระบบต้องเชื่อมต่อเครื่องชั่งได้ |
| **Input** | Scale connection |
| **Output** | Scale connected |
| **Config** | `isusedweight = 1` |
| **Priority** | High |

#### FR-DEV-013: ชั่งน้ำหนักและพิมพ์บาร์โค้ด
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-013 |
| **ชื่อ** | ชั่งน้ำหนักและพิมพ์บาร์โค้ด |
| **Description** | ระบบต้องชั่งน้ำหนักและพิมพ์บาร์โค้ดอัตโนมัติ |
| **Input** | Weight from scale |
| **Output** | Barcode with weight/price |
| **Config** | `is_used_weight_or_price = Q` |
| **Priority** | High |

---

### 2.6 Smart Card Reader

#### FR-DEV-014: เชื่อมต่อ Smart Card Reader
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-014 |
| **ชื่อ** | เชื่อมต่อ Smart Card Reader |
| **Description** | ระบบต้องเชื่อมต่อ Smart Card Reader ได้ |
| **Input** | PCSC connection |
| **Output** | Reader connected |
| **Library** | PCSC, PCSC.Iso7816 |
| **Priority** | Medium |

#### FR-DEV-015: อ่านข้อมูลจากบัตร Chip
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-015 |
| **ชื่อ** | อ่านข้อมูลจากบัตร Chip |
| **Description** | ระบบต้องอ่านข้อมูลจากบัตร Chip ได้ |
| **Input** | Card inserted |
| **Output** | Card data read |
| **Standard** | ISO 7816 |
| **Priority** | Medium |

---

### 2.7 Webcam

#### FR-DEV-016: เชื่อมต่อ Webcam
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DEV-016 |
| **ชื่อ** | เชื่อมต่อ Webcam |
| **Description** | ระบบต้องเชื่อมต่อ Webcam ได้ |
| **Input** | WebSocket COM Port |
| **Output** | Webcam connected |
| **Config** | `ws_com_port`, `ws_on_off` |
| **Priority** | Low |

---

## 3. Non-Functional Requirements

### NFR-DEV-001: Device Connection Reliability
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-DEV-001 |
| **ชื่อ** | ความน่าเชื่อถือของการเชื่อมต่อ |
| **Description** | อุปกรณ์ต้องเชื่อมต่อได้ 99.9% |
| **Measurement** | Connection success rate >= 99.9% |

### NFR-DEV-002: Print Speed
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-DEV-002 |
| **ชื่อ** | ความเร็วพิมพ์ |
| **Description** | การพิมพ์ใบเสร็จต้องเสร็จสิ้นภายใน 3 วินาที |
| **Measurement** | Print time < 3s |

### NFR-DEV-003: Cash Drawer Response Time
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-DEV-003 |
| **ชื่อ** | เวลาตอบสนอง Cash Drawer |
| **Description** | Cash Drawer ต้องเปิดภายใน 1 วินาที |
| **Measurement** | Open time < 1s |

---

## 4. Data Requirements

### DR-DEV-001: Device Configuration
| Field | Type | Description |
|-------|------|-------------|
| DeviceType | VARCHAR | ประเภทอุปกรณ์ |
| COMPort | VARCHAR | COM Port |
| USBPort | INT | USB Port |
| Model | VARCHAR | รุ่น |
| Status | VARCHAR | สถานะ |

---

## 5. Business Rules

### BR-DEV-001: Cash Drawer Auto Open
```
IF transaction.Type = "CashSale" AND transaction.Completed THEN
  Open Cash Drawer
```

### BR-DEV-002: Scale Integration
```
IF isusedweight = 1 THEN
  Read weight from scale
  Calculate price = weight × unit_price
  Print barcode with weight/price
```

### BR-DEV-003: Device Fallback
```
IF device NOT connected THEN
  Show warning "Device not connected"
  Allow manual operation
```

---

## 6. Configuration Reference

### system.ini — [Terminal]

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `cash_drawer_com_port` | NONE | COM Port เงินสด |
| `cash_drawer_usb_port_id` | 7 | USB Port เงินสด |
| `cash_drawer_send_type` | PIN5 | ประเภทส่งสัญญาณ |
| `tmu` | TMU220A | รุ่น Printer |
| `tmu_com_port` | NONE | COM Port Printer |
| `tmu_language_code` | KU | ภาษา TMU |
| `cus_dis_on` | 0 | เปิด Customer Display |
| `cus_disp_model` | EPSON | รุ่น Display |
| `edc_name` | HYPERCOM | รุ่น EDC |
| `edc_port` | NONE | COM Port EDC |
| `ws_com_port` | NONE | WebSocket COM Port |
| `ws_on_off` | 1 | เปิด WebSocket |

### system.ini — [FeatureScale]

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `isusedweight` | 1 | ใช้น้ำหนัก |
| `code_prefix` | 21 | รหัสนำหน้า |
| `format_name` | 07H | รูปแบบ |
| `is_used_weight_or_price` | Q | น้ำหนัก/ราคา |

---

## 7. Hardware Integration Matrix

| อุปกรณ์ | Connection | Config Key | Library |
|---------|------------|------------|---------|
| Receipt Printer | COM/USB | `tmu_com_port` | - |
| Cash Drawer | USB/PIN | `cash_drawer_usb_port_id` | - |
| Customer Display | COM | `cus_dis_on` | - |
| EDC | COM | `edc_port` | - |
| Scale | COM/USB | `isusedweight` | ftd2xx |
| Smart Card | PCSC | - | PCSC, PCSC.Iso7816 |
| Webcam | WebSocket | `ws_com_port` | Microsoft.Web.WebView2 |
