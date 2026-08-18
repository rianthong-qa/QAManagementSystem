# Requirements — โมดูล Inventory (คลังสินค้า)

> อ้างอิง: `System-Analysis.md` section 3.2
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Inventory รับผิดชอบการจัดการคลังสินค้าทั้งหมด ประกอบด้วย **17 โมดูลย่อย** ตามที่ระบุใน System-Analysis.md section 3.2

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

### 2.1 Items (ข้อมูลสินค้า)

#### FR-INV-001: จัดการข้อมูลสินค้า (Master Item)
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-001 |
| **ชื่อ** | จัดการข้อมูลสินค้า |
| **Description** | ระบบต้องรองรับการเพิ่ม/แก้ไข/ลบ/ค้นหาข้อมูลสินค้า |
| **Input** | ItemId, Barcode, ItemName, UnitName, Price, TaxType, Comments |
| **Output** | ข้อมูลสินค้าที่บันทึกสำเร็จ |
| **Business Rule** | ItemId ต้องไม่ซ้ำกัน, Barcode ต้องไม่ซ้ำกัน |
| **Priority** | Critical |

#### FR-INV-002: ค้นหาสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-002 |
| **ชื่อ** | ค้นหาสินค้า |
| **Description** | ระบบต้องรองรับการค้นหาสินค้าด้วย ItemId, Barcode, ItemName |
| **Input** | Search keyword |
| **Output** | รายการสินค้าที่ตรงกัน |
| **Config** | `search_item_group`, `search_sys_item_id` |
| **Priority** | High |

#### FR-INV-003: เปิด/ปิดสถานะสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-003 |
| **ชื่อ** | เปิด/ปิดสถานะสินค้า |
| **Description** | ระบบต้องรองรับการเปิด/ปิดสถานะการใช้งานสินค้า |
| **Input** | ItemId, Status (Active/Inactive) |
| **Output** | สถานะสินค้าเปลี่ยน |
| **Business Rule** | สินค้าที่ปิดแล้วไม่ปรากฏในรายการธุรกรรม |
| **Priority** | Medium |

#### FR-INV-004: บันทึกรูปภาพสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-004 |
| **ชื่อ** | บันทึกรูปภาพสินค้า |
| **Description** | ระบบต้องรองรับการบันทึกรูปภาพสินค้า |
| **Input** | ItemId, Image file |
| **Output** | รูปภาพถูกบันทึกและแสดง |
| **Config** | `openpicture = 1` |
| **Priority** | Low |

---

### 2.2 ItemTemplate (เทมเพลตสินค้า)

#### FR-INV-005: จัดการเทมเพลตสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-005 |
| **ชื่อ** | จัดการเทมเพลตสินค้า |
| **Description** | ระบบต้องรองรับการสร้าง/แก้ไข/ลบเทมเพลตสินค้า (Bundle/Set) |
| **Input** | Template Name, Items (多个), Price |
| **Output** | เทมเพลตสินค้าที่บันทึกสำเร็จ |
| **Business Rule** | เทมเพลตต้องมีสินค้าย่อยอย่างน้อย 1 รายการ |
| **Priority** | High |

#### FR-INV-006: คำนวณราคาเทมเพลต
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-006 |
| **ชื่อ** | คำนวณราคาเทมเพลต |
| **Description** | ระบบต้องคำนวณราคาเทมเพลตจากสินค้าย่อย |
| **Input** | Items with Quantity and Price |
| **Output** | ราคารวมเทมเพลต |
| **Business Rule** | ราคาเทมเพลต = Σ(Price × Quantity) ของสินค้าย่อย |
| **Priority** | High |

---

### 2.3 StockCount (ตรวจนับสินค้า)

#### FR-INV-007: ตรวจนับสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-007 |
| **ชื่อ** | ตรวจนับสินค้า |
| **Description** | ระบบต้องรองรับการตรวจนับสินค้า (Physical Count) |
| **Input** | ItemId, SystemQty, CountQty |
| **Output** | ผลต่าง (Diff) อัตโนมัติ |
| **Business Rule** | Diff = CountQty - SystemQty |
| **Priority** | High |

