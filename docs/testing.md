# Тестирование

Руководство по запуску и расширению тестов HealthBot.

## Типы тестов
- **Unit-тесты**: проверяют отдельные сервисы (`ReminderService`, `UserService`, хелперы).
- **Интеграционные тесты Telegram**: сценарии в `HealthBot.Tests/TimeZoneHelperTests.cs` моделируют цепочки сообщений и callback-ов через `HandlerHarness`.
- **Планируемые**: e2e тесты с реальным Telegram API (через sandbox-бота), нагрузочные проверки воркера.

## Запуск
```bash
dotnet test
```
Команда выполняет все проекты тестов (на данный момент `HealthBot.Tests`). Для ускорения можно указать фильтр:
```bash
dotnet test --filter "FullyQualifiedName~TelegramUpdateHandlerTests"
```

## Структура `HealthBot.Tests`
- `TimeZoneHelperTests` — сценарии для логики таймзон и Telegram flow.
- `HandlerHarness` — вспомогательный класс, создающий InMemory DbContext, DI-контейнер и `TelegramUpdateHandler`.
- `UpdateFactory` — генерация Telegram `Update`/`Message` JSON для тестов.

## Добавление новых тестов
1. Создайте новый класс в `HealthBot.Tests` (например, `ReminderWorkerTests`).
2. При необходимости используйте `HandlerHarness` или создайте специальный helper.
3. Следуйте Arrange/Act/Assert, используйте FluentAssertions для читаемых проверок.
4. Убедитесь, что тесты не зависят от реальных Telegram API — используйте мок `ITelegramBotClient`.

## Полезные советы
- Для сложных сценариев комбинируйте `SendTextAsync` и `SendCallbackAsync`, очищая `SentMessages` между шагами.
- Проверяйте состояние БД через `harness.QueryDbContextAsync`.
- При выявлении багов сначала воспроизведите их тестом, затем исправляйте код.

## TODO
- Покрыть `ReminderWorker` unit-тестами (симуляция времени, проверка повторов).
- Добавить тесты на обработку ошибок Telegram (удаление сообщений, retry).
- Настроить `dotnet test` в CI/CD.
