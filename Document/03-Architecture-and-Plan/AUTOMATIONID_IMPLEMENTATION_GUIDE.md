# Promaxx2 — คู่มือใส่ AutomationId สำหรับทีม Dev (WPF)

> **เอกสารนี้สำหรับ:** ทีมพัฒนา `Promaxxs.App.exe` และ `PromaxxsPos.exe` (WPF/.NET 10)
> **วัตถุประสงค์:** ให้ Dev ใส่ `AutomationProperties.AutomationId` ให้ครบทุก control ที่ Test Automation (FlaUI/UIA3) ใช้ เพื่อให้ QA ควบคุม/ตรวจสอบ UI ได้เสถียร และทนทานต่อ obfuscation/encryption ในอนาคต
> **อ้างอิง:** `SELECTOR_CONTRACT.md` (DRAFT) + `AUTOMATION_PLAN.md` §4, §6 | เวอร์ชัน 1.0 | 2026-08-24

---

## 1. AutomationId คืออะไร และทำไมต้องใส่

`AutomationProperties.AutomationId` เป็น **attached property** ของ WPF (namespace `System.Windows.Automation`) ที่ใช้ตั้ง "ชื่อเฉพาะ" ให้ control บนหน้าจอ เพื่อให้ UI Automation Framework อ่านเจอได้ตอน runtime

- QA ใช้ FlaUI/UIA3 อ่าน **UI Automation Tree** ที่ WPF ปล่อยออกมาตอน app รัน — **ไม่ได้อ่านจากไฟล์ XAML หรือ DLL**
- เมื่อใส่ AutomationId แล้ว automation จะอ้างอิง control ด้วย ID นี้โดยตรง ไม่ต้องเดาจาก label text / ตำแหน่ง / ลำดับ ซึ่ง fragile มาก
- ต่อให้ไฟล์ assembly ถูก **เข้ารหัส/obfuscate** หรือ label ไทย/Eng เปลี่ยนข้อความ **AutomationId ยังคงเดิม** → งาน automation ไม่พัง

**ผลลัพธ์ที่ต้องการ:** ปุ่ม `ชำระเงิน` ไม่ว่า text จะเปลี่ยนเป็น "Pay" หรือจะถูก encrypt ยังไง QA ยังเจอปุ่มนี้เสมอผ่าน `AutomationId="Pos_SaleBtnPay"`

---

## 2. หลักการตั้งชื่อ (Naming Convention)

ใช้รูปแบบ: **`<Context>_<ControlType><Function>`**

- `Context` = หน้าจอ/พื้นที่ เช่น `Login`, `Sale`, `Product`, `Customer` (ใส่เพื่อกัน ID ซ้ำกันข้ามหน้าจอ)
- `ControlType` = คำย่อชนิด control (ตารางด้านล่าง)
- `Function` = ความหมายการใช้งาน ชัดเจนอ่านแล้วรู้ว่าทำอะไร

### 2.1 ตารางคำย่อชนิด control

| Control | Prefix | ตัวอย่าง |
|---|---|---|
| Button | `Btn` | `LoginBtnSignIn`, `ProductBtnAdd` |
| TextBox | `Txt` | `LoginTxtEmpId`, `SearchTxtKeyword` |
| PasswordBox | `Pwd` | `LoginPwdBox` |
| ComboBox | `Cmb` | `ReportCmbPeriod` |
| CheckBox | `Chk` | `FilterChkIncludeVat` |
| RadioButton | `Rad` | `PayRadCash`, `PayRadCard` |
| DataGrid / List / ItemsControl | `Grid` / `Lst` | `ProductGrid`, `OrderLstItems` |
| TabControl / TabItem | `Tab` | `ProductTabDetail` |
| Menu / MenuItem | `Mnu` | `MainMnuSale`, `MainMnuProduct` |
| Dialog / Window / Popup | `Dlg` / `Wnd` | `ConfirmDlg`, `PayWnd` |
| Label / ข้อความแจ้งเตือน (Toast) | `Lbl` / `Toast` | `ToastText` |
| Icon / Glyph (ตกแต่ง) | — | **ไม่ต้องใส่** (ดูข้อ 2.2) |

### 2.2 หลักเกณฑ์บังคับ

