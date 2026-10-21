# QA Test Plan — โมดูล Transaction (ธุรกรรม)

> ProMaxx 21.0.0-beta.1
> วันที่สร้าง: 17 สิงหาคม 2569

---

## 1. ภาพรวมโมดูล

โมดูล Transaction รับผิดชอบการทำธุรกรรมทุกประเภทของระบบ POS ประกอบด้วย **12 ประเภทธุรกรรม** โดยแต่ละประเภทจะมีการตั้งค่าแยกเฉพาะตัวใน `config/system.ini`

### 1.1 โครงสร้างโมดูล

```
Promaxxs.Modules.Transaction
├── TranPos         ─── ขายหน้าร้าน (POS Sale)
├── TranOut         ─── ขายสินค้า (Sales)
├── TranIn          ─── รับสินค้า (Purchase Receive)
├── TranCnOut       ─── ออกใบลดหนี้ (Credit Note Issue)
├── TranCnIn        ─── รับใบลดหนี้ (Credit Note Receive)
├── TranTbOut       ─── โอนสินค้าออก (Transfer Out)
├── TranTbIn        ─── รับสินค้าโอนเข้า (Transfer In)
├── TranBnOut       ─── ส่งสินค้าเบิก (Branch Send)
├── TranBnIn        ─── รับสินค้าเบิก (Branch Receive)
├── TranDpOut       ─── จ่ายเงินมัดจำ (Deposit Return)
├── TranDpIn        ─── รับเงินมัดจำ (Deposit Receive)
├── TranTwOut       ─── เบิกจ่าย (Transfer Weight Out)
└── TranTwIn        ─── รับเบิก (Transfer Weight In)
```

---

## 2. การตั้งค่าร่วม (Shared Configuration)

### 2.1 Transaction Settings (system.ini)

| Setting | ค่า Default | คำอธิบาย |
|---------|------------|----------|
| `tab_search_item` | 1 | โหมด tab ค้นหาสินค้า |
| `search_item_group` | 3 | กลุ่มสินค้าเริ่มต้น |
| `search_sys_item_id` | 4 | System Item ID สำหรับค้นหา |
| `tax_rate` | 0-7% | อัตราภาษี |
| `default_quantity` | 0-1 | จำนวนสินค้าเริ่มต้น |
| `warehouse` | 0 | คลังสินค้าเริ่มต้น |
| `focus_next_column` | QUANTITY/ITEMNAME/SCANCODE | Column ที่ cursor ไปหลังกรอก |
| `focus_next_row` | NO | เลื่อนไปแถวถัดไปอัตโนมัติ |
| `service_charge_rate` | 0% | อัตราค่าบริการ |

---

## 3. โมดูลย่อย: TranPos (ขายหน้าร้าน)

### 3.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 35 |
| Tax Rate | 0% |
| Focus Column | SCANCODE |
| Person ID Cash | 999 |

### 3.2 ฟังก์ชันหลัก

- ขายสินค้าหน้าร้านแบบ POS
- รองรับการสแกนบาร์โค้ด
- รองรับการค้นหาสินค้า
- พิมพ์ใบเสร็จ (Receipt)

### 3.3 Test Cases

#### TC-POS-001: ขายสินค้า 1 รายการ ปกติ
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | มีสินค้าในระบบอย่างน้อย 1 รายการ ราคา > 0 |
| **Steps** | 1. เปิดหน้า TranPos<br>2. สแกน/ค้นหาสินค้า<br>3. ตรวจสอบราคาและจำนวน<br>4. กดยืนยันขาย |
| **Expected Result** | สร้างธุรกรรมสำเร็จ ยอดรวมถูกต้อง |
| **Priority** | High |

#### TC-POS-002: ขายสินค้าหลายรายการ
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | มีสินค้าในระบบ 3 รายการขึ้นไป |
| **Steps** | 1. เพิ่มสินค้ารายการที่ 1<br>2. เพิ่มสินค้ารายการที่ 2<br>3. เพิ่มสินค้ารายการที่ 3<br>4. ตรวจสอบยอดรวม |
| **Expected Result** | ยอดรวม = ราคา x จำนวน ของทุกรายการ |
| **Priority** | High |

