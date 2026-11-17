export type TorrentCheckOutcome = 'Never' | 'Unchanged' | 'Changed' | 'Error'

export type ScanTrigger = 'Auto' | 'Manual'

export type ScanRunState = 'Running' | 'Completed' | 'Failed'

export interface TorrentView {
  id: number
  topicId: number
  topicUrl: string
  title: string
  infoHashV1: string
  torrentSizeBytes: number
  fileCount: number
  lastChangedAtUtc: string | null
  lastCheckedAtUtc: string | null
  lastCheckOutcome: TorrentCheckOutcome
  lastError: string | null
  isEnabled: boolean
  torrentFileDownloadedAtUtc: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface TorrentListResult {
  items: TorrentView[]
  totalCount: number
  updateAvailableCount: number
  hasUpdates: boolean
}

export interface TorrentCheckResult {
  torrent: TorrentView
  changed: boolean
  errorCode: string | null
  shouldStopScan: boolean
  success: boolean
}

export interface ScanStatus {
  runId: string
  trigger: ScanTrigger
  startedAtUtc: string
  finishedAtUtc: string | null
  status: ScanRunState
  torrentsTotal: number
  changedCount: number
  failedCount: number
  message: string | null
  isRunning: boolean
}

export interface ScanStartResult {
  status: ScanStatus
  started: boolean
  runId: string
}

export interface SettingsView {
  hasCookie: boolean
  maskedCookie: string | null
  cookieVerifiedAtUtc: string | null
  cookieLastError: string | null
  scanIntervalMinutes: number
  autoScanEnabled: boolean
  requestDelayMs: number
  requestTimeoutSeconds: number
  lastScanAtUtc: string | null
  updatedAtUtc: string
}

export interface UpdateSettingsInput {
  rutrackerCookie?: string | null
  scanIntervalMinutes: number
  autoScanEnabled: boolean
  requestDelayMs: number
  requestTimeoutSeconds: number
}

export interface CookieTestResult {
  success: boolean
  checkedAtUtc: string
  message: string
}

export interface SystemStatusView {
  version: string
  currentRun: ScanStatus | null
  lastScanAtUtc: string | null
  nextScanAtUtc: string | null
  autoScanEnabled: boolean
  scanIntervalMinutes: number
  torrentCount: number
  updateAvailableCount: number
  cookieConfigured: boolean
  cookieVerifiedAtUtc: string | null
  cookieLastError: string | null
}

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  code?: string
  traceId?: string
}
