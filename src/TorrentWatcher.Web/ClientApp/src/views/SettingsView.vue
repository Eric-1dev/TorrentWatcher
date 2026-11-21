<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Button from 'primevue/button'
import ConfirmDialog from 'primevue/confirmdialog'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import ToggleSwitch from 'primevue/toggleswitch'
import { useConfirm } from 'primevue/useconfirm'
import { api } from '../api/client'
import type { SettingsView } from '../api/types'
import { useApiError } from '../composables/useApiError'
import { formatDateTime } from '../utils/format'

const { showError, showSuccess } = useApiError()
const confirm = useConfirm()

const settings = ref<SettingsView | null>(null)
const login = ref('')
const password = ref('')
const cookie = ref('')
const cloudflareCookie = ref('')
const cloudflareUserAgent = ref('')
const qbittorrentUrl = ref('')
const qbittorrentUsername = ref('')
const qbittorrentPassword = ref('')
const qbittorrentAutoApply = ref(false)
const intervalMinutes = ref<number | null>(60)
const autoScanEnabled = ref(true)
const requestDelayMs = ref<number | null>(1500)
const requestTimeoutSeconds = ref<number | null>(30)

const loading = ref(false)
const saving = ref(false)
const loggingIn = ref(false)
const testing = ref(false)
const solvingCloudflare = ref(false)
const testingQbittorrent = ref(false)

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
  login.value = value.rutrackerLogin ?? ''
  intervalMinutes.value = value.scanIntervalMinutes
  autoScanEnabled.value = value.autoScanEnabled
  requestDelayMs.value = value.requestDelayMs
  requestTimeoutSeconds.value = value.requestTimeoutSeconds
  cloudflareUserAgent.value = value.cloudflareUserAgent ?? ''
  qbittorrentUrl.value = value.qbittorrentUrl ?? ''
  qbittorrentUsername.value = value.qbittorrentUsername ?? ''
  qbittorrentAutoApply.value = value.qbittorrentAutoApply
}

function validateSchedule(): boolean {
  if (intervalMinutes.value === null) {
    showError(new Error('Укажите интервал автоскана.'))
    return false
  }

  if (requestDelayMs.value === null || requestDelayMs.value < 0) {
    showError(new Error('Задержка между запросами не может быть отрицательной.'))
    return false
  }

  if (requestTimeoutSeconds.value === null || requestTimeoutSeconds.value <= 0) {
    showError(new Error('Таймаут запроса должен быть положительным.'))
    return false
  }

  return true
}

async function saveSettings() {
  if (!validateSchedule()) {
    return
  }

  saving.value = true
  try {
    const updated = await api.updateSettings({
      // null — «не менять». Пустая строка логина очищает его.
      rutrackerLogin: login.value.trim(),
      rutrackerPassword: password.value.length > 0 ? password.value : null,
      // Пустое поле ручной куки = «не менять»; сброс — отдельной кнопкой в «Дополнительно».
      rutrackerCookie: cookie.value.trim().length > 0 ? cookie.value.trim() : null,
      scanIntervalMinutes: intervalMinutes.value!,
      autoScanEnabled: autoScanEnabled.value,
      requestDelayMs: requestDelayMs.value!,
      requestTimeoutSeconds: requestTimeoutSeconds.value!,
      // Пустое поле пропуска = «не менять»; сброс — отдельной кнопкой.
      cloudflareClearanceCookie:
        cloudflareCookie.value.trim().length > 0 ? cloudflareCookie.value.trim() : null,
      cloudflareUserAgent: cloudflareUserAgent.value.trim(),
      qbittorrentUrl: qbittorrentUrl.value.trim(),
      qbittorrentUsername: qbittorrentUsername.value.trim(),
      qbittorrentPassword:
        qbittorrentPassword.value.length > 0 ? qbittorrentPassword.value : null,
      qbittorrentAutoApply: qbittorrentAutoApply.value,
    })

    applySettings(updated)
    password.value = ''
    cookie.value = ''
    cloudflareCookie.value = ''
    qbittorrentPassword.value = ''
    showSuccess('Настройки сохранены и применены без перезапуска.')
  } catch (error) {
    showError(error, 'Не удалось сохранить настройки.')
  } finally {
    saving.value = false
  }
}

