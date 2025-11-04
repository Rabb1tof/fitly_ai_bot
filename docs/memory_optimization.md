# Оптимизация потребления памяти

Документ описывает проведённые оптимизации для предотвращения роста памяти в HealthBot.

## Проблемы и решения

### 1. InMemoryConversationContextStore

**Проблема:** `ConcurrentDictionary<long, SessionEntry>` накапливал сессии до истечения TTL (30 минут по умолчанию), очистка происходила только при обращениях к стору.

**Решение:**
- Добавлен периодический `Timer` с интервалом 5 минут для фоновой очистки истёкших сессий
- Реализован `IDisposable` для корректного освобождения ресурсов при остановке приложения
- Проверка `_disposed` предотвращает работу с освобождёнными объектами

**Файл:** `HealthBot.Infrastructure/Telegram/InMemoryConversationContextStore.cs`

### 2. TelegramUpdateHandler

**Проблема:** Дублирование логики обработки сообщений и callback-запросов, отсутствие централизованной обработки исключений, использование `CreateScope` вместо `CreateAsyncScope`.

**Решение:**
- Вынесена общая логика в метод `ProcessUpdateAsync`
- Использование `CreateAsyncScope` для корректного освобождения асинхронных ресурсов
- Добавлена обработка исключений с автоматическим сбросом сессии при ошибках
- Добавлены `.ConfigureAwait(false)` для снижения накладных расходов на синхронизацию контекста

**Файл:** `HealthBot.Infrastructure/Services/TelegramUpdateHandler.cs`

### 3. TelegramPollingService

**Проблема:** Периодический рестарт поллинга мог приводить к накоплению ресурсов при некорректной обработке `CancellationTokenSource`.

**Решение:**
- Улучшена логика создания и освобождения `linkedCts`
- Корректная обработка `OperationCanceledException` с различением причин отмены
- Гарантированное освобождение `linkedCts` в блоке `finally`

**Файл:** `HealthBot.Infrastructure/Services/TelegramPollingService.cs`

### 4. UserService — утечка через EF Core ChangeTracker

**Проблема:** `DbContext.Attach()` и отслеживаемые запросы накапливали сущности в `ChangeTracker`, который не очищался между запросами в scoped-сервисе.

**Решение:**
- Использование `AsNoTracking()` для чтения пользователей из БД
- Явное отсоединение сущностей через `EntityState.Detached` после сохранения
- Избежание `Attach()` кешированных объектов — вместо этого загрузка свежей сущности для обновления

**Файл:** `HealthBot.Infrastructure/Services/UserService.cs`

### 5. ReminderService — накопление сущностей в ChangeTracker

**Проблема:** Напоминания, загруженные с `Include(r => r.User)`, оставались в `ChangeTracker` после обработки, что приводило к росту памяти при большом количестве напоминаний.

**Решение:**
- Отсоединение сущностей после `SaveChangesAsync` в методах:
  - `ScheduleReminderAsync`
  - `MarkAsSentAsync`
  - `DeactivateReminderAsync`
- Это гарантирует, что `ChangeTracker` не удерживает обработанные объекты

**Файл:** `HealthBot.Infrastructure/Services/ReminderService.cs`

## Рекомендации по мониторингу

### Метрики для отслеживания

1. **InMemoryConversationContextStore:**
   - Количество активных сессий (`_sessions.Count`)
   - Частота срабатывания очистки
   - Средний TTL сессий

2. **DbContext:**
   - Количество отслеживаемых сущностей (`ChangeTracker.Entries().Count()`)
   - Время жизни scoped-контекстов
   - Частота вызовов `SaveChangesAsync`

3. **Общие метрики памяти:**
   - GC Heap Size (Gen 0, Gen 1, Gen 2)
   - Количество коллекций GC
   - Allocated memory per request
   - Working Set / Private Bytes процесса

### Инструменты диагностики

```bash
# Мониторинг GC и памяти в реальном времени
dotnet-counters monitor --process-id <PID> \
  System.Runtime \
  Microsoft.AspNetCore.Hosting

# Создание дампа памяти для анализа
dotnet-gcdump collect --process-id <PID>

# Анализ дампа
dotnet-gcdump report <dump-file>

# Профилирование с dotnet-trace
dotnet-trace collect --process-id <PID> --profile gc-collect
```

### Настройки для продакшена

