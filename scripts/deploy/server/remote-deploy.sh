#!/usr/bin/env bash
# ============================================================
# Развёртывание TorrentWatcher на сервере в Docker (compose).
# Запускается через plink из deploy_server.bat.
#
# Ожидает рядом с собой:
#   torrentwatcher.tar  — образ (теги latest и <build_id>)
#   docker-compose.yaml — шаблон (кладётся только при первом деплое)
#
# Аргументы:
#   $1 — опубликованный порт на хосте (APP_PORT, по умолчанию 8088)
#   $2 — тег нового образа (по умолчанию latest)
#   $3 — каталог установки (по умолчанию /torrentwatcher)
#   $4 — режим sudo: nopass (NOPASSWD или docker без sudo) | file
#        (пароль sudo лежит в файле $5)
#   $5 — путь к файлу с паролем sudo (только для режима file)
#
# Модель работы:
#   * docker-compose.yaml в ${INSTALL_DIR} создаётся ОДИН РАЗ и далее не
#     перезаписывается — конфиг правится на сервере вручную;
#   * при каждом деплое образ из tar добавляется в Docker как latest, после
#     чего выполняется `docker compose up -d --force-recreate`;
#   * каталог data принадлежит пользователю app (UID образа), иначе SQLite
#     и профиль Chromium не смогут писать.
# ============================================================
# Страховка от CRLF: с Windows-переводами строк bash читает "pipefail\r" и
# падает с "set: pipefail: invalid option name". Строка ниже состоит только из
# простых команд, поэтому разбирается даже с CRLF: правим свою копию и
# перезапускаемся. Комментарий в конце строки гасит случайный \r.
grep -q $'\r' "$0" 2>/dev/null && sed -i 's/\r$//' "$0" && exec bash "$0" "$@" # CRLF-fix

set -euo pipefail

IMAGE_NAME="torrentwatcher"
CONTAINER_NAME="torrentwatcher"

APP_PORT="${1:-8088}"
NEW_TAG="${2:-latest}"
INSTALL_DIR="${3:-/torrentwatcher}"
SUDO_MODE="${4:-nopass}"
SUDO_FILE="${5:-}"

COMPOSE_FILE="${INSTALL_DIR}/docker-compose.yaml"
STATE_DIR="${INSTALL_DIR}/state"
DATA_DIR="${INSTALL_DIR}/data"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TAR_PATH="${SCRIPT_DIR}/${IMAGE_NAME}.tar"
COMPOSE_SRC="${SCRIPT_DIR}/docker-compose.yaml"

WARN=0
warn() { echo "[deploy][WARN] $*" >&2; WARN=1; }

# ---------- Права: root, sudo (пароль/NOPASSWD) или docker без sudo ----------
SUDO_PASSWORD=""
if [ "${SUDO_MODE}" = "file" ]; then
	if [ -z "${SUDO_FILE}" ] || [ ! -f "${SUDO_FILE}" ]; then
		echo "[deploy][ERROR] Не найден файл с паролем sudo: ${SUDO_FILE}" >&2
		exit 1
	fi

	# Файл загружен pscp; сразу закрываем от чужих глаз и удаляем после чтения.
	chmod 600 "${SUDO_FILE}" 2>/dev/null || true
	IFS= read -r SUDO_PASSWORD < "${SUDO_FILE}" || true
	SUDO_PASSWORD="${SUDO_PASSWORD%$'\r'}"
	rm -f "${SUDO_FILE}"
fi

DEPLOY_UID="$(id -u)"
DEPLOY_GID="$(id -g)"
NEEDS_CHOWN=0

if [ "${DEPLOY_UID}" -eq 0 ]; then
	as_root() { "$@"; }
	refresh_sudo() { :; }
elif [ -n "${SUDO_PASSWORD}" ]; then
	# Пароль вводим один раз (sudo -v), дальше работаем через sudo -n: так stdin
	# остаётся свободным для потока образа `gzip -dc | docker load`.
	if ! printf '%s\n' "${SUDO_PASSWORD}" | sudo -S -p '' -v 2>/dev/null; then
		echo "[deploy][ERROR] Пароль sudo не подошёл для $(id -un)." >&2
		exit 1
	fi
	as_root() { sudo -n "$@"; }
	refresh_sudo() {
		printf '%s\n' "${SUDO_PASSWORD}" | sudo -S -p '' -v 2>/dev/null || true
	}
	NEEDS_CHOWN=1
