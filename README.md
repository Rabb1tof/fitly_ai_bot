# HealthBot

Telegram-бот для здоровья и wellness-напоминаний. Проект состоит из нескольких .NET 9 сервисов и использует PostgreSQL для хранения данных.

## Стек
- .NET 9 (ASP.NET Core Web API)
- Entity Framework Core 9 + Npgsql
- Telegram.Bot 22.x
- Docker / Docker Compose
- PostgreSQL 16

## Структура решения
- `HealthBot.Api` — API-хост, конфигурация DI, запуск фоновых сервисов и автоматическое применение миграций.
- `HealthBot.Core` — доменные сущности (`User`, `Reminder`, `ReminderTemplate`).
- `HealthBot.Infrastructure` — DbContext, миграции, сервисы для напоминаний и обработки Telegram-обновлений, фоновый `ReminderWorker`.
- `HealthBot.Shared` — общие модели и настройки.

## Подготовка окружения
1. Скопируйте `.env` на основе `.env.example` (если появится) и укажите переменные:
   ```env
   TELEGRAM_BOT_TOKEN=1234567890:abcdef...
   ```
2. Убедитесь, что установлены Docker, Docker Compose и .NET 9 SDK.

## Локальный запуск (Docker)
```bash
docker compose up -d --build
```
Сервис `healthbot_api` автоматически применит миграции при старте.

## Локальный запуск (без Docker)
1. Запустите PostgreSQL локально и создайте базу `healthbot`.
2. Установите переменную окружения `ConnectionStrings__Postgres`.
3. Примените миграции:
   ```bash
   dotnet ef database update \
     -p HealthBot.Infrastructure \
     -s HealthBot.Api \
     --connection "Host=localhost;Port=5432;Database=healthbot;Username=...;Password=..."
   ```
4. Запустите API:
   ```bash
   dotnet run --project HealthBot.Api
   ```

## Миграции
Создание новой миграции:
```bash
dotnet ef migrations add <Name> \
  -p HealthBot.Infrastructure \
  -s HealthBot.Api
```
Применение:
```bash
dotnet ef database update \
  -p HealthBot.Infrastructure \
  -s HealthBot.Api
```

## Логика напоминаний
- Системные шаблоны напоминаний (вода, перекус, растяжка) хранятся в таблице `reminder_templates` и сидируются при миграции.
- Пользователь может выбрать шаблон или создать кастомное напоминание через Telegram.
- Повторные напоминания задаются интервалом.
- Фоновый `ReminderWorker` раз в минуту выбирает активные напоминания и отправляет сообщения через Telegram Bot API.

## Telegram-команды и взаимодействие
- `/start` — приветствие и главное меню.
- `/menu` — открыть меню заново.
- `/cancel` — сброс текущего сценария.
- Главное меню предлагает выбор системных шаблонов, создание кастомного напоминания, просмотр и отключение активных напоминаний.
- Все сообщения бота удаляются перед отправкой новых, чтобы чат оставался чистым.

## Скрипты и полезные команды
- `docker compose up -d --build` — собрать и запустить сервисы.
- `docker compose down -v` — остановить и очистить данные Postgres.
- `dotnet build` — быстрая проверка сборки.

## TODO / дальнейшие шаги
- Добавить автоматические тесты.
- Расширить документацию `docs/` и покрытие кейсов Telegram-бота.
- Подготовить CI/CD (GitHub Actions).
