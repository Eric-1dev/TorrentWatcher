<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import Button from 'primevue/button'
import Column from 'primevue/column'
import ConfirmDialog from 'primevue/confirmdialog'
import DataTable from 'primevue/datatable'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import ToggleSwitch from 'primevue/toggleswitch'
import { useConfirm } from 'primevue/useconfirm'
import { api } from '../api/client'
import type { TorrentView } from '../api/types'
import { useApiError } from '../composables/useApiError'
import { useSystemStatus } from '../composables/useSystemStatus'
import { formatBytes, formatDateTime } from '../utils/format'

const confirm = useConfirm()
const { showError, showInfo, showSuccess } = useApiError()
const { status, refresh: refreshStatus } = useSystemStatus()

const torrents = ref<TorrentView[]>([])
const loading = ref(false)
const adding = ref(false)
const checkingAll = ref(false)
const newUrl = ref('')
const busyIds = reactive(new Set<number>())
const now = ref(Date.now())

let clock: ReturnType<typeof setInterval> | undefined

const cookieError = computed(() => status.value?.cookieLastError ?? null)

const scanLine = computed(() => {
  const run = status.value?.currentRun
  if (!run) {
    return 'Скан ещё не запускался.'
  }

  if (run.isRunning) {
    return `Идёт скан: ${run.torrentsTotal} торрентов…`
  }

  if (run.status === 'Failed') {
    return run.message ?? 'Последний скан завершился с ошибкой.'
  }

  return `Последний скан: обновлено ${run.changedCount}, ошибок ${run.failedCount}.`
})

const nextScanLine = computed(() => {
  if (!status.value?.autoScanEnabled) {
    return 'Автоскан выключен.'
  }

  const nextAt = status.value.nextScanAtUtc
  if (!nextAt) {
    return 'Следующий автоскан: скоро.'
  }

  const diffMs = new Date(nextAt).getTime() - now.value
  return diffMs <= 0
    ? 'Следующий автоскан: вот-вот.'
    : `До автоскана: ${formatDuration(diffMs)}.`
})

onMounted(() => {
  void loadTorrents()
  clock = setInterval(() => {
    now.value = Date.now()
  }, 1000)
})

onBeforeUnmount(() => {
  if (clock !== undefined) {
    clearInterval(clock)
  }
})

watch(
  () => status.value?.currentRun?.isRunning ?? false,
  (running, wasRunning) => {
    if (wasRunning && !running) {
      void loadTorrents()
    }
  },
)

async function loadTorrents() {
  loading.value = true
  try {
    const result = await api.getTorrents()
    torrents.value = result.items
  } catch (error) {
    showError(error, 'Не удалось загрузить список торрентов.')
  } finally {
    loading.value = false
  }
}

async function addTorrent() {
  const url = newUrl.value.trim()
  if (url.length === 0) {
    showError(new Error('Введите ссылку на топик рутрекера.'))
    return
  }

  adding.value = true
  try {
    const created = await api.addTorrent(url)
    torrents.value = [created, ...torrents.value]
    newUrl.value = ''
    showSuccess(`«${created.title}» добавлен.`)
    await refreshStatus()
  } catch (error) {
    showError(error, 'Не удалось добавить торрент.')
  } finally {
    adding.value = false
  }
}

async function checkAll() {
  checkingAll.value = true
  try {
    const result = await api.checkAll()
    showInfo(
      result.started
        ? 'Проверка всех торрентов запущена.'
        : 'Проверка уже идёт, дождитесь её завершения.',
    )
    await refreshStatus()
  } catch (error) {
    showError(error, 'Не удалось запустить проверку.')
  } finally {
    checkingAll.value = false
  }
}

