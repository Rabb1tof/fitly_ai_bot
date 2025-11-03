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

## Резервное копирование
- Рекомендуется регулярный backup БД (`pg_dump` / `pg_dumpall`).
- Для контейнеров — используйте именованные volume (`fitly_ai_bot_postgres_data`).

## Планируемые улучшения
- Добавить health-check endpoints и метрики (Prometheus).
- Включить структурированное логирование и корреляцию.
- Интегрировать alerting (PagerDuty/Slack) на ошибки воркера.
