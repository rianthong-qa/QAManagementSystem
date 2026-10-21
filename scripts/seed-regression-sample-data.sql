-- ============================================================================
-- seed-regression-sample-data.sql
-- ตัวอย่างข้อมูล Regression (Impact Analysis / Suite / Cycle / History) แบบ
-- อ้างอิงจากโปรเจกต์จริง (PMX2) และโมดูลจริงที่สร้างโดย scripts/seed-promaxx2-modules.sql
-- แทนที่จะเป็นข้อมูลสมมติแบบ Lorem Ipsum
--
-- สิ่งที่สคริปต์นี้เพิ่ม (idempotent — รันซ้ำได้ ครั้งที่ 2 จะข้ามถ้าเคยรันแล้ว):
--   - TestCases 8 รายการ (TestType='Regression') ผูกกับโมดูลจริง เช่น
--     ปรับราคาขาย / นับสต๊อกสินค้า / e-Tax Invoice / ปิดกะ / โอนสินค้าระหว่างสาขา
--   - TestSuite (SuiteType='Regression') รวมเคสทั้งหมด
--   - TestCycle (CycleType='Regression') พร้อม TestCycleCases และ TestExecutions
--     ผลลัพธ์ผสม Pass/Fail/Blocked/NotRun เพื่อให้ตัวเลข Metrics ดูสมจริง
--   - RegressionAnalyses (ประวัติการวิเคราะห์ผลกระทบ) 2 รายการ
--   - RegressionActivities (ประวัติกิจกรรม) 3 รายการ
--   - RegressionProfiles (ค่าเริ่มต้นของการวิเคราะห์ผลกระทบ) 1 รายการ
--   - RegressionSchedules (เฝ้าระวัง Build ใหม่) 1 รายการ
--   - Release / Build / TestEnvironment ใหม่ จะถูกสร้างเฉพาะกรณีที่โปรเจกต์ยังไม่มีอยู่จริงเท่านั้น
--     (ปกติจะ "ใช้ของจริงที่มีอยู่แล้ว" ไม่สร้างซ้อน)
--
-- ข้อควรระวัง: ฐานข้อมูลนี้เป็นข้อมูลจริงของทีม โปรดตรวจสอบสคริปต์นี้ก่อนรัน
-- และรันกับฐานข้อมูล dev/UAT เท่านั้น ไม่ใช่ Production
--
-- วิธีลบข้อมูลตัวอย่างชุดนี้ทั้งหมดภายหลัง (ทุกแถวมี marker [SEED:REGRESSION-DEMO]):
--   DELETE TestExecutions WHERE TestCycleCaseId IN (SELECT TestCycleCaseId FROM TestCycleCases WHERE TestCycleId IN (SELECT TestCycleId FROM TestCycles WHERE Notes LIKE '%[SEED:REGRESSION-DEMO]%'));
--   DELETE TestCycleCases WHERE TestCycleId IN (SELECT TestCycleId FROM TestCycles WHERE Notes LIKE '%[SEED:REGRESSION-DEMO]%');
--   DELETE TestCycles WHERE Notes LIKE '%[SEED:REGRESSION-DEMO]%';
--   DELETE TestSuiteCases WHERE TestSuiteId IN (SELECT TestSuiteId FROM TestSuites WHERE Description LIKE '%[SEED:REGRESSION-DEMO]%');
--   DELETE TestSuites WHERE Description LIKE '%[SEED:REGRESSION-DEMO]%';
--   DELETE TestSteps WHERE TestCaseId IN (SELECT TestCaseId FROM TestCases WHERE Objective LIKE '%[SEED:REGRESSION-DEMO]%');
--   DELETE TestCases WHERE Objective LIKE '%[SEED:REGRESSION-DEMO]%';
--   DELETE RegressionAnalyses WHERE ChangeNotes LIKE '%[SEED:REGRESSION-DEMO]%';
--   DELETE RegressionActivities WHERE Details LIKE '%[SEED:REGRESSION-DEMO]%';
--   DELETE RegressionSchedules WHERE Name LIKE N'%เฝ้าระวัง Build ใหม่%[SEED:REGRESSION-DEMO]%';
--   DELETE RegressionProfiles WHERE Name = N'ค่าเริ่มต้น - Full Regression [SEED:REGRESSION-DEMO]';
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM TestCases WHERE Objective LIKE N'%[SEED:REGRESSION-DEMO]%')
BEGIN
    PRINT 'Regression demo data already seeded — skipping (see header comment to remove it first).';
    COMMIT;
    RETURN;
