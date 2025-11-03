# Настройка окружения

Подробное руководство по развёртыванию HealthBot локально и в Docker.

## Предварительные требования
- Docker и Docker Compose
- .NET SDK 9.0+
- PostgreSQL 16 (если запускаете без Docker)
- Телеграм-бот и токен (через @BotFather)

## Конфигурация секретов

### Файл `.env`
Пример содержимого:
```env
# Telegram Bot
TELEGRAM_BOT_TOKEN=1234567890:abcdefg

# База данных (используется и Docker, и локально)
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=healthbot;Username=healthbot;Password=healthbot

# Redis (опционально)
Redis__ConnectionString=localhost:6379
Redis__KeyPrefix=healthbot:
Redis__DefaultTtlMinutes=30
Redis__MessageRateLimitPerMinute=30
Redis__CallbackRateLimitPerMinute=60
Redis__RateLimitWindowSeconds=60
Redis__ReminderLockSeconds=30
Redis__ReminderBatchSize=50
Redis__ReminderLookaheadMinutes=30
Redis__ReminderWorkerPollSeconds=5
```
> Не коммитите `.env`; используйте `.env.example` для шаблона.

### User-secrets (альтернатива для локальной разработки)
```bash
dotnet user-secrets init --project HealthBot.Api
dotnet user-secrets set "TELEGRAM_BOT_TOKEN" "1234567890:abcdefg" --project HealthBot.Api
```

## Запуск в Docker
```bash
docker compose up -d --build
```
- Контейнер `healthbot_api` применяет миграции и запускает long polling.
- Контейнер `healthbot_redis` поднимает Redis. Он используется для хранения сессий, кэшей, rate limiting и очереди напоминаний (см. `Redis__*`).
- Логи: `docker compose logs -f healthbot_api`.
- Остановить и очистить данные: `docker compose down -v`.

## Локальный запуск без Docker
1. Установите и запустите PostgreSQL, создайте базу `healthbot`.
2. Задайте строку подключения (`ConnectionStrings__Postgres`).
3. Примените миграции:
   ```bash
   dotnet ef database update -p HealthBot.Infrastructure -s HealthBot.Api
   ```
4. Запустите API:
   ```bash
   dotnet run --project HealthBot.Api
   ```
5. Проверьте логи: убедитесь, что polling запустился и бот реагирует на `/start`.

## Миграции и сиды
- Создание миграции:
  ```bash
  dotnet ef migrations add <Name> -p HealthBot.Infrastructure -s HealthBot.Api
  ```
- Применение:
  ```bash
  dotnet ef database update -p HealthBot.Infrastructure -s HealthBot.Api
  ```
- Системные шаблоны сидируются автоматически (таблица `reminder_templates`).

## Полезные команды
- `dotnet build` — проверка сборки
- `dotnet test` — запуск тестов
- `dotnet format` — форматирование (если подключено)
- `psql` подключение: `psql -h localhost -U healthbot -d healthbot`

## Типичные проблемы
| Проблема | Решение |
| --- | --- |
| Бот не отвечает | Проверьте токен Telegram и интернет-соединение контейнера/процесса. |
| Миграции не применяются | Убедитесь, что строка подключения корректна и доступ к БД открыт. |
| Ошибка удаления сообщений | Проверьте, что `LastBotMessageId` не сбрасывается досрочно и бот имеет права удалять сообщение. |
| ReminderWorker не отправляет уведомления | Проверьте логи, убедитесь, что время напоминаний наступило и пользователь имеет таймзону. |
| Redis не подключается | Убедитесь, что `Redis__ConnectionString` корректен и контейнер `healthbot_redis` в статусе `healthy`. |
| Срабатывает rate limiting | При необходимости поднимите `Redis__MessageRateLimitPerMinute` / `Redis__CallbackRateLimitPerMinute` или очистите ключи `rl:*` через `redis-cli`. |
| Напоминания не попадают в очередь | Проверьте ключ `reminders:queue` в Redis и убедитесь, что `Redis__ReminderWorkerPollSeconds` не слишком велик. |

## Обновление
- Перед обновлением схемы БД создайте резервную копию (`pg_dump`).
- Для обновления контейнеров используйте `docker compose pull` и `docker compose up -d --build`.
