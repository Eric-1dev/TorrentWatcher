import { ref } from 'vue'
import { api } from '../api/client'
import type { SystemStatusView } from '../api/types'
import { usePolling } from './usePolling'

/** Опрос /api/system/status раз в 15 секунд; ошибки не шумят тостами. */
export function useSystemStatus(intervalMs = 15_000) {
  const status = ref<SystemStatusView | null>(null)
  const error = ref<string | null>(null)

  async function refresh() {
    try {
      status.value = await api.getSystemStatus()
      error.value = null
    } catch (exception) {
      error.value =
        exception instanceof Error ? exception.message : 'Не удалось получить статус.'
    }
  }

  const { refresh: poll } = usePolling(refresh, intervalMs)

  return { status, error, refresh: poll }
}
