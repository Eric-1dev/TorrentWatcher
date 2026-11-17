<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Button from 'primevue/button'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import ToggleSwitch from 'primevue/toggleswitch'
import { api } from '../api/client'
import type { SettingsView } from '../api/types'
import { useApiError } from '../composables/useApiError'
import { formatDateTime } from '../utils/format'

const { showError, showSuccess } = useApiError()

const settings = ref<SettingsView | null>(null)
const cookie = ref('')
const intervalMinutes = ref<number | null>(60)
const autoScanEnabled = ref(true)
const requestDelayMs = ref<number | null>(1500)
const requestTimeoutSeconds = ref<number | null>(30)

const loading = ref(false)
const saving = ref(false)
const testing = ref(false)

onMounted(() => {
  void loadSettings()
})

async function loadSettings() {
  loading.value = true
  try {
    applySettings(await api.getSettings())
  } catch (error) {
    showError(error, 'Не удалось загрузить настройки.')
  } finally {
    loading.value = false
  }
}

function applySettings(value: SettingsView) {
  settings.value = value
  intervalMinutes.value = value.scanIntervalMinutes
  autoScanEnabled.value = value.autoScanEnabled
  requestDelayMs.value = value.requestDelayMs
  requestTimeoutSeconds.value = value.requestTimeoutSeconds
}

async function saveSettings() {
  if (intervalMinutes.value === null) {
    showError(new Error('Укажите интервал автоскана.'))
    return
  }

  if (requestDelayMs.value === null || requestDelayMs.value < 0) {
    showError(new Error('Задержка между запросами не может быть отрицательной.'))
    return
  }

  if (requestTimeoutSeconds.value === null || requestTimeoutSeconds.value <= 0) {
    showError(new Error('Таймаут запроса должен быть положительным.'))
    return
  }

  saving.value = true
  try {
    const enteredCookie = cookie.value.trim()

    const updated = await api.updateSettings({
      // Пустое поле = «не менять куку»: маску из API обратно не отправляем.
      rutrackerCookie: enteredCookie.length > 0 ? enteredCookie : null,
      scanIntervalMinutes: intervalMinutes.value,
      autoScanEnabled: autoScanEnabled.value,
      requestDelayMs: requestDelayMs.value,
      requestTimeoutSeconds: requestTimeoutSeconds.value,
    })

    applySettings(updated)
    cookie.value = ''
    showSuccess('Настройки сохранены и применены без перезапуска.')
  } catch (error) {
    showError(error, 'Не удалось сохранить настройки.')
  } finally {
    saving.value = false
  }
}

async function testCookie() {
  testing.value = true
  try {
    const result = await api.testCookie()

    if (result.success) {
      showSuccess(result.message)
    } else {
      showError(new Error(result.message))
    }

    await loadSettings()
  } catch (error) {
    showError(error, 'Не удалось проверить куку.')
  } finally {
    testing.value = false
  }
}
</script>

<template>
  <section class="settings-page">
    <div class="page-header">
      <div>
        <h1>Настройки</h1>
        <p class="muted">Кука рутрекера и расписание автоскана. Изменения применяются сразу.</p>
      </div>
    </div>

    <Message
      v-if="settings?.cookieLastError"
      severity="error"
      :closable="false"
      class="cookie-banner"
    >
      Последняя ошибка куки: {{ settings.cookieLastError }}
    </Message>

    <div class="settings-card">
      <h2>Кука рутрекера</h2>

      <div class="field">
        <label for="cookie">Новая кука</label>
        <InputText
          id="cookie"
          v-model="cookie"
          type="password"
          autocomplete="off"
          placeholder="bb_session=…; bb_data=…"
          :disabled="loading || saving"
        />
        <small class="muted">
          Оставьте поле пустым, чтобы не менять сохранённую куку. Кука хранится в БД и в API
          отдаётся только маской.
        </small>
      </div>

      <div v-if="settings" class="cookie-state">
        <Tag
          :severity="settings.hasCookie ? 'success' : 'secondary'"
          :value="settings.hasCookie ? `Сохранена: ${settings.maskedCookie}` : 'Кука не задана'"
        />
        <Tag
          v-if="settings.cookieVerifiedAtUtc"
          severity="success"
          :value="`Проверена: ${formatDateTime(settings.cookieVerifiedAtUtc)}`"
        />
        <Tag v-else severity="warn" value="Ещё не проверялась" />
      </div>

      <div class="cookie-actions">
        <Button
          label="Проверить куку"
          icon="pi pi-shield"
          severity="secondary"
          :loading="testing"
          :disabled="testing || loading || !settings?.hasCookie"
          @click="testCookie"
        />
        <small class="muted">Проверка использует сохранённую куку, а не текст в поле выше.</small>
      </div>
    </div>

    <div class="settings-card">
      <h2>Автоскан</h2>

      <div class="field-row">
        <div class="field">
          <label for="interval">Интервал, минут</label>
          <InputNumber
            id="interval"
            v-model="intervalMinutes"
            :min="1"
            :max="1440"
            :use-grouping="false"
            suffix=" мин"
            :disabled="loading || saving"
          />
          <small class="muted">От 1 до 1440 минут; применяется без перезапуска.</small>
        </div>

        <div class="field">
          <label for="auto-scan">Автоскан включён</label>
          <div class="switch-row">
            <ToggleSwitch
              id="auto-scan"
              v-model="autoScanEnabled"
              :disabled="loading || saving"
            />
            <span class="muted">{{ autoScanEnabled ? 'включён' : 'выключен' }}</span>
          </div>
        </div>
      </div>

      <div class="field-row">
        <div class="field">
          <label for="delay">Пауза между запросами, мс</label>
          <InputNumber
            id="delay"
            v-model="requestDelayMs"
            :min="0"
            :max="60000"
            :use-grouping="false"
            suffix=" мс"
            :disabled="loading || saving"
          />
          <small class="muted">Чтобы не долбить рутрекер при проверке всех торрентов.</small>
        </div>

        <div class="field">
          <label for="timeout">Таймаут запроса, секунд</label>
          <InputNumber
            id="timeout"
            v-model="requestTimeoutSeconds"
            :min="1"
            :max="600"
            :use-grouping="false"
            suffix=" с"
            :disabled="loading || saving"
          />
          <small class="muted">Сколько ждать ответа рутрекера на один .torrent.</small>
        </div>
      </div>
    </div>

    <div class="footer-actions">
      <Button
        label="Сохранить"
        icon="pi pi-check"
        :loading="saving"
        :disabled="loading || saving"
        @click="saveSettings"
      />
      <small v-if="settings" class="muted">
        Обновлено: {{ formatDateTime(settings.updatedAtUtc) }}
      </small>
    </div>
  </section>
</template>

<style scoped>
.settings-page {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 900px;
}

.page-header h1 {
  margin-bottom: 0.2rem;
}

.page-header p {
  margin: 0;
}

.settings-card {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1.25rem;
  border: 1px solid var(--p-surface-200, #e2e8f0);
  border-radius: 0.85rem;
  background: var(--p-surface-0, #fff);
}

.settings-card h2 {
  margin: 0;
  font-size: 1.05rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  flex: 1;
}

.field label {
  font-size: 0.9rem;
  font-weight: 550;
}

.field-row {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
}

.cookie-state {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.cookie-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.6rem;
}

.switch-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-height: 2.4rem;
}

.footer-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
}
</style>
