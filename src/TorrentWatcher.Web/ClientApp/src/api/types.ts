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
  appliedInfoHashV1: string | null
  appliedAtUtc: string | null
  applyError: string | null
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

export interface TorrentApplyResult {
  torrent: TorrentView
  applied: boolean
  errorCode: string | null
  message: string
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
  rutrackerLogin: string | null
  hasPassword: boolean
  hasCredentials: boolean
  cookieVerifiedAtUtc: string | null
  cookieLastError: string | null
  hasCloudflareClearance: boolean
  cloudflareUserAgent: string | null
  cloudflareClearanceObtainedAtUtc: string | null
  cloudflareClearanceExpiresAtUtc: string | null
  cloudflareLastError: string | null
  scanIntervalMinutes: number
  autoScanEnabled: boolean
  requestDelayMs: number
  requestTimeoutSeconds: number
  lastScanAtUtc: string | null
  updatedAtUtc: string
  qbittorrentUrl: string | null
  qbittorrentUsername: string | null
  hasQbittorrentPassword: boolean
  qbittorrentConfigured: boolean
  qbittorrentAutoApply: boolean
  qbittorrentVerifiedAtUtc: string | null
  qbittorrentLastError: string | null
}

export interface UpdateSettingsInput {
  rutrackerCookie?: string | null
  rutrackerLogin?: string | null
  rutrackerPassword?: string | null
  scanIntervalMinutes: number
  autoScanEnabled: boolean
  requestDelayMs: number
  requestTimeoutSeconds: number
  cloudflareClearanceCookie?: string | null
  cloudflareUserAgent?: string | null
  qbittorrentUrl?: string | null
  qbittorrentUsername?: string | null
  qbittorrentPassword?: string | null
  qbittorrentAutoApply?: boolean | null
}

export interface TorrentClientTestResult {
  success: boolean
  checkedAtUtc: string
  message: string
}

export interface CloudflareSolveResult {
  success: boolean
  message: string
  obtainedAtUtc: string | null
  expiresAtUtc: string | null
}

export interface CookieTestResult {
  success: boolean
  checkedAtUtc: string
  message: string
}

export interface RutrackerLoginResult {
  success: boolean
  message: string
  loggedInAtUtc: string | null
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