elif command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
	as_root() { sudo -n "$@"; }
	refresh_sudo() { :; }
	NEEDS_CHOWN=1
elif docker info >/dev/null 2>&1; then
	echo "[deploy] sudo недоступен, но docker работает без root — деплою от $(id -un)."
	as_root() { "$@"; }
	refresh_sudo() { :; }

	if ! as_root mkdir -p "${INSTALL_DIR}" 2>/dev/null; then
		echo "[deploy][ERROR] Нет прав на ${INSTALL_DIR}." >&2
		echo "         Укажите INSTALL_DIR в домашнем каталоге, например:" >&2
		echo "           INSTALL_DIR=${HOME}/torrentwatcher" >&2
		exit 1
	fi
else
	echo "[deploy][ERROR] Пользователь $(id -un) не root, и sudo без пароля недоступен." >&2
	echo "         Варианты:" >&2
	echo "         1) разрешить sudo без пароля для этой УЗ (рекомендуется):" >&2
	echo "            echo '$(id -un) ALL=(ALL) NOPASSWD: ALL' | sudo tee /etc/sudoers.d/torrentwatcher" >&2
	echo "            sudo chmod 0440 /etc/sudoers.d/torrentwatcher" >&2
	echo "         2) добавить УЗ в группу docker и указать INSTALL_DIR в домашнем каталоге;" >&2
	echo "         3) запускать deploy_server.bat под root." >&2
	exit 1
fi

# ---------- Каталоги ----------
if ! as_root mkdir -p "${INSTALL_DIR}" "${STATE_DIR}" "${DATA_DIR}"; then
	echo "[deploy][ERROR] Не удалось создать ${INSTALL_DIR}." >&2
	exit 1
fi

# ---------- Docker Compose ----------
# На сервере может быть только старый docker-compose (v1) без плагина
# `docker compose`; тогда Docker CLI разбирает "-f" как свою опцию.
COMPOSE_BIN=()
if as_root docker compose version >/dev/null 2>&1; then
	COMPOSE_BIN=(docker compose)
elif command -v docker-compose >/dev/null 2>&1; then
	echo "[deploy] Плагин compose не найден, использую docker-compose."
	COMPOSE_BIN=(docker-compose)
else
	echo "[deploy][ERROR] Docker Compose не найден на сервере." >&2
	echo "         Установите плагин: apt install docker-compose-plugin" >&2
	echo "         или standalone: apt install docker-compose" >&2
	exit 1
fi

# Файлы установки должны быть доступны деплоящему пользователю, а data —
# пользователю app из образа (ниже). chown нужен только при работе через sudo.
if [ "${NEEDS_CHOWN}" -eq 1 ]; then
	as_root chown "${DEPLOY_UID}:${DEPLOY_GID}" "${INSTALL_DIR}" \
		|| warn "не удалось сменить владельца ${INSTALL_DIR}"
	as_root chown -R "${DEPLOY_UID}:${DEPLOY_GID}" "${STATE_DIR}" \
		|| warn "не удалось сменить владельца ${STATE_DIR}"
fi

# ---------- Образ ----------
if [ ! -f "${TAR_PATH}" ]; then
	echo "[deploy][ERROR] Не найден ${TAR_PATH}." >&2
	exit 1
fi

echo "[deploy] Загрузка образа из ${TAR_PATH}..."
as_root docker load -i "${TAR_PATH}"
refresh_sudo

# ---------- docker-compose.yaml (создаётся один раз) ----------
if [ -f "${COMPOSE_FILE}" ]; then
	echo "[deploy] ${COMPOSE_FILE} уже существует — оставляю без изменений."

	missing=""
	for key in APP_PORT ConnectionStrings__Default Storage__TorrentsDirectory Security__KeysDirectory; do
		if ! grep -q "${key}" "${COMPOSE_FILE}"; then
			missing="${missing} ${key}"
		fi
	done
	if [ -n "${missing}" ]; then
		warn "в ${COMPOSE_FILE} нет параметров:${missing}"
		warn "файл создаётся один раз и не перезаписывается; чтобы взять новый шаблон:"
		warn "  rm ${COMPOSE_FILE} && повторите деплой (настройки из старого файла потеряются)"
	fi
