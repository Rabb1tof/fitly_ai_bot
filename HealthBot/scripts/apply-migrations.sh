#!/bin/bash
set -e

# Ждем, пока база данных будет готова
until PGPASSWORD=healthbot psql -h postgres -U healthbot -d healthbot -c 'SELECT 1' > /dev/null 2>&1; do
  echo "Waiting for database to start..."
  sleep 1
done

# Проверяем, существует ли база данных
if ! PGPASSWORD=healthbot psql -h postgres -U healthbot -lqt | cut -d \| -f 1 | grep -qw healthbot; then
  echo "Database does not exist, creating..."
  PGPASSWORD=healthbot createdb -h postgres -U healthbot healthbot
fi

# Проверяем, есть ли таблицы в базе данных
TABLES_EXIST=$(PGPASSWORD=healthbot psql -h postgres -U healthbot -d healthbot -t -c "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_schema = 'public' LIMIT 1);" | tr -d '[:space:]')

if [ "$TABLES_EXIST" != "t" ]; then
  echo "No tables found, applying migrations..."
  dotnet ef database update -p /app/HealthBot.Infrastructure -s /app/HealthBot.Api
else
  echo "Database already has tables, skipping migrations"
fi

# Запускаем основное приложение
exec "$@"