async function loginRutracker() {
  loggingIn.value = true
  try {
    const result = await api.loginRutracker()

    if (result.success) {
      showSuccess(result.message)
    } else {
      showError(new Error(result.message))
    }

    await loadSettings()
  } catch (error) {
    showError(error, 'Не удалось выполнить вход в рутрекер.')
  } finally {
    loggingIn.value = false
  }
}

async function testSession() {
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
    showError(error, 'Не удалось проверить сеанс.')
  } finally {
    testing.value = false
  }
}

async function testQbittorrent() {
  testingQbittorrent.value = true
  try {
    const result = await api.testQbittorrent()

    if (result.success) {
      showSuccess(result.message)
    } else {
      showError(new Error(result.message))
    }

    await loadSettings()
  } catch (error) {
    showError(error, 'Не удалось проверить соединение с qBittorrent.')
  } finally {
    testingQbittorrent.value = false
  }
}

function forgetCredentials() {
  confirm.require({
    message: 'Удалить сохранённые логин, пароль и сеанс рутрекера?',
    header: 'Забыть данные рутрекера',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Удалить',
    rejectLabel: 'Отмена',
    accept: () => void doForgetCredentials(),
  })
}

async function doForgetCredentials() {
  if (!validateSchedule()) {
    return
  }

  saving.value = true
  try {
    const updated = await api.updateSettings({
      rutrackerLogin: '',
      rutrackerPassword: '',
      rutrackerCookie: '',
      scanIntervalMinutes: intervalMinutes.value!,
      autoScanEnabled: autoScanEnabled.value,
      requestDelayMs: requestDelayMs.value!,
      requestTimeoutSeconds: requestTimeoutSeconds.value!,
      cloudflareUserAgent: cloudflareUserAgent.value.trim(),
    })

    applySettings(updated)
    login.value = ''
    password.value = ''
    cookie.value = ''
    showSuccess('Логин, пароль и сеанс рутрекера удалены.')
  } catch (error) {
    showError(error, 'Не удалось удалить данные рутрекера.')
  } finally {
    saving.value = false
  }
}

async function clearCookie() {
  if (!validateSchedule()) {
    return
  }

  saving.value = true
  try {
    const updated = await api.updateSettings({
      rutrackerCookie: '',
      scanIntervalMinutes: intervalMinutes.value!,
      autoScanEnabled: autoScanEnabled.value,
      requestDelayMs: requestDelayMs.value!,
      requestTimeoutSeconds: requestTimeoutSeconds.value!,
      cloudflareUserAgent: cloudflareUserAgent.value.trim(),
    })

    applySettings(updated)
    cookie.value = ''
    showSuccess('Сохранённая кука сеанса удалена.')
  } catch (error) {
    showError(error, 'Не удалось удалить куку.')
  } finally {
    saving.value = false
  }
}

async function solveCloudflare() {
  solvingCloudflare.value = true
  try {
    const result = await api.solveCloudflare()

    if (result.success) {
      showSuccess(result.message)
    } else {
      showError(new Error(result.message))
    }

    await loadSettings()
  } catch (error) {
    showError(error, 'Не удалось пройти проверку Cloudflare.')
  } finally {
    solvingCloudflare.value = false
  }
}

