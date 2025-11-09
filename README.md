# HealthBot

Telegram-бот ([@fitly_robot](https://t.me/fitly_robot)) для wellbeing-напоминаний, построенный на .NET 9 и PostgreSQL. Проект включает API, фоновые задачи и обработку Telegram-апдейтов, исполняется локально или в Docker.

## Содержание
- [Обзор](#обзор)
- [Быстрый старт](#быстрый-старт)
- [Структура решения](#структура-решения)
- [Конфигурация окружения](#конфигурация-окружения)
- [Команды разработки](#команды-разработки)
- [Пользовательские сценарии](#пользовательские-сценарии)
- [Документация](#документация)

## Обзор
- **Стек:** .NET 9, ASP.NET Core, EF Core + Npgsql, Telegram.Bot 22.x, Docker, PostgreSQL 16, Redis (опционально).
- **Основные сервисы:** API-хост, ReminderWorker и CommandDispatcher, управляющий Telegram-командами.
- **Особенности:** хранение напоминаний в UTC с учётом пользовательской таймзоны, шаблоны и кастомные сценарии, автоматическое удаление предыдущих ответов бота, режим «тихих часов» для индивидуальных ограничений по времени.

Подробная архитектура описана в [docs/architecture.md](docs/architecture.md).

## Быстрый старт

### Docker
```bash
docker compose up -d --build
```
Сервис `healthbot_api` применяет миграции автоматически и начинает long polling.

### Локальная разработка
1. Запустите PostgreSQL и создайте БД `healthbot`.
2. Установите переменную `ConnectionStrings__Postgres` (см. [docs/setup.md](docs/setup.md)).
3. Примените миграции:
   ```bash
   dotnet ef database update -p HealthBot.Infrastructure -s HealthBot.Api
   ```
4. Запустите API:
   ```bash
   dotnet run --project HealthBot.Api
   ```

## Структура решения
- `HealthBot.Api` — конфигурация DI, запуск фоновых сервисов, миграции.
- `HealthBot.Core` — доменные сущности (`User`, `Reminder`, `ReminderTemplate`).
- `HealthBot.Infrastructure` — DbContext, Telegram-потоки, фоновые задачи, инфраструктура Redis и постоянное хранение сессий в PostgreSQL (`conversation_sessions`).
- `HealthBot.Shared` — общие модели и настройки.
- `HealthBot.Tests` — интеграционные и unit-тесты для Telegram-потоков и сервисов.

Диаграммы и описание потоков: [docs/architecture.md](docs/architecture.md).

## Конфигурация окружения
- `.env` содержит секреты (см. пример в [docs/setup.md](docs/setup.md)).
- Ключевые переменные:
  - `TELEGRAM_BOT_TOKEN`
  - `ConnectionStrings__Postgres`
  - опциональные настройки Redis: `Redis__ConnectionString`, `Redis__KeyPrefix`, `Redis__DefaultTtlMinutes`, `Redis__ConversationSessionTtlMinutes`
- для rate limiting: `Redis__MessageRateLimitPerMinute`, `Redis__CallbackRateLimitPerMinute`, `Redis__RateLimitWindowSeconds`
- для планировщика напоминаний: `Redis__ReminderLockSeconds`, `Redis__ReminderBatchSize`, `Redis__ReminderLookaheadMinutes`, `Redis__ReminderWorkerPollSeconds`, `Redis__ReminderQueueRecoveryWindowMinutes`
- При отсутствии Redis автоматически используются in-memory реализации (сессии, кэши, воркер).
- Миграции и сиды напоминаний описаны в [docs/setup.md](docs/setup.md#миграции-и-инициализация).

## Команды разработки
- `dotnet build` — проверка сборки.
- `dotnet test` — запуск тестов (детали в [docs/testing.md](docs/testing.md)).
- `dotnet ef migrations add <Name> -p HealthBot.Infrastructure -s HealthBot.Api` — новая миграция.
- `docker compose down -v` — остановка и очистка данных.
- `dotnet format` — форматирование (если подключено).

Расширенный список команд: [docs/operations.md](docs/operations.md).

## Пользовательские сценарии
- `/start` / `/menu` — главное меню.
- `/cancel` — сброс текущего сценария без очистки истории.
- Раздел «Напоминания» поддерживает шаблоны и кастомные сценарии.
- «Настройки» позволяют выбрать таймзону (`Continent/City`, `UTC±N`) и влияют на отображение времени, а также задать «тихие часы» (напоминания переносятся на ближайшее разрешённое время).

Подробные потоки, клавиатуры и состояния сессии описаны в [docs/telegram-flows.md](docs/telegram-flows.md).

## Документация
- [docs/architecture.md](docs/architecture.md) — компоненты и техничка.
- [docs/setup.md](docs/setup.md) — развёртывание и конфигурация (PostgreSQL, Redis).
- [docs/operations.md](docs/operations.md) — эксплуатация, миграции, полезные команды.
- [docs/testing.md](docs/testing.md) — структура и запуск тестов.
- [docs/contributing.md](docs/contributing.md) — правила для разработчиков.
- [docs/telegram-flows.md](docs/telegram-flows.md) — сценарии взаимодействия в Telegram.

См. также [assistant_guide.md](assistant_guide.md) для AI-помощников и внутренних инструментов.
