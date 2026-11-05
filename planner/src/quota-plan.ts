export type QuotaUnit = 'credits' | 'tokens' | 'requests' | 'percent';
export type QuotaPlanInput = { remaining?: unknown; resetAt?: unknown; daily?: unknown; unit?: unknown };
export type QuotaPlanErrors = Partial<Record<'remaining' | 'resetAt' | 'daily', 'required' | 'balance' | 'percent' | 'future'>>;
const DAY_MS = 86_400_000;

function finiteNumber(value: unknown): number | null {
  if (typeof value !== 'string' && typeof value !== 'number') return null;
  if (typeof value === 'string' && value.trim() === '') return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function amount(value: unknown): number | null {
  const parsed = finiteNumber(value);
  return parsed !== null && parsed >= 0 && parsed <= 1_000_000 ? parsed : null;
}

function outsidePercentRange(value: unknown): boolean {
  const parsed = finiteNumber(value);
  return parsed !== null && (parsed < 0 || parsed > 100);
}

export function validateQuotaPlan(input: QuotaPlanInput, now = Date.now()): QuotaPlanErrors {
  const errors: QuotaPlanErrors = {};
  const remaining = amount(input.remaining);
  if (input.unit === 'percent' && outsidePercentRange(input.remaining)) errors.remaining = 'percent';
  else if (remaining === null) errors.remaining = typeof input.remaining === 'string' && input.remaining.trim() === '' || input.remaining == null ? 'required' : 'balance';

  if (input.daily !== undefined) {
    const daily = amount(input.daily);
    if (input.unit === 'percent' && outsidePercentRange(input.daily)) errors.daily = 'percent';
    else if (daily === null) errors.daily = typeof input.daily === 'string' && input.daily.trim() === '' || input.daily == null ? 'required' : 'balance';
  }

  const resetAt = typeof input.resetAt === 'string' ? Date.parse(input.resetAt) : NaN;
  if (!Number.isFinite(resetAt) || resetAt <= now) errors.resetAt = 'future';
  return errors;
}

export function calculateDailyBudget(input: QuotaPlanInput, now = Date.now()): number | null {
  if (Object.keys(validateQuotaPlan(input, now)).length) return null;
  const remaining = amount(input.remaining);
  const resetAt = typeof input.resetAt === 'string' ? Date.parse(input.resetAt) : NaN;
  if (remaining === null || !Number.isFinite(resetAt)) return null;
  return remaining / ((resetAt - now) / DAY_MS);
}