END

DECLARE @ProjectId uniqueidentifier = (SELECT TOP(1) ProjectId FROM Projects WHERE ProjectCode='PMX2' AND IsActive=1);
IF @ProjectId IS NULL THROW 50001,'Active PMX2 project not found.',1;

DECLARE @Now datetime2(0) = SYSUTCDATETIME();
DECLARE @UserId uniqueidentifier = (SELECT TOP(1) UserId FROM Users WHERE IsActive=1 ORDER BY CreatedAt);

-- ---------------------------------------------------------------------------
-- 1) โมดูลจริง: ผูกกับโมดูลที่ scripts/seed-promaxx2-modules.sql สร้างไว้ (จับคู่ด้วย marker
--    [05-Module:<key>] ใน Description) ถ้ายังไม่เคยรันสคริปต์นั้น จะ fallback ไปที่โมดูลอื่นที่ยัง active อยู่
-- ---------------------------------------------------------------------------
DECLARE @FallbackModuleId uniqueidentifier = (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 ORDER BY SortOrder);
IF @FallbackModuleId IS NULL THROW 50002,'Project PMX2 has no active module — seed modules first (see scripts/seed-promaxx2-modules.sql).',1;

DECLARE @ModPriceAdjust uniqueidentifier = COALESCE(
    (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND Description LIKE N'%[[]05-Module:Inventory_PriceAdjust]%'),
    (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND ModuleName=N'ปรับราคาขาย'),
    @FallbackModuleId);
DECLARE @ModStockCount uniqueidentifier = COALESCE(
    (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND Description LIKE N'%[[]05-Module:Inventory_StockCount]%'),
    (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND ModuleName=N'นับสต๊อกสินค้า'),
    @FallbackModuleId);
DECLARE @ModETax uniqueidentifier = COALESCE(
    (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND Description LIKE N'%[[]05-Module:Transaction_ETax]%'),
    (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND ModuleName=N'ใบกำกับภาษีอิเล็กทรอนิกส์'),
    @FallbackModuleId);
DECLARE @ModShift uniqueidentifier = COALESCE(
    (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND Description LIKE N'%[[]05-Module:Transaction_ShiftMaintenance]%'),
    (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND ModuleName=N'ปิดกะ'),
    @FallbackModuleId);
DECLARE @ModTransferBranch uniqueidentifier = COALESCE(
    (SELECT TOP(1) ModuleId FROM Modules WHERE ProjectId=@ProjectId AND IsActive=1 AND Description LIKE N'%[[]05-Module:Transaction_TransferBranch]%'),
    @FallbackModuleId);

-- ---------------------------------------------------------------------------
-- 2) Release / Build: ใช้ของจริงที่มีอยู่แล้ว (รอบล่าสุดที่ยังไม่ปิด) ถ้าไม่มีเลยจึงสร้างใหม่
-- ---------------------------------------------------------------------------
DECLARE @ReleaseId uniqueidentifier = (SELECT TOP(1) ReleaseId FROM Releases WHERE ProjectId=@ProjectId AND Status IN ('Draft','Testing','Ready') ORDER BY CreatedAt DESC);
IF @ReleaseId IS NULL SET @ReleaseId = (SELECT TOP(1) ReleaseId FROM Releases WHERE ProjectId=@ProjectId ORDER BY CreatedAt DESC);
IF @ReleaseId IS NULL
BEGIN
    SET @ReleaseId = NEWID();
    INSERT Releases(ReleaseId,ProjectId,ReleaseCode,Version,ReleaseType,Scope,PlannedReleaseDate,ActualReleaseDate,Status,ReleaseOwnerUserId,CreatedAt,CreatedBy,UpdatedAt,UpdatedBy)
    VALUES(@ReleaseId,@ProjectId,N'PMX2-REL-'+FORMAT(@Now,'yyyyMM'),FORMAT(@Now,'yyyy.MM')+N'.0',N'Minor',NULL,NULL,NULL,'Testing',@UserId,@Now,@UserId,NULL,NULL);
END
DECLARE @ReleaseCode nvarchar(100) = (SELECT ReleaseCode FROM Releases WHERE ReleaseId=@ReleaseId);

