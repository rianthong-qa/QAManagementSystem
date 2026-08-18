# Requirements — โมดูล Transaction (ธุรกรรม)

> อ้างอิง: `System-Analysis.md` _section 3.1, 7_
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Transaction รับผิดชอบการทำธุรกรรมทุกประเภทของระบบ POS ประกอบด้วย **13 ประเภทธุรกรรม** ตามที่ระบุใน System-Analysis.md section 3.1

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

### 2.1 กลุ่มธุรกรรมการขาย (Sales Transactions)

#### FR-TRN-001: ขายสินค้าหน้าร้าน (TranPos)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-001 |
| **ชื่อ** | ขายสินค้าหน้าร้าน (POS Sale) |
| **Voucher ID** | 35 |
| **Description** | ระบบต้องรองรับการขายสินค้าหน้าร้านแบบ POS ด้วยความเร็วสูง |
| **Input** | Barcode/ItemId, Quantity, Price, PersonId (พนักงาน) |
| **Output** | ใบเสร็จ (Receipt), Stock ลดลง, บันทึกธุรกรรม |
| **Business Rule** | ไม่คิด VAT (tax_rate=0), ใช้ PersonId=999 สำหรับ Cash Sale |
| **Priority** | Critical |

#### FR-TRN-002: ขายสินค้าแบบ Invoice (TranOut)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-002 |
| **ชื่อ** | ขายสินค้า (Sales Transaction) |
| **Voucher ID** | 45 |
| **Description** | ระบบต้องรองรับการขายสินค้าแบบ Invoice สำหรับลูกค้า |
| **Input** | CustomerId, ItemId, Quantity, Price, Warehouse |
| **Output** | Sales Invoice, Stock ลดลง, VAT 7% |
| **Business Rule** | คิด VAT 7%, ใช้ PersonId=999 สำหรับ Cash Sale, รองรับ Service Charge |
| **Priority** | Critical |

#### FR-TRN-003: ออกใบลดหนี้ (TranCnOut)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-003 |
| **ชื่อ** | ออกใบลดหนี้ (Credit Note Issue) |
| **Voucher ID** | 52 |
| **Description** | ระบบต้องรองรับการออกใบลดหนี้สำหรับลูกค้า |
| **Input** | CustomerId, ItemId, Quantity, Amount |
| **Output** | Credit Note, Stock เพิ่มขึ้น (คืนสินค้า) |
| **Business Rule** | ไม่คิด VAT (tax_rate=0), Stock เพิ่มกลับ |
| **Priority** | High |

#### FR-TRN-004: รับใบลดหนี้ (TranCnIn)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-004 |
| **ชื่อ** | รับใบลดหนี้ (Credit Note Receive) |
| **Voucher ID** | 18 |
| **Description** | ระบบต้องรองรับการรับใบลดหนี้จากซัพพลายเออร์ |
| **Input** | SupplierId, ItemId, Quantity |
| **Output** | ลดหนี้ซัพพลายเออร์, Stock ลดลง |
| **Business Rule** | ไม่คิด VAT (tax_rate=0), default_quantity=1 |
| **Priority** | High |

---

### 2.2 กลุ่มธุรกรรมรับสินค้า (Purchase Transactions)

#### FR-TRN-005: รับสินค้า (TranIn)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-005 |
| **ชื่อ** | รับสินค้า (Purchase Order Receive) |
| **Voucher ID** | 7 |
| **Description** | ระบบต้องรองรับการรับสินค้าจากการสั่งซื้อ |
| **Input** | PO Number, SupplierId, ItemId, Quantity, Cost, Warehouse |
| **Output** | ใบรับสินค้า, Stock เพิ่ม, บันทึกต้นทุน |
| **Business Rule** | ไม่คิด VAT (tax_rate=0), รับได้เฉพาะจำนวนที่สั่งหรือน้อยกว่า, default_quantity=0 |
| **Priority** | Critical |

---

### 2.3 กลุ่มธุรกรรมโอนสินค้า (Transfer Transactions)

#### FR-TRN-006: โอนสินค้าออก (TranTbOut)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-006 |
| **ชื่อ** | โอนสินค้าออก (Transfer Out) |
| **Voucher ID** | 53 |
| **Description** | ระบบต้องรองรับการโอนสินค้าระหว่างคลัง |
| **Input** | SourceWarehouse, DestWarehouse, ItemId, Quantity |
| **Output** | เอกสารโอนออก, Stock ต้นทางลด |
| **Business Rule** | คิด VAT 7%, default_quantity=1 |
| **Priority** | High |

#### FR-TRN-007: รับสินค้าโอนเข้า (TranTbIn)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-007 |
| **ชื่อ** | รับสินค้าโอนเข้า (Transfer In) |
| **Voucher ID** | 64 |
| **Description** | ระบบต้องรองรับการรับสินค้าโอนเข้า |
| **Input** | Transfer Document, ItemId, Quantity |
| **Output** | ใบรับโอน, Stock ปลายทางเพิ่ม |
| **Business Rule** | ไม่คิด VAT (tax_rate=0), default_quantity=1 |
| **Priority** | High |

