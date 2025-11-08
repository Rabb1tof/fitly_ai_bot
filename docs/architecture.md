# Архитектура HealthBot

Документ описывает текущее устройство HealthBot: основные компоненты, взаимодействие сервисов и направления развития.

## Цели
- Telegram-бот для wellbeing-напоминаний с настраиваемыми сценариями.
- Надёжное хранение данных в PostgreSQL через EF Core.
- Фоновая обработка оповещений без внешних брокеров.
- Чёткое разделение ответственности между проектами solution.

## Обзор компонентов

| Проект | Ответственность |
| --- | --- |
| **HealthBot.Api** | ASP.NET Core host, конфигурация DI, регистрация `ITelegramBotClient`, запуск `TelegramPollingService` и `ReminderWorker`, автоматическое применение миграций. |
| **HealthBot.Core** | Доменные сущности (`User`, `Reminder`, `ReminderTemplate`), вспомогательные типы. |
| **HealthBot.Infrastructure** | `HealthBotDbContext`, миграции, сервисы (`UserService`, `ReminderService`), `ReminderWorker`, Telegram update pipeline (`TelegramUpdateHandler`, `CommandDispatcher`, обработчики команд). |
| **HealthBot.Shared** | Общие модели и настройки (`TelegramOptions`, `ReminderWorkerOptions`). |
| **HealthBot.Tests** | Интеграционные и unit-тесты для Telegram-сценариев, вспомогательные хелперы. |

Внешние зависимости: PostgreSQL 16 (Docker Compose), Telegram Bot API, .NET 9 SDK, Redis 7 (для кэшей и очереди напоминаний).

### Redis в архитектуре
- `session:{chatId}` — кэш `ConversationContext` для каждого чата. TTL задаётся `Redis__ConversationSessionTtlMinutes` и используется как экспирация кэша; источник истины хранится в таблице `conversation_sessions` PostgreSQL.
- `user:{telegramId}` — кэш профиля пользователя, ускоряющий повторные запросы.
- `reminders:queue` — SortedSet с напоминаниями и их `NextTriggerAt`, из которого читает `ReminderWorker`. При очистке Redis очередь автоматически восстанавливается из PostgreSQL в пределах окна `Redis__ReminderQueueRecoveryWindowMinutes` под защитой блокировки `lock:reminders:restore`.
- `reminder_templates` и `reminders:user:{guid}` — кэши системных шаблонов и активных напоминаний пользователя.
- `rl:msg:{chatId}` / `rl:cb:{chatId}` — ключи для rate limiting сообщений и callback-ов.

## Потоки данных

### 1. Telegram update flow
1. `TelegramPollingService` получает обновления через `ITelegramBotClient` (long polling).
2. Каждое обновление обрабатывает `TelegramUpdateHandler`, формируя `CommandContext` с `ConversationContext`, данными пользователя и DI-сервисами.
3. `CommandDispatcher` перебирает зарегистрированных обработчиков (`MessageCommandHandlerBase`, `CallbackCommandHandlerBase`) в порядке `Priority` и вызывает первый подходящий по `CanHandle`.
4. Обработчик (например, `StartCommandHandler`, `TemplateSelectCallbackHandler`) модифицирует `ConversationContext`, обращается к доменным сервисам и отправляет ответ через `CommandContext.SendMessageAsync`/`DeleteLastMessageAsync`.
5. Ответ отправляется в Telegram, `ConversationContext.LastBotMessageId` обновляется для последующего удаления сообщения.

### 2. Reminder scheduling
1. Сценарии (шаблонные и кастомные) завершаются вызовом `ReminderWorkflow.FinalizeReminderAsync`.
2. `ReminderService.ScheduleReminderAsync` сохраняет напоминание в БД в виде UTC-времени, привязанного к пользователю и (опционально) шаблону, и ставит его в Redis-очередь.
3. При отображении времени пользователю используется `TimeZoneHelper`, переводящий UTC в выбранную таймзону.

### 3. Reminder worker
1. `ReminderWorker` (наследник `BackgroundService`) периодически вызывает `ReminderService.DequeueDueRemindersAsync`, который сначала читает Redis, а при пустой очереди восстанавливает её из БД.
2. Для каждого напоминания отправляется сообщение в Telegram и вызывается `ReminderService.MarkAsSentAsync`, обновляющий `NextTriggerAt` или деактивирующий запись и при необходимости повторно ставящий напоминание в очередь.

### 4. Persistence
- `HealthBotDbContext` описывает сущности пользователей, напоминаний, шаблонов и сессий (`conversation_sessions`).
- Миграции хранятся в `HealthBot.Infrastructure/Migrations`; при старте `HealthBot.Api` выполняет `Database.Migrate()` и создаёт таблицу сессий.
- Системные шаблоны и иные сиды применяются миграциями автоматически.

## ConversationContext
- Хранит `Flow`, `Stage`, выбранный шаблон, интервалы, пользовательские сообщения, `ExpectManualInput` и `LastBotMessageId`.
- `ResetFlowState()` очищает состояние сценария, не затрагивая `LastBotMessageId`, что позволяет корректно удалять последнее сообщение.
- `Reset()` сбрасывает и состояние сценария, и `LastBotMessageId`, используется при полном сбросе диалога.

## Конфигурация и окружение
- Настройки считываются через `IOptions<T>` (`TelegramOptions`, `ReminderWorkerOptions`).
- `.env` хранит секреты (токен Telegram, строку подключения), Docker Compose пробрасывает их в контейнеры.
- Для локальной разработки переменные можно задавать через `dotnet user-secrets` или оболочку.

## Безопасность и наблюдаемость
- Токены и пароли не коммитятся; используются внешние секреты.
- Логирование осуществляется через стандартный `ILogger<T>`; планируется интеграция с Serilog/Seq.
- При высоких нагрузках необходимы rate limiting и защита от спама.

## Направления развития
- Поддержка webhook-режима (через reverse proxy/HTTPS).
- Введение очередей (RabbitMQ/Kafka) для масштабируемых напоминаний.
- Интеграция LLM/аналитики для персонализации.
- Добавление кэша (Redis) и централизованного мониторинга (Prometheus + Grafana).
- Веб- или мобильная админка для управления пользователями и шаблонами.

## Высокоуровневая схема
```
Telegram Bot API <---> TelegramPollingService --> TelegramUpdateHandler --> CommandDispatcher
                                                                      |--> Message/Callback Handlers
                                                                      |--> ReminderWorkflow / UserService / ReminderService
                                                                      |--> HealthBotDbContext (PostgreSQL)

ReminderWorker --> ReminderService --> HealthBotDbContext --> (Telegram Bot API)
```

Диаграмма может быть визуализирована в draw.io/PlantUML при необходимости.
