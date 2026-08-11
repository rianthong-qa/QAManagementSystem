import { useEffect, useMemo, useState } from 'react'
import './App.css'
import './Login.css'

type Page = 'dashboard' | 'projects' | 'releases' | 'requirements' | 'rtm' | 'test-cases' | 'test-suites' | 'test-cycles' | 'execution' | 'defects' | 'regression' | 'summary' | 'risks' | 'signoff' | 'users' | 'audit'
type SessionUser = { userId: string; username: string; displayName: string; roles: string[]; permissions: string[] }
const apiUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5038/api/v1'

const nav: { label: string; items: { id: Page; icon: string; label: string }[] }[] = [
  { label: 'ภาพรวม', items: [{ id: 'dashboard', icon: '▦', label: 'Dashboard' }, { id: 'projects', icon: 'P', label: 'Project / Module' }, { id: 'releases', icon: '◫', label: 'Release / Build' }] },
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

type ProjectItem={projectId:string;projectCode:string;projectName:string;description?:string;status:string;isActive:boolean;createdAt:string}
function ProjectsPage({search,refresh}:{search:string;refresh:number}){
  const [items,setItems]=useState<ProjectItem[]>([]),[error,setError]=useState(''),[loading,setLoading]=useState(true)
  useEffect(()=>{const token=localStorage.getItem('qa.accessToken');fetch(`${apiUrl}/projects`,{headers:{Authorization:`Bearer ${token}`}}).then(async r=>{if(!r.ok)throw new Error(r.status===401?'Session หมดอายุ กรุณาเข้าสู่ระบบใหม่':'โหลดข้อมูลโครงการไม่สำเร็จ');return r.json()}).then(setItems).catch(e=>setError(e.message)).finally(()=>setLoading(false))},[refresh])
  if(loading)return <article className="card empty"><p>กำลังโหลดข้อมูลโครงการ...</p></article>
  if(error)return <article className="card empty"><div className="login-error">{error}</div></article>
  const filtered=items.filter(x=>`${x.projectCode} ${x.projectName} ${x.description??''}`.toLowerCase().includes(search.toLowerCase()))
  return <article className="card"><div className="table-tools"><div><select><option>ทุกสถานะ</option></select></div><span>{filtered.length} โครงการ</span></div><div className="table-wrap"><table><thead><tr><th>Project Code</th><th>Project Name</th><th>Description</th><th>Status</th><th>Active</th><th>Created At</th></tr></thead><tbody>{filtered.map(x=><tr key={x.projectId}><td><b>{x.projectCode}</b></td><td>{x.projectName}</td><td>{x.description??'-'}</td><td><Badge tone={x.isActive?'green':'red'}>{x.status}</Badge></td><td>{x.isActive?'ใช้งาน':'ปิดใช้งาน'}</td><td>{new Date(x.createdAt).toLocaleDateString('th-TH')}</td></tr>)}</tbody></table></div></article>
}
type ReleaseItem={releaseId:string;projectId:string;releaseCode:string;version:string;releaseType?:string;plannedReleaseDate?:string;status:string;createdAt:string}
function ReleasesPage({search,refresh}:{search:string;refresh:number}){
 const[items,setItems]=useState<ReleaseItem[]>([]),[error,setError]=useState(''),[loading,setLoading]=useState(true)
 useEffect(()=>{fetch(`${apiUrl}/releases`,{headers:{Authorization:`Bearer ${localStorage.getItem('qa.accessToken')}`}}).then(async r=>{if(!r.ok)throw new Error('โหลดข้อมูล Release ไม่สำเร็จ');return r.json()}).then(setItems).catch(e=>setError(e.message)).finally(()=>setLoading(false))},[refresh])
 if(loading)return <article className="card empty"><p>กำลังโหลดข้อมูล Release...</p></article>;if(error)return <article className="card empty"><div className="login-error">{error}</div></article>
 const filtered=items.filter(x=>`${x.releaseCode} ${x.version} ${x.releaseType??''} ${x.status}`.toLowerCase().includes(search.toLowerCase()))
 return <article className="card"><div className="table-tools"><div><select><option>ทุกสถานะ</option><option>Draft</option><option>Testing</option><option>Ready</option></select></div><span>{filtered.length} Release</span></div><div className="table-wrap"><table><thead><tr><th>Release Code</th><th>Version</th><th>Type</th><th>Planned Date</th><th>Status</th><th>Readiness</th></tr></thead><tbody>{filtered.map(x=><tr key={x.releaseId}><td><b>{x.releaseCode}</b></td><td>{x.version}</td><td>{x.releaseType??'-'}</td><td>{x.plannedReleaseDate?new Date(x.plannedReleaseDate).toLocaleDateString('th-TH'):'-'}</td><td><Badge tone={x.status==='Released'||x.status==='Ready'?'green':x.status==='Cancelled'?'red':'yellow'}>{x.status}</Badge></td><td><Badge tone="blue">Pending Gate</Badge></td></tr>)}</tbody></table></div></article>
}
type RequirementItem={requirementId:string;requirementCode:string;title:string;priority:string;riskLevel?:string;status:string;revisionNo:number;isInScope:boolean;moduleId:string;releaseId?:string}
function RequirementsPage({search,refresh}:{search:string;refresh:number}){
 const[items,setItems]=useState<RequirementItem[]>([]),[error,setError]=useState(''),[loading,setLoading]=useState(true)
 useEffect(()=>{fetch(`${apiUrl}/requirements`,{headers:{Authorization:`Bearer ${localStorage.getItem('qa.accessToken')}`}}).then(async r=>{if(!r.ok)throw new Error('โหลด Requirement ไม่สำเร็จ');return r.json()}).then(setItems).catch(e=>setError(e.message)).finally(()=>setLoading(false))},[refresh])
 if(loading)return <article className="card empty"><p>กำลังโหลด Requirement...</p></article>;if(error)return <article className="card empty"><div className="login-error">{error}</div></article>;const filtered=items.filter(x=>`${x.requirementCode} ${x.title} ${x.priority} ${x.status}`.toLowerCase().includes(search.toLowerCase()))
 return <article className="card"><div className="table-tools"><div><select><option>ทุกสถานะ</option></select><select><option>ทุก Priority</option><option>P0</option><option>P1</option></select></div><span>{filtered.length} Requirements</span></div><div className="table-wrap"><table><thead><tr><th>Requirement ID</th><th>Title</th><th>Priority</th><th>Risk</th><th>Revision</th><th>In Scope</th><th>Status</th></tr></thead><tbody>{filtered.map(x=><tr key={x.requirementId}><td><b>{x.requirementCode}</b></td><td>{x.title}</td><td><Badge tone={x.priority==='P0'||x.priority==='P1'?'red':'blue'}>{x.priority}</Badge></td><td>{x.riskLevel??'-'}</td><td>Rev. {x.revisionNo}</td><td>{x.isInScope?'Yes':'No'}</td><td><Badge tone={x.status==='Approved'||x.status==='Implemented'?'green':'yellow'}>{x.status}</Badge></td></tr>)}</tbody></table></div></article>
}

function Login({ onLogin }: { onLogin: (user: SessionUser) => void }) {
  const [username,setUsername]=useState(''), [password,setPassword]=useState(''), [error,setError]=useState(''), [loading,setLoading]=useState(false)
  const submit=async(e:React.FormEvent)=>{e.preventDefault();setError('');setLoading(true);try{const response=await fetch(`${apiUrl}/auth/login`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username,password})});if(!response.ok)throw new Error('ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง');const data=await response.json();localStorage.setItem('qa.accessToken',data.accessToken);localStorage.setItem('qa.user',JSON.stringify(data.user));onLogin(data.user)}catch(ex){setError(ex instanceof Error?ex.message:'ไม่สามารถเชื่อมต่อระบบได้')}finally{setLoading(false)}}
  return <div className="login-page"><div className="login-visual"><div><div className="login-logo">QA</div><h1>ProMaxx2 QA Hub</h1><p>บริหาร Requirement, Test Execution, Defect และ Release Readiness ในที่เดียว</p></div><small>Quality Assurance Management System</small></div><form className="login-card" onSubmit={submit}><div className="mobile-brand"><div className="login-logo">QA</div><b>ProMaxx2 QA Hub</b></div><span className="eyebrow">WELCOME BACK</span><h2>เข้าสู่ระบบ</h2><p>กรอกบัญชีผู้ใช้งานเพื่อเข้าสู่ QA Workspace</p>{error&&<div className="login-error">{error}</div>}<label>ชื่อผู้ใช้<input autoFocus autoComplete="username" value={username} onChange={e=>setUsername(e.target.value)} required placeholder="Username"/></label><label>รหัสผ่าน<input type="password" autoComplete="current-password" value={password} onChange={e=>setPassword(e.target.value)} required placeholder="Password"/></label><label className="remember"><input type="checkbox"/> จดจำการเข้าสู่ระบบ</label><button className="btn primary login-button" disabled={loading}>{loading?'กำลังเข้าสู่ระบบ...':'เข้าสู่ระบบ'}</button><small>หากไม่สามารถเข้าสู่ระบบได้ กรุณาติดต่อ System Administrator</small></form></div>
}