DECLARE @BuildId uniqueidentifier = (SELECT TOP(1) BuildId FROM Builds WHERE ReleaseId=@ReleaseId ORDER BY CreatedAt DESC);
IF @BuildId IS NULL
BEGIN
    SET @BuildId = NEWID();
    INSERT Builds(BuildId,ReleaseId,BuildNumber,ApplicationVersion,PackageVersion,CommitReference,BuildDate,ChangeNotes,KnownIssues,IsReleaseCandidate,Status,CreatedAt,CreatedBy)
    VALUES(@BuildId,@ReleaseId,N'B'+FORMAT(@Now,'yyyyMMdd')+N'.1',NULL,NULL,NULL,@Now,N'ปรับ Flow ปรับราคาขาย, ปิดกะ, e-Tax Invoice และการโอนสินค้าระหว่างสาขา',NULL,0,'Testing',@Now,@UserId);
END
DECLARE @BuildNumber nvarchar(100) = (SELECT BuildNumber FROM Builds WHERE BuildId=@BuildId);

DECLARE @EnvId uniqueidentifier = (SELECT TOP(1) TestEnvironmentId FROM TestEnvironments WHERE ProjectId=@ProjectId AND IsActive=1 ORDER BY EnvironmentName);
IF @EnvId IS NULL
BEGIN
    SET @EnvId = NEWID();
    INSERT TestEnvironments(TestEnvironmentId,ProjectId,EnvironmentName,BaseUrl,IsActive) VALUES(@EnvId,@ProjectId,N'UAT',NULL,1);
END

-- ---------------------------------------------------------------------------
-- 3) TestSuite (Regression)
-- ---------------------------------------------------------------------------
DECLARE @SuiteId uniqueidentifier = NEWID();
DECLARE @SuiteNext int = (SELECT ISNULL(MAX(TRY_CONVERT(int,SUBSTRING(SuiteCode,LEN('PMX2-TS-')+1,10))),0)+1 FROM TestSuites WHERE ProjectId=@ProjectId AND SuiteCode LIKE 'PMX2-TS-%');
DECLARE @SuiteCode nvarchar(100) = N'PMX2-TS-'+CAST(@SuiteNext AS nvarchar(10));
DECLARE @SuiteName nvarchar(200) = N'ชุดทดสอบ Regression - รอบ '+FORMAT(@Now,'MMM yyyy');
INSERT TestSuites(TestSuiteId,ProjectId,SuiteCode,SuiteName,SuiteType,Description,RiskTier,IsActive,CreatedBy,CreatedAt)
VALUES(@SuiteId,@ProjectId,@SuiteCode,@SuiteName,N'Regression',N'สร้างจากผลวิเคราะห์ผลกระทบ (Impact Analysis) ของ Build '+@BuildNumber+N': ปรับราคาขาย, นับสต๊อก, ปิดกะ, e-Tax และโอนสินค้าระหว่างสาขา [SEED:REGRESSION-DEMO]',N'P1',1,@UserId,DATEADD(DAY,-1,@Now));

-- ---------------------------------------------------------------------------
-- 4) TestCycle (Regression)
-- ---------------------------------------------------------------------------
DECLARE @CycleId uniqueidentifier = NEWID();
DECLARE @CycleNext int = (SELECT ISNULL(MAX(TRY_CONVERT(int,SUBSTRING(CycleCode,LEN('PMX2-CYC-REG-')+1,10))),0)+1 FROM TestCycles WHERE ProjectId=@ProjectId AND CycleCode LIKE 'PMX2-CYC-REG-%');
DECLARE @CycleCode nvarchar(100) = N'PMX2-CYC-REG-'+RIGHT('000'+CAST(@CycleNext AS nvarchar(10)),3);
INSERT TestCycles(TestCycleId,ProjectId,ReleaseId,BuildId,EnvironmentId,TestSuiteId,CycleCode,CycleName,CycleType,StartDate,EndDate,OwnerUserId,Status,Notes,CreatedAt,CreatedBy)
VALUES(@CycleId,@ProjectId,@ReleaseId,@BuildId,@EnvId,@SuiteId,@CycleCode,N'Regression Cycle - '+@ReleaseCode+N' / '+@BuildNumber,N'Regression',DATEADD(HOUR,-20,@Now),NULL,@UserId,'InProgress',N'สร้างจากผลวิเคราะห์ผลกระทบอัตโนมัติ [SEED:REGRESSION-DEMO]',DATEADD(HOUR,-20,@Now),@UserId);