1. **ค่าเป็น literal string คงที่** — ห้ามสร้างจากชื่อ class/property/method/บวกสตริงทีละครั้ง (`$"{nameof(BtnPay)}"` ใช้ได้แต่ต้องไม่ถูก rename)
2. **unique ภายใน scope ที่ automation ใช้** (window/view นั้น) — ต้องไม่ซ้ำกัน
3. อ่านแล้วรู้ความหมาย (เช่น `Pos_SaleBtnPay` ดีกว่า `Btn1` หรือ `abc123`)
4. ใช้ได้ทั้งตัวพิมพ์เล็ก/ใหญ่ ผสมกันได้ แต่**ห้ามเว้นวรรค**และ**ห้ามอักขระพิเศษ** (space, ไทย, สระ, เครื่องหมาย) — ภาษาอังกฤษ a–z, A–Z, 0–9, `_` เท่านั้น
5. Control ที่เป็น **การตกแต่งล้วน** (glyph icon, หัวมุม window, separator, border) **ไม่ต้องใส่ ID** — จะได้ไม่รก baseline ของ QA

---

## 3. วิธีใส่ AutomationId

### 3.1 ใน XAML (วิธีหลัก ใช้กับ control เกือบทั้งหมด)

```xml
<StackPanel AutomationProperties.AutomationId="LoginPanel" ...>
    <TextBox AutomationProperties.AutomationId="LoginTxtEmpId" />
    <PasswordBox AutomationProperties.AutomationId="LoginPwdBox" />
    <Button AutomationProperties.AutomationId="LoginBtnSignIn" Content="เข้าสู่ระบบ" />
</StackPanel>
```

### 3.2 ใน DataTemplate / ItemTemplate (Item ใน List/Grid)

```xml
<ListBox AutomationProperties.AutomationId="OrderLstItems">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <Button AutomationProperties.AutomationId="OrderBtnRemove"
                    Command="{Binding RemoveCommand}" Content="ลบ" />
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

> ⚠️ ถ้า item มีปุ่มแบบนี้หลายแถว ID จะซ้ำกันทุกแถว — QA ใช้ชื่อปุ่ม + row ที่ได้จาก Grid/List เป็นตัวระบุแทน (ดูกติกา D5 ในข้อ 7) ซึ่งใช้งานได้ปกติ ไม่ต้องห้าม

### 3.3 สร้าง control ใน code-behind

```csharp
using System.Windows.Automation;

var btn = new Button { Content = "ชำระเงิน" };
AutomationProperties.SetAutomationId(btn, "Pos_SaleBtnPay");
```

### 3.4 ใส่ให้ Window/Dialog และ Control อื่น

- Window/Dialog/UserControl: ใส่ `AutomationProperties.AutomationId` ที่ root element (เช่น `DlgConfirm`, `PayWnd`)
- TabItem, MenuItem, DataGrid: ใส่เช่นเดียวกับ control ทั่วไป

---

## 4. ตัวอย่างตามชนิด control ที่ใช้บ่อย

### 4.1 หน้าจอ Login (ตัวอย่างจริงจาก registry ที่ QA verify แล้ว)

```xml
<Grid AutomationProperties.AutomationId="LoginOverlay">
    <TextBox  AutomationProperties.AutomationId="TxtEmpId"   />
    <PasswordBox AutomationProperties.AutomationId="PwdBox"  />
    <Button   AutomationProperties.AutomationId="BtnSignIn" Content="เข้าสู่ระบบ" />
    <TextBlock AutomationProperties.AutomationId="ToastText" />   <!-- แสดง error/result -->
</Grid>
```

### 4.2 หน้าขาย / POS (ยังต้องเติม)

```xml
<Grid AutomationProperties.AutomationId="SaleMainGrid">
    <TextBox  AutomationProperties.AutomationId="SaleTxtBarcode"  />   <!-- ช่องสแกน -->
    <DataGrid AutomationProperties.AutomationId="SaleGridItems"   />    <!-- ตารางรายการบิล -->
    <Button   AutomationProperties.AutomationId="SaleBtnPay" Content="ชำระเงิน" />
    <Button   AutomationProperties.AutomationId="SaleBtnDiscount" Content="ส่วนลด" />
    <Button   AutomationProperties.AutomationId="SaleBtnCancelBill" Content="ยกเลิกบิล" />
