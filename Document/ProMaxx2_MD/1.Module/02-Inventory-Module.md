# QA Test Plan — โมดูล Inventory (คลังสินค้า)

> ProMaxx 21.0.0-beta.1
> วันที่สร้าง: 17 สิงหาคม 2569

---

## 1. ภาพรวมโมดูล

โมดูล Inventory รับผิดชอบการจัดการคลังสินค้าทั้งหมดของระบบ ประกอบด้วย **17 โมดูลย่อย**

### 1.1 โครงสร้างโมดูล

```
Promaxxs.Modules.Inventory
├── Items                  ─── จัดการข้อมูลสินค้า (Master)
├── ItemTemplate           ─── เทมเพลตสินค้า (Bundle/Set)
├── GroupItem              ─── กลุ่มสินค้า
├── StockCount             ─── ตรวจนับสินค้า
├── SerialCount            ─── ตรวจนับตาม Serial
├── LotCount               ─── ตรวจนับตาม Lot
├── StockAdjustment        ─── ปรับปรุงยอดสินค้า
├── PriceChange            ─── เปลี่ยนแปลงราคา
├── ItemStockMinMax        ─── ตั้งค่า Max/Min
├── TransLocation          ─── โอนย้ายระหว่างคลัง
├── DeviceDoc              ─── เอกสารอุปกรณ์
├── DocInInventory         ─── เอกสารรับเข้าคลัง
├── ExportCustomer         ─── ส่งออกข้อมูลลูกค้า
├── IStockQuantityBalance  ─── ยอดคงเหลือ
├── IStockSerialMovement   ─── การเคลื่อนไหว Serial
├── IStockTransaction      ─── ธุรกรรมสินค้า
├── CategoryConflict       ─── ตรวจสอบ Category
├── SetItemControlledDrug  ─── ตั้งค่ายาควบคุม
└── Suite                  ─── จัดชุดสินค้า
```

---

## 2. โมดูลย่อย: Items (ข้อมูลสินค้า)

### 2.1 ข้อมูลสินค้า (Master Item)

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| ItemId | 115 | รหัสสินค้า |
| Barcode | 100 | บาร์โค้ด |
| ItemName | 140 | ชื่อสินค้า |
| UnitName | 90 | หน่วย |
| Price | 90 | ราคาขาย |
| TaxtypeStr | 90 | ประเภทภาษี |
| FusedStr | 105 | สถานะใช้งาน |
| FShowDetailStr | 165 | แสดงรายละเอียด |
| Comments | 451 | หมายเหตุ |

### 2.2 Test Cases

#### TC-ITEM-001: เพิ่มสินค้าใหม่
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิดหน้า Items<br>2. กดเพิ่มใหม่<br>3. กรอกข้อมูลสินค้าครบถ้วน<br>4. บันทึก |
| **Expected Result** | สินค้าถูกสร้างสำเร็จ ปรากฏในรายการ |
| **Priority** | High |

#### TC-ITEM-002: เพิ่มสินค้าที่มีรหัสซ้ำ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เพิ่มสินค้ารหัส "A001"<br>2. พยายามเพิ่มสินค้ารหัส "A001" อีกครั้ง |
| **Expected Result** | แสดง Error Message ไม่ให้บันทึกซ้ำ |
| **Priority** | High |

#### TC-ITEM-003: แก้ไขข้อมูลสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้า<br>2. แก้ไขชื่อ/ราคา<br>3. บันทึก |
| **Expected Result** | ข้อมูลอัพเดทสำเร็จ |
| **Priority** | High |

#### TC-ITEM-004: ลบสินค้าที่ไม่มีธุรกรรม
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้าที่ไม่เคยมีธุรกรรม<br>2. ลบ |
| **Expected Result** | ลบสำเร็จ |
| **Priority** | High |

#### TC-ITEM-005: ลบสินค้าที่มีธุรกรรมแล้ว
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้าที่เคยมีธุรกรรม<br>2. ลบ |
| **Expected Result** | BLOCK ไม่ให้ลบ หรือ Soft Delete |
| **Priority** | High |