---

### 2.4 กลุ่มธุรกรรมเบิกสินค้า (Branch Transactions)

#### FR-TRN-008: ส่งสินค้าเบิก (TranBnOut)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-008 |
| **ชื่อ** | ส่งสินค้าเบิก (Branch Send) |
| **Voucher ID** | 26 |
| **Description** | ระบบต้องรองรับการส่งสินค้าเบิก |
| **Input** | BranchId, ItemId, Quantity |
| **Output** | เอกสารเบิก, Stock ลด |
| **Business Rule** | คิด VAT 7%, focus_next_column=QUANTITY |
| **Priority** | High |

#### FR-TRN-009: รับสินค้าเบิก (TranBnIn)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-009 |
| **ชื่อ** | รับสินค้าเบิก (Branch Receive) |
| **Voucher ID** | 71 |
| **Description** | ระบบต้องรองรับการรับสินค้าเบิก |
| **Input** | BranchDocument, ItemId, Quantity |
| **Output** | รับสินค้าสำเร็จ |
| **Business Rule** | ไม่คิด VAT (tax_rate=0), focus_next_column=ITEMNAME |
| **Priority** | High |

---

### 2.5 กลุ่มธุรกรรมเงินมัดจำ (Deposit Transactions)

#### FR-TRN-010: จ่ายเงินมัดจำ (TranDpOut)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-010 |
| **ชื่อ** | จ่ายเงินมัดจำ (Deposit Return) |
| **Voucher ID** | 29 |
| **Description** | ระบบต้องรองรับการจ่ายเงินมัดจำคืน |
| **Input** | CustomerId, Amount |
| **Output** | เอกสารมัดจำจ่าย, บันทึกยอดจ่าย |
| **Business Rule** | คิด VAT 7% |
| **Priority** | Medium |

#### FR-TRN-011: รับเงินมัดจำ (TranDpIn)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-011 |
| **ชื่อ** | รับเงินมัดจำ (Deposit Receive) |
| **Voucher ID** | 69 |
| **Description** | ระบบต้องรองรับการรับเงินมัดจำ |
| **Input** | CustomerId, Amount |
| **Output** | เอกสารมัด.ObjectModelรับ, บันทึกรับเงิน |
| **Business Rule** | ไม่คิด VAT (tax_rate=0) |
| **Priority** | Medium |

---

### 2.6 กลุ่มธุรกรรมเบิก/รับเบิก (Weight Transactions)

#### FR-TRN-012: เบิกจ่าย (TranTwOut)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-012 |
| **ชื่อ** | เบิกจ่าย (Transfer Weight Out) |
| **Voucher ID** | 28 |
| **Description** | ระบบต้องรองรับการเบิกจ่าย |
| **Input** | ItemId, Quantity |
| **Output** | เอกสารเบิกจ่าย |
| **Business Rule** | ไม่คิด VAT (tax_rate=0) |
| **Priority** | Medium |

#### FR-TRN-013: รับเบิก (TranTwIn)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-013 |
| **ชื่อ** | รับเบิก (Transfer Weight In) |
| **Voucher ID** | 66 |
| **Description** | ระบบต้องรองรับการรับเบิก |
| **Input** | Document, ItemId, Quantity |
| **Output** | รับสินค้าสำเร็จ |
| **Business Rule** | ไม่คิด VAT (tax_rate=0) |
| **Priority** | Medium |

---

### 2.7 กลุ่มธุรกรรมอื่นๆ

#### FR-TRN-014: ใบแจ้งหนี้/ใบเสร็จ (Notation)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-TRN-014 |
| **ชื่อ** | ใบแจ้งหนี้/ใบเสร็จ (Notation) |
| **Voucher ID** | 26 |
| **Description** | ระบบต้องรองรับการสร้างใบแจ้งหนี้/ใบเสร็จ |
| **Input** | CustomerId, Items, Amount |
| **Output** | เอกสาร Notation |
| **Business Rule** | คิด VAT 7% |
| **Priority** | High |

---

## 3. ข้อกำหนดที่ไม่ใช่ฟังก์ชัน (Non-Functional Requirements)

### NFR-TRN-001: ประสิทธิภาพ
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-TRN-001 |
| **ชื่อ** | ประสิทธิภาพการทำธุรกรรม |
| **Description** | ธุรกรรม POS ต้องเสร็จสิ้นภายใน 3 วินาที |
| **Measurement** | Transaction completion time < 3s |

### NFR-TRN-002: ความปลอดภัย
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-TRN-002 |
| **ชื่อ** | การเข้าถึงธุรกรรม |
| **Description** | พนักงานต้อง PIN เพื่อเข้าทำธุรกรรม |
| **Measurement** | PIN authentication required |