function App() {
  const [page, setPage] = useState<Page>('dashboard'), [menu, setMenu] = useState(false), [search, setSearch] = useState(''), [modal, setModal] = useState(false)
  const [user,setUser]=useState<SessionUser|null>(()=>{try{const value=localStorage.getItem('qa.user');return value?JSON.parse(value):null}catch{return null}})
  const [code,setCode]=useState(''),[name,setName]=useState(''),[details,setDetails]=useState(''),[refresh,setRefresh]=useState(0),[saving,setSaving]=useState(false)
  const description = useMemo(() => page === 'dashboard' ? 'สถานะคุณภาพและความพร้อม Release แบบรวมศูนย์' : `จัดการข้อมูล ${pageNames[page]} ของ Release ปัจจุบัน`, [page])
  const go = (id: Page) => { setPage(id); setMenu(false); window.history.replaceState(null,'',`#/${id}`) }
  const logout=()=>{localStorage.removeItem('qa.accessToken');localStorage.removeItem('qa.user');setUser(null)}
  const save=async()=>{if(!['projects','releases','requirements'].includes(page)){setModal(false);return}setSaving(true);try{const headers={'Content-Type':'application/json',Authorization:`Bearer ${localStorage.getItem('qa.accessToken')}`};let url=`${apiUrl}/projects`,body:object={projectCode:code,projectName:name,description:details||null,ownerUserId:null};if(page==='releases'||page==='requirements'){const projectsResponse=await fetch(`${apiUrl}/projects`,{headers});const projects:ProjectItem[]=await projectsResponse.json();if(!projects.length)throw new Error('กรุณาสร้าง Project ก่อน');if(page==='releases'){url=`${apiUrl}/projects/${projects[0].projectId}/releases`;body={releaseCode:code,version:name,releaseType:'Major',plannedReleaseDate:null,scope:details||null,releaseOwnerUserId:user?.userId??null}}else{const[modules,releases]=await Promise.all([fetch(`${apiUrl}/projects/${projects[0].projectId}/modules`,{headers}).then(r=>r.json()),fetch(`${apiUrl}/projects/${projects[0].projectId}/releases`,{headers}).then(r=>r.json())]);if(!modules.length)throw new Error('กรุณาสร้าง Module ก่อน');url=`${apiUrl}/requirements`;body={projectId:projects[0].projectId,releaseId:releases[0]?.releaseId??null,moduleId:modules[0].moduleId,requirementCode:code,title:name,description:details||null,acceptanceCriteria:null,priority:'P1',riskLevel:'High',source:'Manual',ownerUserId:user?.userId??null,isInScope:true}}}const response=await fetch(url,{method:'POST',headers,body:JSON.stringify(body)});if(!response.ok){const problem=await response.json();throw new Error(problem.detail??'บันทึกข้อมูลไม่สำเร็จ')}setModal(false);setCode('');setName('');setDetails('');setRefresh(x=>x+1)}catch(e){window.alert(e instanceof Error?e.message:'บันทึกไม่สำเร็จ')}finally{setSaving(false)}}
  if(!user)return <Login onLogin={setUser}/>
  return <div className="app">
    <aside className={menu?'sidebar open':'sidebar'}><div className="brand"><div className="logo">QA</div><div><b>ProMaxx2 QA Hub</b><small>Quality Assurance Management</small></div></div>{nav.map(g=><div className="nav-group" key={g.label}><p>{g.label}</p>{g.items.map(i=><button key={i.id} className={page===i.id?'active':''} onClick={()=>go(i.id)}><i>{i.icon}</i>{i.label}</button>)}</div>)}</aside>
    <main><header className="topbar"><button className="menu-btn" onClick={()=>setMenu(v=>!v)}>☰</button><div className="context"><select><option>ProMaxx2</option></select><select><option>Release 2026.08</option><option>Release 2026.09</option></select><select><option>Build 10.0.228 RC2</option></select></div><div className="profile"><Badge tone="yellow">2 Blockers</Badge><span className="bell">●</span><div className="avatar">{user.displayName.slice(0,2).toUpperCase()}</div><div><b>{user.displayName}</b><button className="logout" onClick={logout}>ออกจากระบบ</button></div></div></header>
      <div className="content"><div className="page-head"><div><h1>{pageNames[page]}</h1><p>{description}</p></div><div className="actions"><label className="search">⌕<input value={search} onChange={e=>setSearch(e.target.value)} placeholder="ค้นหา..."/></label><button className="btn">Export</button><button className="btn primary" onClick={()=>setModal(true)}>+ สร้างรายการ</button></div></div>{page==='dashboard'?<Dashboard/>:page==='projects'?<ProjectsPage search={search} refresh={refresh}/>:page==='releases'?<ReleasesPage search={search} refresh={refresh}/>:page==='requirements'?<RequirementsPage search={search} refresh={refresh}/>:<DataPage page={page} search={search}/>}</div>
    </main>
    {modal&&<div className="modal" onMouseDown={()=>setModal(false)}><div className="modal-box" onMouseDown={e=>e.stopPropagation()}><div className="modal-head"><h2>สร้าง {pageNames[page]}</h2><button onClick={()=>setModal(false)}>×</button></div><div className="form-grid"><label>รหัส<input value={code} onChange={e=>setCode(e.target.value)} placeholder="ระบุรหัส" required/></label><label>ชื่อรายการ<input value={name} onChange={e=>setName(e.target.value)} placeholder="ระบุชื่อ" required/></label><label className="full">รายละเอียด<textarea value={details} onChange={e=>setDetails(e.target.value)} rows={4}/></label></div><div className="modal-actions"><button className="btn" onClick={()=>setModal(false)}>ยกเลิก</button><button className="btn primary" disabled={saving||!code.trim()||!name.trim()} onClick={save}>{saving?'กำลังบันทึก...':'บันทึก'}</button></div></div></div>}
  </div>
}

export default App
