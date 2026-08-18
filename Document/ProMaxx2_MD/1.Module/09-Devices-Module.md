# QA Test Plan — โมดูล Devices (Hardware Integration)

> ProMaxx 21.0.0-beta.1
> วันที่สร้าง: 17 สิงหาคม 2569

---

## 1. ภาพรวมโมดูล

โมดูล Devices รับผิดชอบการเชื่อมต่อกับอุปกรณ์ POS ภายนอกทั้งหมด

### 1.1 โครงสร้างโมดูล

```
Promaxxs.Devices
├── Receipt Printer (TMU)     ─── เครื่องพิมพ์ใบเสร็จ
├── Cash Drawer               ─── ลิ้นชักเก็บเงิน
├── Customer Display          ─── จอแสดงผลลูกค้า
├── Barcode Scanner           ─── เครื่องสแกนบาร์โค้ด
├── EDC (Payment Terminal)    ─── เครื่องรูดบัตร
├── Scale (Weighing)          ─── เครื่องชั่ง
├── Smart Card Reader         ─── เครื่องอ่านบัตร Chip
└── Webcam                    ─── กล้องวงจรปิด
```

### 1.2 Libraries

- **PCSC** / **PCSC.Iso7816** - Smart Card Reader
- **Microsoft.Web.WebView2** - Embedded Web Browser
- **SkiaSharp** - Graphics Rendering
- **ftd2xx** - FTDI USB Device

---

## 2. Receipt Printer (TMU)

### 2.1 Configuration

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `tmu` | TMU220A | รุ่น Printer |
| `tmu_com_port` | NONE | COM Port |
| `tmu_language_code` | KU | ภาษาพิมพ์ |
| `tmu_print_immediate` | 1 | พิมพ์ทันที |
| `form_tmu` | TMU_Temp.SLP | เทมเพลต |
| `print_head` | 1 | หัวพิมพ์ |

### 2.2 Test Cases

#### TC-DEV-PRN-001: เชื่อมต่อ Printer
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้งค่า tmu_com_port<br>2. เชื่อมต่อ |
| **Expected Result** | Printer เชื่อมต่อสำเร็จ |
| **Priority** | High |

#### TC-DEV-PRN-002: พิมพ์ใบเสร็จทดสอบ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. กดพิมพ์ทดสอบ<br>2. ตรวจสอบกระดาษ |
| **Expected Result** | ใบเสร็จพิมพ์ออกครบถ้วน |
| **Priority** | High |

#### TC-DEV-PRN-003: พิมพ์ใบเสร็จภาษาไทย
| รายการ | รายละเอียด |
|--------|------------|
| **Config** | `tmu_language_code = KU`, `thai_chk_tmu_termal = 1` |
| **Steps** | 1. ทำรายการภาษาไทย<br>2. พิมพ์ใบเสร็จ |
| **Expected Result** | ภาษาไทยพิมพ์ถูกต้อง ไม่เพี้ยน |
| **Priority** | High |

#### TC-DEV-PRN-004: Cut Paper อัตโนมัติ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. พิมพ์ใบเสร็จ<br>2. ตรวจสอบการตัดกระดาษ |
| **Expected Result** | กระดาษถูกตัดอัตโนมัติ |
| **Priority** | Medium |

#### TC-DEV-PRN-005: ตั้งค่า Character Code
| รายการ | รายละเอียด |
|--------|------------|
| **Config** | `character_code = 1` |
| **Steps** | 1. เปลี่ยน character_code<br>2. พิมพ์ทดสอบ |
| **Expected Result** | ตัวอักษรพิมพ์ถูกต้องตาม Code Table |
| **Priority** | Low |

---

## 3. Cash Drawer (ลิ้นชักเก็บเงิน)

