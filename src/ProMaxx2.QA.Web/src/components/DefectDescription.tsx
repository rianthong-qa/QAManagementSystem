/** รายละเอียด Defect แบบอ่านง่าย — ข้อความที่ระบบสร้าง (เช่น Defect จาก Execution) เป็นบรรทัด "ป้าย: ค่า" ต่อกัน
 * แยกเป็น 3 ส่วน: ค่าสั้นเป็นตารางข้อมูลย่อ (grid) → ค่ายาวเป็นแถว ป้าย | ข้อความ → หัวข้อที่ตามด้วยบรรทัด "- …" เป็นรายการ
 * (เช่น "ขั้นตอนที่ Fail") — ข้อความที่ผู้ใช้พิมพ์เองซึ่งไม่มีรูปแบบนี้แสดงเป็นย่อหน้าตามเดิม */
type Pair = { label: string; value: string };
type Block = { kind: "field"; pair: Pair } | { kind: "list"; label: string | null; items: string[] } | { kind: "text"; text: string };

const shortValueMax = 48;
const labeledLine = /^([^:/]{1,40}):\s*(.*)$/; // ป้ายไม่มี ":" หรือ "/" — กันเวลา "11:00" และ URL ถูกตัดเป็นป้าย
const bulletLine = /^[-•]\s*(.+)$/;
const stepItem = /^((?:ขั้นที่|Step)\s*\d+)\s*:\s*(.*)$/i;

function parseDescription(text: string, hide: string[]) {
  const facts: Pair[] = [];
  const blocks: Block[] = [];
  const hidden = new Set(hide.map((x) => x.trim()).filter(Boolean));
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) continue;
    const bullet = line.match(bulletLine);
    if (bullet) {
      const last = blocks[blocks.length - 1];
      if (last?.kind === "list") last.items.push(bullet[1]);
      else blocks.push({ kind: "list", label: null, items: [bullet[1]] });
      continue;
    }
    const labeled = line.match(labeledLine);
    if (!labeled) { blocks.push({ kind: "text", text: line }); continue; }
    if (!labeled[2]) { blocks.push({ kind: "list", label: labeled[1].trim(), items: [] }); continue; }
    // "Priority: P1 | ประเภท Test Case: Functional" = หลายค่าในบรรทัดเดียว
    const pairs: Pair[] = [{ label: labeled[1].trim(), value: "" }];
    for (const [index, part] of labeled[2].split(" | ").entries()) {
      const inner = index > 0 ? part.match(labeledLine) : null;
      if (inner) pairs.push({ label: inner[1].trim(), value: inner[2].trim() });
      else pairs[pairs.length - 1].value += (pairs[pairs.length - 1].value ? " | " : "") + part.trim();
    }
    for (const pair of pairs) {
      if (hidden.has(pair.value)) continue; // ซ้ำกับกล่องผลจริง/ผลที่คาดหวังด้านล่าง
      if (pair.value.length <= shortValueMax) facts.push(pair);
      else blocks.push({ kind: "field", pair });
    }
  }
  return { facts, blocks: blocks.filter((b) => b.kind !== "list" || b.items.length > 0) };
}

function ListItem({ text }: { text: string }) {
  const step = text.match(stepItem);
  const [main, ...rest] = (step ? step[2] : text).split(" — ");
  return (
    <li>
      {step && <span className="dj-desc-step">{step[1]}</span>}
      <div>
        <p>{main || "-"}</p>
        {rest.map((part, i) => {
          const labeled = part.match(labeledLine);
          return <p key={i} className="dj-desc-sub">{labeled ? <><b>{labeled[1]}:</b> {labeled[2]}</> : part}</p>;
        })}
      </div>
    </li>
  );
}

export function DefectDescription({ text, hide = [] }: { text: string; hide?: string[] }) {
  const { facts, blocks } = parseDescription(text, hide);
  return (
    <div className="dj-desc">
      {facts.length > 0 && (
        <dl className="dj-desc-facts">
          {facts.map((f, i) => <div key={`${i}-${f.label}`}><dt>{f.label}</dt><dd>{f.value}</dd></div>)}
        </dl>
      )}
      {blocks.map((b, i) => {
        if (b.kind === "field") return <div key={i} className="dj-desc-field"><span className="dj-desc-label">{b.pair.label}</span><p>{b.pair.value}</p></div>;
        if (b.kind === "text") return <p key={i} className="dj-desc-text">{b.text}</p>;
        return (
          <div key={i} className="dj-desc-field">
            {b.label && <span className="dj-desc-label">{b.label}</span>}
            <ul className="dj-desc-list">{b.items.map((item, j) => <ListItem key={j} text={item} />)}</ul>
          </div>
        );
      })}
    </div>
  );
}
