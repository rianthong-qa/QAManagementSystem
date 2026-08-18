# Requirements — โมดูล Promotion Engine (ส่งเสริมการขาย)

> อ้างอิง: `System-Analysis.md` section 3.7
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Promotion Engine รับผิดชอบการจัดการโปรโมชันและส่งเสริมการขาย ตามที่ระบุใน System-Analysis.md section 3.7

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

#### FR-PMO-001: สร้างโปรโมชันใหม่
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-001 |
| **ชื่อ** | สร้างโปรโมชันใหม่ |
| **Description** | ระบบต้องรองรับการสร้างโปรโมชันใหม่ |
| **Input** | Code, Name, StartDate, EndDate, Status |
| **Output** | โปรโมชันที่สร้างสำเร็จ |
| **Priority** | Critical |

#### FR-PMO-002: เปิด/ปิดโปรโมชัน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-002 |
| **ชื่อ** | เปิด/ปิดโปรโมชัน |
| **Description** | ระบบต้องรองรับการเปิด/ปิดโปรโมชัน |
| **Input** | PromotionId, IsOn |
| **Output** | สถานะโปรโมชันเปลี่ยน |
| **Priority** | High |

#### FR-PMO-003: แก้ไขโปรโมชัน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-003 |
| **ชื่อ** | แก้ไขโปรโมชัน |
| **Description** | ระบบต้องรองรับการแก้ไขโปรโมชัน |
| **Input** | PromotionId, New data |
| **Output** | ข้อมูลโปรโมชันอัพเดท |
| **Priority** | High |

#### FR-PMO-004: ลบโปรโมชัน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-004 |
| **ชื่อ** | ลบโปรโมชัน |
| **Description** | ระบบต้องรองรับการลบโปรโมชัน |
| **Input** | PromotionId |
| **Output** | โปรโมชันถูกลบ |
| **Business Rule** | ลบได้เฉพาะโปรโมชันที่ยังไม่ได้ใช้ |
| **Priority** | Medium |

#### FR-PMO-005: โปรโมชันส่วนลด %
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-005 |
| **ชื่อ** | โปรโมชันส่วนลด % |
| **Description** | ระบบต้องรองรับโปรโมชันส่วนลดแบบเปอร์เซ็นต์ |
| **Input** | DiscountPercent, Conditions |
| **Output** | ราคาสินค้าลดตาม % ที่กำหนด |
| **Priority** | High |

#### FR-PMO-006: โปรโมชันส่วนลด เงินคงที่
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-006 |
| **ชื่อ** | โปรโมชันส่วนลด เงินคงที่ |
| **Description** | ระบบต้องรองรับโปรโมชันส่วนลดแบบเงินคงที่ |
| **Input** | DiscountAmount, Conditions |
| **Output** | ราคาสินค้าลดตามเงินที่กำหนด |
| **Priority** | High |

#### FR-PMO-007: โปรโมชัน Buy X Get Y
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-007 |
| **ชื่อ** | โปรโมชัน Buy X Get Y |
| **Description** | ระบบต้องรองรับโปรโมชันซื้อ X แถม Y |
| **Input** | BuyQty, GetQty, Items |
| **Output** | ได้รับสินค้าเพิ่มตามเงื่อนไข |
| **Priority** | High |

#### FR-PMO-008: โปรโมชันตามช่วงเวลา
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-008 |
| **ชื่อ** | โปรโมชันตามช่วงเวลา |
| **Description** | ระบบต้องรองรับโปรโมชันที่มีวันเริ่ม-สิ้นสุด |
| **Input** | StartDate, EndDate |
| **Output** | โปรโมชันใช้ได้เฉพาะช่วงที่กำหนด |
| **Priority** | High |

#### FR-PMO-009: โปรโมชันสำหรับสมาชิก
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-009 |
| **ชื่อ** | โปรโมชันสำหรับสมาชิก |
| **Description** | ระบบต้องรองรับโปรโมชันเฉพาะสมาชิก |
| **Input** | MemberOnly flag |
| **Output** | สมาชิกได้รับส่วนลด ไม่ใช่สมาชิกไม่ได้รับ |
| **Priority** | Medium |

