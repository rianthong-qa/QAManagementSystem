type MyWorkDetail = {
  testCaseCode: string;
  title: string;
  priority?: string;
  currentStatus: string;
  objective?: string;
  preconditions?: string;
  steps?: { stepNo: number; action: string; testData?: string; expectedResult: string }[];
};

export function MyWorkDetailModal({ detail, onClose, onRun }: { detail: MyWorkDetail; onClose: () => void; onRun?: () => void }) {
  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="my-work-detail-title" onMouseDown={onClose}>
    <div className="modal-box testcase-detail" onMouseDown={(event) => event.stopPropagation()}>
      <div className="modal-head"><h2 id="my-work-detail-title">{detail.testCaseCode}</h2><button type="button" onClick={onClose} aria-label="ปิด">×</button></div>
      <h3>{detail.title}</h3>
      <p>{detail.objective || "ยังไม่มี Objective"}</p>
      {detail.preconditions && <p><b>Preconditions:</b> {detail.preconditions}</p>}
      <h3>Test Steps ({detail.steps?.length ?? 0})</h3>
      <ol>{(detail.steps ?? []).map((step) => <li key={step.stepNo}><b>{step.action}</b>{step.testData && <small> · {step.testData}</small>}<div>{step.expectedResult}</div></li>)}</ol>
      <div className="modal-actions"><button type="button" className="btn" onClick={onClose}>ปิด</button>{onRun && <button type="button" className="btn primary" onClick={onRun}>Run Test Case</button>}</div>
    </div>
  </div>;
}
