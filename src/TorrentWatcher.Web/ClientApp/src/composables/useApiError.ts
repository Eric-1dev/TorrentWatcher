import { useToast } from 'primevue/usetoast'
import { ApiError } from '../api/client'

/** Единый показ ошибок API в тостах PrimeVue. */
export function useApiError() {
  const toast = useToast()

  function showError(error: unknown, fallback = 'Не удалось выполнить запрос.') {
    let detail = fallback

    if (error instanceof ApiError) {
      detail = error.message
    } else if (error instanceof Error && error.message.length > 0) {
      detail = error.message
    }

    toast.add({
      severity: 'error',
      summary: 'Ошибка',
      detail,
      life: 6000,
    })
  }

  function showSuccess(detail: string) {
    toast.add({
      severity: 'success',
      summary: 'Готово',
      detail,
      life: 3500,
    })
  }

  function showInfo(detail: string) {
    toast.add({
      severity: 'info',
      summary: 'Информация',
      detail,
      life: 4000,
    })
  }

  return { showError, showSuccess, showInfo }
}
