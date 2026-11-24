# Обновление с 2.0.2 на 2.1.0

Сохраните копию проекта и настройки старых профилей. Готовые переводы и таблицы ассетов можно использовать повторно.

1. Обновите пакет. Если меняете язык из кода, замените `Localization.SetLanguage(code)` на `await Localization.SetLanguageAsync(code)`.
2. В окне **Simply Localize → Setup** выберите языки, укажите основной и резервный, нажмите **Create localization**.
3. В **Settings → Content sets → Connect a source** подключите свои JSON и таблицы к **Common**. Укажите язык каждого файла. В **Translations** нажмите **Register** для найденных ключей.
4. Если используете **Bootstrap**, назначьте новый конфиг и стартовый язык. Общие шрифты задайте в **Appearance**. В записях `LocalizedProfileOverride` и `LocalizedEvent` укажите код языка, например `ru`. Привязки методов сохраняются.

[Другие изменения API и особые случаи](../release-checks/20261124-openupm-comparison/REPORT.md)
