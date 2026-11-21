# AGENTS.md — правила работы в репозитории TorrentWatcher

## Главное

**`PLAN.md` — источник истины.** Перед началом любой работы прочитай его целиком.

**После завершения каждой задачи `TW-xx` обязательно отметь её в `PLAN.md`:**

1. `- [ ]` → `- [x]`;
2. весь текст задачи оберни в `~~...~~` (вычеркнуть, **не удалять**);
3. допиши в конец `— done YYYY-MM-DD`;
4. обнови в шапке строку «Следующий пункт» на следующую невыполненную задачу.

Пример:

```markdown
- [x] ~~TW-05. Репозитории портов Application, WAL/`busy_timeout`/`foreign_keys` при старте.
      Готово: перезапуск сохраняет данные. — done 2026-11-18~~
```

Пользователю **не нужно напоминать** об этом — это часть Definition of Done каждой задачи.

## Корректировки плана

Если по ходу реализации меняется формулировка, порядок, состав или оценка задачи — это нормально.
Правило: старый текст не удаляем, а зачёркиваем, новую формулировку пишем рядом, и добавляем
запись в раздел «Журнал изменений плана» (дата, было → стало, причина). Молчаливых отклонений нет.

## Стек и архитектура

- .NET 10, ASP.NET Core (MVC-контроллеры), EF Core 10 + SQLite, Kestrel.
- Vue 3 + TypeScript + Vite + PrimeVue 4 (MIT). Сборка кладётся в `src/TorrentWatcher.Web/wwwroot`.
- Трёхслойка, зависимости строго в одну сторону:
  `Domain ← Application ← Infrastructure`, `Web → Application (+ Infrastructure только для DI)`.
- Domain не знает про EF Core, HTTP и ASP.NET. Application не знает про ASP.NET и EF Core
  (только порты и доменные сущности). DbContext, HttpClient'ы, файловое хранилище — в Infrastructure.
- Контроллеры не содержат бизнес-логики: только DTO ↔ use-case.
- Никакой авторизации и мультипользовательности. qBittorrent пока не реализуем — только порт-заглушка.

## Договорённости по коду

- `Nullable` и `ImplicitUsings` включены, предупреждения трактуются как ошибки.
- `CancellationToken` протягивается во все асинхронные методы; `async void` запрещён.
- Время — только UTC в БД и API, интерфейс форматирует локально. Для тестируемости — `TimeProvider`.
- Куку рутрекера никогда не логировать, в API отдавать маскированной.
- Ошибки API — `ProblemDetails` (RFC 7807) с машинным кодом (`RUTRACKER_COOKIE_INVALID` и т.п.).
- Комментарии и документация — по-русски, идентификаторы и код — по-английски.

## Команды

```powershell
dotnet build
dotnet test
dotnet run --project src/TorrentWatcher.Web

cd src/TorrentWatcher.Web/ClientApp
npm ci
npm run dev      # проксирует /api на http://localhost:5080
npm run build    # пишет в ../wwwroot

# миграции
dotnet ef migrations add <Name> -p src/TorrentWatcher.Infrastructure -s src/TorrentWatcher.Web
dotnet ef database update -p src/TorrentWatcher.Infrastructure -s src/TorrentWatcher.Web
```

## Ограничения окружения

- Проект лежит в `C:\Work\TorrentWatcher` (не в git-репозитории рабочей папки сессии).
- Порт по умолчанию: 5080 для разработки, 8080 в контейнере (наружу публикуется 8088).
