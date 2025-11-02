# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Копируем только файлы проектов для кэширования
COPY HealthBot.sln ./
COPY HealthBot.Api/HealthBot.Api.csproj HealthBot.Api/
COPY HealthBot.Core/HealthBot.Core.csproj HealthBot.Core/
COPY HealthBot.Infrastructure/HealthBot.Infrastructure.csproj HealthBot.Infrastructure/
COPY HealthBot.Shared/HealthBot.Shared.csproj HealthBot.Shared/

# Восстанавливаем зависимости
RUN dotnet restore HealthBot.Api/HealthBot.Api.csproj

# Копируем исходный код
COPY . ./

# Собираем приложение
RUN dotnet publish HealthBot.Api/HealthBot.Api.csproj -c Release -o /app/publish

# Копируем скрипт для миграций
RUN mkdir -p /app/scripts
COPY scripts/apply-migrations.sh /app/scripts/
RUN chmod +x /app/scripts/apply-migrations.sh

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Копируем приложение
COPY --from=build /app/publish .

# Устанавливаем переменные окружения
ENV ASPNETCORE_URLS=http://+:8080

# Запускаем приложение
ENTRYPOINT ["dotnet", "HealthBot.Api.dll"]