</Grid>
```

### 4.3 หน้าสินค้า (Master Data / App)

```xml
<Grid AutomationProperties.AutomationId="ProductPanel">
    <TextBox   AutomationProperties.AutomationId="ProductTxtSearch" />
    <Button    AutomationProperties.AutomationId="ProductBtnAdd" Content="เพิ่มสินค้า" />
    <Button    AutomationProperties.AutomationId="ProductBtnEdit" Content="แก้ไข" />
    <Button    AutomationProperties.AutomationId="ProductBtnDelete" Content="ลบ" />
    <DataGrid  AutomationProperties.AutomationId="ProductGrid" />
</Grid>
```

### 4.4 เมนูนำทาง (Navigation)

```xml
<MenuItem AutomationProperties.AutomationId="MainMnuSale"     Header="ขายหน้าร้าน" />
<MenuItem AutomationProperties.AutomationId="MainMnuProduct" Header="สินค้า" />
<MenuItem AutomationProperties.AutomationId="MainMnuMaster"  Header="ข้อมูลหลัก" />
```

---

## 5. Custom Control / UserControl ที่ automation ต้องอ่านค่า

ถ้าคุณเขียน control เอง (DataGrid ที่ custom, list, tree) แล้ว automation ต้องอ่านแถว/ค่า **ต้อง expose ผ่าน UIA peer** ไม่เช่นนั้น FlaUI เห็น control แต่ไม่เห็นข้อมูลข้างใน

### 5.1 สร้าง AutomationPeer ให้ control เอง

```csharp
// ใน CustomControl
protected override AutomationPeer OnCreateAutomationPeer()
    => new MyDataGridAutomationPeer(this);

// Peer
public class MyDataGridAutomationPeer : FrameworkElementAutomationPeer
{
    public MyDataGridAutomationPeer(FrameworkElement owner) : base(owner) { }

    protected override string GetClassNameCore() => "MyDataGrid";
    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.DataGrid;