async function checkOne(torrent: TorrentView) {
  if (busyIds.has(torrent.id)) {
    return
  }

  busyIds.add(torrent.id)
  try {
    const result = await api.checkTorrent(torrent.id)
    replaceTorrent(result.torrent)

    if (!result.success) {
      showError(
        new Error(result.torrent.lastError ?? 'Проверка завершилась ошибкой.'),
      )
    } else if (result.changed) {
      showSuccess(`Обнаружено обновление: «${result.torrent.title}».`)
    } else {
      showInfo('Изменений нет.')
    }

    await refreshStatus()
  } catch (error) {
    showError(error, 'Не удалось проверить торрент.')
  } finally {
    busyIds.delete(torrent.id)
  }
}

async function toggleTorrent(torrent: TorrentView, isEnabled: boolean) {
  if (busyIds.has(torrent.id)) {
    return
  }

  busyIds.add(torrent.id)
  try {
    const updated = await api.setTorrentEnabled(torrent.id, isEnabled)
    replaceTorrent(updated)
    showSuccess(isEnabled ? 'Торрент вернулся в автоскан.' : 'Торрент поставлен на паузу.')
  } catch (error) {
    showError(error, 'Не удалось изменить состояние торрента.')
  } finally {
    busyIds.delete(torrent.id)
  }
}

function askDelete(torrent: TorrentView) {
  confirm.require({
    header: 'Удаление торрента',
    message: `Убрать «${torrent.title}» из отслеживания? Файл .torrent будет удалён.`,
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Удалить',
    rejectLabel: 'Отмена',
    acceptClass: 'p-button-danger',
    accept: () => {
      void deleteTorrent(torrent)
    },
  })
}

async function deleteTorrent(torrent: TorrentView) {
  if (busyIds.has(torrent.id)) {
    return
  }

  busyIds.add(torrent.id)
  try {
    await api.deleteTorrent(torrent.id)
    torrents.value = torrents.value.filter((item) => item.id !== torrent.id)
    showSuccess(`«${torrent.title}» больше не отслеживается.`)
    await refreshStatus()
  } catch (error) {
    showError(error, 'Не удалось удалить торрент.')
  } finally {
    busyIds.delete(torrent.id)
  }
}

function replaceTorrent(updated: TorrentView) {
  const index = torrents.value.findIndex((item) => item.id === updated.id)
  if (index >= 0) {
    torrents.value[index] = updated
  }
}

function statusOf(torrent: TorrentView): { label: string; severity: 'success' | 'warn' | 'danger' | 'secondary' } {
  switch (torrent.lastCheckOutcome) {
    case 'Changed':
      return { label: 'Обновление', severity: 'warn' }
    case 'Unchanged':
      return { label: 'Актуален', severity: 'success' }
    case 'Error':
      return { label: 'Ошибка', severity: 'danger' }
    default:
      return { label: 'Не проверялся', severity: 'secondary' }
  }
}

function formatDuration(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000))
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60

  if (hours > 0) {
    return `${hours} ч ${minutes} мин`
  }

  if (minutes > 0) {
    return `${minutes}:${String(seconds).padStart(2, '0')}`
  }

  return `${seconds} с`
}
</script>

