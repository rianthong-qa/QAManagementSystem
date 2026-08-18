# ระบบรายงาน

แหล่ง metadata: `Promaxxs.Modules.Reporting.dll`

## เมนูที่ลงทะเบียน

| ลำดับ | รหัสเมนู | ชื่อแสดงผล |
|---:|---|---|
| 1 | `Reporting` | รายงาน |
| 2 | `Reporting_Receipt` | ใบเสร็จ |

## หน้าจอ/ความสามารถที่พบ

- Receipt view
- PDF preview ผ่าน WebView2
- พิมพ์รายงาน
- Export PDF
- Template `Receipt.frx`

## Test Focus เฟส 1

- ค้นและเปิดใบเสร็จด้วยข้อมูลจริง
- ยอดก่อนภาษี/ส่วนลด/VAT/ยอดสุทธิ/รับเงิน/เงินทอนตรงกับ transaction
- ชื่อลูกค้า สาขา cashier และรายการสินค้าครบ
- Preview, print และ export PDF
- filename/path, ภาษาไทย, page break และกรณีหลายรายการ
- ผู้ไม่มีสิทธิ์ไม่สามารถเปิด/ส่งออกข้อมูลได้