**appsettings.Production.json:**
```json
{
  "Redis": {
    "ConnectionString": "redis:6379",
    "DefaultTtlMinutes": 10,
    "MessageRateLimitPerMinute": 30,
    "CallbackRateLimitPerMinute": 60
  },
  "Telegram": {
    "PollingRestartMinutes": 30
  }
}
```

**Рекомендации:**
- Обязательно использовать Redis в продакшене для `IConversationContextStore`
- Снизить `DefaultTtlMinutes` до 5-10 минут для in-memory режима
- Настроить rate limiting для защиты от спама
- Включить периодический рестарт поллинга (30-60 минут)

## Реализованные улучшения

### 1. DbContext Pooling ✅

**Реализовано:** Использование `AddDbContextPool` вместо `AddDbContext` с размером пула 128 экземпляров.

**Преимущества:**
- Снижение аллокаций на создание/уничтожение контекстов
- Переиспользование уже инициализированных экземпляров
- Улучшение производительности при высоких нагрузках
- Автоматическое управление жизненным циклом контекстов

**Файл:** `HealthBot.Api/Program.cs`

**Важно:** При использовании pooling необходимо обязательно отсоединять сущности через `EntityState.Detached` после сохранения, иначе они будут передаваться между запросами.

### 2. Нагрузочные тесты ✅

**Реализовано:** Комплект нагрузочных тестов для проверки поведения под нагрузкой.

**Файлы:**
- `HealthBot.Tests/LoadTests/TelegramLoadTests.cs` — набор тестов
- `scripts/run-load-tests.sh` — скрипт для запуска

**Тесты включают:**
1. **LoadTest_1000_ConcurrentUsers** — симуляция 1000 пользователей с 10 сообщениями каждый
2. **LoadTest_DbContextPool** — проверка переиспользования контекстов из пула
3. **LoadTest_ConversationContextStore** — проверка автоматической очистки сессий
4. **LoadTest_UserService** — проверка отсутствия утечек в UserService
5. **LoadTest_ReminderService** — проверка отсутствия утечек в ReminderService

**Запуск:**
```bash

# Все тесты (включая нагрузочные)
dotnet test

# Отдельный тест
dotnet test --filter "FullyQualifiedName~LoadTest_1000_ConcurrentUsers_ShouldHandleWithoutMemoryLeak"

# Только нагрузочные тесты
dotnet test --filter "Category=Load"

# Скрипт сборки и запуска нагрузки
./scripts/run-load-tests.sh
```

**Критерии успеха:**
- Рост памяти < 100 MB на 10000 запросов
- ChangeTracker пуст после каждого запроса
- DbContext переиспользуется из пула
- Отсутствие утечек сущностей

## Дальнейшие улучшения

1. **Метрики и телеметрия:**
   - Интеграция с Prometheus/Grafana
   - Добавление custom metrics для отслеживания размера `_sessions`, `ChangeTracker`
   - Application Insights / OpenTelemetry для распределённой трассировки

3. **Оптимизация сериализации:**
   - Использование `System.Text.Json` source generators для снижения аллокаций
   - Пулинг буферов при работе с Redis

4. **Ограничение параллелизма:**
   - Throttling для обработки обновлений Telegram
   - Семафоры для ограничения одновременных запросов к БД

5. **Профилирование под нагрузкой:**
   - Нагрузочное тестирование с симуляцией 1000+ активных пользователей
   - Анализ memory dumps при пиковых нагрузках
   - Идентификация hot paths через CPU profiling

## Контрольный список перед деплоем

- [ ] Redis настроен и доступен
- [ ] `DefaultTtlMinutes` установлен в разумные пределы (5-10 минут)
- [ ] Rate limiting включён и настроен
- [ ] Логирование настроено (Serilog/Seq)
- [ ] Мониторинг памяти и GC настроен
- [x] DbContext pooling включён (poolSize: 128)
- [x] Проведено нагрузочное тестирование
- [ ] Настроены алерты на рост памяти
- [x] Документация обновлена
- [x] Все сервисы отсоединяют сущности после SaveChangesAsync

## Ссылки

- [EF Core Performance Best Practices](https://learn.microsoft.com/en-us/ef/core/performance/)
- [ASP.NET Core Memory Management](https://learn.microsoft.com/en-us/aspnet/core/performance/memory)
- [.NET GC Fundamentals](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals)