-- ---------------------------------------------------------------------------
-- 5) TestCases (Regression) + Steps + SuiteCases + CycleCases + Executions
-- ---------------------------------------------------------------------------
DECLARE @Cases TABLE(
    Seq int IDENTITY(1,1) PRIMARY KEY,
    ModuleId uniqueidentifier,
    Priority nvarchar(5),
    Title nvarchar(300),
    Objective nvarchar(2000),
    Preconditions nvarchar(1000),
    Step1Action nvarchar(1000), Step1Data nvarchar(300), Step1Expected nvarchar(1000),
    Step2Action nvarchar(1000), Step2Data nvarchar(300), Step2Expected nvarchar(1000),
    Step3Action nvarchar(1000), Step3Data nvarchar(300), Step3Expected nvarchar(1000),
    DemoStatus nvarchar(20)
);
INSERT INTO @Cases (ModuleId,Priority,Title,Objective,Preconditions,Step1Action,Step1Data,Step1Expected,Step2Action,Step2Data,Step2Expected,Step3Action,Step3Data,Step3Expected,DemoStatus)
VALUES
(@ModPriceAdjust,'P1',N'ตรวจสอบการปรับราคาขายสินค้าแบบรายตัว',
 N'ตรวจสอบว่าเมื่อปรับราคาขายสินค้ารายตัวแล้ว ราคาใหม่มีผลกับรายการขายที่บันทึกหลังจากนั้นทันที [SEED:REGRESSION-DEMO]',
 N'มีสิทธิ์เมนูปรับราคาขาย และมีสินค้าที่ยังไม่หมดอายุราคาปัจจุบัน',
 N'เข้าเมนู คลังสินค้า > ปรับราคาขาย แล้วเลือกสินค้าที่ต้องการปรับราคา',N'รหัสสินค้า SKU-001',N'ระบบแสดงราคาขายปัจจุบันของสินค้าที่เลือก',
 N'กำหนดราคาขายใหม่และวันที่มีผล แล้วกดบันทึก',N'ราคาใหม่ 129.00 บาท',N'ระบบบันทึกราคาใหม่สำเร็จและแสดงในประวัติการปรับราคา',
 N'บันทึกรายการขายสินค้าดังกล่าวที่หน้าขาย (POS)',N'-',N'ราคาขายที่แสดงในบิลตรงกับราคาที่ปรับใหม่',
 'Pass'),
(@ModPriceAdjust,'P2',N'ตรวจสอบการปรับราคาขายสินค้าแบบกลุ่มตามกลุ่มสินค้า (Batch)',
 N'ตรวจสอบว่าการปรับราคาขายแบบเลือกทั้งกลุ่มสินค้า อัปเดตราคาครบทุกรายการในกลุ่มโดยไม่กระทบสินค้ากลุ่มอื่น [SEED:REGRESSION-DEMO]',
 N'มีกลุ่มสินค้าที่มีสินค้าอย่างน้อย 2 รายการขึ้นไป',
 N'เข้าเมนู คลังสินค้า > ปรับราคาขาย > เลือกปรับราคาตามกลุ่มสินค้า',N'กลุ่มสินค้า: เครื่องดื่ม',N'ระบบแสดงรายการสินค้าทั้งหมดในกลุ่มที่เลือก',
 N'ระบุ % ส่วนปรับราคา แล้วกดยืนยัน',N'ปรับเพิ่ม 5%',N'ระบบคำนวณและปรับราคาสินค้าทุกตัวในกลุ่มถูกต้องตาม %',
 N'ตรวจสอบราคาสินค้านอกกลุ่มที่เลือก',N'-',N'ราคาสินค้ากลุ่มอื่นไม่เปลี่ยนแปลง',
 'Pass'),