#### FR-PMO-010: นับจำนวนการใช้โปรโมชัน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-PMO-010 |
| **ชื่อ** | นับจำนวนการใช้โปรโมชัน |
| **Description** | ระบบต้องนับจำนวนครั้งที่ใช้โปรโมชัน |
| **Input** | PromotionId |
| **Output** | Used count |
| **Priority** | Medium |

---

## 3. Non-Functional Requirements

### NFR-PMO-001: Promotion Calculation Speed
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-PMO-001 |
| **ชื่อ** | ความเร็วคำนวณโปรโมชัน |
| **Description** | การคำนวณโปรโมชันต้องเสร็จสิ้นภายใน 1 วินาที |
| **Measurement** | Calculation time < 1s |

### NFR-PMO-002: Multiple Promotions
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-PMO-002 |
| **ชื่อ** | โปรโมชันซ้อนกัน |
| **Description** | ระบบต้องกำหนดว่าโปรโมชันซ้อนกันได้หรือไม่ |
| **Measurement** | Business rule defined |

---

## 4. Data Requirements

### DR-PMO-001: Promotion Master
| Field | Type | Description |
|-------|------|-------------|
| PromotionId | VARCHAR(PK) | รหัสโปรโมชัน |
| Code | VARCHAR | รหัสโปรโมชัน |
| Name | VARCHAR | ชื่อโปรโมชัน |
| StartDate | DATE | วันที่เริ่ม |
| EndDate | DATE | วันที่สิ้นสุด |
| Status | VARCHAR | สถานะ (Active/Inactive/Expired) |
| IsOn | BOOLEAN | เปิด/ปิด |
| MemberOnly | BOOLEAN | เฉพาะสมาชิก |
| DiscountType | VARCHAR | ประเภทส่วนลด (Percent/Amount/BuyXGetY) |
| DiscountValue | DECIMAL | ค่าส่วนลด |
| BuyQty | INT | จำนวนซื้อ (BuyXGetY) |
| GetQty | INT | จำนวนแถม (BuyXGetY) |
| UsedCount | INT | จำนวนครั้งที่ใช้ |

### DR-PMO-002: Promotion Items
| Field | Type | Description |
|-------|------|-------------|
| PromotionId | VARCHAR(FK) | รหัสโปรโมชัน |
| ItemId | VARCHAR(FK) | รหัสสินค้า |

---

## 5. Business Rules

### BR-PMO-001: Discount Calculation
```
IF DiscountType = "Percent" THEN
  Discount = Price × (DiscountValue / 100)
  FinalPrice = Price - Discount
ELSE IF DiscountType = "Amount" THEN
  Discount = DiscountValue
  FinalPrice = Price - Discount
```

### BR-PMO-002: Buy X Get Y
```
IF Quantity >= BuyQty THEN
  FreeItems = FLOOR(Quantity / BuyQty) × GetQty
  ChargedItems = Quantity - FreeItems
  TotalPrice = ChargedItems × Price
```

### BR-PMO-003: Promotion Validity
```
IF TODAY NOT BETWEEN StartDate AND EndDate THEN
  Promotion NOT applicable
```

### BR-PMO-004: Member Only
```
IF MemberOnly = TRUE AND Customer.IsMember = FALSE THEN
  Promotion NOT applicable
```

### BR-PMO-005: Promotion Stacking
```
IF multiple promotions apply THEN
  Use ONLY the promotion with HIGHEST discount
  (หรือตาม Business Rule ที่กำหนด)
```

---

## 6. Promotion List Grid

| Column | ความกว้าง | คำอธิบาย |
|--------|-----------|----------|
| No | 44 | ลำดับ |
| Code | 140 | รหัสโปรโมชัน |
| Name | 928 | ชื่อโปรโมชัน |
| Start | 186 | วันที่เริ่ม |
| Used | 116 | จำนวนที่ใช้ |
| Status | 130 | สถานะ |
| IsOn | 52 | เปิด/ปิด |
| Menu | 36 | เมนู |
