# Requirements — โมดูล Dashboard

> อ้างอิง: `System-Analysis.md` section 3.4
> เวอร์ชัน: ProMaxx 21.0.0-beta.1

---

## 1. ขอบเขตโมดูล (Scope)

โมดูล Dashboard รับผิดชอบการแสดงภาพรวมและสถิติสำคัญของระบบ POS ตามที่ระบุใน System-Analysis.md section 3.4

---

## 2. ข้อกำหนดฟังก์ชัน (Functional Requirements)

#### FR-DB-001: แสดงยอดขายวันนี้
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-001 |
| **ชื่อ** | แสดงยอดขายวันนี้ |
| **Description** | ระบบต้องแสดงยอดขายรวม, จำนวนรายการ, กำไรเบื้องต้นของวันนี้ |
| **Input** | None (auto-load) |
| **Output** | TotalSales, TransactionCount, GrossProfit |
| **Priority** | High |

#### FR-DB-002: แสดงยอดขายรายวัน/สัปดาห์/เดือน
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-002 |
| **ชื่อ** | แสดงยอดขายรายวัน/สัปดาห์/เดือน |
| **Description** | ระบบต้องรองรับการเปลี่ยนมุมมองยอดขาย |
| **Input** | View mode (Daily/Weekly/Monthly) |
| **Output** | Sales data ตามช่วงเวลา |
| **Priority** | High |

#### FR-DB-003: เปรียบเทียบยอดขาย
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-003 |
| **ชื่อ** | เปรียบเทียบยอดขาย |
| **Description** | ระบบต้องแสดง % การเปลี่ยนแปลงยอดขาย |
| **Input** | Current vs Previous period |
| **Output** | % Change ( tăng/giảm) |
| **Priority** | Medium |

#### FR-DB-004: แสดงสินค้าใกล้หมด
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-004 |
| **ชื่อ** | แสดงสินค้าใกล้หมด |
| **Description** | ระบบต้องแสดงสินค้าที่ stock ต่ำกว่า Min |
| **Input** | None |
| **Output** | List of items below Min stock |
| **Priority** | High |

#### FR-DB-005: แสดงสินค้าหมดอายุ
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-005 |
| **ชื่อ** | แสดงสินค้าหมดอายุ |
| **Description** | ระบบต้องแสดงสินค้าที่ Lot หมดอายุ |
| **Input** | None |
| **Output** | List of expired items |
| **Priority** | Medium |

#### FR-DB-006: แสดงสินค้าขายดี Top 10
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-006 |
| **ชื่อ** | แสดงสินค้าขายดี Top 10 |
| **Description** | ระบบต้องแสดง 10 อันดับสินค้าขายดี |
| **Input** | Date range |
| **Output** | Top 10 products |
| **Priority** | Medium |

#### FR-DB-007: อัพเดทข้อมูลแบบ Real-time
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | FR-DB-007 |
| **ชื่อ** | อัพเดทข้อมูลแบบ Real-time |
| **Description** | Dashboard ต้องอัพเดทข้อมูลอัตโนมัติ |
| **Input** | None |
| **Output** | Updated data |
| **Priority** | Medium |

---

## 3. Non-Functional Requirements

### NFR-DB-001: Dashboard Load Time
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-DB-001 |
| **ชื่อ** | เวลาโหลด Dashboard |
| **Description** | Dashboard ต้องโหลดเสร็จภายใน 5 วินาที |
| **Measurement** | Load time < 5s |

### NFR-DB-002: Real-time Update
| รายการ | รายละเอียด |
|--------|------------|
| **ID** | NFR-DB-002 |
| **ชื่อ** | การอัพเดทแบบ Real-time |
| **Description** | Dashboard ต้องอัพเดทข้อมูลทุก 30 วินาที |
| **Measurement** | Update interval = 30s |

---

## 4. Data Requirements

### DR-DB-001: Dashboard Summary
| Field | Type | Description |
|-------|------|-------------|
| TotalSales | DECIMAL | ยอดขายรวม |
| TransactionCount | INT | จำนวนรายการ |
| GrossProfit | DECIMAL | กำไรเบื้องต้น |
| VAT | DECIMAL | ภาษีรวม |
| TopProducts | LIST | สินค้าขายดี |
| LowStockItems | LIST | สินค้าใกล้หมด |
| ExpiredItems | LIST | สินค้าหมดอายุ |

---

## 5. Business Rules

### BR-DB-001: Low Stock Alert
```
LowStockItems = SELECT * FROM Stock
WHERE Quantity < MinQuantity
ORDER BY (MinQuantity - Quantity) DESC
```

### BR-DB-002: Expired Items Alert
```
ExpiredItems = SELECT * FROM ItemLot
WHERE ExpireDate <= TODAY
```

### BR-DB-003: Top Products
```
TopProducts = SELECT TOP 10 ItemId, SUM(Quantity) as TotalQty
FROM TransactionDetail
WHERE TranDate BETWEEN StartDate AND EndDate
GROUP BY ItemId
ORDER BY TotalQty DESC
```

---

## 6. Dashboard Layout

### 6.1 Recommended Sections

| Section | ตำแหน่ง | ข้อมูล |
|---------|---------|--------|
| Sales Summary | บนซ้าย | ยอดขายวันนี้, เปรียบเทียบ |
| Transaction Count | บนขวา | จำนวนรายการ |
| Top Products | กลางซ้าย | 10 อันดับสินค้าขายดี |
| Low Stock Alert | กลางขวา | สินค้าใกล้หมด |
| Gross Profit | ล่างซ้าย | กำไรเบื้องต้น |
| Expired Items | ล่างขวา | สินค้าหมดอายุ |

### 6.2 Chart Types

| Chart | ข้อมูล |
|-------|--------|
| Bar Chart | ยอดขายรายวัน |
| Pie Chart | สัดส่วนสินค้าขายดี |
| Line Chart | แนวโน้มยอดขาย |
