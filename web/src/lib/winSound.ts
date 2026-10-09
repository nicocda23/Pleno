/** Fanfarria corta sintetizada con Web Audio (sin archivos). Silenciosa si el navegador no la soporta. */
export function playWinSound(): void {
  try {
    const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!Ctx) return;
    const ctx = new Ctx();
    const master = ctx.createGain();
    master.gain.value = 0.18;
    master.connect(ctx.destination);

    // Arpegio ascendente do-mi-sol-do y un acorde final.
    const notes = [523.25, 659.25, 783.99, 1046.5];
    notes.forEach((freq, i) => tone(ctx, master, freq, ctx.currentTime + i * 0.11, 0.22));
    const end = ctx.currentTime + notes.length * 0.11;
    [523.25, 659.25, 783.99, 1046.5].forEach((freq) => tone(ctx, master, freq, end, 0.7));

    window.setTimeout(() => void ctx.close(), 1500);
  } catch {
    /* el sonido es decorativo: nunca debe romper la ronda */
  }
}

function tone(ctx: AudioContext, out: AudioNode, freq: number, start: number, duration: number) {
  const osc = ctx.createOscillator();
  const gain = ctx.createGain();
  osc.type = "triangle";
  osc.frequency.value = freq;
  gain.gain.setValueAtTime(0.0001, start);
  gain.gain.exponentialRampToValueAtTime(1, start + 0.02);
  gain.gain.exponentialRampToValueAtTime(0.0001, start + duration);
  osc.connect(gain).connect(out);
  osc.start(start);
  osc.stop(start + duration + 0.05);
}
