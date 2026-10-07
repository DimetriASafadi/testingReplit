// Articulated construction-worker rig. Each frame is posed from joint angles (hip/knee/shoulder/elbow),
// and the body is lowered so the lowest foot always touches the ground line.
export const WK_W = 56, WK_H = 80, WK_FOOT = 72;
export type WorkerRole = 'walk' | 'carry' | 'hammer';
export const WK_FRAMES = 8;
const SKIN = ['#b98862', '#8f6240', '#d1a37b'], SHIRT = ['#3f5f73', '#7a6a4a', '#8c4a3a'], VEST = ['#e0762a', '#d9b42a', '#e0762a'], HELM = ['#e8b020', '#e9e4d6', '#d95a24'];

export function drawWorker(ctx: CanvasRenderingContext2D, role: WorkerRole, frame: number, variant: number) {
  const v = variant % 3; const p = (frame / WK_FRAMES) * Math.PI * 2;
  const ph = role === 'hammer' ? 0 : p;
  const L1 = 15, L2 = 15, A1 = 12, A2 = 11;
  const leg = (s: number) => { // s=+1 or -1 phase offset
    const a = role === 'hammer' ? s * 0.18 : Math.sin(ph + (s > 0 ? 0 : Math.PI)) * 0.62;
    const lift = role === 'hammer' ? 0.08 : Math.max(0, Math.cos(ph + (s > 0 ? 0 : Math.PI))) ;
    const bend = role === 'hammer' ? 0.12 : 0.15 + (a < 0 ? 0 : 0) + lift * 0.9 * (Math.sin(ph + (s > 0 ? 0 : Math.PI)) < 0.2 ? 1 : 0.2);
    return { a, bend };
  };
  const lean = role === 'carry' ? 0.1 : role === 'hammer' ? 0.2 : 0.06;
  const bob = role === 'hammer' ? 0 : Math.abs(Math.sin(ph)) * 1.5;
  const hipX = 28, torso = 25;
  const legs = [leg(1), leg(-1)].map(l => {
    const kx = Math.sin(l.a) * L1, ky = Math.cos(l.a) * L1; const a2 = l.a - l.bend;
    return { kx, ky, fx: kx + Math.sin(a2) * L2, fy: ky + Math.cos(a2) * L2 };
  });
  const hipY = WK_FOOT - Math.max(legs[0].fy, legs[1].fy) - bob * 0 ; // lowest foot touches ground
  const neck = [hipX + Math.sin(lean) * torso, hipY - Math.cos(lean) * torso];
  ctx.clearRect(0, 0, WK_W, WK_H);
  ctx.fillStyle = 'rgba(30,22,14,.32)'; ctx.beginPath(); ctx.ellipse(hipX + 2, WK_FOOT + 1, 15, 4.2, 0, 0, Math.PI * 2); ctx.fill();
  const limb = (pts: number[][], w: number, col: string, hi: string) => {
    ctx.lineCap = 'round'; ctx.lineJoin = 'round'; ctx.strokeStyle = 'rgba(25,18,12,.85)'; ctx.lineWidth = w + 1.6; ctx.beginPath(); pts.forEach(([x, y], i) => i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)); ctx.stroke();
    ctx.strokeStyle = col; ctx.lineWidth = w; ctx.stroke(); ctx.strokeStyle = hi; ctx.lineWidth = Math.max(1, w * 0.28); ctx.globalAlpha = 0.5; ctx.translate(-w * 0.18, -w * 0.18); ctx.stroke(); ctx.translate(w * 0.18, w * 0.18); ctx.globalAlpha = 1;
  };
  const drawLeg = (l: typeof legs[number], far: boolean) => {
    const c = far ? '#2f3a46' : '#394756'; limb([[hipX, hipY], [hipX + l.kx, hipY + l.ky], [hipX + l.fx, hipY + l.fy]], 6.2, c, '#6b7c8e');
    ctx.fillStyle = far ? '#2a211b' : '#3a2c22'; ctx.beginPath(); ctx.ellipse(hipX + l.fx + 2.4, hipY + l.fy - 1, 5.6, 3, 0, 0, Math.PI * 2); ctx.fill();
  };
  const arm = (far: boolean) => {
    const sh = [neck[0] - Math.sin(lean) * 3, neck[1] + 3];
    let a: number, b: number;
    if (role === 'walk') { const s = Math.sin(ph + (far ? 0 : Math.PI)); a = s * 0.7; b = a + 0.35 + (s > 0 ? 0.35 : 0); }
    else if (role === 'carry') { a = 0.95; b = 1.9; }
    else { const k = Math.sin(p); const strike = far ? 0.3 : (k > 0 ? -2.3 + k * 0.1 : 1.5 + k * 1.1); a = far ? 0.9 : strike; b = far ? 1.6 : a + 0.5; }
    const e = [sh[0] + Math.sin(a) * A1, sh[1] + Math.cos(a) * A1]; const h = [e[0] + Math.sin(b) * A2, e[1] + Math.cos(b) * A2];
    limb([sh, e, h], 5, SHIRT[v], '#aab7c2'); ctx.fillStyle = SKIN[v]; ctx.beginPath(); ctx.arc(h[0], h[1], 2.8, 0, Math.PI * 2); ctx.fill();
    if (role === 'hammer' && !far) { ctx.strokeStyle = '#4a3a2a'; ctx.lineWidth = 2.2; const ex = h[0] + Math.sin(b) * 9, ey = h[1] + Math.cos(b) * 9; ctx.beginPath(); ctx.moveTo(h[0], h[1]); ctx.lineTo(ex, ey); ctx.stroke(); ctx.fillStyle = '#5b5e63'; ctx.fillRect(ex - 4, ey - 2.5, 8, 5); }
    return h;
  };
  drawLeg(legs[1], true); arm(true);
  // torso with vest
  ctx.save(); ctx.translate(hipX, hipY); ctx.rotate(lean);
  const g = ctx.createLinearGradient(-6, 0, 7, 0); g.addColorStop(0, '#2e2a26'); g.addColorStop(0, SHIRT[v]); g.addColorStop(1, '#1e2a33');
  ctx.fillStyle = g; ctx.strokeStyle = 'rgba(25,18,12,.85)'; ctx.lineWidth = 1.4; ctx.beginPath(); ctx.roundRect(-6.5, -torso, 13, torso + 2, 4); ctx.fill(); ctx.stroke();
  ctx.fillStyle = VEST[v]; ctx.fillRect(-6, -torso + 2, 12, torso - 7); ctx.fillStyle = 'rgba(235,235,215,.85)'; ctx.fillRect(-6, -torso + 10, 12, 2.2); ctx.fillRect(-6, -torso + 17, 12, 2.2);
  ctx.fillStyle = '#4a3a2a'; ctx.fillRect(-6.5, -4, 13, 2.4);
  ctx.restore();
  drawLeg(legs[0], false);
  // head, helmet
  ctx.fillStyle = SKIN[v]; ctx.strokeStyle = 'rgba(25,18,12,.8)'; ctx.lineWidth = 1.2; ctx.beginPath(); ctx.arc(neck[0] + 2, neck[1] - 5, 5.2, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
  ctx.fillStyle = HELM[v]; ctx.beginPath(); ctx.arc(neck[0] + 1.6, neck[1] - 6.6, 5.9, Math.PI, 0); ctx.closePath(); ctx.fill(); ctx.stroke();
  ctx.fillStyle = 'rgba(255,255,255,.35)'; ctx.beginPath(); ctx.arc(neck[0] - 0.4, neck[1] - 9, 2.4, Math.PI, Math.PI * 1.7); ctx.fill();
  ctx.fillStyle = HELM[v]; ctx.fillRect(neck[0] + 1, neck[1] - 6.8, 9, 1.8); ctx.strokeRect(neck[0] + 1, neck[1] - 6.8, 9, 1.8);
  const h = arm(false);
  if (role === 'carry') { // concrete block held at the chest
    ctx.fillStyle = '#9a958a'; ctx.strokeStyle = 'rgba(25,18,12,.8)'; ctx.lineWidth = 1.2; ctx.beginPath(); ctx.roundRect(h[0] - 8, h[1] - 8, 17, 10, 1.5); ctx.fill(); ctx.stroke();
    ctx.fillStyle = 'rgba(255,255,255,.28)'; ctx.fillRect(h[0] - 7, h[1] - 7, 15, 2.5); ctx.fillStyle = '#6f6a62'; ctx.fillRect(h[0] - 3, h[1] - 4, 3, 4); ctx.fillRect(h[0] + 3, h[1] - 4, 3, 4);
  }
}