(@ModStockCount,'P1',N'ตรวจสอบการนับสต๊อกสินค้าและปรับยอดคงเหลือให้ตรงกับผลนับจริง',
 N'ตรวจสอบว่าหลังบันทึกผลนับสต๊อก ระบบปรับยอดคงเหลือในคลังให้ตรงกับผลนับจริงและบันทึกส่วนต่างเป็นรายการปรับปรุงสต๊อก [SEED:REGRESSION-DEMO]',
 N'มีสิทธิ์เมนูนับสต๊อกสินค้า และทราบยอดคงเหลือในระบบก่อนนับ',
 N'เข้าเมนู คลังสินค้า > นับสต๊อกสินค้า แล้วสร้างใบนับสต๊อกใหม่',N'คลัง: สาขา 1',N'ระบบแสดงยอดคงเหลือตามระบบของสินค้าทุกรายการ',
 N'กรอกยอดนับจริงที่แตกต่างจากยอดในระบบแล้วบันทึก',N'สินค้า A ระบบ 100 นับได้ 96',N'ระบบคำนวณผลต่างและรอการยืนยัน',
 N'ยืนยันใบนับสต๊อกเพื่อปรับยอด',N'-',N'ยอดคงเหลือในคลังถูกปรับเป็น 96 และมีประวัติการปรับปรุงสต๊อก',
 'Fail'),
(@ModETax,'P0',N'ตรวจสอบการออกใบกำกับภาษีอิเล็กทรอนิกส์ (e-Tax Invoice) หลังบันทึกขาย',
 N'ตรวจสอบว่าหลังบันทึกรายการขายที่ลูกค้าขอใบกำกับภาษีเต็มรูปแบบ ระบบออกใบกำกับภาษีอิเล็กทรอนิกส์และส่งข้อมูลให้กรมสรรพากรได้ถูกต้อง [SEED:REGRESSION-DEMO]',
 N'ตั้งค่าระบบ e-Tax เรียบร้อยแล้ว และมีข้อมูลลูกค้าที่มีเลขผู้เสียภาษี',
 N'บันทึกรายการขายและระบุให้ออกใบกำกับภาษีเต็มรูปแบบ',N'ลูกค้า: บจก. ตัวอย่าง เลขผู้เสียภาษี 13 หลัก',N'ระบบสร้างใบกำกับภาษีพร้อมเลขที่เอกสารอัตโนมัติ',
 N'กดส่งข้อมูลใบกำกับภาษีอิเล็กทรอนิกส์',N'-',N'ระบบส่งไฟล์ e-Tax ได้สำเร็จและแสดงสถานะ "ส่งแล้ว"',
 N'ตรวจสอบรายงานใบกำกับภาษีอิเล็กทรอนิกส์ประจำวัน',N'-',N'รายการที่ออกแสดงอยู่ในรายงานครบถ้วน',
 'Pass'),
(@ModETax,'P1',N'ตรวจสอบการยกเลิกใบกำกับภาษีอิเล็กทรอนิกส์และแจ้งกรมสรรพากร',
 N'ตรวจสอบว่าเมื่อยกเลิกใบกำกับภาษีอิเล็กทรอนิกส์ที่ส่งไปแล้ว ระบบส่งใบแจ้งยกเลิกและปรับสถานะเอกสารถูกต้อง [SEED:REGRESSION-DEMO]',
 N'มีใบกำกับภาษีอิเล็กทรอนิกส์ที่ส่งสำเร็จแล้วอย่างน้อย 1 ใบ',
 N'เปิดใบกำกับภาษีที่ต้องการยกเลิกแล้วกดยกเลิกเอกสาร',N'-',N'ระบบแสดงเหตุผลการยกเลิกให้ระบุ',
 N'ระบุเหตุผลและยืนยันการยกเลิก',N'เหตุผล: ออกเอกสารผิดจำนวนเงิน',N'ระบบส่งใบแจ้งยกเลิกไปยังกรมสรรพากร',
 N'ตรวจสอบสถานะเอกสารหลังยกเลิก',N'-',N'สถานะเอกสารเปลี่ยนเป็น "ยกเลิกแล้ว"',
 'Blocked'),
(@ModShift,'P0',N'ตรวจสอบการปิดกะการขายและสรุปยอดขายประจำกะ',
 N'ตรวจสอบว่าการปิดกะสรุปยอดขาย เงินสด และช่องทางชำระเงินทั้งหมดของกะถูกต้องตรงกับรายการขายจริง [SEED:REGRESSION-DEMO]',
 N'มีรายการขายเกิดขึ้นระหว่างกะที่จะปิด',
 N'เข้าเมนู เอกสารขาย > ปิดกะ แล้วเลือกกะที่ต้องการปิด',N'กะ: กะเช้า วันที่ปัจจุบัน',N'ระบบแสดงสรุปยอดขายแยกตามช่องทางชำระเงิน',
 N'นับเงินสดจริงแล้วกรอกยอดนับเข้าระบบ',N'เงินสดนับได้ 15,000 บาท',N'ระบบเทียบยอดนับกับยอดตามระบบและแสดงส่วนต่าง (ถ้ามี)',
 N'ยืนยันปิดกะ',N'-',N'สถานะกะเปลี่ยนเป็น "ปิดกะแล้ว" และไม่สามารถบันทึกขายเพิ่มในกะนี้ได้อีก',
 'Pass'),