    // สำคัญ: บอกให้ UIA รู้จัก item ภายใน
    protected override List<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        foreach (var row in ((MyDataGrid)Owner).Rows)
            children.Add(new MyDataGridRowAutomationPeer(row));
        return children;
    }
}
```

### 5.2 ทางเลือกที่ง่ายกว่า (ใช้ได้เสมอ)

- ใช้ **DataGrid/ListView มาตรฐานของ WPF** ซึ่งมี `DataGridRowAutomationPeer`/`DataGridCellAutomationPeer` ในตัวอยู่แล้ว — แนะนำให้ใช้ถ้าเป็นไปได้
- ถ้า custom จริง ๆ และไม่มีเวลาเขียน peer: ให้ `Name` ของ row control มีค่าที่ระบุแถวชัดเจน เช่น `"Row-0001"` แล้ว QA อ้างอิงผ่าน `Name` แทน (ตกลงกับ QA ล่วงหน้า)

---

## 6. ห้าม / ระวัง (Do's & Don'ts)

| ❌ ห้าม | ✅ ให้ทำ |
|---|---|
| ตั้ง ID เป็นชื่อ class/property/`nameof(...)` ที่ obfuscator rename ได้ | ตั้งเป็น literal string คงที่ |
| ตั้ง ID ซ้ำกันใน window/view เดียว | ตรวจ uniqueness ตามข้อ 2.2 |
| ใช้ ID ที่ derive จาก label text (เช่น `this.Name`) | ใช้ ID ที่คงที่แม้ text เปลี่ยน |
| ลบ/เปลี่ยน ID ที่เคยประกาศแล้ว โดยไม่แจ้ง QA | แจ้ง QA ใน release note + ระบุรายการที่เปลี่ยน |
| ปล่อยให้ obfuscator เข้ารหัส/rename ค่า AutomationId | ตั้ง obfuscation config ให้ exclude (ข้อ 8) |
| ตั้ง ID เป็นภาษาไทย / มี space / อักขระพิเศษ | ใช้ a–z, A–Z, 0–9, `_` |

---

## 7. กติกาผูกพัน (อ้างอิง SELECTOR_CONTRACT.md)

| # | กติกา |
|---|---|
| D1 | Control ที่ automation ใช้ (ปุ่ม, input, grid, tab, dialog สำคัญ) ต้องมี `AutomationProperties.AutomationId` เสมอ |
| D2 | ห้าม derive AutomationId จากชื่อ class/property/method ที่ obfuscator rename ได้ |
| D3 | Obfuscation config ต้อง exclude: (ก) ทุก string ที่ใช้เป็น AutomationId, (ข) attribute `AutomationProperties.AutomationId*` ไม่ถูก strip |
| D4 | เปลี่ยน/ลบ AutomationId ที่มีอยู่ = **Breaking Change** → แจ้ง QA ล่วงหน้า + sync รายการ |
| D5 | Custom control ที่ automation อ่านค่า (grid/list) ต้อง expose rows/cells ผ่าน UIA peer หรือให้ `Name` ระบุแถวชัดเจน |

---

## 8. เงื่อนไขเมื่อใช้ obfuscator / เข้ารหัสไฟล์

เมื่อทีม Dev จะ obfuscate/encrypt assembly + resource (ตามแผน Encryption Readiness):

1. **Obfuscation config ต้อง exclude** ค่า string ที่ใช้เป็น AutomationId จากการเข้ารหัส/renaming
   - เช่น ใน rule ของ obfuscator: ยกเว้น string literal ที่ค่าตรงกับ pattern ID (`*Btn*`, `*Txt*`, `*Grid*`, ...) หรือ exclude attribute `AutomationProperties.*` ไม่ให้ strip
2. ห้ามเปิด string encryption กับ XAML resource ที่เก็บ AutomationId
3. เปลี่ยน AutomationId ยังไง = breaking change เหมือนเดิม (D4)
4. **ส่ง build ที่ encrypt แล้วให้ QA รัน Smoke Pack ทุก release ก่อน sign-off** (Automation Compatibility Check)

---

## 9. รายการ AutomationId ที่ QA ต้องการด่วน (ต้องกรอกก่อน release ถัดไป)

> ตารางนี้ = single source of truth ร่วมกับ `SELECTOR_CONTRACT.md` §4 — Dev ประกาศ ID ตอน implement / QA อ้างอิงตอนเขียน PageObject
> ✅ = QA verify แล้ว (runtime scan 1.0.0-beta.2) | ⬜ = **ยังรอ Dev กำหนด/ยืนยัน**

### 9.1 PromaxxsPos.exe (POS)

| Screen | Control | AutomationId | สถานะ |
|---|---|---|---|
| Login | overlay | `LoginOverlay` | ✅ |
| Login | ช่องรหัสพนักงาน | `TxtEmpId` | ✅ |
| Login | ช่องรหัสผ่าน | `PwdBox` | ✅ |
| Login | ปุ่มเข้าสู่ระบบ | `BtnSignIn` | ✅ |
| Login | ข้อความแจ้งผล/error | `ToastText` | ✅ |
| Sale | ช่องสแกนบาร์โค้ด | `SaleTxtBarcode` | ⬜ |
| Sale | ตารางรายการบิล | `SaleGridItems` | ⬜ |
| Sale | ปุ่มชำระเงิน | `SaleBtnPay` | ⬜ |
| Sale | ปุ่มส่วนลด | `SaleBtnDiscount` | ⬜ |
| Sale | ปุ่มยกเลิกบิล | `SaleBtnCancelBill` | ⬜ |
| Sale | ปุ่มชำระเงินสด | `PayRadCash` | ⬜ |
| Sale | ปุ่มชำระด้วยบัตร | `PayRadCard` | ⬜ |
| Navigation | เมนูขาย | `MainMnuSale` | ⬜ |
| Navigation | เมนูสินค้า | `MainMnuProduct` | ⬜ |

### 9.2 Promaxxs.App.exe (Master Data)

| Screen | Control | AutomationId | สถานะ |
|---|---|---|---|
| Login | panel | `LoginPanel` | ✅ |
| Login | ช่อง username | `TxtUsername` | ✅ |
| Login | ช่องรหัสผ่าน | `PwdBox` | ✅ |
| Login | ปุ่มเข้าสู่ระบบ | `BtnSignIn` | ✅ |
| Login | ข้อความแจ้งผล/error | `ToastText` | ✅ |
| Product | ช่องค้นหา | `ProductTxtSearch` | ⬜ |
| Product | ปุ่มเพิ่มสินค้า | `ProductBtnAdd` | ⬜ |
| Product | ปุ่มแก้ไข | `ProductBtnEdit` | ⬜ |
| Product | ปุ่มลบ | `ProductBtnDelete` | ⬜ |
| Product | ตารางรายการสินค้า | `ProductGrid` | ⬜ |
| Navigation | เมนูข้อมูลหลัก | `MasterMenu` | ⬜ |

### 9.3 ปัญหาที่พบจากการ scan 1.0.0-beta.2 (QA ตรวจแล้ว)

| App | Screens | Elements | Missing actionable ID | Duplicate ID |
|---|---:|---:|---:|---:|
| Pos | 2 | 171 | **42** | 0 |
| App | 9 | 1,104 | **108** | **20** |

ID ซ้ำที่เจอใน App: `glyphIcon`, `BtnMinimize`, `BtnMaximize`, `BtnClose`, `root` — **กรุณาทำให้ unique** (เช่น ต่อท้ายชื่อหน้าจอ/context) ไม่งั้น automation จับผิด control ได้

---

## 10. วิธีตรวจตัวเองก่อนส่ง release (QA จะ scan ซ้ำ)

QA รัน Automated Scanner เปิด app แล้วเดินหน้าจอตาม manifest เทียบ baseline:

```powershell
dotnet run --project Automation/Promaxx2.Automation/src/Promaxx2.Automation.Runner -- scan `
  --manifest Automation/Promaxx2.Automation/examples/scanner.app.json `
  --baseline Automation/Promaxx2.Automation/artifacts/app-report.json `
  --out Automation/Promaxx2.Automation/artifacts/app-report.json
```

