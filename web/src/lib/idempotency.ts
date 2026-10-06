/** A fresh key per intended change. Retrying the same change re-sends the same key; a new click gets a new one. */
export function newIdempotencyKey(): string {
  return crypto.randomUUID()
}
