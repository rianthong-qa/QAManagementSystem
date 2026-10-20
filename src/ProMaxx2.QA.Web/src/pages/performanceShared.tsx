// component ที่หน้า ผลงานและการประเมิน และส่วนการประเมิน (PerformanceEvaluations) ใช้ร่วมกัน

export function MetricGroup({ icon, title, items }: { icon: string; title: string; items: [string, number | string][] }) {
  return (
    <article className="content-card perf-metric-group" data-perf-category={icon}>
      <h3><span className="material-symbols-outlined" aria-hidden="true">{icon}</span>{title}</h3>
      <dl>
        {items.map(([label, value]) => (
          <div key={label}><dt>{label}</dt><dd>{typeof value === "number" ? value.toLocaleString("th-TH") : value}</dd></div>
        ))}
      </dl>
    </article>
  );
}