### 3.1 Configuration

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `cash_drawer_com_port` | NONE | COM Port |
| `cash_drawer_usb_port_id` | 7 | USB Port |
| `cash_drawer_send_type` | PIN5 | ประเภทสัญญาณ |
| `cash_drawer_send_text` | Nop | ข้อความส่ง |
| `status_drawer` | 1 | สถานะลิ้นชัก |

### 3.2 Test Cases

#### TC-DEV-CD-001: เชื่อมต่อ Cash Drawer
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้งค่า USB Port<br>2. เชื่อมต่อ |
| **Expected Result** | Cash Drawer เชื่อมต่อสำเร็จ |
| **Priority** | High |

#### TC-DEV-CD-002: เปิดลิ้นชักอัตโนมัติ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ทำรายการขายสด<br>2. ยืนยันการขาย |
| **Expected Result** | ลิ้นชักเปิดอัตโนมัติ |
| **Priority** | High |

#### TC-DEV-CD-003: เปิดลิ้นชักด้วย PIN
| รายการ | รายละเอียด |
|--------|------------|
| **Config** | `cash_drawer_send_type = PIN5` |
| **Steps** | 1. เปิดลิ้นชักด้วย PIN |
| **Expected Result** | ลิ้นชักเปิด |
| **Priority** | High |

#### TC-DEV-CD-004: ตรวจสอบสถานะลิ้นชัก
| รายการ | รายละเอียด |
|--------|------------|
| **Config** | `status_drawer = 1` |
| **Steps** | 1. ปิดลิ้นชักไม่สนิท<br>2. ตรวจสอบสถานะ |
| **Expected Result** | แสดงสถานะลิ้นชักเปิดอยู่ |
| **Priority** | Medium |

---

## 4. Customer Display (จอแสดงผลลูกค้า)

### 4.1 Configuration

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `cus_dis_on` | 0 | เปิด/ปิด |
| `cus_disp_model` | EPSON | รุ่น |
| `cus_disp_text_upper` | Welcome | ข้อความบรรทัดบน |
| `cus_disp_text_lower` | | ข้อความบรรทัดล่าง |

### 4.2 Test Cases

#### TC-DEV-CDP-001: เปิด Customer Display
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง cus_dis_on = 1<br>2. เปิด POS |
| **Expected Result** | Customer Display แสดงข้อความ Welcome |
| **Priority** | Medium |

#### TC-DEV-CDP-002: แสดงราคาสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. สแกนสินค้า<br>2. ดู Customer Display |
| **Expected Result** | แสดงชื่อสินค้าและราคา |
| **Priority** | Medium |

#### TC-DEV-CDP-003: แสดงยอดรวม
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ขายสินค้าหลายรายการ<br>2. ดู Customer Display |
| **Expected Result** | แสดงยอดรวม |
| **Priority** | Medium |

---

## 5. EDC (Electronic Data Capture)

### 5.1 Configuration

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `edc_name` | HYPERCOM | รุ่น |
| `edc_port` | NONE | COM Port |

### 5.2 Test Cases

#### TC-DEV-EDC-001: เชื่อมต่อ EDC
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้งค่า edc_port<br>2. เชื่อมต่อ |
| **Expected Result** | EDC เชื่อมต่อสำเร็จ |
| **Priority** | High |

#### TC-DEV-EDC-002: รูดบัตรเครดิต
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ขายสินค้า<br>2. เลือกชำระด้วยบัตรเครดิต<br>3. รูดบัตร |
| **Expected Result** | การชำระสำเร็จ EDC ยืนยัน |
| **Priority** | High |

---

## 6. Scale (เครื่องชั่ง)

### 6.1 Configuration

```ini
[FeatureScale]
isusedweight = 1
code_prefix = 21
format_name = 07H
is_used_weight_or_price = Q
decimal_format = 1
```

### 6.2 Test Cases

#### TC-DEV-SCL-001: เชื่อมต่อเครื่องชั่ง
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง isusedweight = 1<br>2. เชื่อมต่อเครื่องชั่ง |
| **Expected Result** | เครื่องชั่งเชื่อมต่อสำเร็จ |
| **Priority** | High |