(@ModTransferBranch,'P1',N'ตรวจสอบการโอนสินค้าระหว่างสาขาและปรับปรุงสต๊อกทั้งสองฝั่ง',
 N'ตรวจสอบว่าเมื่อสาขาปลายทางรับสินค้าที่โอนมาแล้ว สต๊อกสาขาต้นทางลดลงและสต๊อกสาขาปลายทางเพิ่มขึ้นตรงตามจำนวนที่โอน [SEED:REGRESSION-DEMO]',
 N'มีสต๊อกสินค้าเพียงพอที่สาขาต้นทาง',
 N'สร้างใบโอนสินค้าระหว่างสาขาจากสาขาต้นทางไปสาขาปลายทาง',N'สินค้า A จำนวน 20 ชิ้น',N'ระบบตัดสต๊อกสาขาต้นทางทันทีที่ยืนยันโอน',
 N'สาขาปลายทางรับสินค้าตามใบโอน',N'-',N'ระบบเพิ่มสต๊อกสาขาปลายทางตามจำนวนที่รับจริง',
 N'ตรวจสอบยอดคงเหลือทั้งสองสาขาหลังโอน',N'-',N'ยอดคงเหลือทั้งสองสาขาถูกต้องตรงกับจำนวนที่โอน-รับ',
 'Pass'),
(@ModTransferBranch,'P2',N'ตรวจสอบการยกเลิกใบโอนสินค้าระหว่างสาขาก่อนได้รับสินค้า',
 N'ตรวจสอบว่าการยกเลิกใบโอนสินค้าก่อนสาขาปลายทางรับสินค้า คืนสต๊อกให้สาขาต้นทางถูกต้องและใบโอนไม่สามารถใช้รับสินค้าได้อีก [SEED:REGRESSION-DEMO]',
 N'มีใบโอนสินค้าระหว่างสาขาที่ยังไม่ได้รับที่ปลายทาง',
 N'เปิดใบโอนสินค้าที่ต้องการยกเลิกแล้วกดยกเลิกใบโอน',N'-',N'ระบบแสดงคำยืนยันการยกเลิก',
 N'ยืนยันการยกเลิกใบโอน',N'-',N'สต๊อกสาขาต้นทางถูกคืนกลับตามจำนวนในใบโอน',
 N'ตรวจสอบสถานะใบโอนและพยายามรับสินค้าอีกครั้ง',N'-',N'สถานะใบโอนเป็น "ยกเลิกแล้ว" และไม่สามารถรับสินค้าได้อีก',
 'NotRun');

DECLARE @Total int = (SELECT COUNT(*) FROM @Cases);
DECLARE @CodeBase int = (SELECT ISNULL(MAX(TRY_CONVERT(int,SUBSTRING(TestCaseCode,LEN('PMX2-REG-')+1,10))),0) FROM TestCases WHERE ProjectId=@ProjectId AND TestCaseCode LIKE 'PMX2-REG-%');

DECLARE @i int = 1;
DECLARE @TcId uniqueidentifier, @Code nvarchar(100), @ModuleId uniqueidentifier, @Priority nvarchar(5), @Title nvarchar(300), @Objective nvarchar(2000), @Pre nvarchar(1000), @DemoStatus nvarchar(20);
DECLARE @S1A nvarchar(1000),@S1D nvarchar(300),@S1E nvarchar(1000),@S2A nvarchar(1000),@S2D nvarchar(300),@S2E nvarchar(1000),@S3A nvarchar(1000),@S3D nvarchar(300),@S3E nvarchar(1000);
DECLARE @TccId uniqueidentifier, @CaseWeight int;