#### TC-POS-003: แก้ไขจำนวนสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | มีสินค้าในตะกร้าอย่างน้อย 1 รายการ |
| **Steps** | 1. เลือกสินค้าในตะกร้า<br>2. เปลี่ยนจำนวน<br>3. ตรวจสอบยอดรวม |
| **Expected Result** | ยอดรวมเปลี่ยนตามจำนวนใหม่ |
| **Priority** | High |

#### TC-POS-004: ลบสินค้าจากตะกร้า
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | มีสินค้าในตะกร้า 2 รายการ |
| **Steps** | 1. เลือกสินค้ารายการที่ 1<br>2. กดลบ<br>3. ตรวจสอบตะกร้า |
| **Expected Result** | เหลือสินค้า 1 รายการ ยอดรวมถูกต้อง |
| **Priority** | High |

#### TC-POS-005: ขายสินค้าที่มี Tax 7%
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | ตั้งค่า tax_rate = 7 ใน TranPos |
| **Steps** | 1. ขายสินค้าที่มี VAT<br>2. ตรวจสอบยอด Tax |
| **Expected Result** | Tax = ยอดขาย x 7% |
| **Priority** | High |

#### TC-POS-006: ขายสินค้าที่ไม่มีในสต็อก
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | สินค้ามี stock = 0 |
| **Steps** | 1. พยายามขายสินค้า |
| **Expected Result** | แสดง warning หรือ BLOCK ไม่ให้ขาย |
| **Priority** | Medium |

#### TC-POS-007: สแกนบาร์โค้ดสินค้า
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | มีสินค้าที่มีบาร์โค้ดในระบบ |
| **Steps** | 1. ใช้ barcode scanner สแกน |
| **Expected Result** | สินค้าถูกเพิ่มลงตะกร้าอัตโนมัติ |
| **Priority** | High |

#### TC-POS-008: ค้นหาสินค้าด้วยชื่อ
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | มีสินค้า "น้ำเปล่า" ในระบบ |
| **Steps** | 1. พิมพ์ "น้ำ" ในช่องค้นหา<br>2. เลือกจากผลลัพธ์ |
| **Expected Result** | แสดงรายการสินค้าที่ตรงกับคำค้น |
| **Priority** | Medium |

#### TC-POS-009: พิมพ์ใบเสร็จ
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | ทำรายการขายสำเร็จ |
| **Steps** | 1. ยืนยันการขาย<br>2. เลือกพิมพ์ใบเสร็จ |
| **Expected Result** | ใบเสร็จพิมพ์ออก receipt printer |
| **Priority** | Medium |

#### TC-POS-010: ปิด/ยกเลิกรายการขาย
| รายการ | รายละเอียด |
|--------|------------|
| **Precondition** | กำลังทำรายการขายค้างอยู่ |
| **Steps** | 1. กดยกเลิกรายการ |
| **Expected Result** | รายการถูกยกเลิก ไม่มีผลต่อ stock |
| **Priority** | Medium |

---

## 4. โมดูลย่อย: TranOut (ขายสินค้า)

### 4.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 45 |
| Tax Rate | 7% |
| Focus Column | QUANTITY |
| Person ID Cash | 999 |

### 4.2 ฟังก์ชันหลัก

- ขายสินค้าแบบ Invoice (ไม่ใช่ POS mode)
- รองรับการเลือกลูกค้า
- รองรับหลายคลังสินค้า
- คิด VAT 7%

### 4.3 Test Cases

#### TC-OUT-001: สร้างรายการขาย (Sales Invoice)
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranOut<br>2. เลือกลูกค้า<br>3. เพิ่มสินค้า<br>4. บันทึก |
| **Expected Result** | สร้าง Sales Invoice เลขที่ถูกต้อง |
| **Priority** | High |

