const dateTime = new Intl.DateTimeFormat('en-ZA', { dateStyle: 'medium', timeStyle: 'short' })

/** The API sends UTC ISO strings; show them in the viewer's local time. */
export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso))
}