WHILE @i <= @Total
BEGIN
    SELECT @ModuleId=ModuleId,@Priority=Priority,@Title=Title,@Objective=Objective,@Pre=Preconditions,
           @S1A=Step1Action,@S1D=Step1Data,@S1E=Step1Expected,
           @S2A=Step2Action,@S2D=Step2Data,@S2E=Step2Expected,
           @S3A=Step3Action,@S3D=Step3Data,@S3E=Step3Expected,
           @DemoStatus=DemoStatus
    FROM @Cases WHERE Seq=@i;

    SET @TcId = NEWID();
    SET @Code = N'PMX2-REG-'+RIGHT('000'+CAST(@CodeBase+@i AS nvarchar(10)),3);

    INSERT TestCases(TestCaseId,ProjectId,ModuleId,TestScenarioId,TestCaseCode,Title,Objective,Preconditions,Priority,TestType,AutomationCandidate,AutomationTarget,Status,RevisionNo,OwnerUserId,ComplexityWeight,EstimatedMinutes,RequiredSkillLevel,IsCritical,ReviewerRequired,IsDeleted,CreatedAt,CreatedBy,UpdatedAt,UpdatedBy)
    VALUES(@TcId,@ProjectId,@ModuleId,NULL,@Code,@Title,@Objective,@Pre,@Priority,N'Regression',0,NULL,'Ready',1,@UserId,1,30,1,CASE WHEN @Priority='P0' THEN 1 ELSE 0 END,0,0,@Now,@UserId,NULL,NULL);

    INSERT TestSteps(TestStepId,TestCaseId,RevisionNo,StepNo,Action,TestDataText,ExpectedResult) VALUES
        (NEWID(),@TcId,1,1,@S1A,@S1D,@S1E),
        (NEWID(),@TcId,1,2,@S2A,@S2D,@S2E),
        (NEWID(),@TcId,1,3,@S3A,@S3D,@S3E);

    INSERT TestSuiteCases(TestSuiteId,TestCaseId,SortOrder,IsRequired) VALUES(@SuiteId,@TcId,@i,CASE WHEN @Priority IN('P0','P1') THEN 1 ELSE 0 END);

    SET @TccId = NEWID();
    SET @CaseWeight = CASE @Priority WHEN 'P0' THEN 43 WHEN 'P1' THEN 33 WHEN 'P2' THEN 23 ELSE 13 END;
    INSERT TestCycleCases(TestCycleCaseId,TestCycleId,TestCaseId,TestCaseRevisionNo,AssignedTesterUserId,Priority,ExecutionOrder,CurrentStatus,CaseWeight,EstimatedMinutesSnapshot,RequiredSkillLevelSnapshot,AlgorithmVersion,AssignmentVersion)
    VALUES(@TccId,@CycleId,@TcId,1,@UserId,@Priority,@i,@DemoStatus,@CaseWeight,30,1,'weighted-v1',CONVERT(varbinary(16),NEWID()));

    IF @DemoStatus<>'NotRun' AND @UserId IS NOT NULL
    BEGIN
        INSERT TestExecutions(TestExecutionId,TestCycleCaseId,ExecutionNo,BuildId,EnvironmentId,TesterUserId,StartedAt,CompletedAt,Status,ExecutionType,ActualResult,Comment,IsDeleted,DeletedAt,DeletedBy)
        VALUES(NEWID(),@TccId,1,@BuildId,@EnvId,@UserId,DATEADD(MINUTE,-45,@Now),DATEADD(MINUTE,-30,@Now),@DemoStatus,'Manual',
            CASE @DemoStatus
                WHEN 'Pass' THEN N'ทดสอบผ่านตามผลลัพธ์ที่คาดหวังทุกขั้นตอน'
                WHEN 'Fail' THEN N'พบข้อผิดพลาด: ระบบไม่ปรับยอดคงเหลือตามผลนับจริง ต้องแจ้งทีมพัฒนาตรวจสอบ'
                ELSE N'ไม่สามารถทดสอบต่อได้เนื่องจากติดปัญหาการเชื่อมต่อกับผู้ให้บริการ e-Tax'
            END,
            NULL,0,NULL,NULL);
    END

    SET @i += 1;
END

