#!/bin/bash

# Скрипт для запуска нагрузочных тестов HealthBot
# Использование: ./scripts/run-load-tests.sh

set -e

echo "========================================="
echo "Запуск нагрузочных тестов HealthBot"
echo "========================================="

# Переход в корневую директорию проекта
cd "$(dirname "$0")/.."

# Проверка наличия .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo "Ошибка: .NET SDK не найден"
    exit 1
fi

echo ""
echo "Сборка проекта..."
dotnet build -c Release --no-incremental

echo ""
echo "========================================="
echo "Тест 1: 1000 одновременных пользователей"
echo "========================================="
dotnet test --no-build -c Release \
    --filter "FullyQualifiedName~LoadTest_1000_ConcurrentUsers_ShouldHandleWithoutMemoryLeak" \
    --logger "console;verbosity=detailed"

echo ""
echo "========================================="
echo "Тест 2: DbContext Pooling"
echo "========================================="
dotnet test --no-build -c Release \
    --filter "FullyQualifiedName~LoadTest_DbContextPool_ShouldReuseContexts" \
    --logger "console;verbosity=detailed"

echo ""
echo "========================================="
echo "Тест 3: Очистка сессий ConversationContextStore"
echo "========================================="
dotnet test --no-build -c Release \
    --filter "FullyQualifiedName~LoadTest_ConversationContextStore_ShouldCleanupExpiredSessions" \
    --logger "console;verbosity=detailed"

echo ""
echo "========================================="
echo "Тест 4: UserService - утечки памяти"
echo "========================================="
dotnet test --no-build -c Release \
    --filter "FullyQualifiedName~LoadTest_UserService_ShouldNotLeakEntities" \
    --logger "console;verbosity=detailed"

echo ""
echo "========================================="
echo "Тест 5: ReminderService - утечки памяти"
echo "========================================="
dotnet test --no-build -c Release \
    --filter "FullyQualifiedName~LoadTest_ReminderService_ShouldNotLeakEntities" \
    --logger "console;verbosity=detailed"

echo ""
echo "========================================="
echo "Все нагрузочные тесты завершены"
echo "========================================="
echo ""
echo "Для профилирования памяти используйте:"
echo "  dotnet-gcdump collect --process-id <PID>"
echo "  dotnet-trace collect --process-id <PID> --profile gc-collect"
echo ""