#### FR-INV-008: อนุมัติผลตรวจนับ
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-008 |
| **ชื่อ** | อนุมัติผลตรวจนับ |
| **Description** | ระบบต้องรองรับการอนุมัติผลตรวจนับเพื่อปรับ Stock |
| **Input** | StockCount ID |
| **Output** | Stock ปรับตาม Diff |
| **Business Rule** | Stock ใหม่ = SystemQty + Diff |
| **Priority** | High |

---

### 2.4 SerialCount (ตรวจนับ Serial)

#### FR-INV-009: ตรวจนับ Serial Number
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-009 |
| **ชื่อ** | ตรวจนับ Serial Number |
| **Description** | ระบบต้องรองรับการตรวจนับตาม Serial Number |
| **Input** | ItemId, SerialLotNo, SystemQty, CountQty |
| **Output** | สถานะ Serial (ปกติ/ขาด/เกิน) |
| **Priority** | High |

---

### 2.5 LotCount (ตรวจนับ Lot)

#### FR-INV-010: ตรวจนับ Lot Number
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-010 |
| **ชื่อ** | ตรวจนับ Lot Number |
| **Description** | ระบบต้องรองรับการตรวจนับตาม Lot Number |
| **Input** | ItemId, LotNo, SystemQty, CountQty |
| **Output** | สถานะ Lot (ปกติ/ขาด/หมดอายุ) |
| **Priority** | High |

---

### 2.6 StockAdjustment (ปรับปรุงยอดสินค้า)

#### FR-INV-011: ปรับปรุง Stock
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-011 |
| **ชื่อ** | ปรับปรุง Stock |
| **Description** | ระบบต้องรองรับการปรับปรุง Stock สินค้า |
| **Input** | ItemId, QtyRemain, QtyAdjust, Comments |
| **Output** | Stock ปรับตาม Diff |
| **Business Rule** | Diff = QtyAdjust - QtyRemain |
| **Priority** | High |

---

### 2.7 PriceChange (เปลี่ยนแปลงราคา)

#### FR-INV-012: เปลี่ยนแปลงราคาสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-012 |
| **ชื่อ** | เปลี่ยนแปลงราคาสินค้า |
| **Description** | ระบบต้องรองรับการเปลี่ยนแปลงราคาสินค้า |
| **Input** | ItemId, NewPrice, PriceLevelNo, DiscountItem, DiscountRow |
| **Output** | ราคาสินค้าเปลี่ยน |
| **Business Rule** | รองรับการเปลี่ยนราคาหลาย Level |
| **Priority** | High |

---

### 2.8 ItemStockMinMax (ตั้งค่า Max/Min)

#### FR-INV-013: ตั้งค่า Max/Min สินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-013 |
| **ชื่อ** | ตั้งค่า Max/Min สินค้า |
| **Description** | ระบบต้องรองรับการตั้งค่า Max/Min สำหรับสินค้า |
| **Input** | ItemId, MaxQuantity, MinQuantity |
| **Output** | ค่า Max/Min ถูกบันทึก |
| **Business Rule** | Max ต้องมากกว่า Min เสมอ |
| **Priority** | High |

---

### 2.9 TransLocation (โอนย้ายระหว่างคลัง)

#### FR-INV-014: โอนย้ายสินค้าระหว่างคลัง
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-014 |
| **ชื่อ** | โอนย้ายสินค้าระหว่างคลัง |
| **Description** | ระบบต้องรองรับการโอนย้ายสินค้าระหว่างคลัง |
| **Input** | SourceWarehouse, DestWarehouse, ItemId, Quantity |
| **Output** | Stock ต้นทางลด, Stock ปลายทางเพิ่ม |
| **Business Rule** | ต้องมี Stock มากพอในคลังต้นทาง |
| **Priority** | High |

---

### 2.10 DeviceDoc (เอกสารอุปกรณ์)

