# Telegram сценарии

Документ описывает пользовательские сценарии в Telegram, состояния сессии и соответствующие обработчики.

## Общее устройство
- Входящие обновления обрабатываются `TelegramUpdateHandler`, который создаёт `CommandContext` с `ConversationContext`.
- `CommandDispatcher` выбирает обработчик по `Priority` и `CanHandle`.
- Для каждой команды регистрируется отдельный handler (message или callback).

## ConversationContext
| Поле | Назначение |
| --- | --- |
| `Flow` | Текущий сценарий (`None`, `Template`, `Custom`). |
| `Stage` | Этап сценария (`None`, `AwaitingFirstDelayMinutes`, `AwaitingRepeatMinutes`, `AwaitingTimeZoneManual`, и т. д.). |
| `TemplateCode`, `TemplateId`, `TemplateTitle` | Данные выбранного шаблона. |
| `FirstDelayMinutes`, `TemplateDefaultRepeat` | Интервалы для первого напоминания и повтора. |
| `CustomMessage` | Текст кастомного напоминания. |
| `ExpectManualInput` | Ожидается ли ручной ввод вместо callback-а. |
| `LastBotMessageId` | Идентификатор последнего сообщения бота для удаления. |

- `ResetFlowState()` — очищает состояние сценария, оставляя `LastBotMessageId`.
- `Reset()` — полный сброс (используется при `/cancel` и повторном `/start`).

## Основные команды

### `/start` / `/menu`
- Обработчик: `StartCommandHandler`.
- Действие: `Session.Reset()`, показ главного меню (`MenuWorkflow.SendMainMenuAsync`).
- Ответ: приветствие (для `/start`) + клавиатура основного меню.

### `/cancel`
- Обработчик: `CancelCommandHandler`.
- Действие: `Session.Reset()`, удаление последнего сообщения бота, уведомление о сбросе.

### Главное меню (callback `menu`)
- Обработчик: `MenuCallbackHandler`.
- Действие: `Session.ResetFlowState()`, отображение меню без сброса `LastBotMessageId`.

### Раздел «Напоминания»
- Callback: `main_reminders` → обработчик `MainRemindersCallbackHandler`.
- Стартует `ReminderWorkflow.ShowDashboardAsync`, показывая кнопки: список, шаблоны, кастомное.

### Шаблонное напоминание
1. `reminders_templates` → `RemindersTemplatesCallbackHandler`: показывает шаблоны.
2. `tpl:<code>` → `TemplateSelectCallbackHandler`: заполняет `Session` данными шаблона, спрашивает «Через сколько минут?», клавиатура `KeyboardFactory.DelayKeyboard`.
3. `tpl_delay:<code>:<value>` → `TemplateDelayCallbackHandler`:
   - `manual` → ожидание ручного ввода минут.
   - число → сохраняет `FirstDelayMinutes`, переводит сценарий на `AwaitingRepeatMinutes`, выводит «Как часто повторять?», клавиатура `KeyboardFactory.RepeatKeyboard`.
4. `tpl_repeat:<code>:<value>` → `TemplateRepeatCallbackHandler`:
   - `manual` → ожидание ручного ввода повторного интервала.
   - `default` или число → финализация сценария (`ReminderWorkflow.FinalizeReminderAsync`), сообщение «Готово!…».

### Список напоминаний
- Callback: `reminders_list` → `RemindersListCallbackHandler`.
- Загружает активные напоминания пользователя, форматирует с учётом таймзоны (`TimeZoneHelper`).
- Выводит список с inline-кнопками для отключения (если реализовано).

### Кастомное напоминание
1. Callback: `custom_new` → `CustomNewCallbackHandler` запускает текстовый ввод.
2. Сообщение пользователя → `ReminderWorkflow.HandleCustomMessageAsync`: спрашивает задержку.
3. `custom_delay` callbacks или ручной ввод → сохранение `FirstDelayMinutes`.
4. `custom_repeat` callbacks → финализация (`ReminderWorkflow.FinalizeReminderAsync`).

### Настройки таймзоны
- Callback цепочка: `main_settings` → `settings_timezone` → `settings_timezone_select`/`settings_timezone_manual`.
- Обработчики: `MainSettingsCallbackHandler`, `SettingsTimezoneCallbackHandler`, `SettingsTimezoneSelectCallbackHandler`, `SettingsTimezoneManualCallbackHandler`.
- Позволяет выбрать популярные таймзоны или ввести `Continent/City`/`UTC±N`.

## Удаление сообщений
- `SendTrackedMessageAsync` сохраняет `LastBotMessageId`.
- `DeleteLastBotMessageAsync` удаляет сообщение и обнуляет `LastBotMessageId`.
- При переходе между сценариями используйте `ResetFlowState()` вместо `Reset()`, чтобы сохранить возможность удалить сообщение.

## Расширение сценариев
- Для новых команд создавайте отдельные обработчики с уникальными `Priority`.
- Для inline-клавиатур придерживайтесь префиксов (`prefix:value`), чтобы `CanHandle` мог легко распознать callback.
- При добавлении новых стадий обновляйте `ConversationStage` и документы.
