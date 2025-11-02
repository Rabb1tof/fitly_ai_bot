# Assistant Guide — HealthBot

## Общие правила
1. Всегда отвечать пользователю на русском языке и держать ответы краткими.
2. В первую очередь опираться на документацию в каталоге `docs/` и актуальный код.
3. Не выполнять потенциально деструктивные действия без явного разрешения пользователя.
4. Секреты (токен Telegram, строки подключения) хранить во внешних источниках (`dotnet user-secrets`, переменные окружения). Не коммитить реальные значения.

## Структура решения
- **HealthBot.Api** — веб-API (.NET 9), регистрация DI, health-check endpoint `/`.
- **HealthBot.Core** — доменные сущности (`User`, `Reminder`) и будущие доменные сервисы.
- **HealthBot.Infrastructure** — `HealthBotDbContext`, EF Core, сервисы работы с пользователями/напоминаниями, фоновый воркер, интеграция Telegram.Bot.
- **HealthBot.Shared** — общие DTO и опции (`TelegramOptions`).
- Корневой `docker-compose.yml` поднимает PostgreSQL 16 с пользователем `healthbot`.

## Основной workflow
1. **БД**: запуск `docker-compose up -d`.
2. **Миграции**:
   ```bash
   dotnet tool install --global dotnet-ef   # один раз
   dotnet ef migrations add <Name> -p HealthBot.Infrastructure -s HealthBot.Api
   dotnet ef database update -p HealthBot.Infrastructure -s HealthBot.Api
   ```
3. **Запуск API**:
   - В контейнерах: `TELEGRAM_BOT_TOKEN=<token> docker-compose up --build`
   - Локально: `dotnet run --project HealthBot.Api` (нужны переменные окружения и запущенный Postgres)
4. **Создание данных** — использовать /remind в Telegram (polling слушает команды), либо временные скрипты.
5. **Тестирование** — проверить логи `ReminderWorker` и убедиться, что сообщения отправляются ботом.

## Сервисы и зависимости
- DI регистрируется в `Program.cs` (DbContext, UserService, ReminderService, ReminderWorker, ITelegramBotClient, TelegramUpdateHandler, TelegramPollingService).
- `ReminderWorker` работает как `BackgroundService`, опрашивая БД каждые 60 секунд и отправляя напоминания.
- Polling запускается через `TelegramPollingService`, команды `/start` и `/remind` поддерживаются.

## Рекомендации по разработке
1. Следить за миграциями: любые изменения сущностей отражать миграциями.
2. Добавлять логирование через `ILogger<T>`.
3. Для новых внешних интеграций создавать отдельные сервисы в Infrastructure и описывать настройки в Shared.
4. При расширении функциональности (например, добавление webhook) обновлять `docs/analysis.md` и `docs/implementation_plan.md`.
5. Перед внедрением очередей/LLM подготовить технико-архитектурный документ.

## Диагностика и отладка
- Проверка БД: `psql -h localhost -U healthbot -d healthbot -c "SELECT * FROM users;"`.
- Логи приложения — стандартный вывод `dotnet run`.
- Telegram: контролировать отправку сообщений через логи и `getUpdates` (при тестах локально);
  при работе в Docker — следить за логами сервисов `api` и `postgres`.

## TODO для будущих итераций
- Добавить интеграционные тесты.
- Рассмотреть Serilog + Seq для логирования.
- Подготовить CI/CD pipeline (GitHub Actions или Azure DevOps).
- Рассмотреть кеш Redis и брокер (RabbitMQ) для повышения надёжности.
