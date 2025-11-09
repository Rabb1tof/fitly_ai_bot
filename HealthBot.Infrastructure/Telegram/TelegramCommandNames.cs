namespace HealthBot.Infrastructure.Telegram;

public static class TelegramCommandNames
{
    public const string CallbackMenu = "menu";
    public const string CallbackMainReminders = "main_reminders";
    public const string CallbackMainNutrition = "main_nutrition";
    public const string CallbackMainSettings = "main_settings";
    public const string CallbackTemplateSelect = "tpl";
    public const string CallbackTemplateDelay = "tpl_delay";
    public const string CallbackTemplateRepeat = "tpl_repeat";
    public const string CallbackCustomNew = "custom_new";
    public const string CallbackCustomDelay = "custom_delay";
    public const string CallbackCustomRepeat = "custom_repeat";
    public const string CallbackRemindersList = "reminders_list";
    public const string CallbackRemindersTemplates = "reminders_templates";
    public const string CallbackRemindersDisable = "reminders_disable";
    public const string CallbackSettingsTimezone = "settings_timezone";
    public const string CallbackSettingsTimezoneSelect = "settings_timezone_select";
    public const string CallbackSettingsTimezoneManual = "settings_timezone_manual";
    public const string CallbackSettingsQuietHours = "settings_quiet_hours";
    public const string CallbackSettingsQuietHoursEdit = "settings_quiet_hours_edit";
    public const string CallbackSettingsQuietHoursDisable = "settings_quiet_hours_disable";
}