elif [ -f "${COMPOSE_SRC}" ]; then
	echo "[deploy] Первый деплой: создаю ${COMPOSE_FILE} из шаблона."
	cp "${COMPOSE_SRC}" "${COMPOSE_FILE}"
else
	echo "[deploy][ERROR] Не найден шаблон ${COMPOSE_SRC}." >&2
	exit 1
fi

if [ "${NEEDS_CHOWN}" -eq 1 ]; then
	as_root chown "${DEPLOY_UID}:${DEPLOY_GID}" "${COMPOSE_FILE}" \
		|| warn "не удалось сменить владельца ${COMPOSE_FILE}"
fi

# docker-compose v1 без ключа version разбирает файл как старый формат и
# может проигнорировать volumes. Добавляем ключ, если его ещё нет.
if ! grep -qE '^[[:space:]]*version[[:space:]]*:' "${COMPOSE_FILE}"; then
	echo "[deploy] Добавляю version: \"2.4\" в ${COMPOSE_FILE} для совместимости с compose v1."
	tmp_compose="${COMPOSE_FILE}.tmp"
	{
		printf 'version: "2.4"\n'
		cat "${COMPOSE_FILE}"
	} > "${tmp_compose}"
	mv "${tmp_compose}" "${COMPOSE_FILE}"
fi

# ---------- Права на data ----------
# Контейнер работает под непривилегированным пользователем app: bind-mount,
# созданный root'ом, ему недоступен. Меняем владельца через тот же образ,
# чтобы не зависеть от UID на конкретной версии базового образа.
echo "[deploy] Права на ${DATA_DIR} (пользователь app)..."
# --user root обязателен: в образе USER app, а chown требует root.
as_root docker run --rm \
	--user root \
	-v "${DATA_DIR}:/data" \
	--entrypoint chown \
	"${IMAGE_NAME}:latest" -R app:app /data \
	|| warn "не удалось сменить владельца ${DATA_DIR}; проверьте права вручную"

# ---------- Фаервол ----------
if command -v ufw >/dev/null 2>&1 && as_root ufw status 2>/dev/null | grep -q "Status: active"; then
	echo "[deploy] ufw активен, открываю ${APP_PORT}/tcp..."
	as_root ufw allow "${APP_PORT}/tcp" >/dev/null 2>&1 || warn "не удалось открыть порт в ufw"
elif command -v firewall-cmd >/dev/null 2>&1 && firewall-cmd --state >/dev/null 2>&1; then
	echo "[deploy] firewalld активен, открываю ${APP_PORT}/tcp..."
	as_root firewall-cmd --permanent --add-port="${APP_PORT}/tcp" >/dev/null 2>&1 \
		|| warn "не удалось открыть порт в firewalld"
	as_root firewall-cmd --reload >/dev/null 2>&1 || true
fi

# ---------- История версий (для rollback) ----------
CURRENT_TAG=""
if [ -f "${STATE_DIR}/current_tag" ]; then
	CURRENT_TAG="$(cat "${STATE_DIR}/current_tag")"
fi
if [ -n "${CURRENT_TAG}" ]; then
	echo "${CURRENT_TAG}" > "${STATE_DIR}/previous_tag"
fi
echo "${NEW_TAG}" > "${STATE_DIR}/current_tag"

# ---------- Запуск через compose ----------
# Порт передаём через .env рядом с compose: не зависим от того, разрешает ли
# sudoers `env`, и одинаково работает с docker compose и docker-compose.
printf 'APP_PORT=%s\n' "${APP_PORT}" > "${INSTALL_DIR}/.env"

# Убираем контейнер от старого способа запуска (docker run или другой compose),
# иначе будет конфликт имени: compose его не узнаёт.
as_root docker rm -f "${CONTAINER_NAME}" >/dev/null 2>&1 || true
echo "[deploy] ${COMPOSE_BIN[*]} up -d --force-recreate (тег ${NEW_TAG}, порт ${APP_PORT})..."
as_root "${COMPOSE_BIN[@]}" -f "${COMPOSE_FILE}" up -d --force-recreate

