# ระบบตั้งค่า

แหล่ง metadata: `Promaxxs.Modules.Settings.dll`

## เมนูที่ลงทะเบียน

| ลำดับ | รหัสเมนู | ชื่อแสดงผล |
|---:|---|---|
| 1 | `Settings` | ตั้งค่า |
| 2 | `Settings_General` | ตั้งค่าทั่วไป |
| 3 | `Settings_PosGeneral` | ตั้งค่าทั่วไป POS |
| 4 | `Settings_Company` | ข้อมูลองค์กร |
| 5 | `Settings_Payment_Methods` | ข้อมูลพื้นฐานการชำระเงิน |
| 6 | `Settings_Person` | ข้อมูลพื้นฐานบุคคล |
| 7 | `Settings_Inventory` | ข้อมูลพื้นฐานคลังสินค้า |
| 8 | `Settings_Purchase` | กำหนดเอกสารซื้อ |
| 9 | `Settings_Sales` | กำหนดเอกสารขาย |
| 10 | `Settings_Permission` | กำหนดสิทธิ์การใช้งาน |
| 11 | `Settings_Employee` | ทะเบียนพนักงาน |
| 12 | `Settings_Extension` | ระบบเสริม |
| 13 | `Settings_Shift` | กำหนดระบบกะ |

## หน้าจอย่อยที่พบใน resource

- ทั่วไป/POS: running document, e-Tax, negative stock, rounding,
  discount priority/calculation, shift central/branch, voucher/form preview
- บุคคล: ประเทศ/จังหวัด/อำเภอ/ตำบล/รหัสไปรษณีย์, คำนำหน้า, ศาสนา,
  กลุ่ม/ประเภทลูกค้า, กลุ่มผู้จำหน่าย, member type, relation, ID format
- คลัง: หน่วย, category, item condition
- ซื้อ/ขาย: system document, document, voucher form, credit/debit note,
  branch document/voucher
- การชำระเงิน: ธนาคาร, สมุดบัญชี, cash card, credit/discount card,
  exchange rate, EDC
- สิทธิ์: branch, document, system, column และ permission role
- ระบบเสริม: report delivery, MyProduct/MyMaxx และ setup ที่เกี่ยวข้อง

## Test Focus เฟส 1

- ค่า default โหลด/บันทึก/ยกเลิก และยังอยู่หลัง login ใหม่
- ผลของค่าตั้งต่อ transaction จริง
- master data ซ้ำ/ถูกอ้างอิง/ลบไม่ได้
- role × branch × document × system × column permission
- พนักงานและสิทธิ์สาขา
- secret/API/credential ต้อง mask และไม่ปรากฏใน log/evidence
- เปลี่ยน setting สำคัญต้องมี backup/ค่ากลับคืนหลังทดสอบ

