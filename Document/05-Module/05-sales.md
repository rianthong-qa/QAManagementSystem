# ระบบเอกสารขาย

แหล่ง metadata: `Promaxxs.Modules.Transaction.dll`

## เมนูที่ลงทะเบียน

| ลำดับ | รหัสเมนู | ชื่อแสดงผล | Voucher hint |
|---:|---|---|---|
| 1 | `Transaction` | เอกสารขาย | — |
| 2 | `Transaction_Order` | Order | `O:NN` |
| 3 | `Transaction_TransferBranch` | โอนสินค้าระหว่างสาขา | `O:TB` |
| 4 | `Transaction_TransferWarehouse` | โอนสินค้าระหว่างคลัง | `O:TW` |
| 5 | `Transaction_Deposit` | รับมัดจำ | `O:DP` |
| 6 | `Transaction_CreditDebitNote` | เอกสารลดหนี้/เพิ่มหนี้ | `O:CN` |
| 7 | `Transaction_Bill` | วางบิล | `O:BN` |
| 8 | `Transaction_ShiftMaintenance` | ปิดกะ | — |
| 9 | `Transaction_SaleWithholdingTax` | ภาษีถูกหัก ณ ที่จ่าย | `O` |
| 10 | `Transaction_Cheque` | เช็ครับ | — |
| 11 | `Transaction_ClearShiftWorking` | เคลียร์กะที่ค้างทำงาน | — |
| 12 | `Transaction_Workflow` | Workflow | — |
| 13 | `Transaction_ShiftVerify` | ตรวจสอบกะ | — |
| 14 | `Transaction_EditDocument` | การจัดการเอกสาร | — |
| 15 | `Transaction_EBilling` | eBilling | — |
| 16 | `Transaction_ETax` | ใบกำกับภาษีอิเล็กทรอนิกส์ | — |

> `Order` ไม่มีข้อความชื่อภาษาไทยติดอยู่ใน user-string heap จึงคงชื่ออังกฤษตามรหัส
> เมนู ส่วนชื่อโอน/ลดหนี้/วางบิลใช้ข้อความร่วมที่พบในชุดเมนู transaction

## ความสามารถย่อยที่พบในหน้าจอ Order

- Save/Delete, ค้นบิล, serial/lot/spec, พิมพ์บิล/ฉลากยา
- ปรับ VAT, withholding tax, มัดจำ, เงื่อนไข/ส่วนลดชำระเงิน
- เงินสด, บัตรเครดิต, cash card, โอนเงิน, foreign cash และ ePayment
- ค้น/สร้างลูกค้าแบบเร็ว, ค้นสินค้า, alternate code
- movement/cost/ราคาขายล่าสุด, import items และเอกสารที่เกี่ยวข้อง
- OCR scan (พบ provider Gemini/OpenAI ใน metadata)

## Test Focus เฟส 1

- ขายเงินสด/เครดิต, หลายหน่วย, serial/lot/spec และ stock validation
- ส่วนลดระดับรายการ/ท้ายบิล, VAT, rounding และยอดชำระ
- วิธีชำระเงินแต่ละแบบที่ config พร้อม; integration ไม่พร้อมให้ Blocked
- มัดจำ/ลดหนี้/เพิ่มหนี้/วางบิล/เช็ครับ/ภาษีถูกหัก
- ปิดกะ–ตรวจสอบกะ–เคลียร์กะ และยอดเงินตาม cashier
- Workflow/การจัดการเอกสาร/permission
- eBilling/e-Tax/OCR โดยไม่บันทึก credential ลง test evidence

