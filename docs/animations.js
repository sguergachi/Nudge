// ═══════════════════════════════════════════════════════════
// Nudge — atmospheric abstract Canvas animations
// ═══════════════════════════════════════════════════════════
(function () {
  'use strict';

  function makeResizer(canvas, ctx, fw, fh) {
    const dpr = window.devicePixelRatio || 1;
    let w = fw, h = fh;
    function apply() {
      const r = canvas.parentElement.getBoundingClientRect();
      w = r.width || fw; h = r.height || w * 0.65 || fh;
      canvas.width = w * dpr; canvas.height = h * dpr;
      canvas.style.width = w + 'px'; canvas.style.height = h + 'px';
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }
    apply();
    new ResizeObserver(apply).observe(canvas.parentElement);
    return { get w() { return w; }, get h() { return h; } };
  }

  // ════════════════════════════════════════════════════════
  //  ENGINE: atmospheric particle field with signal ripples
  // ════════════════════════════════════════════════════════
  function initEngine(canvas) {
    const ctx = canvas.getContext('2d');
    const D = makeResizer(canvas, ctx, 520, 340);

    // Background field particles (drifting, atmospheric)
    const field = [];
    for (let i = 0; i < 120; i++) {
      field.push({
        x: Math.random() * D.w, y: Math.random() * D.h,
        vx: (Math.random() - 0.5) * 0.15, vy: (Math.random() - 0.5) * 0.15,
        r: 0.5 + Math.random() * 1.5,
        o: 0.12 + Math.random() * 0.25,
        ph: Math.random() * Math.PI * 2,
      });
    }

    // Signal ripples (expanding rings from center)
    const ripples = [];
    const CX = () => D.w * 0.5, CY = () => D.h * 0.5;

    function spawnRipple() {
      ripples.push({ r: 0, o: 0.45, life: 1.0 });
    }

    let last = 0, engineTime = 0, nextRipple = 1800 + Math.random() * 2200;

    function render(now) {
      const dt = last ? Math.min(now - last, 40) : 16; last = now; engineTime += dt;
      const w = D.w, h = D.h;
      if (w <= 0 || h <= 0) { requestAnimationFrame(render); return; }

      ctx.clearRect(0, 0, w, h);
      const cx = CX(), cy = CY();

      // ── Central ambient glow ──
      const glow = ctx.createRadialGradient(cx, cy, w * 0.02, cx, cy, w * 0.5);
      const gAlpha = 0.04 + Math.sin(engineTime * 0.0008) * 0.015;
      glow.addColorStop(0, `rgba(88,166,255,${gAlpha + 0.06})`);
      glow.addColorStop(0.3, `rgba(88,166,255,${gAlpha})`);
      glow.addColorStop(1, 'rgba(88,166,255,0)');
      ctx.fillStyle = glow; ctx.fillRect(0, 0, w, h);

      // ── Drifting field particles ──
      field.forEach(p => {
        p.ph += 0.004;
        p.x += p.vx + Math.sin(p.ph) * 0.06;
        p.y += p.vy + Math.cos(p.ph * 1.3) * 0.06;
        if (p.x < -10) p.x = w + 10; if (p.x > w + 10) p.x = -10;
        if (p.y < -10) p.y = h + 10; if (p.y > h + 10) p.y = -10;

        const dist = Math.hypot(p.x - cx, p.y - cy);
        const a = p.o * (1 - Math.min(1, dist / (w * 0.5)));
        if (a < 0.01) return;

        ctx.beginPath(); ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
        ctx.fillStyle = `rgba(88,166,255,${a})`; ctx.fill();
      });

      // ── Signal ripples ──
      if (engineTime > nextRipple && ripples.length < 4) {
        spawnRipple();
        nextRipple = engineTime + 1200 + Math.random() * 2000;
      }

      for (let i = ripples.length - 1; i >= 0; i--) {
        const rp = ripples[i];
        rp.r += dt * 0.06;
        rp.life -= dt * 0.00035;

        if (rp.life <= 0) { ripples.splice(i, 1); continue; }

        ctx.beginPath();
        ctx.arc(cx, cy, rp.r, 0, Math.PI * 2);
        ctx.strokeStyle = `rgba(88,166,255,${rp.o * rp.life})`;
        ctx.lineWidth = 0.8 + rp.life * 0.6;
        ctx.stroke();
        if (rp.r < 4) rp.o = 0.55;
      }

      // ── Central bright point ──
      const pulse = Math.sin(engineTime * 0.003) * 0.5 + 0.5;
      const cGlow = ctx.createRadialGradient(cx, cy, 0, cx, cy, 12 + pulse * 6);
      cGlow.addColorStop(0, 'rgba(88,166,255,0.35)');
      cGlow.addColorStop(0.5, `rgba(88,166,255,${0.12 + pulse * 0.08})`);
      cGlow.addColorStop(1, 'rgba(88,166,255,0)');
      ctx.fillStyle = cGlow;
      ctx.fillRect(cx - 16, cy - 16, 32, 32);
      ctx.beginPath(); ctx.arc(cx, cy, 2.5, 0, Math.PI * 2);
      ctx.fillStyle = 'rgba(180,210,255,0.7)'; ctx.fill();

      requestAnimationFrame(render);
    }
    requestAnimationFrame(render);
  }

  // ════════════════════════════════════════════════════════
  //  AI BRAIN: breathing core with light tendrils
  // ════════════════════════════════════════════════════════
  function initAI(canvas) {
    const ctx = canvas.getContext('2d');
    const D = makeResizer(canvas, ctx, 520, 340);

    let last = 0, aiTime = 0;
    let coreRadius = 18;
    let coreMax = 32;
    let phase = 0; // 0=expand, 1=contract
    let cycleCount = 0;
    const tendrils = [];
    for (let i = 0; i < 10; i++) {
      tendrils.push({
        angle: (i / 10) * Math.PI * 2 + (Math.random() - 0.5) * 0.4,
        len: 30 + Math.random() * 70,
        curve: -0.6 + Math.random() * 1.2,
        ph: Math.random() * Math.PI * 2,
      });
    }

    function render(now) {
      const dt = last ? Math.min(now - last, 40) : 16; last = now; aiTime += dt;
      const w = D.w, h = D.h;
      if (w <= 0 || h <= 0) { requestAnimationFrame(render); return; }
      ctx.clearRect(0, 0, w, h);

      const cx = w * 0.5, cy = h * 0.5;

      // ── Breathing core cycle ──
      if (phase === 0) {
        coreRadius += dt * 0.004;
        if (coreRadius >= coreMax) { phase = 1; cycleCount++; }
      } else {
        coreRadius -= dt * 0.003;
        if (coreRadius <= 18) {
          phase = 0;
          coreMax = 28 + cycleCount * 2;
          if (coreMax > 48) { coreMax = 32; cycleCount = 0; }
        }
      }

      // ── Ambient field glow ──
      const ambientGlow = ctx.createRadialGradient(cx, cy, w * 0.02, cx, cy, w * 0.6);
      ambientGlow.addColorStop(0, `rgba(34,197,94,${0.02 + coreRadius * 0.0015})`);
      ambientGlow.addColorStop(0.5, 'rgba(34,197,94,0.01)');
      ambientGlow.addColorStop(1, 'rgba(34,197,94,0)');
      ctx.fillStyle = ambientGlow; ctx.fillRect(0, 0, w, h);

      // ── Light tendrils ──
      tendrils.forEach(t => {
        t.ph += 0.006;
        const sway = Math.sin(t.ph) * 0.15;
        const a = t.angle + sway;
        const maxLen = t.len + coreRadius * 0.6;

        ctx.beginPath();
        ctx.moveTo(cx, cy);
        const cpX = cx + Math.cos(a) * maxLen * 0.5 + Math.cos(a + Math.PI * 0.5) * t.curve * maxLen * 0.3;
        const cpY = cy + Math.sin(a) * maxLen * 0.5 + Math.sin(a + Math.PI * 0.5) * t.curve * maxLen * 0.3;
        const epX = cx + Math.cos(a) * maxLen;
        const epY = cy + Math.sin(a) * maxLen;
        ctx.quadraticCurveTo(cpX, cpY, epX, epY);
        ctx.strokeStyle = `rgba(34,197,94,${0.08 + coreRadius * 0.003})`;
        ctx.lineWidth = 0.6 + coreRadius * 0.04;
        ctx.stroke();

        if (coreRadius > 24) {
          ctx.beginPath(); ctx.arc(epX, epY, 1.5 + coreRadius * 0.04, 0, Math.PI * 2);
          ctx.fillStyle = `rgba(34,197,94,${0.15 + coreRadius * 0.005})`; ctx.fill();
        }
      });

      // ── Core itself ──
      const coreGlow = ctx.createRadialGradient(cx, cy, 0, cx, cy, coreRadius * 2.5);
      coreGlow.addColorStop(0, `rgba(34,197,94,${0.25 + coreRadius * 0.008})`);
      coreGlow.addColorStop(0.3, `rgba(34,197,94,${0.12 + coreRadius * 0.004})`);
      coreGlow.addColorStop(0.7, 'rgba(34,197,94,0.02)');
      coreGlow.addColorStop(1, 'rgba(34,197,94,0)');
      ctx.fillStyle = coreGlow;
      ctx.fillRect(cx - coreRadius * 3, cy - coreRadius * 3, coreRadius * 6, coreRadius * 6);

      ctx.beginPath();
      ctx.arc(cx, cy, coreRadius, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(34,197,94,${0.08 + coreRadius * 0.002})`;
      ctx.strokeStyle = `rgba(34,197,94,${0.2 + coreRadius * 0.006})`;
      ctx.lineWidth = 1.2;
      ctx.fill(); ctx.stroke();

      // Inner bright spot
      const innerGlow = ctx.createRadialGradient(cx, cy, 0, cx, cy, coreRadius * 0.5);
      innerGlow.addColorStop(0, 'rgba(180,255,200,0.6)');
      innerGlow.addColorStop(1, 'rgba(34,197,94,0)');
      ctx.fillStyle = innerGlow;
      ctx.fillRect(cx - coreRadius, cy - coreRadius, coreRadius * 2, coreRadius * 2);

      // ── Bloom particles on reset ──
      if (phase === 0 && coreRadius < 20 && cycleCount > 0) {
        for (let i = 0; i < 3; i++) {
          const ba = Math.random() * Math.PI * 2;
          const bd = coreRadius * 1.2;
          const bx = cx + Math.cos(ba) * bd, by = cy + Math.sin(ba) * bd;
          ctx.beginPath(); ctx.arc(bx, by, 1.5, 0, Math.PI * 2);
          ctx.fillStyle = 'rgba(34,197,94,0.5)'; ctx.fill();
        }
      }

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
