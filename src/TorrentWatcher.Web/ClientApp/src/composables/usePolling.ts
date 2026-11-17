import { onBeforeUnmount, onMounted } from 'vue'

/**
 * Периодический вызов задачи. Ошибки задачи не глушатся здесь: вызывающий код
 * сам решает, показывать ли тост. Пересечения вызовов не допускаются.
 */
export function usePolling(
  task: () => Promise<void> | void,
  intervalMs = 15_000,
  runImmediately = true,
  onError?: (error: unknown) => void,
) {
  let timer: ReturnType<typeof setInterval> | undefined
  let inFlight = false
  let stopped = false

  async function tick() {
    if (stopped || inFlight) {
      return
    }

    inFlight = true
    try {
      await task()
    } catch (error) {
      onError?.(error)
    } finally {
      inFlight = false
    }
  }

  onMounted(() => {
    if (runImmediately) {
      void tick()
    }

    timer = setInterval(() => void tick(), intervalMs)
  })

  onBeforeUnmount(() => {
    stopped = true
    if (timer !== undefined) {
      clearInterval(timer)
    }
  })

  return { refresh: tick }
}
