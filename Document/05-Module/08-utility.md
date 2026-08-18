# ระบบเครื่องมือ (Utility)

แหล่ง metadata: `Promaxxs.Modules.Utility.dll`

## เมนูที่ลงทะเบียน

| ลำดับ | รหัสเมนู | ชื่อแสดงผล |
|---:|---|---|
| 1 | `Utility` | Utility |
| 2 | `Utility_PrintBarcode` | พิมพ์บาร์โค้ด |
| 3 | `Utility_SaleTeam` | Sale Team |

## หน้าจอย่อยที่พบใน resource

- พิมพ์บาร์โค้ด: สินค้า, serial, เอกสารซื้อ, เอกสารขาย, sub-unit,
  extra barcode และการตั้งค่าฉลาก
- ฉลาก/รายงานบุคคล: customer label/report และ supplier label/report
- Sale Team: master, team target, employee target, branch target
- หน้าจอ ePayment/อุปกรณ์บางส่วนเป็น service view ภายใน ไม่ใช่เมนู Utility

## Test Focus เฟส 1

- เลือกข้อมูล/จำนวน/เครื่องพิมพ์และ preview ก่อนพิมพ์
- รูปแบบ barcode, ราคา, VAT, ข้อความฉลาก, rotation/font/darkness/gap
- print จากสินค้า/serial/เอกสารซื้อ/เอกสารขาย
- customer/supplier label และ selected columns ใน report
- CRUD Sale Team และเป้ารายปีระดับทีม/พนักงาน/สาขา
- กรณีไม่มี printer/driver ให้แสดง error ที่ใช้งานต่อได้