#### TC-ITEM-006: ค้นหาสินค้าด้วย Barcode
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ค้นหาด้วยบาร์โค้ด |
| **Expected Result** | พบสินค้าที่ตรงกัน |
| **Priority** | High |

#### TC-ITEM-007: ค้นหาสินค้าด้วยชื่อ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ค้นหาด้วยชื่อ (partial match) |
| **Expected Result** | แสดงรายการที่ตรงกัน |
| **Priority** | Medium |

#### TC-ITEM-008: ค้นหาสินค้าด้วย ItemId
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ค้นหาด้วยรหัสสินค้า |
| **Expected Result** | พบสินค้าที่ตรงกัน |
| **Priority** | Medium |

#### TC-ITEM-009: ตั้งค่า Tax Type
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้า<br>2. ตั้งค่า Tax Type = VAT 7% |
| **Expected Result** | สินค้าคำนวณ VAT 7% ในธุรกรรม |
| **Priority** | High |

#### TC-ITEM-010: ปิด/เปิดสถานะสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยนสถานะสินค้าเป็น "ไม่ใช้งาน" |
| **Expected Result** | สินค้าไม่ปรากฏในรายการค้นหาธุรกรรม |
| **Priority** | Medium |

#### TC-ITEM-011: บันทึกรูปภาพสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Config** | `openpicture = 1` ใน system.ini |
| **Steps** | 1. เลือกสินค้า<br>2. เพิ่มรูปภาพ<br>3. บันทึก |
| **Expected Result** | รูปภาพถูกบันทึกและแสดงในหน้าสินค้า |
| **Priority** | Low |

---

## 3. โมดูลย่อย: ItemTemplate (เทมเพลตสินค้า)

### 3.1 ข้อมูล Master

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| ItemId | 114 | รหัสสินค้า |
| Barcode | 99 | บาร์โค้ด |
| ItemName | 139 | ชื่อสินค้า |
| UnitName | 89 | หน่วย |
| Price | 89 | ราคา |
| TaxtypeStr | 89 | ประเภทภาษี |
| FusedStr | 104 | สถานะใช้งาน |
| TemplateShowPriceTypeStr | 119 | แสดงประเภทราคา |
| TemplatePriceTypeStr | 119 | ประเภทราคา |
| GroupName | 144 | กลุ่มสินค้า |
| Comments | 119 | หมายเหตุ |
| PictureName | 100 | ชื่อรูปภาพ |

### 3.2 ข้อมูล Detail

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| No | 50 | ลำดับ |
| ItemId | 115 | รหัสสินค้า |
| ItemName | 393 | ชื่อสินค้า |
| UnitName | 90 | หน่วย |
| Quantity | 90 | จำนวน |
| Price | 150 | ราคา |
| FmlDiscountItem | 90 | ส่วนลดรายการ |
| LineTotal | 110 | ยอดรวมแถว |
| OptLockItem | 90 | ล็อครายการ |
| Comments | 150 | หมายเหตุ |

### 3.3 Test Cases

#### TC-TMPL-001: สร้างเทมเพลตสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด ItemTemplate<br>2. สร้างเทมเพลตใหม่<br>3. เพิ่มสินค้าหลายรายการ<br>4. บันทึก |
| **Expected Result** | เทมเพลตถูกสร้างสำเร็จ |
| **Priority** | High |

#### TC-TMPL-002: แก้ไขเทมเพลต
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกเทมเพลต<br>2. แก้ไขรายการสินค้า<br>3. บันทึก |
| **Expected Result** | เทมเพลตอัพเดทสำเร็จ |
| **Priority** | High |

#### TC-TMPL-003: คำนวณราคาเทมเพลต
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เพิ่มสินค้า A (ราคา 100) จำนวน 2<br>2. เพิ่มสินค้า B (ราคา 50) จำนวน 1 |
| **Expected Result** | ราคาเทมเพลต = 250 |
| **Priority** | High |

