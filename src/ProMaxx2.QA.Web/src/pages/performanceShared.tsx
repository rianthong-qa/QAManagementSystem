// component ที่หน้า ผลงานและการประเมิน และส่วนการประเมิน (PerformanceEvaluations) ใช้ร่วมกัน

/** กล่องผลงานหนึ่งด้าน — ค่าแรกของ items เป็นตัวเลขหลักแสดงตัวใหญ่ ที่เหลือเป็นรายการรอง */
export function MetricGroup({ icon, title, items }: { icon: string; title: string; items: [string, number | string][] }) {
  const fmt = (value: number | string) => (typeof value === "number" ? value.toLocaleString("th-TH") : value);
  const [[headLabel, headValue], ...rest] = items;
  return (
    <article className="content-card perf-metric-group" data-perf-category={icon}>
      <h3><span className="material-symbols-outlined" aria-hidden="true">{icon}</span>{title}</h3>
      <p className="perf-metric-head"><b>{fmt(headValue)}</b><span>{headLabel}</span></p>
      {rest.length > 0 && (
        <dl>
          {rest.map(([label, value]) => (
            <div key={label}><dt>{label}</dt><dd>{fmt(value)}</dd></div>
          ))}
        </dl>
      )}
    </article>
  );
}
