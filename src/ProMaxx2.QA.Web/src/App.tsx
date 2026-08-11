import { useMemo, useState } from 'react'
import './App.css'

type Page = 'dashboard' | 'releases' | 'requirements' | 'rtm' | 'test-cases' | 'test-suites' | 'test-cycles' | 'execution' | 'defects' | 'regression' | 'summary' | 'risks' | 'signoff' | 'users' | 'audit'

const nav: { label: string; items: { id: Page; icon: string; label: string }[] }[] = [
  { label: 'ภาพรวม', items: [{ id: 'dashboard', icon: '▦', label: 'Dashboard' }, { id: 'releases', icon: '◫', label: 'Release / Build' }] },
  { label: 'REQUIREMENT & TEST DESIGN', items: [{ id: 'requirements', icon: 'R', label: 'Requirement' }, { id: 'rtm', icon: '⇄', label: 'RTM' }, { id: 'test-cases', icon: 'TC', label: 'Test Case' }, { id: 'test-suites', icon: '▤', label: 'Test Suite' }] },
  { label: 'TEST EXECUTION', items: [{ id: 'test-cycles', icon: '◎', label: 'Test Cycle' }, { id: 'execution', icon: '▶', label: 'Execution Workspace' }, { id: 'defects', icon: '!', label: 'Defect' }, { id: 'regression', icon: '↻', label: 'Regression' }] },
  { label: 'RELEASE GOVERNANCE', items: [{ id: 'summary', icon: 'Σ', label: 'Test Summary' }, { id: 'risks', icon: '⚠', label: 'Risk Acceptance' }, { id: 'signoff', icon: '✓', label: 'Release Sign-off' }] },
  { label: 'ADMINISTRATION', items: [{ id: 'users', icon: 'U', label: 'User / Role' }, { id: 'audit', icon: '⌕', label: 'Audit Log' }] },
]

const pageNames: Record<Page, string> = Object.fromEntries(nav.flatMap(g => g.items.map(i => [i.id, i.label]))) as Record<Page, string>

const releases = [
  ['REL-2026.08', '10.0.228', 'Major', '28 ส.ค. 2026', 'Testing', 'สมชาย ใจดี', 'Conditional'],
  ['REL-2026.09', '10.0.240', 'Minor', '25 ก.ย. 2026', 'Planning', 'วิภา แสงทอง', 'Pending'],
  ['HOTFIX-226', '10.0.226.1', 'Hotfix', '12 ส.ค. 2026', 'Ready', 'สมชาย ใจดี', 'Go'],
]
const requirements = [
  ['REQ-SALE-142', 'รองรับส่วนลดหลายระดับ', 'Sales / POS', 'P0', '100%', 'Passed', 'Ready'],
  ['REQ-STK-088', 'ปรับยอดสต็อกแบบ Real-time', 'Stock', 'P1', '80%', 'Failed', 'Testing'],
  ['REQ-RPT-071', 'ส่งออกรายงาน PDF/Excel', 'Report', 'P1', '50%', 'Blocked', 'Testing'],
  ['REQ-UPD-031', 'Auto update ผ่าน Velopack', 'Update', 'P0', '100%', 'Passed', 'Ready'],
]
const defects = [
  ['DEF-1042', 'ยอดคงเหลือไม่อัปเดตหลัง Void', 'Stock', 'P1', 'Open', '10.0.228 RC2', 'กิตติ'],
  ['DEF-1038', 'PDF ภาษาไทยตัดคำผิด', 'Report', 'P1', 'Ready for Retest', '10.0.227 RC1', 'ณัฐพล'],
  ['DEF-1021', 'Token หมดอายุเร็วกว่ากำหนด', 'Authentication', 'P2', 'Resolved', '10.0.226', 'กิตติ'],
]

function Badge({ children, tone = 'blue' }: { children: React.ReactNode; tone?: string }) { return <span className={`badge ${tone}`}>{children}</span> }

function Dashboard() {
  return <>
    <div className="kpi-grid">
      {[['Requirement Coverage','94%','188 / 200 Covered','green'],['Execution','82%','984 / 1,200 Cases','blue'],['Pass Rate','91.7%','902 Passed','green'],['Release Blocker','2','P0 0 · P1 2','red']].map(x => <article className="card kpi" key={x[0]}><span>{x[0]}</span><strong>{x[1]}</strong><small className={x[3]}>{x[2]}</small></article>)}
    </div>
    <div className="dashboard-grid">
      <article className="card"><h3>Module Health</h3><div className="table-wrap"><table><thead><tr><th>Module</th><th>Coverage</th><th>Pass</th><th>Defect</th><th>Status</th></tr></thead><tbody>
        {[['Authentication','100%','98%','1','Healthy','green'],['Sales / POS','98%','93%','5','Watch','yellow'],['Stock','95%','89%','8','Watch','yellow'],['Report / Export','92%','87%','4','Risk','red'],['Velopack Update','100%','100%','0','Healthy','green']].map(r => <tr key={r[0]}>{r.slice(0,4).map(c=><td key={c}>{c}</td>)}<td><Badge tone={r[5]}>{r[4]}</Badge></td></tr>)}
      </tbody></table></div></article>
      <article className="card"><h3>Release Gate</h3><div className="gate">
        {[['Smoke Test','100% Pass','green'],['Critical Regression','88%','yellow'],['Open P0','0','green'],['P1 Blocker','2','red'],['Update Test','Passed','green']].map(g=><div className="gate-row" key={g[0]}><span>{g[0]}</span><Badge tone={g[2]}>{g[1]}</Badge></div>)}
      </div><div className="callout">Recommended Decision: <b>CONDITIONAL GO</b></div></article>
    </div>
  </>
}

