# Эксплуатация и операции

Инструкция для поддержки и эксплуатации HealthBot.

## Мониторинг и логи
- Запуск в Docker: `docker compose logs -f healthbot_api` и `docker compose logs -f healthbot_db`.
- Локально: `dotnet run --project HealthBot.Api` выводит подробные логи ASP.NET Core.
- Рекомендуется настроить агрегацию логов (например, Serilog + Seq). Пока используется стандартный консольный логгер.

## Полезные команды
| Назначение | Команда |
| --- | --- |
| Сборка | `dotnet build` |
| Тестирование | `dotnet test` |
| Применение миграций | `dotnet ef database update -p HealthBot.Infrastructure -s HealthBot.Api` |
| Создание миграции | `dotnet ef migrations add <Name> -p HealthBot.Infrastructure -s HealthBot.Api` |
| Пересоздание контейнеров | `docker compose up -d --build` |
| Остановка и очистка данных | `docker compose down -v` |
| Проверка БД | `psql -h localhost -U healthbot -d healthbot` |
| Проверка Redis | `redis-cli -h localhost -p 6379 ping` (в Docker: `docker compose exec redis redis-cli ping`) |
| Просмотр очереди напоминаний | `redis-cli ZRANGE reminders:queue 0 -1 WITHSCORES` |
| Очистка rate limiting ключей | `redis-cli --scan --pattern "rl:*" | xargs -I{} redis-cli DEL {}` |
| Форматирование кода | `dotnet format` |

## Управление секретами
- Telegram токен и строки подключения держите в `.env` или user-secrets.
- Никогда не коммитьте реальные значения; используйте `.env.example`.

## Миграции и обновления
1. Создайте новую миграцию при изменении сущностей (`dotnet ef migrations add ...`).
2. Примените её локально или в контейнере (`dotnet ef database update ...`).
3. Проверьте, что сиды (`reminder_templates`) актуальны.
4. При выкладке в прод окружение убедитесь, что миграции применены до старта API.

## Диагностика проблем
| Симптом | Проверка |
| --- | --- |
| Бот не отвечает | Убедитесь, что polling запущен (`TelegramPollingService` в логах), токен верный. |
| Напоминания не приходят | Проверьте `ReminderWorker` (логи), время срабатывания, таймзону пользователя. |
| Ошибки при удалении сообщений | `LastBotMessageId` должен быть валиден; проверьте права бота в чате. |
| Миграции падают | Проверить строку подключения, права пользователя БД. |
| Redis-операции не работают | Проверьте, что `Redis__ConnectionString` задан, контейнер `healthbot_redis` доступен и `redis-cli ping` возвращает PONG. |
| Напоминания не доставляются | Проверьте `reminders:queue` в Redis, логи `ReminderWorker`, убедитесь, что бот может писать в чат. |
| Воркер спамит повторно | Очистите ключи `reminders:queue` и `lock:reminder:*`, проверьте `ReminderBatchSize` и `ReminderWorkerPollSeconds`. |

## Резервное копирование
- Рекомендуется регулярный backup БД (`pg_dump` / `pg_dumpall`).
- Redis данные в текущей конфигурации не сохраняются (AOF/снапшоты отключены). Если нужно долговременное хранение — включите AOF или volume.
- Для контейнеров — используйте именованные volume (`fitly_ai_bot_postgres_data`).

## Планируемые улучшения
- Добавить health-check endpoints и метрики (Prometheus).
- Включить структурированное логирование и корреляцию.
- Интегрировать alerting (PagerDuty/Slack) на ошибки воркера.