#### FR-INV-015: จัดการเอกสารอุปกรณ์
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-015 |
| **ชื่อ** | จัดการเอกสารอุปกรณ์ |
| **Description** | ระบบต้องรองรับการบันทึกเอกสารอุปกรณ์พร้อม Serial/Lot/Expiry |
| **Input** | ItemId, SerialNo, LotNo, ExpireDate, Quantity, Price |
| **Output** | เอกสารอุปกรณ์ที่บันทึกสำเร็จ |
| **Business Rule** | รองรับ Serial/Lot/Expiry tracking |
| **Priority** | High |

---

### 2.11 IStock (รายงานคลังสินค้า)

#### FR-INV-016: ดูยอดคงเหลือสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-016 |
| **ชื่อ** | ดูยอดคงเหลือสินค้า |
| **Description** | ระบบต้องแสดงยอดคงเหลือสินค้าตามสาขา |
| **Input** | BranchId, ItemId |
| **Output** | จำนวนคงเหลือ |
| **Priority** | High |

#### FR-INV-017: ดูการเคลื่อนไหว Serial
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-017 |
| **ชื่อ** | ดูการเคลื่อนไหว Serial |
| **Description** | ระบบต้องแสดงประวัติการเคลื่อนไหวของ Serial Number |
| **Input** | ItemId, SerialNo |
| **Output** | Transaction history |
| **Priority** | Medium |

#### FR-INV-018: ดูธุรกรรมสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-018 |
| **ชื่อ** | ดูธุรกรรมสินค้า |
| **Description** | ระบบต้องแสดงรายการธุรกรรมสินค้า |
| **Input** | Date range, ItemId |
| **Output** | Transaction list |
| **Priority** | Medium |

---

### 2.12 Suite (จัดชุดสินค้า)

#### FR-INV-019: จัดชุดสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-019 |
| **ชื่อ** | จัดชุดสินค้า |
| **Description** | ระบบต้องรองรับการจัดชุดสินค้า (Product Suite) |
| **Input** | Master Item, Detail Items |
| **Output** | ชุดสินค้าที่บันทึกสำเร็จ |
| **Business Rule** | ขายชุดสินค้าแล้ว Stock สินค้าย่อยลดทุกรายการ |
| **Priority** | High |

---

### 2.13 GroupItem (กลุ่มสินค้า)

#### FR-INV-020: จัดกลุ่มสินค้า 5 ระดับ
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-020 |
| **ชื่อ** | จัดกลุ่มสินค้า 5 ระดับ |
| **Description** | ระบบต้องรองรับการจัดกลุ่มสินค้า 5 ระดับชั้น |
| **Input** | Level 1-5 Group |
| **Output** | กลุ่มสินค้าที่บันทึกสำเร็จ |
| **Config** | `search_item_group_l1..l5` |
| **Priority** | Medium |

---

### 2.14 CategoryConflict

#### FR-INV-021: ตรวจสอบความขัดแย้งของ Category
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-021 |
| **ชื่อ** | ตรวจสอบความขัดแย้งของ Category |
| **Description** | ระบบต้องตรวจสอบสินค้าที่มี Category ซ้ำ/ขัดแย้ง |
| **Input** | None |
| **Output** | รายการสินค้าที่ขัดแย้ง |
| **Priority** | Low |

---

### 2.15 SetItemControlledDrug

#### FR-INV-022: ตั้งค่าสินค้ายาควบคุม
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-INV-022 |
| **ชื่อ** | ตั้งค่าสินค้ายาควบคุม |
| **Description** | ระบบต้องรองรับการตั้งค่าสินค้าเป็น Controlled Drug |
| **Input** | ItemId |
| **Output** | สถานะสินค้า = Controlled Drug |
| **Priority** | Medium |

---

## 3. Non-Functional Requirements

### NFR-INV-001: ความถูกต้องของ Stock
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-INV-001 |
| **ชื่อ** | ความถูกต้องของ Stock |
| **Description** | Stock ต้องถูกต้องตลอดเวลา ไม่สามารถติดลบได้ |
| **Measurement** | Stock >= 0 เสมอ |

### NFR-INV-002: Concurrent Access
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-INV-002 |
| **ชื่อ** | การเข้าถึงพร้อมกัน |
| **Description** | ต้องรองรับการเข้าถึง Stock พร้อมกันหลาย POS |
| **Measurement** | ไม่มี Stock discrepancy |

