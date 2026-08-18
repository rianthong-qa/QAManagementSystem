SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @ProjectId uniqueidentifier=(SELECT TOP(1) ProjectId FROM Projects WHERE ProjectCode='PMX2' AND IsActive=1);
IF @ProjectId IS NULL THROW 50001,'Active PMX2 project not found.',1;

DECLARE @Desired TABLE(SourceKey nvarchar(100) PRIMARY KEY,ParentKey nvarchar(100) NULL,ModuleName nvarchar(200),SortOrder int,Description nvarchar(500));
INSERT @Desired VALUES
('Dashboard',NULL,N'Dashboard',10,N'[05-Module:Dashboard] ภาพรวมยอดขายและข้อมูลสำคัญ'),
('Person',NULL,N'บุคคล',20,N'[05-Module:Person] ระบบบุคคล'),
('Person_Customer','Person',N'ลูกค้า',1,N'[05-Module:Person_Customer]'),
('Person_Supplier','Person',N'ผู้จำหน่าย',2,N'[05-Module:Person_Supplier]'),
('Person_ClearPointLocal','Person',N'Clear Point Local',3,N'[05-Module:Person_ClearPointLocal]'),
('Inventory',NULL,N'คลังสินค้า',30,N'[05-Module:Inventory] ระบบคลังสินค้า'),
('Inventory_ItemGroup','Inventory',N'กลุ่มสินค้า',1,N'[05-Module:Inventory_ItemGroup]'),
('Inventory_Items','Inventory',N'สินค้า',2,N'[05-Module:Inventory_Items]'),
('Inventory_PriceAdjust','Inventory',N'ปรับราคาขาย',3,N'[05-Module:Inventory_PriceAdjust]'),
('Inventory_MinMax','Inventory',N'ปริมาณสูงสุด-ต่ำสุด',4,N'[05-Module:Inventory_MinMax]'),
('Inventory_Promotion','Inventory',N'โปรโมชั่น',5,N'[05-Module:Inventory_Promotion]'),
('Inventory_ItemTemplate','Inventory',N'Template สินค้า',6,N'[05-Module:Inventory_ItemTemplate]'),
('Inventory_Suite','Inventory',N'Suite',7,N'[05-Module:Inventory_Suite]'),
('Inventory_RawMaterialConsumption','Inventory',N'ตัดวัตถุดิบ',8,N'[05-Module:Inventory_RawMaterialConsumption]'),
('Inventory_TransLocation','Inventory',N'Transfer Location',9,N'[05-Module:Inventory_TransLocation]'),
('Inventory_PharmacySystem','Inventory',N'ระบบร้านขายยา',10,N'[05-Module:Inventory_PharmacySystem]'),
('Inventory_iStock','Inventory',N'iStock',11,N'[05-Module:Inventory_iStock]'),
('Inventory_StockCount','Inventory',N'นับสต๊อกสินค้า',12,N'[05-Module:Inventory_StockCount]'),
('Inventory_StockAdjustment','Inventory',N'Stock Adjustment',13,N'[05-Module:Inventory_StockAdjustment]'),
('Transaction_Purchase',NULL,N'เอกสารซื้อ',40,N'[05-Module:Transaction_Purchase] ระบบเอกสารซื้อ'),
('Transaction_PurchaseOrder','Transaction_Purchase',N'Purchase Order',1,N'[05-Module:Transaction_PurchaseOrder]'),
('Transaction_PurchaseTransferBranch','Transaction_Purchase',N'โอนสินค้าระหว่างสาขา',2,N'[05-Module:Transaction_PurchaseTransferBranch]'),
('Transaction_PurchaseTransferWarehouse','Transaction_Purchase',N'โอนสินค้าระหว่างคลัง',3,N'[05-Module:Transaction_PurchaseTransferWarehouse]'),
('Transaction_PurchaseDeposit','Transaction_Purchase',N'จ่ายมัดจำ',4,N'[05-Module:Transaction_PurchaseDeposit]'),
('Transaction_PurchaseCreditDebitNote','Transaction_Purchase',N'เอกสารลดหนี้/เพิ่มหนี้',5,N'[05-Module:Transaction_PurchaseCreditDebitNote]'),
('Transaction_PurchaseBill','Transaction_Purchase',N'วางบิล',6,N'[05-Module:Transaction_PurchaseBill]'),
('Transaction_PurchaseInventoryCost','Transaction_Purchase',N'สินค้าคงเหลือและทุน',7,N'[05-Module:Transaction_PurchaseInventoryCost]'),
('Transaction_BuyWithholdingTax','Transaction_Purchase',N'หัก ณ ที่จ่าย',8,N'[05-Module:Transaction_BuyWithholdingTax]'),
('Transaction_PurchaseCheque','Transaction_Purchase',N'เช็คจ่าย',9,N'[05-Module:Transaction_PurchaseCheque]'),
('Transaction',NULL,N'เอกสารขาย',50,N'[05-Module:Transaction] ระบบเอกสารขาย'),
('Transaction_Order','Transaction',N'Order',1,N'[05-Module:Transaction_Order]'),
('Transaction_TransferBranch','Transaction',N'โอนสินค้าระหว่างสาขา',2,N'[05-Module:Transaction_TransferBranch]'),
('Transaction_TransferWarehouse','Transaction',N'โอนสินค้าระหว่างคลัง',3,N'[05-Module:Transaction_TransferWarehouse]'),
('Transaction_Deposit','Transaction',N'รับมัดจำ',4,N'[05-Module:Transaction_Deposit]'),
('Transaction_CreditDebitNote','Transaction',N'เอกสารลดหนี้/เพิ่มหนี้',5,N'[05-Module:Transaction_CreditDebitNote]'),
('Transaction_Bill','Transaction',N'วางบิล',6,N'[05-Module:Transaction_Bill]'),
('Transaction_ShiftMaintenance','Transaction',N'ปิดกะ',7,N'[05-Module:Transaction_ShiftMaintenance]'),
('Transaction_SaleWithholdingTax','Transaction',N'ภาษีถูกหัก ณ ที่จ่าย',8,N'[05-Module:Transaction_SaleWithholdingTax]'),
('Transaction_Cheque','Transaction',N'เช็ครับ',9,N'[05-Module:Transaction_Cheque]'),
('Transaction_ClearShiftWorking','Transaction',N'เคลียร์กะที่ค้างทำงาน',10,N'[05-Module:Transaction_ClearShiftWorking]'),
('Transaction_Workflow','Transaction',N'Workflow',11,N'[05-Module:Transaction_Workflow]'),
('Transaction_ShiftVerify','Transaction',N'ตรวจสอบกะ',12,N'[05-Module:Transaction_ShiftVerify]'),
('Transaction_EditDocument','Transaction',N'การจัดการเอกสาร',13,N'[05-Module:Transaction_EditDocument]'),
('Transaction_EBilling','Transaction',N'eBilling',14,N'[05-Module:Transaction_EBilling]'),
('Transaction_ETax','Transaction',N'ใบกำกับภาษีอิเล็กทรอนิกส์',15,N'[05-Module:Transaction_ETax]'),
('Settings',NULL,N'ตั้งค่า',60,N'[05-Module:Settings] ระบบตั้งค่า'),
('Settings_General','Settings',N'ตั้งค่าทั่วไป',1,N'[05-Module:Settings_General]'),
('Settings_PosGeneral','Settings',N'ตั้งค่าทั่วไป POS',2,N'[05-Module:Settings_PosGeneral]'),
('Settings_Company','Settings',N'ข้อมูลองค์กร',3,N'[05-Module:Settings_Company]'),
('Settings_Payment_Methods','Settings',N'ข้อมูลพื้นฐานการชำระเงิน',4,N'[05-Module:Settings_Payment_Methods]'),
('Settings_Person','Settings',N'ข้อมูลพื้นฐานบุคคล',5,N'[05-Module:Settings_Person]'),
('Settings_Inventory','Settings',N'ข้อมูลพื้นฐานคลังสินค้า',6,N'[05-Module:Settings_Inventory]'),
('Settings_Purchase','Settings',N'กำหนดเอกสารซื้อ',7,N'[05-Module:Settings_Purchase]'),
('Settings_Sales','Settings',N'กำหนดเอกสารขาย',8,N'[05-Module:Settings_Sales]'),
('Settings_Permission','Settings',N'กำหนดสิทธิ์การใช้งาน',9,N'[05-Module:Settings_Permission]'),
('Settings_Employee','Settings',N'ทะเบียนพนักงาน',10,N'[05-Module:Settings_Employee]'),
('Settings_Extension','Settings',N'ระบบเสริม',11,N'[05-Module:Settings_Extension]'),
('Settings_Shift','Settings',N'กำหนดระบบกะ',12,N'[05-Module:Settings_Shift]'),
('Reporting',NULL,N'รายงาน',70,N'[05-Module:Reporting] ระบบรายงาน'),
('Reporting_Receipt','Reporting',N'ใบเสร็จ',1,N'[05-Module:Reporting_Receipt]'),
('Utility',NULL,N'Utility',80,N'[05-Module:Utility] ระบบเครื่องมือ'),
('Utility_PrintBarcode','Utility',N'พิมพ์บาร์โค้ด',1,N'[05-Module:Utility_PrintBarcode]'),
('Utility_SaleTeam','Utility',N'Sale Team',2,N'[05-Module:Utility_SaleTeam]');

