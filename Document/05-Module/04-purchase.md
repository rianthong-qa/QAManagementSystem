# ระบบเอกสารซื้อ

แหล่ง metadata: `Promaxxs.Modules.Transaction.dll`

## เมนูที่ลงทะเบียน

| ลำดับ | รหัสเมนู | ชื่อแสดงผล | Voucher hint |
|---:|---|---|---|
| 1 | `Transaction_Purchase` | เอกสารซื้อ | — |
| 2 | `Transaction_PurchaseOrder` | Purchase Order | `I:NN` |
| 3 | `Transaction_PurchaseTransferBranch` | โอนสินค้าระหว่างสาขา | `I:TB` |
| 4 | `Transaction_PurchaseTransferWarehouse` | โอนสินค้าระหว่างคลัง | `I:TW` |
| 5 | `Transaction_PurchaseDeposit` | จ่ายมัดจำ | `I:DP` |
| 6 | `Transaction_PurchaseCreditDebitNote` | เอกสารลดหนี้/เพิ่มหนี้ | `I:CN` |
| 7 | `Transaction_PurchaseBill` | วางบิล | `I:BN` |
| 8 | `Transaction_PurchaseInventoryCost` | สินค้าคงเหลือและทุน | — |
| 9 | `Transaction_BuyWithholdingTax` | หัก ณ ที่จ่าย | `I` |
| 10 | `Transaction_PurchaseCheque` | เช็คจ่าย | — |

> `Purchase Order` ไม่มีข้อความชื่อภาษาไทยติดอยู่ใน user-string heap จึงคงชื่อ
> ภาษาอังกฤษตามรหัสเมนู ไม่เดาคำแปลเพิ่ม

## Test Focus เฟส 1

- สร้าง/ค้นหา/แก้ไข/ยกเลิก/พิมพ์เอกสารซื้อ
- ผู้จำหน่าย, สินค้า, หน่วย, ราคา, ส่วนลด, VAT และยอดสุทธิ
- ผลต่อ stock และต้นทุนก่อน–หลังบันทึก/ยกเลิก
- PO → รับซื้อ/เอกสารต่อเนื่อง
- โอนสาขา/คลังและการอ้างอิงเอกสาร
- มัดจำ, ลดหนี้/เพิ่มหนี้, วางบิล, เช็คจ่าย, ภาษีหัก ณ ที่จ่าย
- permission, document running และ validation เอกสารซ้ำ/งวดปิด