<template>
  <section class="torrents-page">
    <div class="page-header">
      <div>
        <h1>Торренты</h1>
        <p class="muted">Список раздач рутрекера и состояние последней проверки.</p>
      </div>

      <div class="header-actions">
        <Tag severity="secondary" :value="`Всего: ${torrents.length}`" />
        <Tag
          v-if="status && status.updateAvailableCount > 0"
          severity="warn"
          :value="`Обновлений: ${status.updateAvailableCount}`"
        />
        <Button
          label="Проверить все"
          icon="pi pi-refresh"
          :loading="checkingAll"
          @click="checkAll"
        />
      </div>
    </div>

    <Message v-if="cookieError" severity="error" :closable="false" class="cookie-banner">
      Рутрекер не принял куку: {{ cookieError }}. Проверьте её в настройках.
    </Message>

    <div class="add-bar">
      <InputText
        v-model="newUrl"
        class="add-input"
        placeholder="Ссылка на топик рутрекера (viewtopic.php?t=…)"
        :disabled="adding"
        @keyup.enter="addTorrent"
      />
      <Button
        label="Добавить"
        icon="pi pi-plus"
        :loading="adding"
        :disabled="adding"
        @click="addTorrent"
      />
    </div>

    <DataTable
      :value="torrents"
      :loading="loading"
      data-key="id"
      striped-rows
      paginator
      :rows="20"
      :rows-per-page-options="[10, 20, 50]"
      class="torrents-table"
    >
      <template #empty>
        <div class="empty-state">
          <i class="pi pi-inbox" aria-hidden="true"></i>
          <p>Пока ничего не отслеживается. Вставьте ссылку на топик выше.</p>
        </div>
      </template>

      <Column header="Раздача">
        <template #body="{ data }">
          <div class="title-cell">
            <a :href="data.topicUrl" target="_blank" rel="noreferrer">{{ data.title }}</a>
            <span class="muted small">
              {{ formatBytes(data.torrentSizeBytes) }} · {{ data.fileCount }}
              {{ data.fileCount === 1 ? 'файл' : 'файлов' }} · {{ data.infoHashV1.slice(0, 12) }}…
            </span>
          </div>
        </template>
      </Column>

      <Column header="Статус">
        <template #body="{ data }">
          <div class="status-cell">
            <Tag :value="statusOf(data).label" :severity="statusOf(data).severity" />
            <span v-if="data.lastError" class="error-text" :title="data.lastError">
              {{ data.lastError }}
            </span>
          </div>
        </template>
      </Column>

      <Column header="Обновлён">
        <template #body="{ data }">
          {{ formatDateTime(data.lastChangedAtUtc) }}
        </template>
      </Column>

      <Column header="Проверен">
        <template #body="{ data }">
          {{ formatDateTime(data.lastCheckedAtUtc) }}
        </template>
      </Column>

      <Column header="Автоскан">
        <template #body="{ data }">
          <ToggleSwitch
            :model-value="data.isEnabled"
            :disabled="busyIds.has(data.id)"
            @update:model-value="toggleTorrent(data, $event)"
          />
        </template>
      </Column>

      <Column header="" style="width: 8rem">
        <template #body="{ data }">
          <div class="row-actions">
            <Button
              icon="pi pi-refresh"
              text
              rounded
              aria-label="Проверить"
              title="Проверить сейчас"
              :loading="busyIds.has(data.id)"
              @click="checkOne(data)"
            />
            <Button
              icon="pi pi-trash"
              text
              rounded
              severity="danger"
              aria-label="Удалить"
              title="Убрать из отслеживания"
              :disabled="busyIds.has(data.id)"
              @click="askDelete(data)"
            />
          </div>
        </template>
      </Column>
    </DataTable>

    <div class="status-line">
      <span>{{ scanLine }}</span>
      <span>{{ nextScanLine }}</span>
    </div>

    <ConfirmDialog />
  </section>
</template>

<style scoped>
.torrents-page {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.page-header h1 {
  margin-bottom: 0.2rem;
}

.page-header p {
  margin: 0;
}

.header-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}

.add-bar {
  display: flex;
  gap: 0.5rem;
}

.add-input {
  flex: 1;
  min-width: 16rem;
}

.title-cell {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.title-cell a {
  color: var(--p-text-color, #1f2937);
  font-weight: 550;
  text-decoration: none;
}

.title-cell a:hover {
  color: var(--p-primary-color, #10b981);
  text-decoration: underline;
}

.small {
  font-size: 0.8rem;
}

.status-cell {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.25rem;
}

.error-text {
  max-width: 16rem;
  color: var(--p-red-500, #ef4444);
  font-size: 0.78rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.row-actions {
  display: flex;
  gap: 0.2rem;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  padding: 2rem 1rem;
  color: var(--p-text-muted-color, #64748b);
}

.empty-state .pi {
  font-size: 2rem;
}

.status-line {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 0.5rem;
  color: var(--p-text-muted-color, #64748b);
  font-size: 0.88rem;
}
</style>
