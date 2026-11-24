# Simply Localize Lab — проект разработки и личные проверки

Открывать эту папку в Unity **6000.0.62f1**. Корень Git-репозитория находится на два уровня выше; весь проект, исходники пакета и подробные отчёты версионируются вместе.

- Пакет: `../../src/SimplyLocalize`. Подключение в `Packages/manifest.json`: `file:../../../src/SimplyLocalize` (путь относительно папки Packages).
- Night Shift: `Assets/HorrorLab/NightShiftMenu.unity`; описание и управление в `Assets/HorrorLab/README.md`.
- В Test Runner доступны независимые проверки EditMode и PlayMode из `Assets/SimplyLocalizeTests`. PlayMode создаёт компоненты для проверки форматирования, смены языка и оформления прямо в тесте.
- Проверка структуры: Editor-метод `RepositoryMigrationChecks.Verify` проверяет источник пакета, GUID-ссылки и сцены.
- Подробный отчёт переноса: [NS-DOC-004](../../docs/project-structure/0001-local-package-migration/REPORT.md).
- Журнал Night Shift: [NS-DOC-001](../../docs/playground-journal/night-shift/README.md).

## Разработка

Скрипты полигона редактируются непосредственно в Assets этого проекта. Копирования из Samples~ больше нет. Пакет редактируется непосредственно в `src/SimplyLocalize` репозитория и отображается в Unity в разделе Packages. После создания нового файла дождитесь импорта и сохраните в Git сам файл вместе с созданным Unity `.meta`. При переименовании или переносе сохраняйте существующий `.meta`; GUID не пересоздавайте.

Коммитить Assets со всеми `.meta`, Packages/manifest.json, Packages/packages-lock.json и ProjectSettings. Library, Temp, Builds, Logs, UserSettings, TestResults и Recovery исключены из Git. Отобранные результаты проверок сохраняются в docs с версиями, хешами и ограничениями измерений.

Откройте меню игры командой **Tools → Simply Localize Lab → Open Night Shift** и нажмите Play. Команда **Configure Night Shift Channels** настраивает каналы и источники данных этого стенда.

Минимальные зависимости среды: uGUI 2.0.0 (с TMP), Newtonsoft Json 3.2.1, Test Framework 1.6.0; URP 17.0.4 и Input System 1.14.2. Unity Pipeline используется только для управления редактором через CLI, runtime плагина от него не зависит.