#### TC-OUT-002: ขายสินค้าหลายคลัง
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกสินค้าจากคลัง A<br>2. เลือกสินค้าจากคลัง B<br>3. บันทึก |
| **Expected Result** | Stock ลดถูกต้องตามคลัง |
| **Priority** | High |

#### TC-OUT-003: ขายสินค้า VAT 7%
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เพิ่มสินค้าที่คิด VAT<br>2. ตรวจสอบยอด VAT |
| **Expected Result** | VAT = (ราคาสินค้า x 7%) ถูกต้อง |
| **Priority** | High |

#### TC-OUT-004: ขายสินค้าไม่มี VAT
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เพิ่มสินค้าที่ไม่คิด VAT<br>2. ตรวจสอบยอด |
| **Expected Result** | ไม่มี VAT ในใบแจ้งหนี้ |
| **Priority** | Medium |

#### TC-OUT-005: ขายสด (Cash Sale)
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ไม่เลือกลูกค้า (ใช้ Person ID 999)<br>2. ขายสินค้า<br>3. รับเงินสด |
| **Expected Result** | สร้างรายการขายสำเร็จ |
| **Priority** | High |

#### TC-OUT-006: ตรวจสอบ Service Charge
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ตั้งค่า service_charge_rate = 10%<br>2. ขายสินค้า<br>3. ตรวจสอบยอด Service Charge |
| **Expected Result** | Service Charge คิดถูกต้อง |
| **Priority** | Medium |

---

## 5. โมดูลย่อย: TranIn (รับสินค้า)

### 5.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 7 |
| Tax Rate | 0% |
| Focus Column | QUANTITY |
| Default Quantity | 0 |
| Service Charge | 0% |

### 5.2 ฟังก์ชันหลัก

- รับสินค้าจากการสั่งซื้อ (Purchase Order)
- บันทึกต้นทุนสินค้า
- เพิ่ม Stock สินค้า
- รองรับหลายคลัง

### 5.3 Test Cases

#### TC-IN-001: รับสินค้าปกติ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranIn<br>2. เลือก PO<br>3. ระบุจำนวนรับ<br>4. บันทึก |
| **Expected Result** | Stock เพิ่มตามจำนวนรับ, ต้นทุนถูกต้อง |
| **Priority** | High |

#### TC-IN-002: รับสินค้าบางส่วน
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. PO มี 10 ชิ้น<br>2. รับแค่ 5 ชิ้น<br>3. บันทึก |
| **Expected Result** | Stock เพิ่ม 5 ชิ้น PO ยังค้าง 5 ชิ้น |
| **Priority** | High |

#### TC-IN-003: รับสินค้าเกินจำนวนสั่ง
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. PO มี 10 ชิ้น<br>2. พยายามรับ 15 ชิ้น |
| **Expected Result** | แสดง Warning หรือ Block |
| **Priority** | Medium |

#### TC-IN-004: รับสินค้าเข้าคลังต่างกัน
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เลือกรับเข้าคลัง A<br>2. บันทึก |
| **Expected Result** | Stock คลัง A เพิ่มถูกต้อง |
| **Priority** | High |

---

## 6. โมดูลย่อย: TranCnOut (ออกใบลดหนี้)

### 6.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 52 |
| Tax Rate | 0% |
| Focus Column | ITEMNAME |

### 6.2 Test Cases

#### TC-CNOUT-001: ออกใบลดหนี้
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranCnOut<br>2. เลือกลูกค้า<br>3. เลือกสินค้าที่ต้องการลดหนี้<br>4. ระบุจำนวน/มูลค่า<br>5. บันทึก |
| **Expected Result** | สร้าง Credit Note, ลดยอดหนี้ลูกค้า |
| **Priority** | High |

#### TC-CNOUT-002: ตรวจสอบ Stock หลังลดหนี้
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ออกใบลดหนี้สินค้า A จำนวน 2<br>2. ตรวจสอบ Stock |
| **Expected Result** | Stock สินค้า A เพิ่ม 2 (คืนสินค้า) |
| **Priority** | High |

---

