// ═══════════════════════════════════════════════════════════
// Nudge — technical abstract animations (Canvas 2D, 60fps)
// ═══════════════════════════════════════════════════════════

(function () {
  'use strict';

  // ── Colors ──────────────────────────────────────────────
  const BLUE = '#58A6FF';
  const BLUE_LOW = 'rgba(88,166,255,0.12)';
  const GREEN = '#22C55E';
  const GREEN_LOW = 'rgba(34,197,94,0.12)';
  const INDIGO = '#818CF8';
  const TEAL = '#2DD4BF';

  // ════════════════════════════════════════════════════════
  //  ENGINE: 3 signals → fusion node → 26 radiating lines
  // ════════════════════════════════════════════════════════

  function initEngine(canvas) {
    const ctx = canvas.getContext('2d');
    const dpr = window.devicePixelRatio || 1;

    function resize() {
      const rect = canvas.parentElement.getBoundingClientRect();
      const w = rect.width;
      const h = rect.height || w * 0.65;
      canvas.width = w * dpr;
      canvas.height = h * dpr;
      canvas.style.width = w + 'px';
      canvas.style.height = h + 'px';
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      return { w, h };
    }

    let dims = resize();

    // Particle streams — each stream has 20 particles
    const N = 20;
    const streams = [
      { x1: 0, y1: dims.h * 0.22, x2: dims.w * 0.38, y2: dims.h * 0.5, color: BLUE, delay: 0 },
      { x1: 0, y1: dims.h * 0.50, x2: dims.w * 0.38, y2: dims.h * 0.5, color: GREEN, delay: 600 },
      { x1: 0, y1: dims.h * 0.78, x2: dims.w * 0.38, y2: dims.h * 0.5, color: INDIGO, delay: 1200 },
    ];

    const particles = [];
    streams.forEach((s) => {
      for (let i = 0; i < N; i++) {
        particles.push({ stream: s, t: -i * 0.05, speed: 0.008 + Math.random() * 0.004 });
      }
    });

    const node = { x: dims.w * 0.42, y: dims.h * 0.5, r: dims.h * 0.06, pulse: 0 };

    function lerp(a, b, t) { return a + (b - a) * t; }
    function easeInOut(t) { return t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t; }

    let last = 0;
    let totalTime = 0;

    function render(now) {
      const dt = last ? Math.min(now - last, 50) : 16;
      last = now;
      totalTime += dt;

      dims = resize();
      const { w, h } = dims;
      ctx.clearRect(0, 0, w, h);

      // Update streams
      streams.forEach(s => {
        s.x1 = 0; s.y1 = h * 0.22;
        s.x2 = w * 0.38; s.y2 = h * 0.5;
        // if multiple streams use different starting y's
      });
      streams[0].y1 = h * 0.22;
      streams[1].y1 = h * 0.50;
      streams[2].y1 = h * 0.78;
      node.x = w * 0.42;
      node.y = h * 0.5;
      node.r = h * 0.06;

      // ── Draw connection lines ──
      streams.forEach(s => {
        ctx.beginPath();
        ctx.moveTo(s.x1, s.y1);
        ctx.lineTo(node.x, node.y);
        ctx.strokeStyle = 'rgba(88,166,255,0.08)';
        ctx.lineWidth = 1;
        ctx.stroke();
      });

      // ── Draw particles ──
      particles.forEach((p) => {
        const elapsed = totalTime - p.stream.delay;
        if (elapsed < 0) return;
        p.t += p.speed * (dt / 16);
        if (p.t > 1) p.t = 0;
        const et = easeInOut(p.t);
        const x = lerp(p.stream.x1, p.stream.x2, et);
        const y = lerp(p.stream.y1, p.stream.y2, et);
        const alpha = p.t < 0.15 ? p.t / 0.15 : p.t > 0.85 ? (1 - p.t) / 0.15 : 1;
        ctx.beginPath();
        ctx.arc(x, y, 2.5, 0, Math.PI * 2);
        ctx.fillStyle = p.stream.color;
        ctx.globalAlpha = alpha * 0.8;
        ctx.fill();

        // Trigger node pulse when particle is near end
        if (p.t > 0.88 && p.t < 0.92) {
          node.pulse = 0.5;
        }
      });
      ctx.globalAlpha = 1;

      // ── Node glow ──
      node.pulse = Math.max(0, node.pulse - dt * 0.001);
      const pulseAlpha = 0.15 + node.pulse * 0.3;
      const glow = ctx.createRadialGradient(node.x, node.y, node.r * 0.3, node.x, node.y, node.r * 2.5);
      glow.addColorStop(0, `rgba(88,166,255,${pulseAlpha + 0.15})`);
      glow.addColorStop(0.4, `rgba(88,166,255,${pulseAlpha})`);
      glow.addColorStop(1, 'rgba(88,166,255,0)');
      ctx.fillStyle = glow;
      ctx.fillRect(node.x - node.r * 3, node.y - node.r * 3, node.r * 6, node.r * 6);

      // Node circle
      ctx.beginPath();
      ctx.arc(node.x, node.y, node.r, 0, Math.PI * 2);
      ctx.fillStyle = BLUE_LOW;
      ctx.fill();
      ctx.strokeStyle = 'rgba(88,166,255,0.3)';
      ctx.lineWidth = 1.5;
      ctx.stroke();

      // ── Radiating lines ──
      for (let i = 0; i < 26; i++) {
        const angle = (i / 26) * Math.PI * 2 - Math.PI * 0.5;
        const endX = node.x + Math.cos(angle) * w * 0.38;
        const endY = node.y + Math.sin(angle) * h * 0.7;
        const grad = ctx.createLinearGradient(node.x, node.y, endX, endY);
        grad.addColorStop(0, 'rgba(88,166,255,0.2)');
        grad.addColorStop(1, 'rgba(88,166,255,0)');
        ctx.beginPath();
        ctx.moveTo(node.x, node.y);
        ctx.lineTo(endX, endY);
        ctx.strokeStyle = grad;
        ctx.lineWidth = 0.6;
        ctx.stroke();
      }

      // Labels
      ctx.font = `${Math.max(8, h * 0.045)}px system-ui, -apple-system, sans-serif`;
      ctx.fillStyle = 'rgba(88,166,255,0.45)';
      ctx.fillText('app', 6, h * 0.24);
      ctx.fillText('idle', 6, h * 0.52);
      ctx.fillText('domain', 6, h * 0.80);

      requestAnimationFrame(render);
    }

    requestAnimationFrame(render);
  }

  // ════════════════════════════════════════════════════════
  //  AI MODEL: data pulse → model → YES/NO + feedback loop
  // ════════════════════════════════════════════════════════

  function initAI(canvas) {
    const ctx = canvas.getContext('2d');
    const dpr = window.devicePixelRatio || 1;

    function resize() {
      const rect = canvas.parentElement.getBoundingClientRect();
      const w = rect.width;
      const h = rect.height || w * 0.65;
      canvas.width = w * dpr;
      canvas.height = h * dpr;
      canvas.style.width = w + 'px';
      canvas.style.height = h + 'px';
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      return { w, h };
    }

    let dims = resize();
    let last = 0;
    let cycleTime = 0;
    let coreBrightness = 0.08; // starts dim, brightens over cycles
    let cycleCount = 0;

    function render(now) {
      const dt = last ? Math.min(now - last, 50) : 16;
      last = now;
      cycleTime += dt;
      if (cycleTime > 8000) { cycleTime = 0; cycleCount++; coreBrightness = 0.08 + cycleCount * 0.05; if (coreBrightness > 0.25) { coreBrightness = 0.08; cycleCount = 0; } }

      dims = resize();
      const { w, h } = dims;

      const inputX = 0;
      const inputW = w * 0.22;
      const nodeX = w * 0.28;
      const nodeW = w * 0.36;
      const nodeH = h * 0.5;
      const nodeY = h * 0.25;
      const outputY1 = h * 0.30;
      const outputY2 = h * 0.60;
      const outputEnd = w * 0.9;

      ctx.clearRect(0, 0, w, h);

      // ── Input pulse ──
      const pulseProgress = (cycleTime % 3000) / 3000;
      const pulseX = lerp(inputX + inputW * 0.2, nodeX - 4, pulseProgress);
      const dashOffset = cycleTime * 0.05 % 12;

      // Input line
      ctx.beginPath();
      ctx.moveTo(inputX + inputW * 0.2, h * 0.5);
      ctx.lineTo(nodeX - 4, h * 0.5);
      ctx.setLineDash([4, 4]);
      ctx.lineDashOffset = -dashOffset;
      ctx.strokeStyle = GREEN_LOW;
      ctx.lineWidth = 1;
      ctx.stroke();
      ctx.setLineDash([]);

      // Input dot
      ctx.beginPath();
      ctx.arc(pulseX, h * 0.5, 3, 0, Math.PI * 2);
      ctx.fillStyle = GREEN;
      ctx.globalAlpha = 0.9;
      ctx.fill();
      ctx.globalAlpha = 1;

      // ── Model core ──
      const bx = nodeX, by = nodeY, bw = nodeW, bh = nodeH;
      ctx.fillStyle = `rgba(34,197,94,${coreBrightness})`;
      ctx.strokeStyle = `rgba(34,197,94,${0.3 + coreBrightness})`;
      ctx.lineWidth = 1.5;
      roundRect(ctx, bx, by, bw, bh, 8);
      ctx.fill();
      ctx.stroke();

      // Core inner particles
      for (let i = 0; i < 6; i++) {
        const angle = (cycleTime * 0.001 + i * 1.05) % (Math.PI * 2);
        const dist = (i % 2 === 0 ? 0.3 : 0.6) * bw * 0.3;
        const px = bx + bw * 0.5 + Math.cos(angle) * dist;
        const py = by + bh * 0.5 + Math.sin(angle) * dist * 0.6;
        ctx.beginPath();
        ctx.arc(px, py, 1.5, 0, Math.PI * 2);
        ctx.fillStyle = GREEN;
        ctx.globalAlpha = 0.6;
        ctx.fill();
      }
      ctx.globalAlpha = 1;

      // Core label
      ctx.font = `${Math.max(8, h * 0.055)}px system-ui, -apple-system, sans-serif`;
      ctx.fillStyle = GREEN;
      ctx.textAlign = 'center';
      ctx.fillText('scikit-learn', bx + bw * 0.5, by + bh * 0.45);
      ctx.font = `${Math.max(7, h * 0.04)}px system-ui, -apple-system, sans-serif`;
      ctx.fillStyle = 'rgba(34,197,94,0.5)';
      ctx.fillText('classifier', bx + bw * 0.5, by + bh * 0.65);
      ctx.textAlign = 'start';

      // ── Output lines ──
      const outX = bx + bw;
      ctx.beginPath();
      ctx.moveTo(outX, by + bh * 0.3);
      ctx.lineTo(outputEnd, outputY1);
      ctx.strokeStyle = GREEN_LOW;
      ctx.lineWidth = 1;
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(outX, by + bh * 0.7);
      ctx.lineTo(outputEnd, outputY2);
      ctx.strokeStyle = GREEN_LOW;
      ctx.lineWidth = 1;
      ctx.stroke();

      // Output dots
      const outProgress = (cycleTime % 2000) / 2000;
      const dot1X = lerp(outX + 4, outputEnd - 20, outProgress);
      const dot1Y = lerp(by + bh * 0.3, outputY1, outProgress);
      const dot2X = lerp(outX + 4, outputEnd - 20, (outProgress + 0.3) % 1);
      const dot2Y = lerp(by + bh * 0.7, outputY2, (outProgress + 0.3) % 1);

      ctx.beginPath();
      ctx.arc(dot1X, dot1Y, 2.5, 0, Math.PI * 2);
      ctx.fillStyle = GREEN;
      ctx.fill();
      ctx.beginPath();
      ctx.arc(dot2X, dot2Y, 2.5, 0, Math.PI * 2);
      ctx.fillStyle = GREEN;
      ctx.fill();

      // Output labels
      ctx.font = `${Math.max(8, h * 0.05)}px system-ui, -apple-system, sans-serif`;
      ctx.fillStyle = GREEN;
      ctx.textAlign = 'center';
      ctx.fillText('YES', outputEnd, outputY1 - 12);
      ctx.fillText('NO', outputEnd, outputY2 + 16);
      ctx.textAlign = 'start';

      // ── Feedback arc ──
      const arcProgress = (cycleTime % 4000) / 4000;
      const feedbackPath = new Path2D();
      feedbackPath.moveTo(outputEnd - 10, by + bh * 0.5);
      feedbackPath.quadraticCurveTo(w * 0.5, h * 0.85, nodeX + bw * 0.5, by + bh + 4);
      ctx.strokeStyle = 'rgba(34,197,94,0.15)';
      ctx.lineWidth = 1;
      ctx.setLineDash([3, 5]);
      ctx.lineDashOffset = -arcProgress * 16;
      ctx.stroke(feedbackPath);
      ctx.setLineDash([]);

      // Feedback dot
      const ax = lerp(outputEnd - 10, nodeX + bw * 0.5, arcProgress);
      const ay = quadLerp(outputEnd - 10, by + bh * 0.5, w * 0.5, h * 0.85, nodeX + bw * 0.5, by + bh + 4, arcProgress);
      ctx.beginPath();
      ctx.arc(ax, ay, 2, 0, Math.PI * 2);
      ctx.fillStyle = GREEN;
      ctx.globalAlpha = 0.8;
      ctx.fill();
      ctx.globalAlpha = 1;

      // Feedback label
      if (arcProgress > 0.3 && arcProgress < 0.8) {
        ctx.font = `${Math.max(6, h * 0.035)}px system-ui, -apple-system, sans-serif`;
        ctx.fillStyle = 'rgba(34,197,94,0.35)';
        ctx.textAlign = 'center';
        ctx.fillText('retrains at 50+', w * 0.5, h * 0.88);
        ctx.textAlign = 'start';
      }

      requestAnimationFrame(render);
    }

    requestAnimationFrame(render);
  }

  function lerp(a, b, t) { return a + (b - a) * t; }
  function quadLerp(p0x, p0y, p1x, p1y, p2x, p2y, t) {
    const mt = 1 - t;
    const x = mt * mt * p0x + 2 * mt * t * p1x + t * t * p2x;
    const y = mt * mt * p0y + 2 * mt * t * p1y + t * t * p2y;
    return y;
  }
  function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.arcTo(x + w, y, x + w, y + r, r);
    ctx.lineTo(x + w, y + h - r);
    ctx.arcTo(x + w, y + h, x + w - r, y + h, r);
    ctx.lineTo(x + r, y + h);
    ctx.arcTo(x, y + h, x, y + h - r, r);
    ctx.lineTo(x, y + r);
    ctx.arcTo(x, y, x + r, y, r);
    ctx.closePath();
  }

  // ── Init ──────────────────────────────────────────────
  window.addEventListener('DOMContentLoaded', () => {
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
    }, { threshold: 0.2 });

    if (engineCanvas) observer.observe(engineCanvas);
    if (aiCanvas) observer.observe(aiCanvas);
  });
})();
