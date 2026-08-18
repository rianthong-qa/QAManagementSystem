# ระบบคลังสินค้า

แหล่ง metadata: `Promaxxs.Modules.Inventory.dll`

## เมนูที่ลงทะเบียน

| ลำดับ | รหัสเมนู | ชื่อแสดงผล |
|---:|---|---|
| 1 | `Inventory` | คลังสินค้า |
| 2 | `Inventory_ItemGroup` | กลุ่มสินค้า |
| 3 | `Inventory_Items` | สินค้า |
| 4 | `Inventory_PriceAdjust` | ปรับราคาขาย |
| 5 | `Inventory_MinMax` | ปริมาณสูงสุด-ต่ำสุด |
| 6 | `Inventory_Promotion` | โปรโมชั่น |
| 7 | `Inventory_ItemTemplate` | Template สินค้า |
| 8 | `Inventory_Suite` | Suite |
| 9 | `Inventory_RawMaterialConsumption` | ตัดวัตถุดิบ |
| 10 | `Inventory_TransLocation` | Transfer Location |
| 11 | `Inventory_PharmacySystem` | ระบบร้านขายยา |
| 12 | `Inventory_iStock` | iStock |
| 13 | `Inventory_StockCount` | นับสต๊อกสินค้า |
| 14 | `Inventory_StockAdjustment` | Stock Adjustment |

## หน้าจอย่อยที่พบใน resource

- สินค้า: barcode/vendor, category, component, condition price, grade/spec/serial,
  movement, location, person, price level/branch, สินค้าทดแทน, ส่วนลดพิเศษ,
  ช่วงเวลาขาย, หน่วย และข้อมูลเพิ่มเติม
- นับสต๊อก: เลือกไฟล์เครื่องตรวจนับ, เลือกกลุ่มสินค้า, preview/report
- ปรับสต๊อก: barcode scanner check, เลือกเอกสาร, ปรับยอด
- โปรโมชั่น: list, gallery, wizard, form, detail, mapping, scope/person group
- ปรับราคา: import, search, price change
- iStock: quantity balance, serial movement, transaction detail
- รับสินค้าเข้า, เอกสารจากเครื่องตรวจนับ, export master/customer/supplier

## Test Focus เฟส 1

- CRUD กลุ่ม/สินค้า/หน่วย/barcode/spec/serial/lot
- ปรับราคาและตรวจราคาที่ transaction
- Min/Max และ negative-stock validation
- โปรโมชั่น: เงื่อนไขเวลา, สินค้า, กลุ่มลูกค้า, price level, limit ต่อคน
- นับและปรับสต๊อก: manual + CSV/TXT, จำนวนขาด/เกิน, report
- โอน location/warehouse/branch และตรวจยอดสองฝั่ง
- ตัดวัตถุดิบ/Suite และผลต่อ component stock
- Pharmacy/iStock เมื่อมี credential; ไม่มีให้บันทึกเป็น Blocked พร้อมเหตุผล

