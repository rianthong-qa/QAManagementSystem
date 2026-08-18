# วิเคราะห์ระบบ ProMaxx 21.0.0-beta.1

> วันที่วิเคราะห์: 17 สิงหาคม 2569
> ผู้วิเคราะห์: AI Assistant

---

## 1. ภาพรวมระบบ (System Overview)

ProMaxx 21.0.0-beta.1 เป็นระบบ **Point of Sale (POS) สำหรับธุรกิจค้าปลีกและค้าส่ง** ที่พัฒนาด้วย .NET 10 (WPF Desktop Application) ออกแบบมาเพื่อใช้งานบน Windows โดยเฉพาะ ระบบมีโมดูลแบ่งแยกชัดเจน รองรับการเชื่อมต่อกับอุปกรณ์ POS หลายประเภท

### ข้อมูลทั่วไป

| รายการ | ค่า |
|--------|------|
| ชื่อระบบ | ProMaxx |
| เวอร์ชัน | 21.0.0-beta.1 |
| Framework | .NET 10.0 (win-x64) |
| UI Framework | WPF (Windows Presentation Foundation) |
| Runtime | Microsoft.NETCore.App + Microsoft.WindowsDesktop.App v10.0.8 |
| ผู้พัฒนา | SeniorSoft |
| ฐานข้อมูลหลัก | Firebird SQL |

---

## 2. สถาปัตยกรรมระบบ (System Architecture)

### 2.1 Modular Architecture

ระบบ采用 Modular Architecture แยกเป็น assembly ต่างๆ อย่างชัดเจน:

```
Promaxxs.App (Main Application)
├── Promaxxs.Data           ─── Data Access Layer
├── Promaxxs.Domain         ─── Domain Models / Business Entities
├── Promaxxs.Services       ─── Business Logic Services
├── Promaxxs.Shared         ─── Shared Utilities / Helpers
├── Promaxxs.UI             ─── UI Components / Views
├── Promaxxs.Devices        ─── Hardware Integration
├── Promaxxs.Modules.Dashboard    ─── Dashboard Module
├── Promaxxs.Modules.Demo         ─── Demo Module
├── Promaxxs.Modules.Inventory    ─── Inventory Management Module
├── Promaxxs.Modules.Person       ─── Personnel/Customer Module
├── Promaxxs.Modules.Reporting    ─── Reporting Module
├── Promaxxs.Modules.Settings     ─── Settings Module
├── Promaxxs.Modules.Transaction  ─── Transaction Module
├── Promaxxs.Modules.Utility      ─── Utility Module
└── Promaxxs.Promotion.Engine     ─── Promotion Engine
```

### 2.2 แผนภาพสถาปัตยกรรม

```
┌─────────────────────────────────────────────────────┐
│                   UI Layer (WPF)                    │
│  Promaxxs.UI  │  MaterialDesignThemes              │
│  CommunityToolkit.Mvvm                             │
├─────────────────────────────────────────────────────┤
│               Module Layer                          │
│  Dashboard │ Inventory │ Transaction │ Person       │
│  Reporting │ Settings  │ Utility     │ Promotion    │
├─────────────────────────────────────────────────────┤
│              Service Layer                          │
│  Promaxxs.Services  │  Promaxxs.Promotion.Engine   │
├─────────────────────────────────────────────────────┤
│              Domain Layer                           │
│  Promaxxs.Domain                                    │
├─────────────────────────────────────────────────────┤
│              Data Layer                             │
│  Promaxxs.Data                                      │
├─────────────────────────────────────────────────────┤
│           External Integrations                     │
│  Promaxxs.Devices │ SeniorSoft.Component            │
│  SeniorSoft.Core12│ WebView2 │ SmartCard (PCSC)     │
├─────────────────────────────────────────────────────┤
│              Database                               │
│  Firebird SQL  │ SQL Server  │ PostgreSQL           │
└─────────────────────────────────────────────────────┘
```

---

## 3. โมดูลหลักของระบบ (Core Modules)

### 3.1 Transaction Module (`Promaxxs.Modules.Transaction`)
ระบบจัดการธุรกรรมการขาย มีหลายประเภทธุรกรรม:

| Section | คำอธิบาย | Voucher ID |
|---------|----------|------------|
| `TranIn` | รับสินค้า (Purchase Order Receive) | 7 |
| `TranOut` | ขายสินค้า (Sales Transaction) | 45 |
| `TranCnIn` | รับใบลดหนี้ (Credit Note Receive) | 18 |
| `TranCnOut` | ออกใบลดหนี้ (Credit Note Issue) | 52 |
| `TranTbIn` | รับสินค้าโอนเข้า (Transfer In) | 64 |
| `TranTbOut` | ส่งสินค้าโอนออก (Transfer Out) | 53 |
| `TranBnIn` | รับสินค้าเบิก (Branch Receive) | 71 |
| `TranBnOut` | ส่งสินค้าเบิก (Branch Send) | 26 |
| `TranDpIn` | รับเงินมัดจำ (Deposit Receive) | 69 |
| `TranDpOut` | จ่ายเงินมัดจำ (Deposit Return) | 29 |
| `TranPos` | ขายหน้าร้าน (POS Sale) | 35 |
| `TranTwIn` | รับเงินเชื่อ (Transfer Weight In) | 66 |
| `TranTwOut` | จ่ายเงินเชื่อ (Transfer Weight Out) | 28 |

### 3.2 Inventory Module (`Promaxxs.Modules.Inventory`)
ระบบจัดการคลังสินค้า:

- **Items** - จัดการข้อมูลสินค้า (Master Item)
- **ItemTemplate** - จัดการเทมเพลตสินค้า (สินค้าชุด/Bundle)
- **StockCount** - ตรวจนับสินค้า (Physical Count)
- **SerialCount** - ตรวจนับตาม Serial Number
- **LotCount** - ตรวจนับตาม Lot Number
- **StockAdjustment** - ปรับปรุงยอดสินค้า
- **PriceChange** - เปลี่ยนแปลงราคาสินค้า
- **ItemStockMinMax** - ตั้งค่า Max/Min สินค้า
- **TransLocation** - โอนย้ายสินค้าระหว่างคลัง
- **DeviceDoc** - เอกสารอุปกรณ์ (มี Serial/Lot/ExpireDate tracking)
- **DocInInventory** - เอกสารรับเข้าคลัง
- **ExportCustomer** - ส่งออกข้อมูลลูกค้า
- **IStockQuantityBalance** - ดูยอดคงเหลือสินค้า
- **IStockSerialMovement** - ดูการเคลื่อนไหว Serial
- **IStockTransactionMaster/Detail** - ดูธุรกรรมสินค้า
- **CategoryConflict** - ตรวจสอบความขัดแย้งของ Category
- **SetItemControlledDrug** - ตั้งค่าสินค้ายาควบคุม

### 3.3 Person Module (`Promaxxs.Modules.Person`)
จัดการข้อมูลบุคคล:

- **ลูกค้า** (Customer) พร้อมระบบ Category และ Account Chart
- **พนักงาน** (Staff/Employee)
- **ซัพพลายเออร์** (Supplier)

### 3.4 Dashboard Module (`Promaxxs.Modules.Dashboard`)
แสดงภาพรวมและสถิติ的重要ของระบบ

### 3.5 Reporting Module (`Promaxxs.Modules.Reporting`)
ระบบรายงาน รองรับ:

- **รายงานยอดขาย** - สูงสุด/ต่ำสุด ตามมูลค่าและจำนวน
- **รายงานกำไร** - กำไรเบื้องต้นตามต้นทุนหลัก
- **สรุปยอดขายประจำวัน** (Daily Summary) - รองรับทั้งรูปแบบไฟล์และข้อความ
- **รายงานอื่นๆ** ผ่าน FastReport Engine

### 3.6 Settings Module (`Promaxxs.Modules.Settings`)
ตั้งค่าต่างๆ ของระบบ

### 3.7 Promotion Engine (`Promaxxs.Promotion.Engine`)
ระบบส่งเสริมการขาย/โปรโมชัน

### 3.8 Utility Module (`Promaxxs.Modules.Utility`)
เครื่องมือเสริมต่างๆ

### 3.9 Demo Module (`Promaxxs.Modules.Demo`)
ตัวอย่าง/สาธิตการใช้งาน

---

## 4. ระบบฐานข้อมูล (Database System)

### 4.1 ฐานข้อมูลที่รองรับ

ระบบรองรับฐานข้อมูล **3 ประเภท**:

| ฐานข้อมูล | โหมด Local | โหมด LAN | สถานะ |
|-----------|------------|----------|-------|
| **Firebird SQL** | `FBMAXX.FDB` | `FBMAXX2.FDB` (port 3053) | **หลัก** |
| **SQL Server** | `.\SQLEXPRESS` / `PROMAXXS` | LAN: port 1433 | รองรับ |
| **PostgreSQL** | localhost / `promaxxs` (port 5432) | LAN: port 5432 | รองรับ |

### 4.2 การเชื่อมต่อ