#### TC-TMPL-004: ลบเทมเพลต
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกเทมเพลต<br>2. ลบ |
| **Expected Result** | เทมเพลตถูกลบ |
| **Priority** | Medium |

---

## 4. โมดูลย่อย: StockCount (ตรวจนับสินค้า)

### 4.1 Grid Layout

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| Itemid | 80 | รหัสสินค้า |
| Itemname | 120 | ชื่อสินค้า |
| Unitname | 50 | หน่วย |
| SystemQty | 80 | จำนวนในระบบ |
| CountQty | 80 | จำนวนนับจริง |
| Diff | 70 | ผลต่าง |

### 4.2 Test Cases

#### TC-SC-001: ตรวจนับสินค้าปกติ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด StockCount<br>2. สร้างรายการตรวจนับ<br>3. ระบุ CountQty ทั้งหมด<br>4. บันทึก |
| **Expected Result** | ระบบคำนวณ Diff อัตโนมัติ |
| **Priority** | High |

#### TC-SC-002: ตรวจนับแล้ว Diff = 0
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. นับสินค้าได้เท่ากับ SystemQty |
| **Expected Result** | Diff = 0, ไม่ต้องปรับ Stock |
| **Priority** | High |

#### TC-SC-003: ตรวจนับแล้วมีสินค้าเกิน
| รายการ | ราย情人หด |
|--------|------------|
| **Steps** | 1. นับสินค้ามากกว่า SystemQty |
| **Expected Result** | Diff > 0, ต้องปรับ Stock เพิ่ม |
| **Priority** | High |

#### TC-SC-004: ตรวจนับแล้วสินค้าขาด
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. นับสินค้าน้อยกว่า SystemQty |
| **Expected Result** | Diff < 0, ต้องปรับ Stock ลด |
| **Priority** | High |

---

## 5. โมดูลย่อย: SerialCount (ตรวจนับ Serial)

### 5.1 Grid Layout

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| Itemid | 80 | รหัสสินค้า |
| Itemname | 110 | ชื่อสินค้า |
| SerialLotNo | 100 | Serial/Lot Number |
| Unitname | 50 | หน่วย |
| SystemQty | 80 | จำนวนในระบบ |
| CountQty | 80 | จำนวนนับจริง |
| Status | 90 | สถานะ |

### 5.2 Test Cases

#### TC-SNC-001: ตรวจนับ Serial Number
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้าที่มี Serial<br>2. นับจำนวน Serial จริง<br>3. บันทึก |
| **Expected Result** | ระบบบันทึกจำนวนนับจริงต่อ Serial |
| **Priority** | High |

#### TC-SNC-002: ตรวจนับ Serial ที่หายไป
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. สินค้ามี Serial A,B,C ในระบบ<br>2. นับได้แค่ A,B |
| **Expected Result** | Serial C แสดง Status = "ขาด" |
| **Priority** | High |

---

## 6. โมดูลย่อย: LotCount (ตรวจนับ Lot)

### 6.1 Grid Layout

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| Itemid | 80 | รหัสสินค้า |
| Itemname | 110 | ชื่อสินค้า |
| LotNo | 100 | Lot Number |
| Unitname | 50 | หน่วย |
| SystemQty | 80 | จำนวนในระบบ |
| CountQty | 80 | จำนวนนับจริง |
| Status | 90 | สถานะ |

### 6.2 Test Cases

#### TC-LC-001: ตรวจนับ Lot Number
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้าที่มี Lot<br>2. นับจำนวน Lot จริง<br>3. บันทึก |
| **Expected Result** | ระบบบันทึกจำนวน Lot จริง |
| **Priority** | High |

#### TC-LC-002: ตรวจนับ Lot ที่หมดอายุ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. สินค้า Lot หมดอายุ<br>2. ตรวจนับ |
| **Expected Result** | แสดง Status ว่าหมดอายุ |
| **Priority** | Medium |

---