### NFR-INV-003: Real-time Update
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-INV-003 |
| **ชื่อ** | การอัพเดทแบบ Real-time |
| **Description** | Stock ต้องอัพเดททันทีหลังทำธุรกรรม |
| **Measurement** | Stock update < 1s |

---

## 4. Data Requirements

### DR-INV-001: Item Master
| Field | Type | Description |
|-------|------|-------------|
| ItemId | VARCHAR(PK) | รหัสสินค้า |
| Barcode | VARCHAR | บาร์โค้ด |
| ItemName | VARCHAR | ชื่อสินค้า |
| UnitName | VARCHAR | หน่วย |
| Price | DECIMAL | ราคาขาย |
| TaxType | VARCHAR | ประเภทภาษี |
| Status | VARCHAR | สถานะ (Active/Inactive) |
| PictureName | VARCHAR | ชื่อรูปภาพ |
| Comments | TEXT | หมายเหตุ |

### DR-INV-002: Stock Balance
| Field | Type | Description |
|-------|------|-------------|
| ItemId | VARCHAR(FK) | รหัสสินค้า |
| WarehouseId | VARCHAR(FK) | รหัสคลัง |
| Quantity | DECIMAL | จำนวนคงเหลือ |
| MaxQuantity | DECIMAL | จำนวนสูงสุด |
| MinQuantity | DECIMAL | จำนวนต่ำสุด |

### DR-INV-003: Stock Movement
| Field | Type | Description |
|-------|------|-------------|
| MovementId | VARCHAR(PK) | รหัสการเคลื่อนไหว |
| ItemId | VARCHAR(FK) | รหัสสินค้า |
| WarehouseId | VARCHAR(FK) | รหัสคลัง |
| TransactionNo | VARCHAR(FK) | เลขที่ธุรกรรม |
| Quantity | DECIMAL | จำนวนที่เคลื่อนไหว |
| MovementType | VARCHAR | ประเภทการเคลื่อนไหว |
| MovementDate | DATE | วันที่เคลื่อนไหว |

---

## 5. Business Rules

### BR-INV-001: Stock Validation
```
FOR EACH transaction_item:
  IF transaction_type IN (Sale, TransferOut, BranchSend) THEN
    IF item.Quantity > GetCurrentStock(item.ItemId, item.WarehouseId) THEN
      BLOCK transaction
```

### BR-INV-002: Stock Update Rule
```
SWITCH transaction_type:
  CASE Sale, TransferOut, BranchSend:
    Stock -= Quantity
  CASE PurchaseReceive, TransferIn, BranchReceive:
    Stock += Quantity
  CASE StockAdjustment:
    Stock = QtyAdjust
```

### BR-INV-003: Max/Min Validation
```
IF NewMaxQuantity < NewMinQuantity THEN
  REJECT update
  SHOW error "Max must be greater than Min"
```

### BR-INV-004: Suite Stock Rule
```
IF item.Type = "Suite" THEN
  FOR EACH sub_item IN item.SubItems:
    sub_item.Stock -= suite_quantity
```

---

## 6. Configuration Reference

| Config Key | Section | ค่า Default | คำอธิบาย |
|------------|---------|------------|----------|
| `tab_search_item` | Items | 1 | โหมด tab ค้นหาสินค้า |
| `search_item_group` | Items | 3 | กลุ่มสินค้าสำหรับค้นหา |
| `search_sys_item_id` | Items | 4 | System Item ID สำหรับค้นหา |
| `default_quantity` | Items | 1 | จำนวนเริ่มต้น |
| `warehouse` | Items | 0 | คลังเริ่มต้น |
| `showquantity` | POS | 1 | แสดงจำนวน |
| `openpicture` | Items | 0 | เปิดรูปภาพสินค้า |
| `isusedweight` | FeatureScale | 1 | ใช้น้ำหนัก |
| `search_item_group_l1..l5` | GroupItem | 3/-1 | ระดับกลุ่มสินค้า |
