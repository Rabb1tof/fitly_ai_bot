# План реализации HealthBot MVP

## 1. Подготовка окружения
1. Установить .NET SDK 9.0.
2. Установить и запустить Docker Desktop.
3. Создать файл `docker-compose.yml` (см. корень проекта) и поднять Postgres:
   ```bash
   docker-compose up -d
   ```
4. Проверить БД: `psql -h localhost -U healthbot -d healthbot`.

## 2. Структура решений
1. Создать каталог `HealthBot`.
2. `dotnet new sln -n HealthBot`.
3. `dotnet new webapi -n HealthBot.Api --framework net9.0`.
4. `dotnet new classlib -n HealthBot.Core --framework net9.0`.
5. `dotnet new classlib -n HealthBot.Infrastructure --framework net9.0`.
6. `dotnet new classlib -n HealthBot.Shared --framework net9.0`.
7. Добавить проекты в solution: `dotnet sln add ...`.
8. Настроить ссылки: Api -> Core/Infrastructure/Shared, Infrastructure -> Core/Shared.

## 3. Домейн и EF Core
1. Добавить пакеты в Infrastructure:
   ```bash
   dotnet add HealthBot.Infrastructure package Microsoft.EntityFrameworkCore
   dotnet add HealthBot.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
   dotnet add HealthBot.Infrastructure package Microsoft.EntityFrameworkCore.Design
   ```
2. Создать `HealthBotDbContext` и сущности `User`, `Reminder` в Core.
3. Настроить `DbContext` (таблицы, индексы, связи).
4. Добавить `UserService`, `ReminderService`, `ReminderWorker`.

## 4. API и DI
1. В `Program.cs` зарегистрировать DbContext, доменные сервисы, фонового воркера и `ITelegramBotClient`.
2. Добавить `appsettings.json` со строкой подключения и настройками Telegram.
3. Подготовить точки расширения для будущего входящего канала (контроллер или long polling), но пока не подключать.

## 5. Миграции
1. Установить глобально инструменты: `dotnet tool install --global dotnet-ef`.
2. `dotnet ef migrations add InitialCreate -p HealthBot.Infrastructure -s HealthBot.Api`.
3. `dotnet ef database update -p HealthBot.Infrastructure -s HealthBot.Api`.
4. Проверить таблицы `users` и `reminders`.

## 6. Запуск и тестирование
1. Заполнить `.env` (переменная `TELEGRAM_BOT_TOKEN`).
2. Собрать и запустить Docker-compose (Postgres + API):
   ```bash
   TELEGRAM_BOT_TOKEN=<token> docker-compose up --build
   ```
3. Проверить логи API (`docker-compose logs -f api`) — убедиться, что polling запущен и бот отвечает на команды `/start`, `/remind`.
4. Локально (без Docker) можно использовать `dotnet run --project HealthBot.Api`, предварительно запустив Postgres и задав переменные окружения.

## 7. Подготовка к расширению
- Добавить покрытие тестами ключевых сервисов.
- Продумать интеграцию RabbitMQ и LLM.
- Ввести секреты через `dotnet user-secrets` или переменные окружения.
- Настроить CI/CD и мониторинг.
