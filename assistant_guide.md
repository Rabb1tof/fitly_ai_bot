# Assistant Guide — HealthBot

## Общие правила
1. Всегда отвечать пользователю на русском языке и держать ответы лаконичными.
2. Основываться на актуальных данных: код проекта и документы в `docs/` (особенно `architecture.md`, `setup.md`, `operations.md`).
3. Не выполнять деструктивные и сетевые действия без явного разрешения пользователя.
4. Секреты (Telegram токен, строки подключения) хранить вне репозитория (`.env`, user-secrets, секреты CI). Никогда не коммитить реальные значения.

## Структура решения
- `HealthBot.Api` — ASP.NET Core host, DI, запуск `TelegramPollingService` и `ReminderWorker`, миграции.
- `HealthBot.Core` — сущности и доменные типы.
- `HealthBot.Infrastructure` — DbContext, сервисы (`UserService`, `ReminderService`), Telegram update pipeline, фоновые задачи.
- `HealthBot.Shared` — опции (`TelegramOptions`, `ReminderWorkerOptions`), вспомогательные модели.
- `HealthBot.Tests` — интеграционные сценарии для Telegram и unit-тесты.
- `docker-compose.yml` поднимает PostgreSQL и API контейнер.

Ссылки на документацию: см. `docs/architecture.md` и `README.md`.

## Основной workflow (см. также `docs/setup.md`)
1. Подготовить `.env`/user-secrets (`TELEGRAM_BOT_TOKEN`, `ConnectionStrings__Postgres`).
2. Для Docker: `docker compose up -d --build` — миграции применяются автоматически.
3. Для локальной разработки:
   ```bash
   dotnet ef database update -p HealthBot.Infrastructure -s HealthBot.Api
   dotnet run --project HealthBot.Api
   ```
4. Проверить логи (polling должен стартовать) и убедиться, что бот отвечает на `/start` и `/menu`.
5. Напоминания создаются пользователем через шаблон/кастомный сценарий (см. `docs/telegram-flows.md`).

## Сервисы и зависимости
- DI настраивается в `Program.cs` (`HealthBot.Api`).
- `ReminderWorker` — `BackgroundService`, интервал задаётся в `ReminderWorkerOptions`.
- Командный конвейер: `TelegramPollingService` → `TelegramUpdateHandler` → `CommandDispatcher` → обработчики (`Message`/`Callback`).
- Детали в `docs/architecture.md`.

## Рекомендации по разработке
1. Любые изменения в сущностях сопровождать миграциями (`docs/setup.md`).
2. Использовать `ILogger<T>` и `CancellationToken` в асинхронных методах.
3. Новые команды Telegram оформлять отдельными обработчиками с корректным `Priority` и проверками.
4. Обновлять документацию (`README.md`, `docs/`) при изменении архитектуры или сценариев.
5. Для новых интеграций описывать настройки в `Shared` и документировать в `docs/architecture.md` или отдельных файлах.

## Диагностика и отладка
- Логи: `docker compose logs -f healthbot_api` или stdout `dotnet run`.
- БД: `psql -h localhost -U healthbot -d healthbot -c "SELECT * FROM reminder_templates;"`.
- Telegram: при необходимости использовать `getUpdates`, но не включать webhook с активным polling.
- Troubleshooting описан в `docs/setup.md#типичные-проблемы` и `docs/operations.md`.

## TODO / roadmap
- Добавить интеграционные и e2e тесты, подключить `dotnet test` к CI/CD.
- Внедрить структурированное логирование (Serilog + Seq), health-checks и метрики.
- Рассмотреть использование Redis/RabbitMQ для масштабирования напоминаний.
- Документировать админ-функциональность при её появлении.