-- ---------------------------------------------------------------------------
-- 6) RegressionAnalyses (ประวัติการวิเคราะห์ผลกระทบ)
-- ---------------------------------------------------------------------------
INSERT RegressionAnalyses(RegressionAnalysisId,ProjectId,ReleaseId,BuildId,ImpactedModules,RecommendedCases,MinimumPriority,ChangeNotes,AnalyzedBy,AnalyzedAt)
VALUES
    (NEWID(),@ProjectId,@ReleaseId,@BuildId,5,8,'P1',N'รอบตรวจสอบผลกระทบเบื้องต้น: เปลี่ยนแปลง Flow ปรับราคาขาย, ปิดกะ, e-Tax และโอนสินค้าระหว่างสาขา [SEED:REGRESSION-DEMO]',@UserId,DATEADD(DAY,-2,@Now)),
    (NEWID(),@ProjectId,@ReleaseId,@BuildId,5,8,'P1',N'อัปเดตผลวิเคราะห์หลังรวมแก้ไขนับสต๊อกสินค้าเพิ่มเติมเข้ากับ Build เดียวกัน [SEED:REGRESSION-DEMO]',@UserId,DATEADD(HOUR,-25,@Now));

-- ---------------------------------------------------------------------------
-- 7) RegressionActivities (ประวัติกิจกรรม)
-- ---------------------------------------------------------------------------
INSERT RegressionActivities(RegressionActivityId,ProjectId,ReleaseId,BuildId,Action,Details,ActorUserId,CreatedAt)
VALUES
    (NEWID(),@ProjectId,@ReleaseId,@BuildId,'ImpactAnalyzed',N'วิเคราะห์ผลกระทบ Build '+@BuildNumber+N' พบโมดูลที่ได้รับผลกระทบ 5 โมดูล แนะนำ 8 เคสทดสอบ [SEED:REGRESSION-DEMO]',@UserId,DATEADD(DAY,-2,@Now)),
    (NEWID(),@ProjectId,@ReleaseId,@BuildId,'SuiteGenerated',N'สร้างชุดทดสอบ '+@SuiteCode+N' ('+@SuiteName+N') จำนวน 8 เคส [SEED:REGRESSION-DEMO]',@UserId,DATEADD(DAY,-1,@Now)),
    (NEWID(),@ProjectId,@ReleaseId,@BuildId,'CasesAddedToCycle',N'เพิ่มเคสทดสอบ 8 รายการเข้ารอบทดสอบ '+@CycleCode+N' [SEED:REGRESSION-DEMO]',@UserId,DATEADD(HOUR,-19,@Now));

-- ---------------------------------------------------------------------------
-- 8) RegressionProfiles (ค่าเริ่มต้นของการวิเคราะห์ผลกระทบ)
-- ---------------------------------------------------------------------------
DECLARE @ProfileId uniqueidentifier = NEWID();
DECLARE @ProfileJson nvarchar(max) = N'{"minimumPriority":"P1","includeSharedDependencies":true,"databaseChange":false,"apiChange":true,"calculationChange":true,"permissionChange":false,"installerChange":false,"defectFix":true,"directImpactWeight":40,"historicalDefectWeight":30,"criticalPriorityWeight":20,"sharedDependencyWeight":10}';
INSERT RegressionProfiles(RegressionProfileId,ProjectId,Name,Visibility,OwnerUserId,SettingsJson,IsActive,CreatedAt,UpdatedAt)
VALUES(@ProfileId,@ProjectId,N'ค่าเริ่มต้น - Full Regression [SEED:REGRESSION-DEMO]',N'Shared',@UserId,@ProfileJson,1,@Now,NULL);

-- ---------------------------------------------------------------------------
-- 9) RegressionSchedules (เฝ้าระวัง Build ใหม่)
-- ---------------------------------------------------------------------------
INSERT RegressionSchedules(RegressionScheduleId,ProjectId,ReleaseId,RegressionProfileId,Name,OwnerUserId,LastNotifiedBuildId,IsActive,CreatedAt)
VALUES(NEWID(),@ProjectId,@ReleaseId,@ProfileId,N'เฝ้าระวัง Build ใหม่ - '+@ReleaseCode+N' [SEED:REGRESSION-DEMO]',@UserId,@BuildId,1,@Now);

COMMIT;

SELECT
    @ReleaseCode AS ReleaseCode, @BuildNumber AS BuildNumber, @SuiteCode AS SuiteCode, @CycleCode AS CycleCode,
    (SELECT COUNT(*) FROM TestCases WHERE ProjectId=@ProjectId AND TestCaseCode LIKE 'PMX2-REG-%') AS RegressionTestCases,
    (SELECT COUNT(*) FROM TestCycleCases WHERE TestCycleId=@CycleId) AS CycleCases,
    (SELECT COUNT(*) FROM TestExecutions WHERE TestCycleCaseId IN (SELECT TestCycleCaseId FROM TestCycleCases WHERE TestCycleId=@CycleId)) AS Executions;