- **Firebird SQL** - ฐานข้อมูลหลัก (default) ใช้ผู้ใช้ `seniorsoft` (local) หรือ `SYSDBA` (LAN)
- **SQL Server** - รองรับ SQL Express สำหรับ Local
- **PostgreSQL** - รองรับทั้ง Local และ LAN

### 4.3 การเข้ารหัสรหัสผ่าน
- ใช้ AES encryption สำหรับรหัสผ่านฐานข้อมูลใน `system.ini`

---

## 5. ระบบฮาร์ดแวร์ (Hardware Integration)

### 5.1 อุปกรณ์ POS ที่รองรับ

| อุปกรณ์ | รายละเอียด |
|---------|------------|
| **TMU (Receipt Printer)** | Epson TMU220A, รองรับหลายรุ่น, เชื่อมต่อผ่าน COM Port |
| **Cash Drawer** | รองรับ PIN5, เชื่อมต่อผ่าน USB Port |
| **Customer Display** | Epson Cus Disp, แสดง Welcome message |
| **Barcode Printer** | รองรับหลายรูปแบบ 128 Barcode, หลายขนาด (3.4x2.0, 2.2x1.2, 5x5) |
| **EDC (Electronic Data Capture)** | Hypercom |
| **Scale/Weighing** | รองรับระบบชั่งน้ำหนัก (weight/price format) |
| **Webcam** | รองรับ WebSocket COM Port |
| **Smart Card** | PCSC/Iso7816 (ผ่าน PCSC.dll) |

### 5.2 การตั้งค่า Terminal

- Terminal ID: `P01`
- POS ID: `1110000AA`
- รองรับหลายภาษา: ไทย (TH), อังกฤษ (EN), จีน, ญี่ปุ่น, เกาหลี, และอื่นๆ
- รองรับ Dark Mode

---

## 6. คุณลักษณะเด่น (Key Features)

### 6.1 Multi-Language Support
รองรับภาษาหลายภาษาผ่าน resource files:
- ไทย (th), อังกฤษ (en), จีน (zh-Hans, zh-Hant)
- ญี่ปุ่น (ja), เกาหลี (ko), เยอรมัน (de)
- สเปน (es), ฝรั่งเศส (fr), อิตาลี (it)
- โปรตุเกส (pt-BR), รัสเซีย (ru), ตุรกิ (tr)
- ฟินแลนด์ (fi), ดัตช์ (nl), โปแลนด์ (pl)

### 6.2 MVVM Pattern
- ใช้ **CommunityToolkit.Mvvm** สำหรับ MVVM architecture
- รองรับ `INotifyPropertyChanging` สำหรับ performance optimization

### 6.3 UI Framework
- **MaterialDesignThemes** v5.3.0 - UI Design System
- รองรับ Dark Mode (`DARKMODE=0/1`)
- รองรับการจัดวาง Grid Layout แบบกำหนดเอง (`GridLayouts.json`)

### 6.4 Barcode System
- รองรับ Barcode Type 128
- หลายรูปแบบ label: 3.4x2.0 (3 ดวง/แถว), 2.2x1.2 (4 ดวง/แถว), 5x5 (2 ดวง/แถว)
- รองรับ QR Code สำหรับ Due Date
- รองรับการพิมพ์หลาย Font (AngsanaUPC)

### 6.5 Email Report System
- รองรับการส่งรายงานทางอีเมล
- รองรับ SMTP (Gmail provider)
- รองรับ CC email
- รายงานที่รองรับ: ยอดขาย, กำไร, สรุปยอดรายวัน

### 6.6 Report Engine
- **FastReport** - Report generation engine
- **FastReport.OpenSource.Export.PdfSimple** - Export to PDF
- ไฟล์เทมเพลต: `Templates/Receipt.frx`

### 6.7 Data Export/Import
- **ClosedXML** - Excel file operations
- **ExcelDataReader** - Read Excel files
- **DocumentFormat.OpenXml** - Office document format

### 6.8 Smart Card / Smartcard Reader
- **PCSC** - PC/SC Smart Card framework
- **PCSC.Iso7816** - ISO 7816 standard

### 6.9 WebView2 Integration
- รองรับการฝัง web content ผ่าน Microsoft WebView2
- ใช้สำหรับ features ที่ต้อง rendering HTML

### 6.10 Chart & Visualization
- **LiveChartsCore** + **SkiaSharp** - สำหรับกราฟและ chart
- **SkiaSharp** - Graphics rendering

---

## 7. การตั้งค่า Transaction (Transaction Configuration)

ทุก Transaction Type มีการตั้งค่า:

| Setting | คำอธิบาย |
|---------|----------|
| `tab_search_item` | โหมดค้นหาสินค้า |
| `search_item_group` | กลุ่มสินค้าสำหรับค้นหา |
| `search_sys_item_id` | System Item ID สำหรับค้นหา |
| `recently_sys_voucher_id` | Voucher ID ล่าสุด |
| `tax_rate` | อัตราภาษี (0-7%) |
| `default_quantity` | จำนวนเริ่มต้น |
| `warehouse` | คลังเริ่มต้น |
| `focus_next_column` | Column ถัดไปหลังกรอก (QUANTITY, ITEMNAME, SCANCODE) |
| `service_charge_rate` | อัตราค่าบริการ |

---

## 8. การเชื่อมต่อภายนอก (External Integrations)

### 8.1 IStock API
- **Production**: `https://blueid.seniorsoft.com/blueid/connect/token`
- **Lab**: `https://lab.seniorsoft.com/blueid/connect/token`
- **API Base**: `https://lab.seniorsoft.com/promaxxapilab`
- **Client ID**: `iView2`
- รองรับ OAuth2 token-based authentication

### 8.2 SeniorSoft Components
- `SeniorSoft.Component` v1.0.0.2
- `SeniorSoft.Core12` v12.0.2.0
- เป็น proprietary component library จาก SeniorSoft

---

## 9. การตั้งค่า UI (UI Configuration)

```ini
[UI]
LANGUAGE=TH          ; ภาษาหลัก
DARKMODE=0           ; 0=Light, 1=Dark
```

### Grid Layouts (`GridLayouts.json`)
รองรับการจัด layout ตารางแบบละเอียด:
- ความกว้างคอลัมน์ (Width)
- ลำดับการแสดงผล (DisplayIndex)
- การแสดง/ซ่อนคอลัมน์ (IsVisible)

---

## 10. ไฟล์ Configuration ทั้งหมด

| ไฟล์ | คำอธิบาย |
|------|----------|
| `config/system.ini` | ตั้งค่าหลักของระบบ (Terminal, Database, Transaction, UI) |
| `config/barcode.ini` | ตั้งค่าการพิมพ์บาร์โค้ด |
| `config/position.ini` | ตั้งค่าการแสดงผล/ซ่อน Fields |
| `config/GridLayouts.json` | ตั้งค่า Layout ของ DataGrid ทั้งหมด |
| `config/ui_amail.ini` | ตั้งค่าการส่งอีเมลรายงาน |
| `appsettings.json` | ตั้งค่า API integration |

---

## 11. Runtime Dependencies

### Third-Party Libraries

| Library | Version | วัตถุประสงค์ |
|---------|---------|-------------|
| CommunityToolkit.Mvvm | 8.4.0 | MVVM Framework |
| MaterialDesignThemes | 5.3.0 | UI Design System |
| Microsoft.Extensions.Hosting | 10.0.5 | Application Hosting |
| Newtonsoft.Json | 13.0.3 | JSON Serialization |
| Microsoft.Web.WebView2 | 1.0.3124.44 | Embedded Web Browser |
| FastReport | - | Report Engine |
| LiveChartsCore | - | Charts & Visualization |
| SkiaSharp | - | Graphics Rendering |
| ClosedXML | - | Excel Operations |
| ExcelDataReader | - | Excel Reading |
| DocumentFormat.OpenXml | - | Office Documents |
| HarfBuzzSharp | - | Text Shaping |
| SixLabors.Fonts | - | Font Processing |
| PCSC / PCSC.Iso7816 | - | Smart Card Reader |
| INIFileParser | - | INI File Handling |
| RBush | - | Spatial Indexing |

---

## 12. สรุป

ProMaxx 21.0.0-beta.1 เป็นระบบ POS ที่ครบวงจร มีจุดเด่นคือ:

1. **Modular Architecture** - แยกโมดูลชัดเจน 易于维护
2. **Multi-Database Support** - รองรับ Firebird, SQL Server, PostgreSQL
3. **Multi-Language** - รองรับ 15+ ภาษา
4. **Hardware Integration** - เชื่อมต่ออุปกรณ์ POS ได้หลากหลาย
5. **Comprehensive Inventory** - ระบบคลังสินค้าครบถ้วน (Serial, Lot, Expiry tracking)
6. **Transaction Variety** - รองรับธุรกรรมหลายรูปแบบ (ซื้อ, ขาย, โอน, เบิก, มัดจำ)
7. **Reporting** - ระบบรายงานผ่าน FastReport
8. **Email Automation** - ส่งรายงานทางอีเมลอัตโนมัติ
9. **Modern UI** - Material Design + Dark Mode support
10. **Promotion Engine** - ระบบส่งเสริมการขายในตัว