async function clearCloudflare() {
  if (!validateSchedule()) {
    return
  }

  saving.value = true
  try {
    const updated = await api.updateSettings({
      scanIntervalMinutes: intervalMinutes.value!,
      autoScanEnabled: autoScanEnabled.value,
      requestDelayMs: requestDelayMs.value!,
      requestTimeoutSeconds: requestTimeoutSeconds.value!,
      cloudflareClearanceCookie: '',
      cloudflareUserAgent: cloudflareUserAgent.value.trim(),
    })

    applySettings(updated)
    cloudflareCookie.value = ''
    showSuccess('Пропуск Cloudflare удалён.')
  } catch (error) {
    showError(error, 'Не удалось удалить пропуск Cloudflare.')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <section class="settings-page">
    <div class="page-header">
      <div>
        <h1>Настройки</h1>
        <p class="muted">
          Аккаунт рутрекера, пропуск Cloudflare и расписание автоскана. Изменения применяются сразу.
        </p>
      </div>
    </div>

    <div class="settings-card">
      <h2>Аккаунт рутрекера</h2>

      <p class="muted">
        Приложение входит в рутрекер само по сохранённым логину и паролю. Вход выполняет браузер
        Chromium, который уже используется для проверки Cloudflare. Пароль хранится зашифрованным.
      </p>

      <Message
        v-if="settings?.cookieLastError"
        severity="error"
        :closable="false"
      >
        Последняя ошибка входа или сеанса: {{ settings.cookieLastError }}
      </Message>

      <div class="field-row">
        <div class="field">
          <label for="rutracker-login">Логин</label>
          <InputText
            id="rutracker-login"
            v-model="login"
            autocomplete="username"
            placeholder="имя пользователя"
            :disabled="loading || saving || loggingIn"
          />
        </div>

        <div class="field">
          <label for="rutracker-password">Пароль</label>
          <InputText
            id="rutracker-password"
            v-model="password"
            type="password"
            autocomplete="new-password"
            placeholder="пусто — не менять"
            :disabled="loading || saving || loggingIn"
          />
          <small class="muted">
            Пустое поле — не менять сохранённый пароль. В БД пароль шифруется ключами
            Data Protection (каталог <code>keys</code> на диске).
          </small>
        </div>
      </div>

      <div class="cookie-state">
        <Tag
          :severity="settings?.hasCredentials ? 'success' : 'secondary'"
          :value="
            settings?.hasCredentials
              ? `Логин сохранён: ${settings.rutrackerLogin}`
              : 'Логин и пароль не заданы'
          "
        />
        <Tag
          :severity="settings?.hasPassword ? 'success' : 'secondary'"
          :value="settings?.hasPassword ? 'Пароль сохранён' : 'Пароля нет'"
        />
        <Tag
          :severity="settings?.hasCookie ? 'success' : 'secondary'"
          :value="settings?.hasCookie ? 'Сеанс активен' : 'Сеанса нет'"
        />
        <Tag
          v-if="settings?.cookieVerifiedAtUtc"
          severity="success"
          :value="`Проверен: ${formatDateTime(settings.cookieVerifiedAtUtc)}`"
        />
      </div>

      <div class="cookie-actions">
        <Button
          label="Войти"
          icon="pi pi-sign-in"
          severity="secondary"
          :loading="loggingIn"
          :disabled="loggingIn || saving || loading || !settings?.hasCredentials"
          @click="loginRutracker"
        />
        <Button
          label="Проверить сеанс"
          icon="pi pi-shield"
          severity="secondary"
          text
          :loading="testing"
          :disabled="testing || loggingIn || saving || loading || !settings?.hasCredentials"
          @click="testSession"
        />
        <Button
          label="Забыть данные"
          icon="pi pi-trash"
          severity="danger"
          text
          :disabled="loggingIn || saving || loading || (!settings?.hasCredentials && !settings?.hasCookie)"
          @click="forgetCredentials"
        />
      </div>

      <small class="muted">
        Кнопки «Войти» и «Проверить сеанс» используют сохранённые данные — сначала нажмите
        «Сохранить», если изменили логин или пароль. Вход занимает до минуты: браузер проходит
        Cloudflare и отправляет форму. Если рутрекер покажет капчу, автоматический вход
        не сработает — используйте куку вручную ниже.
      </small>

      <details class="advanced">
        <summary>Дополнительно: кука сеанса вручную</summary>

        <div class="field advanced-field">
          <label for="cookie">Кука сеанса</label>
          <Textarea
            id="cookie"
            v-model="cookie"
            rows="3"
            auto-resize
            placeholder="bb_session=…; bb_data=…"
            :disabled="loading || saving || loggingIn"
          />
          <small class="muted">
            Аварийный вариант, если браузер недоступен или рутрекер требует капчу. Обычно куку
            заполняет приложение после входа. Пустое поле — не менять; удалить сохранённую куку
            можно кнопкой ниже.
          </small>
        </div>

        <div class="cookie-actions">
          <Tag
            v-if="settings?.maskedCookie"
            severity="secondary"
            :value="`Сохранена: ${settings.maskedCookie}`"
          />
          <Button
            label="Убрать куку"
            icon="pi pi-trash"
            severity="danger"
            text
            :disabled="saving || loggingIn || loading || !settings?.hasCookie"
            @click="clearCookie"
          />
        </div>
      </details>
    </div>

    <div class="settings-card">
      <h2>Cloudflare</h2>

      <Message
        v-if="settings?.cloudflareLastError"
        severity="error"
        :closable="false"
      >
        Проверка Cloudflare не пройдена: {{ settings.cloudflareLastError }}
      </Message>

      <div class="cookie-state">
        <Tag
          :severity="settings?.hasCloudflareClearance ? 'success' : 'secondary'"
          :value="settings?.hasCloudflareClearance ? 'Пропуск сохранён' : 'Пропуска нет'"
        />
        <Tag
          v-if="settings?.cloudflareClearanceExpiresAtUtc"
          severity="info"
          :value="`Действует до: ${formatDateTime(settings.cloudflareClearanceExpiresAtUtc)}`"
        />
        <Tag
          v-else-if="settings?.cloudflareClearanceObtainedAtUtc"
          severity="warn"
          value="Срок действия неизвестен"
        />
      </div>

      <p v-if="settings?.cloudflareUserAgent" class="muted ua-line">
        User-Agent пропуска: <code>{{ settings.cloudflareUserAgent }}</code>
      </p>

      <div class="cookie-actions">
        <Button
          label="Пройти проверку Cloudflare"
          icon="pi pi-shield"
          severity="secondary"
          :loading="solvingCloudflare"
          :disabled="solvingCloudflare || loading || saving || loggingIn"
          @click="solveCloudflare"
        />
        <small class="muted">
          Откроется браузер (в контейнере — под Xvfb) и пройдёт проверку автоматически.
          Может занять до минуты.
        </small>
      </div>

      <div class="field">
        <label for="cf-clearance">cf_clearance вручную (аварийный режим)</label>
        <Textarea
          id="cf-clearance"
          v-model="cloudflareCookie"
          rows="3"
          auto-resize
          placeholder="cf_clearance=…"
          :disabled="loading || saving || loggingIn"
        />
        <small class="muted">
          Если автоматика не работает: скопируйте куку <code>cf_clearance</code> из DevTools
          (Application → Cookies → rutracker.org). Пустое поле — не менять сохранённый пропуск.
        </small>
      </div>

      <div class="field">
        <label for="cf-user-agent">User-Agent</label>
        <InputText
          id="cf-user-agent"
          v-model="cloudflareUserAgent"
          autocomplete="off"
          placeholder="Mozilla/5.0 …"
          :disabled="loading || saving || loggingIn"
        />
        <small class="muted">
          Cloudflare привязывает пропуск к User-Agent: он должен совпадать с браузером,
          из которого скопирована кука. Смена UA сбрасывает старый пропуск.
        </small>
      </div>

      <div class="cookie-actions">
        <Button
          label="Убрать пропуск"
          icon="pi pi-trash"
          severity="danger"
          text
          :disabled="loading || saving || loggingIn || !settings?.hasCloudflareClearance"
          @click="clearCloudflare"
        />
      </div>
    </div>

    <div class="settings-card">
      <h2>qBittorrent</h2>

      <p class="muted">
        Приложение может само заменять раздачу в qBittorrent, когда на рутрекере меняется
        infohash: старая раздача удаляется без файлов, новая добавляется в ту же папку
        с той же категорией, после чего запускается пересчёт хешей.
      </p>

      <Message
        v-if="settings?.qbittorrentLastError"
        severity="error"
        :closable="false"
      >
        qBittorrent: {{ settings.qbittorrentLastError }}
      </Message>

      <div class="cookie-state">
        <Tag
          :severity="settings?.qbittorrentConfigured ? 'success' : 'secondary'"
          :value="settings?.qbittorrentConfigured ? 'Подключение настроено' : 'Не настроено'"
        />
        <Tag
          :severity="settings?.qbittorrentAutoApply ? 'success' : 'secondary'"
          :value="settings?.qbittorrentAutoApply ? 'Обновления применяются сами' : 'Только вручную'"
        />
        <Tag
          v-if="settings?.qbittorrentVerifiedAtUtc"
          severity="success"
          :value="`Проверено: ${formatDateTime(settings.qbittorrentVerifiedAtUtc)}`"
        />
      </div>

      <div class="field-row">
        <div class="field">
          <label for="qbittorrent-url">Адрес Web UI</label>
          <InputText
            id="qbittorrent-url"
            v-model="qbittorrentUrl"
            placeholder="http://plex.local:8080"
            autocomplete="off"
            :disabled="loading || saving"
          />
          <small class="muted">
            Адрес, доступный из контейнера приложения. Порт по умолчанию — 8080.
          </small>
        </div>

        <div class="field">
          <label for="qbittorrent-username">Логин</label>
          <InputText
            id="qbittorrent-username"
            v-model="qbittorrentUsername"
            autocomplete="username"
            placeholder="admin"
            :disabled="loading || saving"
          />
        </div>
      </div>

      <div class="field-row">
        <div class="field">
          <label for="qbittorrent-password">Пароль</label>
          <InputText
            id="qbittorrent-password"
            v-model="qbittorrentPassword"
            type="password"
            autocomplete="new-password"
            placeholder="пусто — не менять"
            :disabled="loading || saving"
          />
          <small class="muted">
            Пустое поле — не менять сохранённый пароль. Пароль хранится зашифрованным.
          </small>
        </div>

        <div class="field">
          <label for="qbittorrent-auto">Применять обновления автоматически</label>
          <div class="switch-row">
            <ToggleSwitch
              id="qbittorrent-auto"
              v-model="qbittorrentAutoApply"
              :disabled="loading || saving"
            />
            <span class="muted">
              {{ qbittorrentAutoApply ? 'включено' : 'только по кнопке «Применить»' }}
            </span>
          </div>
        </div>
      </div>

      <div class="cookie-actions">
        <Button
          label="Проверить соединение"
          icon="pi pi-cloud"
          severity="secondary"
          text
          :loading="testingQbittorrent"
          :disabled="testingQbittorrent || loading || saving || !settings?.qbittorrentConfigured"
          @click="testQbittorrent"
        />
        <small class="muted">
          Проверка использует сохранённые настройки — сначала нажмите «Сохранить».
        </small>
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
            :disabled="loading || saving || loggingIn"
          />
          <small class="muted">От 1 до 1440 минут; применяется без перезапуска.</small>
        </div>

        <div class="field">
          <label for="auto-scan">Автоскан включён</label>
          <div class="switch-row">
            <ToggleSwitch
              id="auto-scan"
              v-model="autoScanEnabled"
              :disabled="loading || saving || loggingIn"
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
            :disabled="loading || saving || loggingIn"
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
            :disabled="loading || saving || loggingIn"
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
        :disabled="loading || saving || loggingIn"
        @click="saveSettings"
      />
      <small v-if="settings" class="muted">
        Обновлено: {{ formatDateTime(settings.updatedAtUtc) }}
      </small>
    </div>

    <ConfirmDialog />
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

.settings-card > p.muted {
  margin: 0;
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

.ua-line {
  margin: 0;
  word-break: break-all;
}

.ua-line code {
  font-size: 0.8rem;
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

.advanced {
  border-top: 1px solid var(--p-surface-200, #e2e8f0);
  padding-top: 0.75rem;
}

.advanced summary {
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 550;
}

.advanced-field {
  margin-top: 0.75rem;
}

.footer-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
}
</style>