## 7. โมดูลย่อย: StockAdjustment (ปรับปรุงยอดสินค้า)

### 7.1 Grid Layout

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| ลำดับ | 55 | ลำดับ |
| ScanCode | 160 | รหัสสินค้า |
| ItemName | 285 | ชื่อสินค้า |
| UnitName | 70 | หน่วย |
| UnitCost | 100 | ต้นทุน/หน่วย |
| QtyRemain | 110 | คงเหลือ |
| QtyAdjust | 140 | จำนวนที่ปรับ |
| Diff | 90 | ผลต่าง |
| Comments | 285 | หมายเหตุ |

### 7.2 Test Cases

#### TC-SA-001: ปรับเพิ่ม Stock
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด StockAdjustment<br>2. เลือกสินค้า<br>3. ระบุ QtyAdjust มากกว่า QtyRemain<br>4. บันทึก |
| **Expected Result** | Stock เพิ่มขึ้นตาม QtyAdjust |
| **Priority** | High |

#### TC-SA-002: ปรับลด Stock
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด StockAdjustment<br>2. เลือกสินค้า<br>3. ระบุ QtyAdjust น้อยกว่า QtyRemain<br>4. บันทึก |
| **Expected Result** | Stock ลดลงตาม Diff |
| **Priority** | High |

#### TC-SA-003: ปรับ Stock เป็น 0
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง QtyAdjust = 0 |
| **Expected Result** | Stock กลายเป็น 0 |
| **Priority** | High |

#### TC-SA-004: ตรวจสอบ Diff อัตโนมัติ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ป้อน QtyAdjust<br>2. ตรวจสอบ Diff |
| **Expected Result** | Diff = QtyAdjust - QtyRemain (คำนวณอัตโนมัติ) |
| **Priority** | High |

---

## 8. โมดูลย่อย: PriceChange (เปลี่ยนแปลงราคา)

### 8.1 Grid Layout

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| ItemCode | 110 | รหัสสินค้า |
| Barcode | 110 | บาร์โค้ด |
| ItemName | 549 | ชื่อสินค้า |
| UnitName | 75 | หน่วย |
| PriceChangeType | 120 | ประเภทการเปลี่ยนราคา |
| FmlPriceChange | 110 | ราคาที่เปลี่ยน |
| PriceLevelNo | 90 | ระดับราคา |
| FmlDiscountItem | 90 | ส่วนลดรายการ |
| FmlDiscountRow | 90 | ส่วนลดแถว |
| PriceTypeName | 140 | ชื่อประเภทราคา |

### 8.2 Test Cases

#### TC-PC-001: เปลี่ยนราคาสินค้าปกติ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด PriceChange<br>2. เลือกสินค้า<br>3. ใส่ราคาใหม่<br>4. บันทึก |
| **Expected Result** | ราคาสินค้าเปลี่ยนสำเร็จ |
| **Priority** | High |

#### TC-PC-002: เปลี่ยนราคาหลายสินค้าพร้อมกัน
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้าหลายรายการ<br>2. ตั้งราคาใหม่<br>3. บันทึก |
| **Expected Result** | ราคาเปลี่ยนทุกรายการ |
| **Priority** | Medium |

#### TC-PC-003: ตั้งค่า Price Level
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยนราคาที่ Price Level ต่างกัน |
| **Expected Result** | ราคาเปลี่ยนเฉพาะ Level ที่เลือก |
| **Priority** | Medium |

#### TC-PC-004: ตั้งค่า Discount
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้งค่าส่วนลดรายการ<br>2. ตั้งค่าส่วนลดแถว<br>3. บันทึก |
| **Expected Result** | ส่วนลดถูกบันทึกและใช้ในธุรกรรม |
| **Priority** | Medium |

---

## 9. โมดูลย่อย: ItemStockMinMax (ตั้งค่า Max/Min)

