const byteUnits = ['Б', 'КиБ', 'МиБ', 'ГиБ', 'ТиБ']

/** Размер в байтах в человекочитаемом виде (binary units). */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return '—'
  }

  let value = bytes
  let unitIndex = 0
  while (value >= 1024 && unitIndex < byteUnits.length - 1) {
    value /= 1024
    unitIndex++
  }

  const digits = value >= 100 || unitIndex === 0 ? 0 : 1
  return `${value.toFixed(digits)} ${byteUnits[unitIndex]}`
}

const dateTimeFormatter = new Intl.DateTimeFormat('ru-RU', {
  dateStyle: 'short',
  timeStyle: 'short',
})

/** UTC-время из API → локальное время браузера. */
export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—'
  }

  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : dateTimeFormatter.format(date)
}