function DataPage({ page, search }: { page: Page; search: string }) {
  let headers: string[] = [], rows: string[][] = []
  if (page === 'releases') { headers=['Release Code','Version','Type','Planned Date','Status','Owner','Readiness']; rows=releases }
  else if (page === 'requirements' || page === 'rtm') { headers=['Requirement','Title','Module','Priority','Coverage','Latest Result','Status']; rows=requirements }
  else if (page === 'defects') { headers=['Defect ID','Title','Module','Severity','Status','Build Found','Assignee']; rows=defects }
  else if (page === 'test-cases') { headers=['Test Case ID','Title','Module','Priority','Type','Revision','Last Result']; rows=[['TC-SALE-201','ใช้ส่วนลดสมาชิกและคูปอง','Sales / POS','P0','Functional','4','Passed'],['TC-STK-114','Void แล้วคืนยอดสต็อก','Stock','P1','Regression','2','Failed'],['TC-RPT-089','Export PDF ภาษาไทย','Report','P1','Functional','3','Blocked']] }
  else if (page === 'test-cycles') { headers=['Cycle Code','Name','Release','Build','Type','Progress','Status']; rows=[['CYC-2608-RC2','Full Regression RC2','REL-2026.08','10.0.228 RC2','Regression','82%','In Progress'],['CYC-2608-SMK','Smoke RC2','REL-2026.08','10.0.228 RC2','Smoke','100%','Closed']] }
  else { return <EmptyPage page={page}/> }
  const filtered = rows.filter(r => r.join(' ').toLowerCase().includes(search.toLowerCase()))
  return <article className="card"><div className="table-tools"><div><select aria-label="สถานะ"><option>ทุกสถานะ</option></select><select aria-label="โมดูล"><option>ทุกโมดูล</option></select></div><span>{filtered.length} รายการ</span></div><div className="table-wrap"><table><thead><tr>{headers.map(h=><th key={h}>{h}</th>)}</tr></thead><tbody>{filtered.map(r=><tr key={r[0]}>{r.map((c,i)=><td key={`${r[0]}-${i}`}>{['P0','P1','Failed','Blocked','Open','Conditional'].includes(c)?<Badge tone={['P0','P1','Failed','Open'].includes(c)?'red':'yellow'}>{c}</Badge>:['Passed','Go','Ready','Resolved','Closed'].includes(c)?<Badge tone="green">{c}</Badge>:c}</td>)}</tr>)}</tbody></table></div></article>
}

function EmptyPage({ page }: { page: Page }) { return <article className="card empty"><div className="empty-icon">{nav.flatMap(n=>n.items).find(i=>i.id===page)?.icon}</div><h3>{pageNames[page]}</h3><p>โมดูลนี้เตรียมไว้ตาม Screen Specification และพร้อมเชื่อมต่อข้อมูลใน vertical slice ถัดไป</p><button className="btn primary">เริ่มสร้างรายการ</button></article> }

function App() {
  const [page, setPage] = useState<Page>('dashboard'), [menu, setMenu] = useState(false), [search, setSearch] = useState(''), [modal, setModal] = useState(false)
  const description = useMemo(() => page === 'dashboard' ? 'สถานะคุณภาพและความพร้อม Release แบบรวมศูนย์' : `จัดการข้อมูล ${pageNames[page]} ของ Release ปัจจุบัน`, [page])
  const go = (id: Page) => { setPage(id); setMenu(false); window.history.replaceState(null,'',`#/${id}`) }
  return <div className="app">
    <aside className={menu?'sidebar open':'sidebar'}><div className="brand"><div className="logo">QA</div><div><b>ProMaxx2 QA Hub</b><small>Quality Assurance Management</small></div></div>{nav.map(g=><div className="nav-group" key={g.label}><p>{g.label}</p>{g.items.map(i=><button key={i.id} className={page===i.id?'active':''} onClick={()=>go(i.id)}><i>{i.icon}</i>{i.label}</button>)}</div>)}</aside>
    <main><header className="topbar"><button className="menu-btn" onClick={()=>setMenu(v=>!v)}>☰</button><div className="context"><select><option>ProMaxx2</option></select><select><option>Release 2026.08</option><option>Release 2026.09</option></select><select><option>Build 10.0.228 RC2</option></select></div><div className="profile"><Badge tone="yellow">2 Blockers</Badge><span className="bell">●</span><div className="avatar">QA</div><div><b>QA Lead</b><small>Administrator</small></div></div></header>
      <div className="content"><div className="page-head"><div><h1>{pageNames[page]}</h1><p>{description}</p></div><div className="actions"><label className="search">⌕<input value={search} onChange={e=>setSearch(e.target.value)} placeholder="ค้นหา..."/></label><button className="btn">Export</button><button className="btn primary" onClick={()=>setModal(true)}>+ สร้างรายการ</button></div></div>{page==='dashboard'?<Dashboard/>:<DataPage page={page} search={search}/>}</div>
    </main>
    {modal&&<div className="modal" onMouseDown={()=>setModal(false)}><div className="modal-box" onMouseDown={e=>e.stopPropagation()}><div className="modal-head"><h2>สร้าง {pageNames[page]}</h2><button onClick={()=>setModal(false)}>×</button></div><div className="form-grid"><label>รหัส<input placeholder="ระบุรหัส"/></label><label>ชื่อรายการ<input placeholder="ระบุชื่อ"/></label><label className="full">รายละเอียด<textarea rows={4}/></label></div><div className="modal-actions"><button className="btn" onClick={()=>setModal(false)}>ยกเลิก</button><button className="btn primary" onClick={()=>setModal(false)}>บันทึก</button></div></div></div>}
  </div>
}

export default App