### 9.1 Grid Layout

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| Itemid | 100 | รหัสสินค้า |
| Barcode | 140 | บาร์โค้ด |
| Itemname | 515 | ชื่อสินค้า |
| Unitname | 80 | หน่วย |
| Maxquantity | 140 | จำนวนสูงสุด |
| Minquantity | 140 | จำนวนต่ำสุด |

### 9.2 Configuration

```ini
[MaxMinQuantity]
item_group_l1_id = -1
item_group_l2_id = -1
item_group_l3_id = -1
item_group_l4_id = -1
item_group_l5_id = -1
```

### 9.3 Test Cases

#### TC-MM-001: ตั้งค่า Max/Min สินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้า<br>2. ตั้ง Max = 100, Min = 10<br>3. บันทึก |
| **Expected Result** | ค่า Max/Min ถูกบันทึก |
| **Priority** | High |

#### TC-MM-002: Max น้อยกว่า Min
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้ง Max = 10, Min = 100 |
| **Expected Result** | แสดง Warning หรือ Block |
| **Priority** | High |

#### TC-MM-003: ค้นหาสินค้าด้วย Level Group
| รายการ | รายละเอียด |
|--------|------------|
| **Config** | `search_item_group_l1..l5` ใน system.ini |
| **Steps** | 1. เลือก Level ต่างกัน<br>2. ค้นหาสินค้า |
| **Expected Result** | แสดงสินค้าตาม Level ที่เลือก |
| **Priority** | Medium |

---

## 10. โมดูลย่อย: TransLocation (โอนย้ายระหว่างคลัง)

### 10.1 Grid Layout

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| ScanCode | 145 | รหัสสินค้า |
| ItemName | 211 | ชื่อสินค้า |
| SelectedSourceLocation | 135 | คลังต้นทาง |
| SelectedDestLocation | 175 | คลังปลายทาง |
| SourceLocationBalance | 140 | คงเหลือต้นทาง |
| DestLocationBalance | 150 | คงเหลือปลายทาง |
| Quantity | 90 | จำนวนโอน |
| UnitName | 70 | หน่วย |
| Comments | 211 | หมายเหตุ |

### 10.2 Test Cases

#### TC-TL-001: โอนสินค้าระหว่างคลังปกติ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกคลังต้นทาง A<br>2. เลือกคลังปลายทาง B<br>3. เลือกสินค้า จำนวน 10<br>4. บันทึก |
| **Expected Result** | Stock A ลด 10, Stock B เพิ่ม 10 |
| **Priority** | High |

#### TC-TL-002: โอนเกิน stock คลังต้นทาง
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. Stock คลัง A = 5<br>2. พยายามโอน 10 |
| **Expected Result** | BLOCK ไม่ให้ทำรายการ |
| **Priority** | High |

#### TC-TL-003: ตรวจสอบ Balance ทั้งสองคลัง
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ดู SourceLocationBalance<br>2. ดู DestLocationBalance |
| **Expected Result** | แสดงจำนวนคงเหลือถูกต้อง |
| **Priority** | High |

---

## 11. โมดูลย่อย: DeviceDoc (เอกสารอุปกรณ์)

### 11.1 Grid Layout

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| Itemid | 80 | รหัสสินค้า |
| Barcode | 90 | บาร์โค้ด |
| Itemname | 140 | ชื่อสินค้า |
| DetailWarehouseName | 80 | คลัง |
| DiscountDisplay | 60 | ส่วนลด |
| SerialNo | 80 | Serial Number |
| Unitname | 50 | หน่วย |
| Quantity | 70 | จำนวน |
| Price | 80 | ราคา |
| ExpireDateDisplay | 90 | วันหมดอายุ |
| LotNo | 80 | Lot Number |

### 11.2 Test Cases

#### TC-DD-001: สร้างเอกสารอุปกรณ์
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด DeviceDoc<br>2. เพิ่มสินค้า + Serial + Lot + ExpireDate<br>3. บันทึก |
| **Expected Result** | เอกสารถูกสร้าง ข้อมูลครบถ้วน |
| **Priority** | High |

