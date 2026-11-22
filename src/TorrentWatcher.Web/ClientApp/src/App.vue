<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'
import Toast from 'primevue/toast'
</script>

<template>
  <div class="app-shell">
    <Toast position="top-right" />

    <header class="app-header">
      <RouterLink class="brand" to="/">
        <svg class="brand-mark" viewBox="0 0 64 64" aria-hidden="true">
          <path
            d="M49.3 22A20 20 0 1 1 37.2 12.7"
            fill="none"
            stroke="currentColor"
            stroke-opacity="0.45"
            stroke-width="4.5"
            stroke-linecap="round"
          />
          <path d="M43.4 14.6l-6.6 1.4 2.6-6.2Z" fill="currentColor" fill-opacity="0.45" />
          <path d="M32 18v21" fill="none" stroke="currentColor" stroke-width="7" stroke-linecap="round" />
          <path
            d="M23 31l9 9 9-9"
            fill="none"
            stroke="currentColor"
            stroke-width="7"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
        <span>TorrentWatcher</span>
      </RouterLink>

      <nav class="app-nav" aria-label="Основная навигация">
        <RouterLink to="/">Торренты</RouterLink>
        <RouterLink to="/settings">Настройки</RouterLink>
      </nav>
    </header>

    <main class="app-main">
      <RouterView />
    </main>
  </div>
</template>