# ---------- Проверка монтирования /data ----------
# Проверяем по списку Destination, не разбирая JSON: форматирование вывода
# docker inspect отличается между версиями.
MOUNTS="$(as_root docker inspect -f '{{range .Mounts}}{{.Destination}} {{end}}' "${CONTAINER_NAME}" 2>&1 || true)"
if ! printf '%s' "${MOUNTS}" | grep -qE '(^| )/data( |$)'; then
	echo "[deploy][ERROR] Контейнер запущен без монтирования /data (${DATA_DIR})." >&2
	echo "         Точки монтирования: ${MOUNTS:-<нет вывода>}" >&2
	echo "         Binds: $(as_root docker inspect -f '{{json .HostConfig.Binds}}' "${CONTAINER_NAME}" 2>/dev/null || echo '?')" >&2
	echo "         Данные на хосте не видны приложению. Проверьте volume в ${COMPOSE_FILE}:" >&2
	echo "           - ./data:/data" >&2
	echo "         Что видит compose:" >&2
	as_root "${COMPOSE_BIN[@]}" -f "${COMPOSE_FILE}" config 2>&1 | sed -n '1,80p' >&2 || true
	# Не оставляем работать контейнер с эфемерным /data: данные пропали бы.
	as_root docker rm -f "${CONTAINER_NAME}" >/dev/null 2>&1 || true
	exit 1
fi

# ---------- Проверка здоровья ----------
echo "[deploy] Ожидание healthcheck..."
CID="$(as_root "${COMPOSE_BIN[@]}" -f "${COMPOSE_FILE}" ps -q "${CONTAINER_NAME}" 2>/dev/null || true)"
HEALTHY=0
STATE=""
for _ in $(seq 1 40); do
	if [ -n "${CID}" ]; then
		STATE="$(as_root docker inspect -f '{{.State.Status}}' "${CID}" 2>/dev/null || echo missing)"
		HEALTH="$(as_root docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "${CID}" 2>/dev/null || echo missing)"

		if [ "${HEALTH}" = "healthy" ]; then
			HEALTHY=1
			break
		fi
		if [ "${HEALTH}" = "unhealthy" ] || [ "${STATE}" = "exited" ]; then
			break
		fi
	fi
	sleep 3
done

if [ "${HEALTHY}" -ne 1 ]; then
	echo "[deploy] Контейнер ещё не healthy (state=${STATE:-unknown}); последние логи:"
	as_root docker logs --tail 60 "${CONTAINER_NAME}" 2>&1 || true
	if [ "${STATE}" != "running" ]; then
		echo "[deploy][ERROR] Контейнер не запущен!" >&2
		exit 1
	fi
	warn "healthcheck не стал healthy за ~120 секунд; приложение может ещё накатывать миграции"
fi

# ---------- Проверка порта ----------
if command -v ss >/dev/null 2>&1; then
	if ! ss -ltn 2>/dev/null | grep -q ":${APP_PORT} "; then
		warn "порт ${APP_PORT} не слушается — проверьте ports в ${COMPOSE_FILE} и занятость порта"
	fi
fi

# ---------- Очистка старых образов ----------
PREV_TAG=""
if [ -f "${STATE_DIR}/previous_tag" ]; then
	PREV_TAG="$(cat "${STATE_DIR}/previous_tag")"
fi

KEEP_TAGS=" ${NEW_TAG} latest "
if [ -n "${PREV_TAG}" ]; then
	KEEP_TAGS="${KEEP_TAGS}${PREV_TAG} "
fi

echo "[deploy] Очистка старых образов (оставляю${KEEP_TAGS})..."
for ref in $(as_root docker images --format '{{.Repository}}:{{.Tag}}' "${IMAGE_NAME}"); do
	tag="${ref#"${IMAGE_NAME}:"}"
	case " ${KEEP_TAGS} " in
		*" ${tag} "*) continue ;;
	esac
	as_root docker rmi "${ref}" >/dev/null 2>&1 || true
done

if [ -f "${TAR_PATH}" ]; then
	rm -f "${TAR_PATH}"
	echo "[deploy] Удалён временный ${TAR_PATH}"
fi

echo "[deploy] Контейнер:"
as_root docker ps --filter "name=${CONTAINER_NAME}"
echo "[deploy] UI/API: http://<сервер>:${APP_PORT}"

if [ "${WARN}" -ne 0 ]; then
	echo "[deploy] Готово, но есть предупреждения (см. WARN выше)." >&2
	exit 2
fi

echo "[deploy] Готово. Активный тег: ${NEW_TAG}."