#### TC-DD-002: ตรวจสอบ Expire Date
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ป้อนวันหมดอายุที่ผ่านไปแล้ว |
| **Expected Result** | แสดง Warning ว่าสินค้าหมดอายุ |
| **Priority** | Medium |

#### TC-DD-003: ตรวจสอบ Lot Number
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ป้อน Lot Number ที่ซ้ำกัน |
| **Expected Result** | ตรวจสอบความถูกต้อง |
| **Priority** | Medium |

---

## 12. โมดูลย่อย: IStock (รายงานคลังสินค้า)

### 12.1 IStockQuantityBalance

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| Ordinary | 60 | ลำดับ |
| ItemId | 120 | รหัสสินค้า |
| ItemName | 530 | ชื่อสินค้า |
| BranchId | 90 | รหัสสาขา |
| BranchName | 160 | ชื่อสาขา |
| Quantity | 120 | จำนวน |

### 12.2 IStockSerialMovement

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| ItemId | 100 | รหัสสินค้า |
| ItemName | 340 | ชื่อสินค้า |
| TranNo | 130 | เลขที่ธุรกรรม |
| BranchName | 130 | สาขา |
| PersonId | 100 | รหัสผู้ทำรายการ |
| PersonName | 160 | ชื่อผู้ทำรายการ |
| TranDate | 120 | วันที่ทำรายการ |

### 12.3 IStockTransactionMaster

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| BranchName | 140 | สาขา |
| TranNo | 710 | เลขที่ธุรกรรม |
| GrandTotal | 120 | ยอดรวม |
| TranDate | 110 | วันที่ |

### 12.4 Test Cases

#### TC-IS-001: ดูยอดคงเหลือสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด IStockQuantityBalance<br>2. เลือกสาขา |
| **Expected Result** | แสดงจำนวนคงเหลือถูกต้อง |
| **Priority** | High |

#### TC-IS-002: ดูการเคลื่อนไหว Serial
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด IStockSerialMovement<br>2. เลือกสินค้า |
| **Expected Result** | แสดงประวัติการเคลื่อนไหว |
| **Priority** | Medium |

#### TC-IS-003: ดูธุรกรรมสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด IStockTransactionMaster<br>2. เลือกช่วงวันที่ |
| **Expected Result** | แสดงรายการธุรกรรมถูกต้อง |
| **Priority** | Medium |

---

## 13. โมดูลย่อย: Suite (จัดชุดสินค้า)

### 13.1 Master Grid

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| ItemId | 115 | รหัสสินค้าหลัก |
| Barcode | 100 | บาร์โค้ด |
| ItemName | 140 | ชื่อสินค้า |
| UnitName | 90 | หน่วย |
| Price | 90 | ราคา |
| TaxtypeStr | 90 | ประเภทภาษี |
| FusedStr | 105 | สถานะใช้งาน |
| FShowDetailStr | 165 | แสดงรายละเอียด |
| Comments | 451 | หมายเหตุ |

### 13.2 Detail Grid

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| No | 50 | ลำดับ |
| ItemId | 115 | รหัสสินค้า |
| ItemName | 301 | ชื่อสินค้า |
| UnitName | 90 | หน่วย |
| SpecName | 110 | ชื่อสเปค |
| Quantity | 90 | จำนวน |
| MemLastPrice | 115 | ราคาล่าสุด |
| MemLastCost | 105 | ต้นทุนล่าสุด |
| LineTotalPrice | 110 | ราคารวมแถว |
| LineTotalCost | 110 | ต้นทุนรวมแถว |
| Comments | 150 | หมายเหตุ |

### 13.3 Test Cases

#### TC-SUITE-001: สร้างชุดสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. สร้างสินค้าหลัก<br>2. เพิ่มสินค้าย่อยหลายรายการ<br>3. บันทึก |
| **Expected Result** | ชุดสินค้าถูกสร้างสำเร็จ |
| **Priority** | High |