### NFR-TRN-003: Audit Trail
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-TRN-003 |
| **ชื่อ** | ประวัติการทำธุรกรรม |
| **Description** | ทุกธุรกรรมต้องบันทึกผู้ทำรายการ, วันที่, เวลา |
| **Measurement** | Audit log ครบถ้วน |

### NFR-TRN-004: Auto Backup
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-TRN-004 |
| **ชื่อ** | การสำรองข้อมูลอัตโนมัติ |
| **Description** | ระบบต้องสำรองข้อมูลหลังทำธุรกรรม (autobackup=1) |
| **Measurement** | Backup file created after transaction |

---

## 4. ข้อกำหนดข้อมูล (Data Requirements)

### DR-TRN-001: Transaction Header
| Field | Type | Description |
|-------|------|-------------|
| TranNo | VARCHAR | เลขที่ธุรกรรม (Unique, Auto-generate) |
| TranDate | DATE | วันที่ทำธุรกรรม |
| VoucherId | INT | ประเภทธุรกรรม |
| PersonId | VARCHAR | รหัสผู้ทำรายการ |
| CustomerId | VARCHAR | รหัสลูกค้า (ถ้ามี) |
| WarehouseId | VARCHAR | รหัสคลัง |
| TaxRate | DECIMAL | อัตราภาษี |
| ServiceChargeRate | DECIMAL | อัตราค่าบริการ |
| GrandTotal | DECIMAL | ยอดรวม |
| Status | VARCHAR | สถานะ (Active/Cancelled/Completed) |

### DR-TRN-002: Transaction Detail
| Field | Type | Description |
|-------|------|-------------|
| TranNo | VARCHAR | เลขที่ธุรกรรม |
| LineNo | INT | ลำดับแถว |
| ItemId | VARCHAR | รหัสสินค้า |
| Quantity | DECIMAL | จำนวน |
| Price | DECIMAL | ราคา/หน่วย |
| Discount | DECIMAL | ส่วนลด |
| TaxAmount | DECIMAL | ยอดภาษี |
| LineTotal | DECIMAL | ยอดรวมแถว |

---

## 5. ข้อกำหนด Interface

### IR-TRN-001: Search Item Interface
| รายการ | รายละเอียด |
|--------|------------|
| **Input** | Barcode, ItemId, ItemName |
| **Output** | Item list with Price, Stock |
| **Config** | `tab_search_item`, `search_item_group`, `search_sys_item_id` |

### IR-TRN-002: Cash Drawer Interface
| รายการ | รายละเอียด |
|--------|------------|
| **Trigger** | Cash Sale completed |
| **Action** | Open Cash Drawer |
| **Config** | `cash_drawer_usb_port_id`, `cash_drawer_send_type` |

### IR-TRN-003: Receipt Printer Interface
| รายการ | รายละเอียด |
|--------|------------|
| **Trigger** | Transaction completed |
| **Action** | Print Receipt |
| **Config** | `tmu_com_port`, `form_tmu` |

---

## 6. Business Rules

### BR-TRN-001: VAT Calculation
```
IF tax_rate = 7 THEN
  VAT = LineTotal × 0.07
  GrandTotal = Σ(LineTotal) + Σ(VAT)
ELSE
  VAT = 0
  GrandTotal = Σ(LineTotal)
```

### BR-TRN-002: Stock Validation
```
FOR EACH item IN transaction:
  IF item.Quantity > item.CurrentStock THEN
    BLOCK transaction
    SHOW error "Stock insufficient"
```

### BR-TRN-003: Auto Number Generation
```
TranNo = VoucherId + Sequential Number
// Example: POS-2026-000001
```

### BR-TRN-004: Cash Drawer Auto Open
```
IF transaction.Type = "CashSale" AND transaction.Completed THEN
  Open Cash Drawer
```

### BR-TRN-005: Service Charge
```
IF service_charge_rate > 0 THEN
  ServiceCharge = Σ(LineTotal) × service_charge_rate
  GrandTotal = Σ(LineTotal) + ServiceCharge + VAT
```

---

## 7. Configuration Reference

| Config Key | Section | ค่า Default | คำอธิบาย |
|------------|---------|------------|----------|
| `tab_search_item` | TranIn/Out | 1 | โหมด tab ค้นหาสินค้า |
| `search_item_group` | TranIn/Out | 3 | กลุ่มสินค้าสำหรับค้นหา |
| `search_sys_item_id` | TranIn/Out | 4 | System Item ID สำหรับค้นหา |
| `recently_sys_voucher_id` | TranPos | 35 | Voucher ID ล่าสุด |
| `tax_rate` | TranOut | 7 | อัตราภาษี (%) |
| `default_quantity` | TranIn | 0 | จำนวนเริ่มต้น |
| `warehouse` | TranIn | 0 | คลังเริ่มต้น |
| `focus_next_column` | TranIn | QUANTITY | Column ถัดไปหลังกรอก |
| `service_charge_rate` | TranIn | 0 | อัตราค่าบริการ (%) |
| `person_id_cash` | TranOut | 999 | Person ID สำหรับ Cash Sale |
