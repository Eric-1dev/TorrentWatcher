import type {
  CloudflareSolveResult,
  CookieTestResult,
  ProblemDetails,
  RutrackerLoginResult,
  ScanStartResult,
  SettingsView,
  SystemStatusView,
  TorrentApplyResult,
  TorrentCheckResult,
  TorrentClientTestResult,
  TorrentListResult,
  TorrentView,
  UpdateSettingsInput,
} from './types'

/** Ошибка API: HTTP-статус + разобранный ProblemDetails (RFC 7807). */
export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  get code(): string {
    return this.problem?.code ?? 'UNKNOWN'
  }
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  const contentType = response.headers.get('content-type') ?? ''

  if (contentType.includes('json')) {
    try {
      return (await response.json()) as ProblemDetails
    } catch {
      return null
    }
  }

  const text = (await response.text()).trim()
  return text.length === 0
    ? null
    : {
        status: response.status,
        title: response.statusText,
        detail: text.slice(0, 500),
      }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  let response: Response
  try {
    response = await fetch(`${apiBaseUrl}${path}`, { ...init, headers })
  } catch (error) {
    throw new ApiError(
      0,
      null,
      error instanceof Error ? error.message : 'Сервер недоступен.',
    )
  }

  if (response.status === 204) {
    return undefined as T
  }

  if (!response.ok) {
    const problem = await readProblem(response)
    throw new ApiError(
      response.status,
      problem,
      problem?.detail ?? problem?.title ?? `Ошибка API (HTTP ${response.status}).`,
    )
  }

  if (!(response.headers.get('content-type') ?? '').includes('json')) {
    return undefined as T
  }

  return (await response.json()) as T
}

export const api = {
  getTorrents: () => request<TorrentListResult>('/api/torrents'),

  addTorrent: (url: string) =>
    request<TorrentView>('/api/torrents', {
      method: 'POST',
      body: JSON.stringify({ url }),
    }),

  deleteTorrent: (id: number) =>
    request<void>(`/api/torrents/${id}`, { method: 'DELETE' }),

  setTorrentEnabled: (id: number, isEnabled: boolean) =>
    request<TorrentView>(`/api/torrents/${id}/enabled`, {
      method: 'PUT',
      body: JSON.stringify({ isEnabled }),
    }),

  checkTorrent: (id: number) =>
    request<TorrentCheckResult>(`/api/torrents/${id}/check`, { method: 'POST' }),

  applyTorrent: (id: number) =>
    request<TorrentApplyResult>(`/api/torrents/${id}/apply`, { method: 'POST' }),

  checkAll: () =>
    request<ScanStartResult>('/api/torrents/check-all', { method: 'POST' }),

  getSettings: () => request<SettingsView>('/api/settings'),

  updateSettings: (input: UpdateSettingsInput) =>
    request<SettingsView>('/api/settings', {
      method: 'PUT',
      body: JSON.stringify(input),
    }),

  loginRutracker: () =>
    request<RutrackerLoginResult>('/api/settings/rutracker/login', { method: 'POST' }),

  testCookie: () =>
    request<CookieTestResult>('/api/settings/test-cookie', { method: 'POST' }),

  testQbittorrent: () =>
    request<TorrentClientTestResult>('/api/settings/qbittorrent/test', { method: 'POST' }),

  solveCloudflare: () =>
    request<CloudflareSolveResult>('/api/settings/cloudflare/solve', { method: 'POST' }),

  getSystemStatus: () => request<SystemStatusView>('/api/system/status'),
}
