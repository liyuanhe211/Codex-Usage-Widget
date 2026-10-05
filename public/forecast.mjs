const number = value => typeof value === 'number' && Number.isFinite(value);
const unknown = () => ({ kind:'unknown', text:'', percentAtReset:null, secondsBeforeReset:null });

function durationText(seconds) {
  const totalHours = Math.floor(seconds / 3600);
  if (seconds < 60) return '<1 m';
  if (totalHours === 0) return Math.floor(seconds / 60) + ' m';
  if (totalHours < 24) return totalHours + ' h';
  return Math.floor(totalHours / 24) + ' d ' + totalHours % 24 + ' h';
}

export function weeklyForecast(window, nowSeconds = Date.now() / 1000) {
  if (window?.kind !== 'weekly' || !number(window.usedPercent) || window.usedPercent <= 0
      || window.usedPercent > 100 || !number(window.durationMinutes) || window.durationMinutes <= 0
      || !number(window.resetsAt) || !number(nowSeconds)) return unknown();
  const durationSeconds = window.durationMinutes * 60;
  const remainingSeconds = window.resetsAt - nowSeconds;
  const elapsedSeconds = durationSeconds - remainingSeconds;
  if (!Number.isFinite(durationSeconds) || elapsedSeconds <= 0 || remainingSeconds <= 0) return unknown();
  const percentAtReset = window.usedPercent * durationSeconds / elapsedSeconds;
  if (!Number.isFinite(percentAtReset)) return unknown();
  if (percentAtReset > 100 + 1e-9) {
    const rate = window.usedPercent / elapsedSeconds;
    const secondsUntilExhaustion = (100 - window.usedPercent) / rate;
    const secondsBeforeReset = Math.max(0, remainingSeconds - secondsUntilExhaustion);
    const showTimeUntilExhaustion = secondsUntilExhaustion <= secondsBeforeReset;
    const displayedSeconds = showTimeUntilExhaustion ? secondsUntilExhaustion : secondsBeforeReset;
    return { kind:'exhaustion', percentAtReset, secondsBeforeReset,
      text:'Runs out ' + (showTimeUntilExhaustion ? 'in ' : '') + durationText(displayedSeconds)
        + (showTimeUntilExhaustion ? '' : ' before reset') };
  }
  return { kind:'consumption', percentAtReset, secondsBeforeReset:null,
    text:'Will consume ' + Math.floor(percentAtReset) + '% before reset.' };
}