- **Exit 0** = ไม่มี ID ว่าง/ซ้ำใหม่ และไม่มี ID หายจาก baseline → ผ่าน
- **Exit 2** = มี finding → QA จะส่ง report มาให้แก้ก่อนรับ build

**Quality Gate จะ fail ถ้า:**
1. มี actionable control ที่ไม่มี AutomationId **เพิ่มจาก baseline**
2. มี AutomationId ซ้ำเพิ่มขึ้น
3. ID เดิมหาย (ไม่อยู่ใน allowlist)
4. ControlType/Class ของ ID เดิมเปลี่ยน (ไม่อยู่ใน allowlist)

> การเพิ่ม allowlist (ยอมรับ ID หาย/เปลี่ยนชั่วคราว) ต้องแนบ release note/change request ของทีม Dev

---

## 11. Checklist ก่อนส่ง release ให้ QA

- [ ] ทุก control ในรายการข้อ 9 ที่ `⬜` ใส่ `AutomationProperties.AutomationId` แล้ว
- [ ] ไม่มี ID ซ้ำ (โดยเฉพาะ `glyphIcon`, `BtnMinimize`, `BtnMaximize`, `BtnClose`, `root`)
- [ ] ID เป็น literal คงที่ ไม่ derive จากชื่อ class/property
- [ ] Custom grid/list expose UIA peer (หรือมี `Name` ระบุแถว)
- [ ] Obfuscation config exclude ค่า AutomationId + attribute `AutomationProperties.*`
- [ ] Release note ระบุรายการ AutomationId ที่**เปลี่ยน/ลบ** (ถ้ามี) → ส่งให้ QA
- [ ] ส่ง build ที่ผ่าน encryption/obfuscation ให้ QA รัน Smoke Pack (ถ้า release นี้เริ่ม encrypt)

---

## 12. การติดต่อ / การอนุมัติ

| ฝ่าย | ผู้รับผิดชอบ | หมายเหตุ |
|---|---|---|
| QA (Automation) | ___________ | รัน scan, baseline, quality gate |
| Dev Promaxx2 (App) | ___________ | เติม ID ใน `Promaxxs.App.exe` |
| Dev Promaxx2 (POS) | ___________ | เติม ID ใน `PromaxxsPos.exe` |

เมื่อเติมครบตามข้อ 11 แล้วให้ QA scan ซ้ำ — baseline ใหม่จะถูกบันทึก และ `SELECTOR_CONTRACT.md` จะถูกอัปเดตสถานะจาก DRAFT เป็นอนุมัติ