# WWS Crown

Заявочная система ремонта скважин (нефтепромысловый контур). On-prem у заказчика, доступ через мобильный интернет.

Продукт: **WWS Crown** (Werkon Web Services). Форк инфраструктуры [WerkonWebServicesRatchet](https://github.com/Selt3Z/WerkonWebServicesRatchet); домен СТО заменён на заявки.

## MVP

- Заявки: список / создание / карточка, статусы как в черновике заказчика
- Вложения к заявке (фото/файлы)
- Роли: **Administrator**, **Dispatcher**, **Initiator**
- Склад и каталог услуг подразделений (per-user, SLA)
- Журнал аудита, настройки, профиль организации, состояние системы
- Транспорт — пункт меню «Скоро»
- UI: Razor Pages (без Blazor Server — стабильнее на LTE)

## Стек

- ASP.NET Core Web API + Razor Pages (`net10.0`)
- PostgreSQL 17, EF Core migrations
- Identity (cookie `Crown.Auth`)
- Docker Compose: `postgres`, `api`, `web`, Caddy, `backup`

## Структура

| Путь | Назначение |
|------|------------|
| `Crown.Api` | API |
| `Crown.Web` | Razor UI |
| `docker-compose.yml` | стек |
| `.env.example` | шаблон секретов |

## Запуск (Docker)

```bash
cp .env.example .env
# задать POSTGRES_PASSWORD, ConnectionStrings__*, Seed__AdminPassword

# один раз: бинари docker CLI внутрь образа API (Linux/mac; на Windows — bash/Git Bash или WSL)
bash scripts/prepare-docker-cli.sh

docker compose up -d --build
```

Hosts (PowerShell от Администратора):

```powershell
powershell -ExecutionPolicy Bypass -File docker\setup-hosts.ps1
```

Открыть: `http://crown.local`  
Логин по умолчанию из `.env` (`Seed__Admin*`).

### Health

| URL | Сервис |
|-----|--------|
| `GET /health` | API liveness |
| `GET /health/ready` | API + БД + миграции |
| `GET /health` | Web |

## Обновление на сервере

GitHub pull из UI **отключён** (`UPDATE_ENABLED=false`). На хосте после `git pull`:

```bash
cd /path/to/WerkonWebServicesCrown
bash scripts/update-stack.sh
```

Если мешают локальные правки tracked-файлов:

```bash
bash scripts/update-stack.sh --reset-local
```

Скрипт: prepare-docker-cli → `compose build api web backup` → recreate api (ждёт healthy/миграции) → web + backup → proxy. Лог: `logs/update-*.log`.

### Офлайн (без registry на объекте)

На машине разработчика:

```bash
docker compose build
docker save mns-api mns-web mns-backup -o crown-images.tar
```

На сервере заказчика:

```bash
docker load -i crown-images.tar
docker compose up -d
```

Перед обновлением — бэкап БД. Вложения на диске (`ATTACHMENTS_HOST_PATH`) в `pg_dump` **не** входят.

### Вложения на отдельном диске

В `.env` (не в UI — нужен remount Docker):

```bash
ATTACHMENTS_HOST_PATH=/mnt/data2/crown-attachments
```

Иерархия внутри: `{номер_заявки}/{guid}_{имя}`. Переезд со старого места: `scripts/migrate-attachments.sh`, временно `ATTACHMENTS_LEGACY_HOST_PATH` для чтения старых файлов.

## Основные API

- Auth: `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`
- Заявки: `GET/POST /api/requests`, `GET /api/requests/{id}`, `POST /api/requests/{id}/transition`, вложения
- Склад / каталог / settings / users / audit / system — см. `Crown.Api/Features/`

## Заметки

- Инициатор видит свои заявки; диспетчер/админ — все и переходы статусов
- Склад включается настройкой `WarehouseEnabled` (по умолчанию включён)
- Логотип: `fulllogo.png` → `Crown.Web/wwwroot/images/fulllogo.png`
"# WerkonWebServicesCrown" 
