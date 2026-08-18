# QA Test Plan — โมดูล Dashboard

> ProMaxx 21.0.0-beta.1
> วันที่สร้าง: 17 สิงหาคม 2569

---

## 1. ภาพรวมโมดูล

โมดูล Dashboard แสดงภาพรวมและสถิติสำคัญของระบบ POS

### 1.1 โครงสร้างโมดูล

```
Promaxxs.Modules.Dashboard
├── Sales Overview        ─── ภาพรวมยอดขาย
├── Stock Summary         ─── สรุปคลังสินค้า
├── Top Products          ─── สินค้าขายดี
└── Real-time Stats       ─── สถิติแบบ Real-time
```

---

## 2. Test Cases

### 2.1 Sales Overview

#### TC-DB-001: แสดงยอดขายวันนี้
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด Dashboard<br>2. ดูยอดขายวันนี้ |
| **Expected Result** | แสดงยอดขายรวม, จำนวนรายการ, กำไรเบื้องต้น |
| **Priority** | High |

#### TC-DB-002: แสดงยอดขายรายวัน/สัปดาห์/เดือน
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปลี่ยนมุมมองเป็น Daily/Weekly/Monthly |
| **Expected Result** | ข้อมูลเปลี่ยนตามช่วงเวลาที่เลือก |
| **Priority** | High |

#### TC-DB-003: เปรียบเทียบยอดขาย
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปรียบเทียบยอดขายวันนี้ vs เมื่อวาน |
| **Expected Result** | แสดง % การเปลี่ยนแปลง ( tăng/giảm) |
| **Priority** | Medium |

### 2.2 Stock Summary

#### TC-DB-004: แสดงสินค้าใกล้หมด
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ดู Dashboard สินค้าใกล้หมด |
| **Expected Result** | แสดงสินค้าที่ stock ต่ำกว่า Min |
| **Priority** | High |

#### TC-DB-005: แสดงสินค้าหมดอายุ
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ดู Dashboard สินค้าหมดอายุ |
| **Expected Result** | แสดงสินค้าที่ Lot หมดอายุ |
| **Priority** | Medium |

### 2.3 Top Products

#### TC-DB-006: แสดงสินค้าขายดี Top 10
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. ดู Dashboard สินค้าขายดี |
| **Expected Result** | แสดง 10 อันดับสินค้าขายดี |
| **Priority** | Medium |

### 2.4 Real-time Stats

#### TC-DB-007: อัพเดทข้อมูลแบบ Real-time
| รายการ | รายละเอียด |
|--------|------------|
| **Steps** | 1. เปิด Dashboard<br>2. ทำรายการขาย<br>3. ตรวจสอบ Dashboard |
| **Expected Result** | ข้อมูล Dashboard อัพเดทอัตโนมัติ |
| **Priority** | Medium |

---

## 3. Regression Test Checklist

- [ ] Dashboard แสดงข้อมูลถูกต้อง
- [ ] ยอดขายตรงกับธุรกรรมจริง
- [ ] สินค้าใกล้หมดแสดงถูกต้อง
- [ ] สินค้าหมดอายุแสดงถูกต้อง
- [ ] สินค้าขายดีเรียงอันดับถูกต้อง
- [ ] ข้อมูลอัพเดท Real-time
- [ ] รองรับภาษาไทย