#### TC-SUITE-002: ขายชุดสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ขายสินค้าชุด (Suite)<br>2. ตรวจสอบ Stock สินค้าย่อย |
| **Expected Result** | Stock สินค้าย่อยทุกรายการลดลง |
| **Priority** | High |

---

## 14. โมดูลย่อย: GroupItem (กลุ่มสินค้า)

### 14.1 Configuration

```ini
[GroupItem]
search_type         = 4
item_group_l1_id    = 3
item_group_l2_id    = -1
item_group_l3_id    = -1
item_group_l4_id    = -1
item_group_l5_id    = -1
```

### 14.2 Test Cases

#### TC-GI-001: จัดกลุ่มสินค้า 5 ระดับ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. สร้าง Level 1<br>2. สร้าง Level 2 ภายใต้ Level 1<br>3. ... ถึง Level 5 |
| **Expected Result** | กลุ่มสินค้ามี 5 ระดับชัดเจน |
| **Priority** | High |

---

## 15. CategoryConflict (ตรวจสอบความขัดแย้ง)

### 15.1 Test Cases

#### TC-CC-001: ตรวจสอบ Category Conflict
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด CategoryConflict<br>2. ตรวจสอบสินค้า |
| **Expected Result** | แสดงสินค้าที่มี Category ซ้ำ/ขัดแย้ง |
| **Priority** | Low |

---

## 16. SetItemControlledDrug (สินค้ายาควบคุม)

### 16.1 Test Cases

#### TC-CD-001: ตั้งค่าสินค้ายาควบคุม
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้า<br>2. ตั้งค่าเป็น Controlled Drug |
| **Expected Result** | สินค้าถูกทำเครื่องหมายเป็นยาควบคุม |
| **Priority** | Medium |

---

## 17. Cross-Cutting Test Scenarios

### 17.1 Stock Movement Matrix

| โมดูล | Stock เพิ่ม | Stock ลด |
|-------|------------|----------|
| Items (Master) | - | - |
| StockCount | (หลังอนุมัติ) | (หลังอนุมัติ) |
| StockAdjustment | ✓ | ✓ |
| TransLocation | ✓ (ปลายทาง) | ✓ (ต้นทาง) |
| DeviceDoc | ✓ | - |
| DocInInventory | ✓ | - |

### 17.2 Configuration Matrix

| Config Key | Module | ค่า Default |
|------------|--------|------------|
| `tab_search_item` | Items, ItemTemplate | 1 |
| `search_item_group` | Items, ItemTemplate | 3 |
| `search_sys_item_id` | Items, ItemTemplate | 4 |
| `default_quantity` | Items | 1 |
| `warehouse` | Items | 0 |
| `showquantity` | POS | 1 |
| `openpicture` | Items | 0 |
| `isusedweight` | FeatureScale | 1 |

---

## 18. Regression Test Checklist

- [ ] เพิ่ม/แก้ไข/ลบ สินค้าได้ถูกต้อง
- [ ] ค้นหาสินค้าด้วย ItemId, Barcode, ชื่อ ได้ถูกต้อง
- [ ] Stock ถูกต้องหลังทุกธุรกรรม
- [ ] StockCount คำนวณ Diff ถูกต้อง
- [ ] SerialCount นับ Serial ถูกต้อง
- [ ] LotCount นับ Lot ถูกต้อง
- [ ] StockAdjustment ปรับ Stock ถูกต้อง
- [ ] PriceChange เปลี่ยนราคาถูกต้อง
- [ ] Max/Min ถูกต้อง
- [ ] TransLocation โอนถูกต้อง ทั้งต้นทางและปลายทาง
- [ ] DeviceDoc บันทึก Serial/Lot/Expiry ถูกต้อง
- [ ] IStock แสดงยอดคงเหลือถูกต้อง
- [ ] Suite ขายแล้ว Stock ย่อยลดถูกต้อง
- [ ] ไม่สามารถขายสินค้าเกิน Stock ได้
- [ ] GroupItem จัดกลุ่มได้ 5 ระดับ