## 7. โมดูลย่อย: TranCnIn (รับใบลดหนี้)

### 7.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 18 |
| Tax Rate | 0% |
| Default Quantity | 1 |

### 7.2 Test Cases

#### TC-CNIN-001: รับใบลดหนี้
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranCnIn<br>2. เลือกซัพพลายเออร์<br>3. เลือกสินค้า<br>4. บันทึก |
| **Expected Result** | ลดยอดหนี้ซัพพลายเออร์, Stock ลดลง |
| **Priority** | High |

---

## 8. โมดูลย่อย: TranTbOut (โอนสินค้าออก)

### 8.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 53 |
| Tax Rate | 7% |
| Default Quantity | 1 |

### 8.2 Test Cases

#### TC-TBOUT-001: โอนสินค้าระหว่างคลัง
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranTbOut<br>2. เลือกคลังต้นทาง<br>3. เลือกคลังปลายทาง<br>4. เลือกสินค้า + จำนวน<br>5. บันทึก |
| **Expected Result** | Stock ต้นทางลด, สร้างเอกสารโอน |
| **Priority** | High |

#### TC-TBOUT-002: โอนสินค้าเกิน stock
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ลองโอนสินค้ามากกว่า stock ที่มี |
| **Expected Result** | BLOCK ไม่ให้ทำรายการ |
| **Priority** | High |

---

## 9. โมดูลย่อย: TranTbIn (รับสินค้าโอนเข้า)

### 9.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 64 |
| Tax Rate | 0% |
| Default Quantity | 1 |

### 9.2 Test Cases

#### TC-TBIN-001: รับสินค้าโอนเข้า
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranTbIn<br>2. เลือกเอกสารโอน<br>3. บันทึกรับ |
| **Expected Result** | Stock คลังปลายทางเพิ่ม |
| **Priority** | High |

---

## 10. โมดูลย่อย: TranBnOut (ส่งสินค้าเบิก)

### 10.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 26 |
| Tax Rate | 7% |
| Focus Column | QUANTITY |

### 10.2 Test Cases

#### TC-BNOUT-001: ส่งสินค้าเบิก
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranBnOut<br>2. เลือกสินค้า<br>3. ระบุจำนวน<br>4. บันทึก |
| **Expected Result** | สร้างเอกสารเบิก, Stock ลด |
| **Priority** | High |

---

## 11. โมดูลย่อย: TranBnIn (รับสินค้าเบิก)

### 11.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 71 |
| Tax Rate | 0% |
| Focus Column | ITEMNAME |

### 11.2 Test Cases

#### TC-BNIN-001: รับสินค้าเบิก
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranBnIn<br>2. เลือกเอกสารเบิก<br>3. บันทึกรับ |
| **Expected Result** | รับสินค้าสำเร็จ |
| **Priority** | High |

---

## 12. โมดูลย่อย: TranDpOut (จ่ายเงินมัดจำ)

### 12.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 29 |
| Tax Rate | 7% |

### 12.2 Test Cases

#### TC-DPOUT-001: จ่ายเงินมัดจำ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranDpOut<br>2. เลือกลูกค้า<br>3. ระบุจำนวนเงิน<br>4. บันทึก |
| **Expected Result** | สร้างเอกสารมัดจำ, บันทึกยอดจ่าย |
| **Priority** | High |

---

## 13. โมดูลย่อย: TranDpIn (รับเงินมัดจำ)

### 13.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 69 |
| Tax Rate | 0% |

### 13.2 Test Cases

#### TC-DPIN-001: รับเงินมัดจำ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranDpIn<br>2. เลือกลูกค้า<br>3. ระบุจำนวนเงิน<br>4. บันทึก |
| **Expected Result** | บันทึกรับเงินมัดจำสำเร็จ |
| **Priority** | High |

---

## 14. โมดูลย่อย: TranTwOut / TranTwIn (เบิก/รับเบิก)

### 14.1 รายละเอียด

