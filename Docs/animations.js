// ═══════════════════════════════════════════════════════════
// Nudge — technical abstract animations (Canvas 2D, 60fps)
// ═══════════════════════════════════════════════════════════
(function () {
  'use strict';

  const BLUE = '#58A6FF';
  const BLUE_LOW = 'rgba(88,166,255,0.15)';
  const GREEN = '#22C55E';
  const GREEN_LOW = 'rgba(34,197,94,0.15)';
  const INDIGO = '#818CF8';

  // ── Throttled resize helper ────────────────────────────
  function makeResizer(canvas, ctx, fallbackW, fallbackH) {
    const dpr = window.devicePixelRatio || 1;
    let w = fallbackW, h = fallbackH;

    function apply() {
      const rect = canvas.parentElement.getBoundingClientRect();
      w = rect.width || fallbackW;
      h = rect.height || w * 0.65 || fallbackH;
      canvas.width = w * dpr;
      canvas.height = h * dpr;
      canvas.style.width = w + 'px';
      canvas.style.height = h + 'px';
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    apply();
    new ResizeObserver(apply).observe(canvas.parentElement);
    return { get w() { return w; }, get h() { return h; } };
  }

  // ════════════════════════════════════════════════════════
  //  ENGINE: 3 signals → fusion node → 26 radiating lines
  // ════════════════════════════════════════════════════════
  function initEngine(canvas) {
    const ctx = canvas.getContext('2d');
    const dims = makeResizer(canvas, ctx, 520, 340);

    const N = 22;
    const streams = [
      { x1: 0, y1: dims.h * 0.22, x2: dims.w * 0.35, y2: dims.h * 0.5, color: BLUE, delay: 0 },
      { x1: 0, y1: dims.h * 0.50, x2: dims.w * 0.35, y2: dims.h * 0.5, color: GREEN, delay: 700 },
      { x1: 0, y1: dims.h * 0.78, x2: dims.w * 0.35, y2: dims.h * 0.5, color: INDIGO, delay: 1400 },
    ];

    const particles = [];
    streams.forEach(s => {
      for (let i = 0; i < N; i++) particles.push({ stream: s, t: -i * 0.04, speed: 0.007 + Math.random() * 0.004 });
    });

    function easeInOut(t) { return t < 0.5 ? 2*t*t : -1 + (4 - 2*t)*t; }

    let last = 0, totalTime = 0, animStart = performance.now();
    const node = { x: dims.w * 0.40, y: dims.h * 0.5, r: dims.h * 0.07, pulse: 0 };

    function render(now) {
      const dt = last ? Math.min(now - last, 40) : 16;
      last = now;
      totalTime += dt;
      const w = dims.w, h = dims.h;
      if (w <= 0 || h <= 0) { requestAnimationFrame(render); return; }

      ctx.clearRect(0, 0, w, h);

      // Update positions
      streams[0].y1 = h * 0.22; streams[0].x2 = w * 0.35; streams[0].y2 = h * 0.5;
      streams[1].y1 = h * 0.50; streams[1].x2 = w * 0.35; streams[1].y2 = h * 0.5;
      streams[2].y1 = h * 0.78; streams[2].x2 = w * 0.35; streams[2].y2 = h * 0.5;
      node.x = w * 0.40; node.y = h * 0.5; node.r = h * 0.07;

      // Connection lines (faint)
      streams.forEach(s => {
        ctx.beginPath(); ctx.moveTo(s.x1, s.y1); ctx.lineTo(node.x, node.y);
        ctx.strokeStyle = 'rgba(88,166,255,0.06)'; ctx.lineWidth = 1; ctx.stroke();
      });

      // Particles
      node.pulse = Math.max(0, node.pulse - dt * 0.0008);
      particles.forEach(p => {
        const elapsed = totalTime - p.stream.delay;
        if (elapsed < 0) return;
        p.t += p.speed * (dt / 16);
        if (p.t > 1) p.t = 0;
        const et = easeInOut(p.t);
        const x = p.stream.x1 + (p.stream.x2 - p.stream.x1) * et;
        const y = p.stream.y1 + (p.stream.y2 - p.stream.y1) * et;
        const alpha = p.t < 0.12 ? p.t / 0.12 : p.t > 0.88 ? (1 - p.t) / 0.12 : 1;
        ctx.beginPath(); ctx.arc(x, y, 4, 0, Math.PI * 2);
        ctx.fillStyle = p.stream.color; ctx.globalAlpha = alpha * 0.85; ctx.fill();
        if (p.t > 0.85 && p.t < 0.95) node.pulse = 0.6;
      });
      ctx.globalAlpha = 1;

      // Node glow
      const pulseA = 0.12 + node.pulse * 0.4;
      const glow = ctx.createRadialGradient(node.x, node.y, node.r * 0.2, node.x, node.y, node.r * 3);
      glow.addColorStop(0, `rgba(88,166,255,${pulseA + 0.2})`);
      glow.addColorStop(0.35, `rgba(88,166,255,${pulseA})`);
      glow.addColorStop(1, 'rgba(88,166,255,0)');
      ctx.fillStyle = glow;
      ctx.fillRect(node.x - node.r * 3.5, node.y - node.r * 3.5, node.r * 7, node.r * 7);

      // Node body
      ctx.beginPath(); ctx.arc(node.x, node.y, node.r, 0, Math.PI * 2);
      ctx.fillStyle = BLUE_LOW; ctx.fill();
      ctx.strokeStyle = 'rgba(88,166,255,0.35)'; ctx.lineWidth = 1.5; ctx.stroke();

      // Radiating lines — animate in from 0 length with stagger
      const lineAnimTime = (totalTime % 3500) / 3500;
      for (let i = 0; i < 26; i++) {
        const stagger = (i / 26) * 0.3;
        const progress = Math.min(1, Math.max(0, (lineAnimTime - stagger) / 0.5));
        const angle = (i / 26) * Math.PI * 2 - Math.PI * 0.5;
        const maxLen = node.r * 5 + (i % 3 + 1) * node.r * 1.5;
        const len = maxLen * easeInOut(progress);
        const ex = node.x + Math.cos(angle) * len;
        const ey = node.y + Math.sin(angle) * len;
        const grad = ctx.createLinearGradient(node.x, node.y, ex, ey);
        grad.addColorStop(0, `rgba(88,166,255,${0.25 * progress})`);
        grad.addColorStop(1, 'rgba(88,166,255,0)');
        ctx.beginPath(); ctx.moveTo(node.x, node.y); ctx.lineTo(ex, ey);
        ctx.strokeStyle = grad; ctx.lineWidth = 0.7; ctx.stroke();
      }

      // Labels
      ctx.font = `${Math.max(9, h * 0.048)}px system-ui, -apple-system, sans-serif`;
      ctx.fillStyle = 'rgba(88,166,255,0.4)';
      ctx.fillText('app', 8, h * 0.235);
      ctx.fillText('idle', 8, h * 0.515);
      ctx.fillText('domain', 8, h * 0.795);

      requestAnimationFrame(render);
    }
    requestAnimationFrame(render);
  }

  // ════════════════════════════════════════════════════════
  //  AI MODEL: data pulse → model → YES/NO + feedback loop
  // ════════════════════════════════════════════════════════
  function initAI(canvas) {
    const ctx = canvas.getContext('2d');
    const dims = makeResizer(canvas, ctx, 520, 340);

    let last = 0, cycleTime = 0, coreBrightness = 0.10, cycleCount = 0;

    function render(now) {
      const dt = last ? Math.min(now - last, 40) : 16;
      last = now;
      cycleTime += dt;
      if (cycleTime > 7000) { cycleTime = 0; cycleCount++; coreBrightness = 0.10 + cycleCount * 0.04; if (coreBrightness > 0.28) { coreBrightness = 0.10; cycleCount = 0; } }

      const w = dims.w, h = dims.h;
      if (w <= 0 || h <= 0) { requestAnimationFrame(render); return; }
      ctx.clearRect(0, 0, w, h);

      const centerY = h * 0.5;
      const coreW = w * 0.30, coreH = h * 0.42;
      const coreX = w * 0.35, coreY = centerY - coreH * 0.5;
      const inputStart = w * 0.05, inputEnd = coreX - 6;
      const outputStart = coreX + coreW + 6, outputEndX = w * 0.92;

      // ── Input pulse (brighter, leaves trail) ──
      const pulseP = (cycleTime % 2800) / 2800;
      const dashOff = cycleTime * 0.04 % 10;
      ctx.beginPath(); ctx.moveTo(inputStart, centerY); ctx.lineTo(inputEnd, centerY);
      ctx.setLineDash([5, 4]); ctx.lineDashOffset = -dashOff;
      ctx.strokeStyle = GREEN_LOW; ctx.lineWidth = 1.2; ctx.stroke(); ctx.setLineDash([]);

      // Input dot with trail
      const px = inputStart + (inputEnd - inputStart) * pulseP;
      for (let tr = 0; tr < 3; tr++) {
        const tp = pulseP - tr * 0.04; if (tp < 0) continue;
        const tx = inputStart + (inputEnd - inputStart) * tp;
        ctx.beginPath(); ctx.arc(tx, centerY, 4 - tr * 0.8, 0, Math.PI * 2);
        ctx.fillStyle = GREEN; ctx.globalAlpha = (1 - tr * 0.3) * 0.8; ctx.fill();
      }
      ctx.globalAlpha = 1;

      // ── Model core ──
      const bx = coreX, by = coreY, bw = coreW, bh = coreH;
      // Core glow
      const cGlow = ctx.createRadialGradient(bx + bw * 0.5, by + bh * 0.5, bw * 0.1, bx + bw * 0.5, by + bh * 0.5, bw * 0.8);
      cGlow.addColorStop(0, `rgba(34,197,94,${coreBrightness + 0.15})`);
      cGlow.addColorStop(1, 'rgba(34,197,94,0)');
      ctx.fillStyle = cGlow;
      ctx.fillRect(bx - bw * 0.3, by - bh * 0.3, bw * 1.6, bh * 1.6);

      // Core box with pulse
      const corePulse = (Math.sin(cycleTime * 0.003) + 1) * 0.5;
      ctx.fillStyle = `rgba(34,197,94,${coreBrightness + corePulse * 0.04})`;
      ctx.strokeStyle = `rgba(34,197,94,${0.25 + coreBrightness + corePulse * 0.15})`;
      ctx.lineWidth = 1.5;
      roundRect(ctx, bx, by, bw, bh, 8); ctx.fill(); ctx.stroke();

      // Core inner particles (more, brighter)
      for (let i = 0; i < 8; i++) {
        const angle = (cycleTime * 0.0012 + i * 0.8) % (Math.PI * 2);
        const dist = (0.25 + (i % 3) * 0.12) * bw * 0.35;
        const ppx = bx + bw * 0.5 + Math.cos(angle) * dist;
        const ppy = by + bh * 0.5 + Math.sin(angle) * dist * 0.55;
        ctx.beginPath(); ctx.arc(ppx, ppy, 2, 0, Math.PI * 2);
        ctx.fillStyle = GREEN; ctx.globalAlpha = 0.55 + corePulse * 0.3; ctx.fill();
      }
      ctx.globalAlpha = 1;

      // Labels
      ctx.font = `${Math.max(9, h * 0.055)}px system-ui, -apple-system, sans-serif`;
      ctx.fillStyle = GREEN; ctx.textAlign = 'center';
      ctx.fillText('scikit-learn', bx + bw * 0.5, by + bh * 0.42);
      ctx.font = `${Math.max(8, h * 0.042)}px system-ui, -apple-system, sans-serif`;
      ctx.fillStyle = 'rgba(34,197,94,0.5)';
      ctx.fillText('binary classifier', bx + bw * 0.5, by + bh * 0.65);
      ctx.textAlign = 'start';

      // ── Output lines ──
      const outX = bx + bw;
      const o1y = by + bh * 0.28, o2y = by + bh * 0.72;
      ctx.beginPath(); ctx.moveTo(outX, o1y); ctx.lineTo(outputEndX, o1y - bh * 0.15);
      ctx.strokeStyle = GREEN_LOW; ctx.lineWidth = 1; ctx.stroke();
      ctx.beginPath(); ctx.moveTo(outX, o2y); ctx.lineTo(outputEndX, o2y + bh * 0.15);
      ctx.strokeStyle = GREEN_LOW; ctx.lineWidth = 1; ctx.stroke();

      // Output dots (larger, brighter)
      const outP = (cycleTime % 1800) / 1800;
      function drawOutDot(t, baseY, dir) {
        const x = outX + 4 + (outputEndX - outX - 20) * t;
        const y = baseY + dir * bh * 0.15 * t;
        for (let tr = 0; tr < 2; tr++) {
          const tt = t - tr * 0.03; if (tt < 0) continue;
          const ttx = outX + 4 + (outputEndX - outX - 20) * tt;
          const tty = baseY + dir * bh * 0.15 * tt;
          ctx.beginPath(); ctx.arc(ttx, tty, 4 - tr, 0, Math.PI * 2);
          ctx.fillStyle = GREEN; ctx.globalAlpha = (1 - tr * 0.35) * 0.8; ctx.fill();
        }
      }
      drawOutDot(outP, o1y, -1); drawOutDot((outP + 0.35) % 1, o2y, 1);
      ctx.globalAlpha = 1;

      ctx.font = `${Math.max(9, h * 0.05)}px system-ui, -apple-system, sans-serif`;
      ctx.fillStyle = GREEN; ctx.textAlign = 'center';
      ctx.fillText('YES', outputEndX, o1y - bh * 0.15 - 10);
      ctx.fillText('NO', outputEndX, o2y + bh * 0.15 + 18);
      ctx.textAlign = 'start';

      // ── Feedback arc (brighter) ──
      const arcP = (cycleTime % 3500) / 3500;
      const arcPath = new Path2D();
      arcPath.moveTo(outputEndX - 8, centerY);
      arcPath.quadraticCurveTo(w * 0.52, h * 0.82, coreX + coreW * 0.5, coreY + coreH + 3);
      ctx.strokeStyle = 'rgba(34,197,94,0.2)'; ctx.lineWidth = 1.2;
      ctx.setLineDash([4, 6]); ctx.lineDashOffset = -arcP * 20; ctx.stroke(arcPath); ctx.setLineDash([]);

      // Feedback dot with trail
      for (let tr = 0; tr < 2; tr++) {
        const at = arcP - tr * 0.03; if (at < 0) continue;
        const ax = (outputEndX - 8) + (coreX + coreW * 0.5 - (outputEndX - 8)) * at;
        const cp1x = w * 0.52, cp1y = h * 0.82;
        const t = at, mt = 1 - t;
        const ay = mt*mt*centerY + 2*mt*t*cp1y + t*t*(coreY + coreH + 3);
        ctx.beginPath(); ctx.arc(ax, ay, 3 - tr * 0.5, 0, Math.PI * 2);
        ctx.fillStyle = GREEN; ctx.globalAlpha = (1 - tr * 0.3) * 0.7; ctx.fill();
      }
      ctx.globalAlpha = 1;

      if (arcP > 0.25 && arcP < 0.75) {
        ctx.font = `${Math.max(7, h * 0.035)}px system-ui, -apple-system, sans-serif`;
        ctx.fillStyle = 'rgba(34,197,94,0.35)'; ctx.textAlign = 'center';
        ctx.fillText('retrains at 50+ samples', w * 0.52, h * 0.87);
        ctx.textAlign = 'start';
      }

      requestAnimationFrame(render);
    }
    requestAnimationFrame(render);
  }

  function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y); ctx.lineTo(x + w - r, y); ctx.arcTo(x + w, y, x + w, y + r, r);
    ctx.lineTo(x + w, y + h - r); ctx.arcTo(x + w, y + h, x + w - r, y + h, r);
    ctx.lineTo(x + r, y + h); ctx.arcTo(x, y + h, x, y + h - r, r);
    ctx.lineTo(x, y + r); ctx.arcTo(x, y, x + r, y, r); ctx.closePath();
  }

  // ════════════════════════════════════════════════════════
  //  INIT — fires reliably after layout settles
  // ════════════════════════════════════════════════════════
  window.addEventListener('load', () => {
    const engineCanvas = document.getElementById('engine-canvas');
    const aiCanvas = document.getElementById('ai-canvas');

    const observer = new IntersectionObserver((entries) => {
      entries.forEach(entry => {
        if (entry.isIntersecting && !entry.target.dataset.started) {
          entry.target.dataset.started = '1';
          if (entry.target === engineCanvas) initEngine(engineCanvas);
          if (entry.target === aiCanvas) initAI(aiCanvas);
        }
      });
    }, { threshold: 0.01, rootMargin: '0px 0px 200px 0px' });

    if (engineCanvas) observer.observe(engineCanvas);
    if (aiCanvas) observer.observe(aiCanvas);
  });
})();
