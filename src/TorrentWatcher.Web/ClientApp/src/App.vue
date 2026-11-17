<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'
import Toast from 'primevue/toast'
</script>

<template>
  <div class="app-shell">
    <Toast position="top-right" />

    <header class="app-header">
      <RouterLink class="brand" to="/">
        <i class="pi pi-download" aria-hidden="true"></i>
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