| รายการ | TranTwOut | TranTwIn |
|--------|-----------|----------|
| Voucher ID | 28 | 66 |
| Tax Rate | 0% | 0% |
| Focus Column | QUANTITY | QUANTITY |

### 14.2 Test Cases

#### TC-TWOUT-001: เบิกจ่าย
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranTwOut<br>2. เลือกสินค้า + จำนวน<br>3. บันทึก |
| **Expected Result** | สร้างเอกสารเบิกจ่ายสำเร็จ |
| **Priority** | High |

#### TC-TWIN-001: รับเบิก
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด TranTwIn<br>2. เลือกเอกสารเบิกจ่าย<br>3. บันทึกรับ |
| **Expected Result** | รับสินค้าสำเร็จ |
| **Priority** | High |

---

## 15. Notation (ใบแจ้งหนี้/ใบเสร็จ)

### 15.1 รายละเอียด

| รายการ | ค่า |
|--------|------|
| Voucher ID | 26 |
| Tax Rate | 7% |

### 15.2 Test Cases

#### TC-NOT-001: สร้างใบแจ้งหนี้
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด Notation<br>2. เลือกลูกค้า<br>3. เพิ่มสินค้า<br>4. บันทึก |
| **Expected Result** | สร้างใบแจ้งหนี้สำเร็จ |
| **Priority** | High |

---

## 16. Credit Note / Deposit

### 16.1 Test Cases

#### TC-CN-001: Credit Note Out (ลดหนี้)
- ตรวจสอบว่า Credit Note ลดยอดหนี้ลูกค้าถูกต้อง

#### TC-CN-002: Credit Note In (รับลดหนี้)
- ตรวจสอบว่ารับ CN แล้วลดยอดหนี้ซัพพลายเออร์ถูกต้อง

#### TC-DEP-001: Deposit Transaction
- ตรวจสอบการบันทึกเงินมัดจำทั้งรับและจ่าย

---

## 17. Cross-Cutting Test Scenarios

### 17.1 Stock Impact Matrix

| ธุรกรรม | Stock เพิ่ม | Stock ลด |
|---------|------------|----------|
| TranPos | - | ✓ |
| TranOut | - | ✓ |
| TranIn | ✓ | - |
| TranCnOut | ✓ | - |
| TranCnIn | - | ✓ |
| TranTbOut | - | ✓ |
| TranTbIn | ✓ | - |
| TranBnOut | - | ✓ |
| TranBnIn | ✓ | - |
| TranDpOut | - | - |
| TranDpIn | - | - |
| TranTwOut | - | ✓ |
| TranTwIn | ✓ | - |

### 17.2 Auto Backup Test

| รายการ | รายละเอียด |
|--------|------------|
| **Config** | `autobackup = 1` ใน system.ini |
| **Test** | ทำธุรกรรมสำเร็จ แล้วตรวจสอบว่า backup ถูกสร้าง |
| **Priority** | Medium |

### 17.3 Language Test

| รายการ | รายละเอียด |
|--------|------------|
| **Config** | `language = TH`, `language_on_employee = EN` |
| **Test** | เปลี่ยนภาษาแล้วตรวจสอบ Transaction UI |
| **Priority** | Low |

---

## 18. Regression Test Checklist

- [ ] ทุกประเภทธุรกรรมบันทึกสำเร็จ
- [ ] Stock ลด/เพิ่มถูกต้องตามประเภทธุรกรรม
- [ ] VAT 7% คิดถูกต้อง
- [ ] Service Charge คิดถูกต้อง
- [ ] ไม่สามารถขายสินค้าเกิน stock ได้
- [ ] เปลี่ยนจำนวนแล้วยอดรวมอัพเดท
- [ ] ลบรายการแล้วยอดรวมอัพเดท
- [ ] Auto Backup ทำงานหลังทำธุรกรรม
- [ ] พิมพ์ใบเสร็จได้ถูกต้อง
- [ ] เลขที่เอกสาร (Voucher) ไม่ซ้ำกัน
- [ ] รองรับทั้งภาษาไทยและอังกฤษ
