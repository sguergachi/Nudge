// ═══════════════════════════════════════════════════════════
// Nudge — premium atmospheric Canvas animations
// ═══════════════════════════════════════════════════════════
(function () {
  'use strict';

  // ── 2D simplex noise (compact, no dependencies) ────────
  const grad = [[1,1],[-1,1],[1,-1],[-1,-1],[1,0],[-1,0],[0,1],[0,-1]];
  const perm = new Uint8Array(512);
  for (let i = 0; i < 256; i++) perm[i] = i;
  for (let i = 255; i > 0; i--) { const j = (i * 137 + 31) & 255; [perm[i],perm[j]] = [perm[j],perm[i]]; }
  for (let i = 0; i < 256; i++) perm[i + 256] = perm[i];
  function noise2D(x, y) {
    const X = Math.floor(x) & 255, Y = Math.floor(y) & 255;
    const xf = x - Math.floor(x), yf = y - Math.floor(y);
    const u = xf*xf*(3-2*xf), v = yf*yf*(3-2*yf);
    const p = perm, aa = p[p[X]+Y], ab = p[p[X]+Y+1], ba = p[p[X+1]+Y], bb = p[p[X+1]+Y+1];
    function dot(g, dx, dy) { return g[0]*dx + g[1]*dy; }
    const n0 = dot(grad[aa&7], xf, yf), n1 = dot(grad[ba&7], xf-1, yf), n2 = dot(grad[ab&7], xf, yf-1), n3 = dot(grad[bb&7], xf-1, yf-1);
    return n0 + u*(n1-n0) + v*(n2-n0) + u*v*(n0-n1-n2+n3);
  }

  // ── Resizer ──────────────────────────────────────────
  function makeResizer(canvas, ctx, fw, fh) {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    let w = fw, h = fh;
    function apply() {
      const r = canvas.parentElement.getBoundingClientRect();
      w = r.width || fw; h = r.height || w * 0.65 || fh;
      canvas.width  = Math.floor(w * dpr);
      canvas.height = Math.floor(h * dpr);
      canvas.style.width  = w + 'px';
      canvas.style.height = h + 'px';
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }
    apply();
    new ResizeObserver(apply).observe(canvas.parentElement);
    return { get w() { return w; }, get h() { return h; } };
  }

  // ── Gradient particle draw (cached radial grad per size) ──
  function drawParticle(ctx, x, y, r, alpha, color) {
    if (alpha < 0.005) return;
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fillStyle = `rgba(${color},${alpha})`;
    ctx.fill();
  }

  function drawGlowParticle(ctx, x, y, r, alpha, color) {
    if (alpha < 0.008) return;
    const g = ctx.createRadialGradient(x, y, 0, x, y, r * 2.5);
    g.addColorStop(0, `rgba(${color},${Math.min(1, alpha * 1.6)})`);
    g.addColorStop(0.4, `rgba(${color},${alpha})`);
    g.addColorStop(1, `rgba(${color},0)`);
    ctx.fillStyle = g;
    ctx.fillRect(x - r * 3, y - r * 3, r * 6, r * 6);
  }

  // ════════════════════════════════════════════════════════
  //  ENGINE: orbital particle field with gravity well
  // ════════════════════════════════════════════════════════
  function initEngine(canvas) {
    const ctx = canvas.getContext('2d');
    const D = makeResizer(canvas, ctx, 520, 340);
    const off = document.createElement('canvas');
    const offCtx = off.getContext('2d');

    // Particles: 3 depth layers
    const layers = [
      { count: 300, speed: 0.0003, minR: 0.8, maxR: 2.5, alpha: 0.25, color: '88,166,255' },  // deep field
      { count: 200, speed: 0.0006, minR: 1.5, maxR: 4.0, alpha: 0.40, color: '120,180,255' }, // mid
      { count: 80,  speed: 0.0012, minR: 2.5, maxR: 7.0, alpha: 0.55, color: '160,210,255' }, // bright
    ];

    const particles = [];
    layers.forEach(l => {
      for (let i = 0; i < l.count; i++) {
        const angle = Math.random() * Math.PI * 2;
        const dist = 30 + Math.random() * 300;
        particles.push({
          x: 0, y: 0, // set per frame
          angle, dist, baseDist: dist,
          speed: l.speed * (0.7 + Math.random() * 0.6),
          r: l.minR + Math.random() * (l.maxR - l.minR),
          alpha: l.alpha * (0.5 + Math.random() * 0.5),
          color: l.color,
          ph: Math.random() * Math.PI * 2,
          phSpeed: 0.002 + Math.random() * 0.006,
        });
      }
    });

    let mouseX = 0.5, mouseY = 0.5, last = 0, totalTime = 0;
    canvas.addEventListener('mousemove', e => {
      const rect = canvas.getBoundingClientRect();
      mouseX = (e.clientX - rect.left) / rect.width;
      mouseY = (e.clientY - rect.top) / rect.height;
    });

    function render(now) {
      const dt = last ? Math.min(now - last, 40) : 16; last = now; totalTime += dt;
      const w = D.w, h = D.h;
      if (w <= 0 || h <= 0) { requestAnimationFrame(render); return; }

      // Resize offscreen canvas
      if (off.width !== w || off.height !== h) { off.width = w; off.height = h; }

      const cx = w * (0.48 + (mouseX - 0.5) * 0.08);
      const cy = h * (0.50 + (mouseY - 0.5) * 0.12);
      const gravStrength = 0.04;

      // ── Trail persistence ──
      offCtx.globalAlpha = 0.88;
      offCtx.drawImage(canvas, 0, 0);
      offCtx.globalAlpha = 1;
      ctx.clearRect(0, 0, w, h);
      ctx.drawImage(off, 0, 0);

      // ── Update + draw particles ──
      particles.forEach(p => {
        p.ph += p.phSpeed * (dt / 16);
        const noiseVal = noise2D(p.angle * 3, totalTime * p.speed * 0.3);

        // Gravity well pull
        const gravity = Math.max(0, 1 - p.dist / 280);
        p.dist = p.baseDist + noiseVal * 60 - gravity * 40 * gravStrength;

        // Orbital drift
        p.angle += p.speed * (1 - gravity * 0.7) * (dt / 16);

        p.x = cx + Math.cos(p.angle) * p.dist;
        p.y = cy + Math.sin(p.angle) * p.dist * 0.75;

        // Brightness peaks near center
        const distFromCenter = Math.hypot(p.x - cx, p.y - cy);
        const nearAlpha = 1 + Math.max(0, 1 - distFromCenter / 60) * 1.8;
        const alpha = Math.min(1, p.alpha * nearAlpha * (0.85 + Math.sin(p.ph) * 0.15));

        drawGlowParticle(ctx, p.x, p.y, p.r, alpha, p.color);
      });

      // ── Central glow ──
      const cPulse = Math.sin(totalTime * 0.0015) * 0.5 + 0.5;
      const cGlow = ctx.createRadialGradient(cx, cy, 0, cx, cy, 60 + cPulse * 20);
      cGlow.addColorStop(0, `rgba(180,210,255,${0.15 + cPulse * 0.08})`);
      cGlow.addColorStop(0.3, `rgba(88,166,255,${0.06 + cPulse * 0.04})`);
      cGlow.addColorStop(1, 'rgba(88,166,255,0)');
      ctx.fillStyle = cGlow;
      ctx.fillRect(cx - 70, cy - 70, 140, 140);

      // Central bright point
      ctx.beginPath(); ctx.arc(cx, cy, 3 + cPulse, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(200,230,255,${0.6 + cPulse * 0.3})`; ctx.fill();

      // ── Expanding rings ──
      for (let i = 0; i < 2; i++) {
        const phase = (totalTime * 0.0004 + i * 0.5) % 1;
        const ringR = 20 + phase * 140;
        const ringAlpha = (1 - phase) * 0.15;
        ctx.beginPath(); ctx.arc(cx, cy, ringR, 0, Math.PI * 2);
        ctx.strokeStyle = `rgba(88,166,255,${ringAlpha})`;
        ctx.lineWidth = 0.6; ctx.stroke();
      }

      // ── Vignette ──
      const vignette = ctx.createRadialGradient(w * 0.5, h * 0.5, w * 0.4, w * 0.5, h * 0.5, w * 0.72);
      vignette.addColorStop(0, 'rgba(0,0,0,0)');
      vignette.addColorStop(1, 'rgba(0,0,0,0.35)');
      ctx.fillStyle = vignette; ctx.fillRect(0, 0, w, h);

      requestAnimationFrame(render);
    }
    requestAnimationFrame(render);
  }

  // ════════════════════════════════════════════════════════
  //  AI BRAIN: breathing core with luminous filaments
  // ════════════════════════════════════════════════════════
  function initAI(canvas) {
    const ctx = canvas.getContext('2d');
    const D = makeResizer(canvas, ctx, 520, 340);
    const off = document.createElement('canvas');
    const offCtx = off.getContext('2d');

    // Orbiting particles around the breathing core
    const particles = [];
    for (let i = 0; i < 250; i++) {
      const angle = Math.random() * Math.PI * 2;
      const orbit = 20 + Math.random() * 160;
      particles.push({
        angle, orbit, baseOrbit: orbit,
        speed: (0.4 + Math.random() * 0.8) * 0.001,
        r: 0.8 + Math.random() * 3.5,
        alpha: 0.2 + Math.random() * 0.55,
        color: Math.random() < 0.3 ? '160,255,200' : '34,197,94',
        ph: Math.random() * Math.PI * 2,
      });
    }

    // Filaments
    const filaments = [];
    for (let i = 0; i < 8; i++) {
      filaments.push({
        angle: (i / 8) * Math.PI * 2,
        length: 60 + Math.random() * 100,
        curve: (Math.random() - 0.5) * 1.5,
        sway: 0.003 + Math.random() * 0.005,
        ph: Math.random() * Math.PI * 2,
      });
    }

    let last = 0, totalTime = 0, coreRadius = 16, coreMax = 38, phase = 0, bloomAge = 0;
    let mouseX = 0.5, mouseY = 0.5;
    canvas.addEventListener('mousemove', e => {
      const rect = canvas.getBoundingClientRect();
      mouseX = (e.clientX - rect.left) / rect.width;
      mouseY = (e.clientY - rect.top) / rect.height;
    });

    function render(now) {
      const dt = last ? Math.min(now - last, 40) : 16; last = now; totalTime += dt;
      const w = D.w, h = D.h;
      if (w <= 0 || h <= 0) { requestAnimationFrame(render); return; }
      if (off.width !== w || off.height !== h) { off.width = w; off.height = h; }

      const cx = w * (0.48 + (mouseX - 0.5) * 0.06);
      const cy = h * (0.50 + (mouseY - 0.5) * 0.10);

      // ── Breathing cycle ──
      if (phase === 0) {
        coreRadius += dt * 0.006;
        if (coreRadius >= coreMax) { phase = 1; bloomAge = 0; }
      } else {
        coreRadius -= dt * 0.005;
        if (coreRadius <= 16) {
          phase = 0;
          coreMax = Math.min(55, coreMax + 2);
          if (coreMax >= 55) { coreMax = 32; bloomAge = 0; }
        }
      }
      bloomAge += dt;
      const isBlooming = bloomAge < 600;

      // ── Trail persistence ──
      offCtx.globalAlpha = 0.90;
      offCtx.drawImage(canvas, 0, 0);
      offCtx.globalAlpha = 1;
      ctx.clearRect(0, 0, w, h);
      ctx.drawImage(off, 0, 0);

      // ── Ambient field glow ──
      const aGlow = ctx.createRadialGradient(cx, cy, w * 0.05, cx, cy, w * 0.65);
      aGlow.addColorStop(0, `rgba(34,197,94,${0.015 + coreRadius * 0.001})`);
      aGlow.addColorStop(0.6, 'rgba(34,197,94,0.003)');
      aGlow.addColorStop(1, 'rgba(34,197,94,0)');
      ctx.fillStyle = aGlow; ctx.fillRect(0, 0, w, h);

      // ── Particles ──
      particles.forEach(p => {
        p.ph += 0.005;
        const noise = noise2D(p.angle * 2, totalTime * 0.0002);
        p.orbit = p.baseOrbit + noise * 25;
        p.angle += p.speed * (1 + (coreRadius / 40) * 0.5) * (dt / 16);
        const px = cx + Math.cos(p.angle) * p.orbit;
        const py = cy + Math.sin(p.angle) * p.orbit * 0.7;
        const dCore = Math.hypot(px - cx, py - cy);
        const nearBoost = dCore < coreRadius * 2 ? 1.8 : 1;
        const alpha = p.alpha * nearBoost * (0.8 + Math.sin(p.ph) * 0.2);
        drawGlowParticle(ctx, px, py, p.r, alpha, p.color);
        if (isBlooming) { drawParticle(ctx, px, py, p.r * 1.5, alpha * 0.4, p.color); }
      });

      // ── Filaments ──
      filaments.forEach(f => {
        f.ph += f.sway * (dt / 16);
        f.angle += 0.0008 * (dt / 16);
        const sway = Math.sin(f.ph) * 0.2;
        const a = f.angle + sway;
        const len = f.length + coreRadius * 0.4;
        ctx.beginPath();
        ctx.moveTo(cx, cy);
        const cpx = cx + Math.cos(a) * len * 0.5 + Math.cos(a + 1.57) * f.curve * len * 0.25;
        const cpy = cy + Math.sin(a) * len * 0.5 + Math.sin(a + 1.57) * f.curve * len * 0.25;
        const epx = cx + Math.cos(a) * len, epy = cy + Math.sin(a) * len;
        ctx.quadraticCurveTo(cpx, cpy, epx, epy);
        ctx.strokeStyle = `rgba(34,197,94,${0.06 + coreRadius * 0.003})`;
        ctx.lineWidth = 0.5 + coreRadius * 0.04;
        ctx.stroke();
        if (coreRadius > 24 || isBlooming) {
          ctx.beginPath(); ctx.arc(epx, epy, 1.5 + coreRadius * 0.03, 0, Math.PI * 2);
          ctx.fillStyle = `rgba(34,197,94,${0.12 + coreRadius * 0.005})`; ctx.fill();
        }
      });

      // ── Core ──
      const cGlow = ctx.createRadialGradient(cx, cy, 0, cx, cy, coreRadius * 3);
      cGlow.addColorStop(0, `rgba(34,197,94,${0.2 + coreRadius * 0.007})`);
      cGlow.addColorStop(0.35, `rgba(34,197,94,${0.08 + coreRadius * 0.003})`);
      cGlow.addColorStop(0.7, 'rgba(34,197,94,0.015)');
      cGlow.addColorStop(1, 'rgba(34,197,94,0)');
      ctx.fillStyle = cGlow;
      ctx.fillRect(cx - coreRadius * 3.5, cy - coreRadius * 3.5, coreRadius * 7, coreRadius * 7);

      ctx.beginPath(); ctx.arc(cx, cy, coreRadius, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(34,197,94,${0.06 + coreRadius * 0.002})`;
      ctx.strokeStyle = `rgba(34,197,94,${0.18 + coreRadius * 0.005})`;
      ctx.lineWidth = 1; ctx.fill(); ctx.stroke();

      // Inner bright point
      const iGlow = ctx.createRadialGradient(cx, cy, 0, cx, cy, coreRadius * 0.4);
      iGlow.addColorStop(0, 'rgba(200,255,220,0.55)'); iGlow.addColorStop(1, 'rgba(34,197,94,0)');
      ctx.fillStyle = iGlow;
      ctx.fillRect(cx - coreRadius, cy - coreRadius, coreRadius * 2, coreRadius * 2);

      // ── Bloom burst ──
      if (phase === 0 && coreRadius < 18 && coreMax > 33) {
        for (let i = 0; i < 12; i++) {
          const ba = Math.random() * Math.PI * 2, bd = coreRadius * 1.3;
          ctx.beginPath(); ctx.arc(cx + Math.cos(ba) * bd, cy + Math.sin(ba) * bd, 2, 0, Math.PI * 2);
          ctx.fillStyle = 'rgba(34,197,94,0.5)'; ctx.fill();
        }
      }

      // ── Vignette ──
      const vignette = ctx.createRadialGradient(w * 0.5, h * 0.5, w * 0.35, w * 0.5, h * 0.5, w * 0.72);
      vignette.addColorStop(0, 'rgba(0,0,0,0)'); vignette.addColorStop(1, 'rgba(0,0,0,0.35)');
      ctx.fillStyle = vignette; ctx.fillRect(0, 0, w, h);

      requestAnimationFrame(render);
    }
    requestAnimationFrame(render);
  }

  // ════════════════════════════════════════════════════════
  //  INIT
  // ════════════════════════════════════════════════════════
  window.addEventListener('load', () => {
    const e = document.getElementById('engine-canvas');
    const a = document.getElementById('ai-canvas');
    const obs = new IntersectionObserver(entries => {
      entries.forEach(en => {
        if (en.isIntersecting && !en.target.dataset.started) {
          en.target.dataset.started = '1';
          if (en.target === e) initEngine(e);
          if (en.target === a) initAI(a);
        }
      });
    }, { threshold: 0.01, rootMargin: '0px 0px 200px 0px' });
    if (e) obs.observe(e);
    if (a) obs.observe(a);
  });
})();
