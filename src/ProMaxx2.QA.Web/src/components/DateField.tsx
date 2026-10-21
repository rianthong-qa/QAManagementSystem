import "./DateField.css";

// ค่า yyyy-mm-dd แสดงเป็น วัน/เดือน/ปี พ.ศ. — รูปแบบที่ <input type="date"> แสดงเองขึ้นกับ locale ของเบราว์เซอร์ (เช่น ด/ว/ค.ศ.)
const dateFieldLabel = (value: string) => {
  const [year, month, day] = value.split("-");
  return year && month && day ? `${day}/${month}/${Number(year) + 543}` : "";
};

type DateFieldProps = {
  value: string;
  onChange: (value: string) => void;
  min?: string;
  max?: string;
  disabled?: boolean;
  invalid?: boolean;
  /** ใช้เมื่อช่องไม่ได้อยู่ใน <label> */
  ariaLabel?: string;
  /** ล้างค่าได้ (ปุ่ม Clear ของปฏิทิน) — ปิดไว้สำหรับช่องที่ต้องมีค่าเสมอ เช่น ช่วงวันที่ */
  allowEmpty?: boolean;
};

/**
 * ช่องวันที่ที่แสดง วัน/เดือน/ปี พ.ศ. เสมอ: ข้อความที่จัดรูปแบบเองอยู่ใต้ native input โปร่งใสเต็มช่อง
 * (คลิกเรียก showPicker(), ใช้คีย์บอร์ด/ปฏิทินของเบราว์เซอร์ได้ตามเดิม) — pattern เดียวกับ CrmDateField
 */
export function DateField({ value, onChange, min, max, disabled, invalid, ariaLabel, allowEmpty }: DateFieldProps) {
  return (
    <span className="date-field" data-invalid={invalid || undefined}>
      <span className={value ? "date-field-text" : "date-field-text placeholder"}>{value ? dateFieldLabel(value) : "วว/ดด/ปปปป"}</span>
      <span className="material-symbols-outlined" aria-hidden="true">calendar_month</span>
      <input
        type="date"
        className="date-field-native"
        value={value}
        min={min}
        max={max}
        disabled={disabled}
        aria-invalid={invalid || undefined}
        aria-label={ariaLabel}
        onClick={(event) => { try { event.currentTarget.showPicker(); } catch { /* เบราว์เซอร์ที่ไม่รองรับ showPicker ใช้การพิมพ์/ปุ่มปฏิทินเดิม */ } }}
        onChange={(event) => { if (event.target.value || allowEmpty) onChange(event.target.value); }}
      />
    </span>
  );
}