DECLARE @Map TABLE(SourceKey nvarchar(100) PRIMARY KEY,ModuleId uniqueidentifier);
INSERT @Map(SourceKey,ModuleId) SELECT 'Inventory',ModuleId FROM Modules WHERE ProjectId=@ProjectId AND ModuleCode='STOCK' AND IsActive=1;
DECLARE @Key nvarchar(100),@ParentKey nvarchar(100),@Name nvarchar(200),@Sort int,@Description nvarchar(500),@ParentId uniqueidentifier,@ModuleId uniqueidentifier,@Next int,@Code nvarchar(50);
WHILE EXISTS(SELECT 1 FROM @Desired d WHERE NOT EXISTS(SELECT 1 FROM @Map m WHERE m.SourceKey=d.SourceKey))
BEGIN
 SELECT TOP(1) @Key=d.SourceKey,@ParentKey=d.ParentKey,@Name=d.ModuleName,@Sort=d.SortOrder,@Description=d.Description
 FROM @Desired d WHERE NOT EXISTS(SELECT 1 FROM @Map m WHERE m.SourceKey=d.SourceKey) AND (d.ParentKey IS NULL OR EXISTS(SELECT 1 FROM @Map p WHERE p.SourceKey=d.ParentKey)) ORDER BY CASE WHEN d.ParentKey IS NULL THEN 0 ELSE 1 END,d.SortOrder;
 SET @ParentId=CASE WHEN @ParentKey IS NULL THEN NULL ELSE (SELECT ModuleId FROM @Map WHERE SourceKey=@ParentKey) END;
 SET @ModuleId=CASE WHEN @Key='Inventory' THEN (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND ModuleCode='STOCK' AND IsActive=1) ELSE (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND ModuleName=@Name AND ((@ParentId IS NULL AND ParentModuleId IS NULL) OR ParentModuleId=@ParentId) ORDER BY CASE WHEN Description LIKE N'%[[]05-Module:%' THEN 0 ELSE 1 END,SortOrder,CreatedAt) END;
 IF @ModuleId IS NULL
 BEGIN
  SELECT @Next=ISNULL(MAX(TRY_CONVERT(int,SUBSTRING(ModuleCode,LEN('PMX2-MOD-')+1,20))),0)+1 FROM Modules WHERE ProjectId=@ProjectId AND ModuleCode LIKE 'PMX2-MOD-%';
  SET @Code=CONCAT('PMX2-MOD-',RIGHT(CONCAT('000',@Next),3));SET @ModuleId=NEWID();
  INSERT Modules(ModuleId,ProjectId,ParentModuleId,ModuleCode,ModuleName,Description,OwnerUserId,IsActive,SortOrder,CreatedAt,CreatedBy,UpdatedAt,UpdatedBy) VALUES(@ModuleId,@ProjectId,@ParentId,@Code,@Name,@Description,NULL,1,@Sort,SYSUTCDATETIME(),NULL,NULL,NULL);
 END
 INSERT @Map VALUES(@Key,@ModuleId);SET @Key=NULL;
END
COMMIT;
SELECT COUNT(*) AS DocumentModules, SUM(CASE WHEN m.Description LIKE N'%[[]05-Module:%' THEN 1 ELSE 0 END) AS SeededModules FROM @Map x JOIN Modules m ON m.ModuleId=x.ModuleId;