#### TC-DEV-SCL-002: ชั่งสินค้าและพิมพ์บาร์โค้ด
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. วางสินค้าบนเครื่องชั่ง<br>2. พิมพ์บาร์โค้ด |
| **Expected Result** | บาร์โค้ดมีน้ำหนัก/ราคาถูกต้อง |
| **Priority** | High |

#### TC-DEV-SCL-003: ตั้งค่า Decimal Format
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยน decimal_format<br>2. ชั่งสินค้า |
| **Expected Result** | น้ำหนักแสดงทศนิยมตามที่กำหนด |
| **Priority** | Low |

---

## 7. Smart Card Reader

### 7.1 Libraries

- **PCSC** - PC/SC Smart Card framework
- **PCSC.Iso7816** - ISO 7816 standard

### 7.2 Test Cases

#### TC-DEV-SC-001: เชื่อมต่อ Smart Card Reader
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เชื่อมต่อ Smart Card Reader<br>2. ใส่บัตร Chip |
| **Expected Result** | อ่านข้อมูลจากบัตรสำเร็จ |
| **Priority** | Medium |

#### TC-DEV-SC-002: อ่านข้อมูลสมาชิกจากบัตร
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ใส่บัตรสมาชิก<br>2. อ่านข้อมูล |
| **Expected Result** | แสดงข้อมูลสมาชิกถูกต้อง |
| **Priority** | Medium |

---

## 8. Webcam

### 8.1 Configuration

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `ws_com_port` | NONE | WebSocket COM Port |
| `ws_on_off` | 1 | เปิด/ปิด |

### 8.2 Test Cases

#### TC-DEV-WCM-001: เชื่อมต่อ Webcam
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง ws_com_port<br>2. เปิด ws_on_off = 1 |
| **Expected Result** | Webcam เชื่อมต่อและแสดงภาพ |
| **Priority** | Low |

---

## 9. Display Video

### 9.1 Configuration

| Key | ค่า Default | คำอธิบาย |
|-----|------------|----------|
| `display_open_vdo` | 0 | เปิด Video |
| `display_path_vdo` | ------ | Path ไฟล์ Video |
| `display_open_vdo_time` | 0 | เวลาแสดง |

### 9.2 Test Cases

#### TC-DEV-VDO-001: แสดง Video บน Customer Display
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง display_open_vdo = 1<br>2. ตั้ง path vdo<br>3. เปิด POS |
| **Expected Result** | Video แสดงบน Customer Display |
| **Priority** | Low |

---

## 10. Hardware Integration Matrix

| อุปกรณ์ | Connection | Config Key |
|---------|------------|------------|
| Receipt Printer | COM/USB | `tmu_com_port` |
| Cash Drawer | USB/PIN | `cash_drawer_usb_port_id` |
| Customer Display | COM | `cus_dis_on` |
| EDC | COM | `edc_port` |
| Scale | COM/USB | `isusedweight` |
| Smart Card | PCSC | - |
| Webcam | WebSocket | `ws_com_port` |

---

## 11. Regression Test Checklist

- [ ] Printer พิมพ์ใบเสร็จถูกต้อง
- [ ] Printer รองรับภาษาไทย
- [ ] Cash Drawer เปิดอัตโนมัติหลังขายสด
- [ ] Cash Drawer เปิดด้วย PIN ได้
- [ ] Customer Display แสดงราคาถูกต้อง
- [ ] EDC เชื่อมต่อและรูดบัตรได้
- [ ] Scale ชั่งน้ำหนักถูกต้อง
- [ ] Smart Card อ่านข้อมูลได้
- [ ] ทุกอุปกรณ์เชื่อมต่อได้ทั้ง Local/LAN
- [ ] ไม่มี Memory Leak เมื่อใช้อุปกรณ์นาน
