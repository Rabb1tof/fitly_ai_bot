# Assistant Guide — HealthBot

## Общие правила
1. Всегда отвечать пользователю на русском языке и держать ответы лаконичными.
2. Основываться на актуальных данных: код проекта и документы в `docs/` (особенно `architecture.md`, `setup.md`, `operations.md`).
3. Не выполнять деструктивные и сетевые действия без явного разрешения пользователя.
4. Секреты (Telegram токен, строки подключения) хранить вне репозитория (`.env`, user-secrets, секреты CI). Никогда не коммитить реальные значения.

## Структура решения
- `HealthBot.Api` — ASP.NET Core host, DI, запуск `TelegramPollingService` и `ReminderWorker`, миграции.
- `HealthBot.Core` — сущности и доменные типы.
- `HealthBot.Infrastructure` — DbContext, сервисы (`UserService`, `ReminderService`), Telegram update pipeline, фоновые задачи, интеграция Redis.
- `HealthBot.Shared` — опции (`TelegramOptions`, `ReminderWorkerOptions`), вспомогательные модели.
- `HealthBot.Tests` — интеграционные сценарии для Telegram и unit-тесты.
- `docker-compose.yml` поднимает PostgreSQL, Redis и API контейнер.

Ссылки на документацию: см. `docs/architecture.md` и `README.md`.

## Основной workflow (см. также `docs/setup.md`)
1. Подготовить `.env`/user-secrets (`TELEGRAM_BOT_TOKEN`, `ConnectionStrings__Postgres`, при необходимости `Redis__ConnectionString`).
2. Для Docker: `docker compose up -d --build` — миграции применяются автоматически, поднимаются контейнеры PostgreSQL и Redis.
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
6. **КРИТИЧНО:** Всегда отсоединять сущности через `EntityState.Detached` после `SaveChangesAsync` при использовании DbContext pooling.
7. Использовать `AsNoTracking()` для read-only запросов к БД.
8. Избегать `Attach()` кешированных объектов — загружать свежие сущности для обновления.

## Диагностика и отладка
- Логи: `docker compose logs -f healthbot_api` или stdout `dotnet run`.
- БД: `psql -h localhost -U healthbot -d healthbot -c "SELECT * FROM reminder_templates;"`.
- Telegram: при необходимости использовать `getUpdates`, но не включать webhook с активным polling.
- Troubleshooting описан в `docs/setup.md#типичные-проблемы` и `docs/operations.md`.

## Оптимизация памяти (см. `docs/memory_optimization.md`)

**Реализовано:**
- ✅ DbContext pooling (poolSize: 128) для снижения аллокаций
- ✅ Периодическая очистка InMemoryConversationContextStore через Timer
- ✅ Отсоединение сущностей в UserService и ReminderService
- ✅ CreateAsyncScope вместо CreateScope в TelegramUpdateHandler
- ✅ Нагрузочные тесты (1000+ пользователей) в `HealthBot.Tests/LoadTests/`

**Запуск нагрузочных тестов:**
```bash
./scripts/run-load-tests.sh
```

**Важно:** При работе с EF Core всегда проверять, что `ChangeTracker.Entries().Count() == 0` после обработки запроса.

## TODO / roadmap
- Внедрить структурированное логирование (Serilog + Seq), health-checks и метрики (Prometheus/Grafana).
- Настроить алерты на рост памяти и аномалии GC.
- Документировать админ-функциональность при её появлении.
- Рассмотреть использование RabbitMQ для масштабирования напоминаний.
