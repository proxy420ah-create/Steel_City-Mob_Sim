/* construct/emit_worker.js — emitVox's scan+fill+column pack, off the main thread.
   Dual-loaded: as a Worker (new Worker('construct/emit_worker.js')) and as a
   classic <script> in construct.html (window.emitScan = the same math, inline
   fallback when Worker is unavailable). All I/O is transferable typed arrays —
   no THREE, no DOM, no closures over page state. The port of 03_tables.js's
   emitVox loops is verbatim: same surf/under semantics, same partition law,
   same flat-plate skip — the only difference is who runs it. */
const emitScan = d => {
  const { vox, W, H, D, plate, grid, vs, mirX, zflip, pal, dflt, glassLUT, maxPart, maxParts } = d;
  const WH = W * H;
  const surf = (x, y, z, b) =>                                           // direct row-index reads with edge guards —
    !((x > 0 ? vox[b - 1] : 0) && (x < W - 1 ? vox[b + 1] : 0) &&        // OOB reads 0, same contract as main
      (y > 0 ? vox[b - W] : 0) && (y < H - 1 ? vox[b + W] : 0) &&
      (z > 0 ? vox[b - WH] : 0) && (z < D - 1 ? vox[b + WH] : 0));
  const under = (x, z, b0) =>                                            // interior-bottom: exposed face is only the unseen underside
    (x > 0 ? vox[b0 - 1] : 0) && (x < W - 1 ? vox[b0 + 1] : 0) &&
    (z > 0 ? vox[b0 - WH] : 0) && (z < D - 1 ? vox[b0 + WH] : 0) &&
    (H > 1 ? vox[b0 + W] : 0);
  const fx = x => mirX ? (W - 1 - x) - W / 2 + .5 : x - W / 2 + .5;      // Unity truth mirror law — same as main's fx/fz
  const fz = z => zflip ? (D - 1 - z) - D / 2 + .5 : z - D / 2 + .5;

  /* pass 1 — count scan: surface cull + per-column count/top + occupied bounds */
  let ng = 0, ngg = 0, gx0 = W, gx1 = -1, gz0 = D, gz1 = -1, gy1 = -1;
  const colN = new Uint16Array(W * D), colLast = new Int16Array(W * D).fill(-1);
  for (let y = 0; y < H; y++) {
    for (let z = 0; z < D; z++) { const rb = W * (y + H * z); for (let x = 0; x < W; x++) {
      const id = vox[rb + x]; if (!id) continue;
      if (x < gx0) gx0 = x; if (x > gx1) gx1 = x; if (z < gz0) gz0 = z; if (z > gz1) gz1 = z; if (y > gy1) gy1 = y;
      const ck = x + W * z; colN[ck]++; colLast[ck] = y;                 // ys ascend — last IS the top
      if (surf(x, y, z, rb + x) && !(y === 0 && under(x, z, rb + x))) { ng++; if (glassLUT[id]) ngg++; }
    } } }

  /* pass 2 — instance fill: raw matrix/color writes straight into per-part buffers
     (scale+translate = 7 writes; the rest of each 16-slot is already zero) */
  const opaque = (ng - ngg) || 1;
  const imParts = Math.max(1, Math.min(maxParts, Math.ceil(opaque / maxPart)));
  const cap = Math.ceil(opaque / imParts);
  const parts = [];
  for (let i = 0; i < imParts; i++)
    parts.push({ n: 0, mat: new Float32Array(cap * 16), col: new Float32Array(cap * 3) });
  const gMat = new Float32Array((ngg || 1) * 16), gCol = new Float32Array((ngg || 1) * 3);
  let no = 0, gi = 0;
  for (let y = 0; y < H; y++) {
    for (let z = 0; z < D; z++) { const rb = W * (y + H * z); for (let x = 0; x < W; x++) {
      const id = vox[rb + x]; if (!id) continue;
      if (!surf(x, y, z, rb + x) || (y === 0 && under(x, z, rb + x))) continue;
      const px = fx(x) * vs, py = (y + .5) * vs, pz = fz(z) * vs, l = pal[id] || dflt;
      if (glassLUT[id]) { const o = gi * 16;
        gMat[o] = vs; gMat[o + 5] = vs; gMat[o + 10] = vs; gMat[o + 12] = px; gMat[o + 13] = py; gMat[o + 14] = pz; gMat[o + 15] = 1;
        gCol[gi * 3] = l[0]; gCol[gi * 3 + 1] = l[1]; gCol[gi * 3 + 2] = l[2]; gi++; }
      else { const p = parts[Math.min(imParts - 1, (no * imParts / opaque) | 0)], o = p.n * 16;
        p.mat[o] = vs; p.mat[o + 5] = vs; p.mat[o + 10] = vs; p.mat[o + 12] = px; p.mat[o + 13] = py; p.mat[o + 14] = pz; p.mat[o + 15] = 1;
        const o3 = p.n * 3; p.col[o3] = l[0]; p.col[o3 + 1] = l[1]; p.col[o3 + 2] = l[2];
        p.n++; no++; }
    } } }

  /* pass 3 — column pack: grid tops + kept-column lists, packed flat for transfer */
  const gT = grid ? new Int16Array(W * D).fill(-1) : null, gI = grid ? new Uint8Array(W * D) : null;
  const colIdx = [], colDx = [], colDz = [], colCounts = [], yBuf = [];
  for (let z = 0; z < D; z++) for (let x = 0; x < W; x++) {
    const k = x + W * z, n = colN[k]; if (!n) continue;
    const ty = colLast[k];
    if (gT) { gT[k] = ty; gI[k] = vox[x + W * (ty + H * z)]; }           // flat cells included (the grid needs the roads)
    if (plate && n === 2 && ty === 1) continue;                        // flat ground [0,1] — the plate writes these tops
    colIdx.push(k); colDx.push(fx(x) * vs); colDz.push(fz(z) * vs); colCounts.push(n);
    const cb = x + W * H * z;
    for (let y = 0; y <= ty; y++) if (vox[cb + W * y]) yBuf.push(y);
  }
  const nk = colIdx.length, ci = new Int32Array(nk), cdx = new Float32Array(nk),
    cdz = new Float32Array(nk), cc = new Uint16Array(nk), yf = new Int16Array(yBuf);
  for (let i = 0; i < nk; i++) { ci[i] = colIdx[i]; cdx[i] = colDx[i]; cdz[i] = colDz[i]; cc[i] = colCounts[i]; }
  return { ng, ngg, gx0, gx1, gz0, gz1, gy1, cap, parts,
    gMat, gCol, gT, gI, colIdx: ci, colDx: cdx, colDz: cdz, colCounts: cc, colYsFlat: yf };
};
if (typeof WorkerGlobalScope !== 'undefined' && typeof window === 'undefined') {   // worker context only — the classic
  self.onmessage = e => { const r = emitScan(e.data), xfer = [r.gMat.buffer, r.gCol.buffer];   // script path is the fallback
    r.parts.forEach(p => xfer.push(p.mat.buffer, p.col.buffer));
    ['gT', 'gI', 'colIdx', 'colDx', 'colDz', 'colCounts', 'colYsFlat'].forEach(k => r[k] && xfer.push(r[k].buffer));
    self.postMessage(r, xfer); };
}
