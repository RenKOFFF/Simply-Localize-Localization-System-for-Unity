# Этап 6.2: фиксированный автозапуск и настройка проекта

2026-11-16, Unity 6000.0.62f1, Windows x64, Mono. Рабочая ветка dev/localization-stability. Версия package.json пока 2.0.2; это рабочее изменение после релиза, не публикация новой версии.

## Результат для разработчика игры

- Автозапуск и Localization.Initialize() без параметров используют только Resources-адрес SimplyLocalizeSettings. Процедура настройки создаёт Assets/SimplyLocalize/Resources/SimplyLocalizeSettings.asset.
- Генерируемый каталог, Builder и команда Rebuild Bootstrap Catalog удалены. Перед сборкой выполняется только проверка конфликтов зарезервированного адреса; файлы не генерируются.
- В окне редактора без конфигурации есть Set up localization. Создаётся начальный английский язык. В Inspector конфига — добавление языков/недостающих файлов и перенос выбранного конфига для автозапуска. Настройки языка, fallback и автозапуска остаются видимыми и редактируемыми; служебный адрес видим только для чтения.
- Общий Editor API: LocalizationProjectSetup.Configure, AddLanguages, UseForAutomaticStartup. Configure возвращает Config, CreatedPaths и ValidationErrors (проверка конфигурации, не полный аудит содержимого переводов).
- CLI: -executeMethod SimplyLocalize.Editor.LocalizationProjectSetup.ConfigureFromCommandLine -slLanguages en,ru -slDefault en -slFallback en. Диалоги не нужны.
- Повторный вызов не перезаписывает существующие JSON или профили. Configure явно применяет переданные default/fallback; AddLanguages не меняет их. Существующие флаги запуска и логирования сохраняются. При наличии другого конфига создание нового требует явного createAlongsideExistingConfigs:true; обычная настройка останавливается с объяснением.
- Внутренние тесты перенесены с сохранением .meta в Assets/SimplyLocalizeTests полигона. В пакете их больше нет, testables из manifest полигона удалён. README, changelog и лицензия остаются в корне репозитория по решению автора.
- Coverage перестал запрашивать текст для loadTextData=false. Исправлена подсказка Settings о фиксированном пути.
- Объявлена зависимость com.unity.modules.audio:1.0.0: чистая минимальная установка выявила, что LocalizedAudioClip ранее зависел от модуля, не указанного в package.json.

## Переход с предыдущего поведения

Выбрать существующий стартовый LocalizationConfig и нажать Use for automatic startup в Inspector. GUID, ссылки на языки и несохранённые правки сохраняются. Занятый адрес не перезаписывается. Пользовательские строковые пути загрузки при переносе потребуется изменить.

Старый GeneratedConfigCatalog.asset больше не используется; его и пустую папку SimplyLocalizeGenerated можно удалить. В нашем полигоне они удалены. Явный Localization.Initialize(config), LocalizationBootstrap и независимые менеджеры не требуют переноса конфигурации. Для исключительно явной инициализации отключить autoInitialize.

## Проверки

- EditMode: 299 passed, 0 failed, 0 skipped. PlayMode: 26 passed, 0 failed, 0 skipped. Полные результаты рядом.
- Новые проверки текущего контракта: 16 случаев ConfigDiscoveryTests. Они заменяют прежние 10 случаев каталога; ещё одна проверка старого обнаружения удалена из ResourcesLoadingTests. Проверены создание, повторная настройка, внешний JSON, миграция GUID/правок, занятые адреса/профили, невалидные и повторяющиеся коды, read-only, явный старт и asset-only диагностика.
- Первый прогон выявил потерю несохранённого defaultLanguage при AssetDatabase.MoveAsset; исправлено восстановлением снимка настроек после переноса. Финальный полный прогон проходит.
- npm pack: 215 файлов, 0 внутренних тестов, 0 полигонов. package-files.json фиксирует список и контрольные суммы архива.
- Установка npm-архива в отдельный чистый проект: настройка создала 5 файлов, повторная настройка — 0, GUID сохранился. Внешний JSON не перезаписан. Вкладка из пользовательского Assets/Editor обнаружена через TypeCache. Сборки SimplyLocalize.Tests отсутствуют. Windows Player собран без ошибок.
- В этом Player подтверждены автозапуск до Awake, чтение перевода после внешней правки JSON и независимый менеджер со сторонним провайдером. Процесс завершился с кодом 0, player.json содержит результат.
- Второй чистый проект установлен через Git/UPM с подпапкой src/SimplyLocalize. Использован локальный тестовый Git-репозиторий со снимком текущего пакета; внешний сервер и OpenUPM не изменялись. Те же проверки настройки/внешней вкладки прошли, Windows Player собран без ошибок. Это проверка Git UPM, не проверка доступности будущего релиза в GitHub/OpenUPM.
- Настоящая batch-mode точка входа ConfigureFromCommandLine повторно вызвана в первом проекте: created 0 files, exit 0 (setup-cli-result.txt).
- Проверка данных Night Shift в открытом полигоне: PASS, 9 visual resolutions, fallback provenance, menu translations and global-manager isolation. Новый полный Night Shift Player benchmark не выполнялся; исторические отчёты не изменены.

## Воспроизведение

Из src/SimplyLocalize выполнить npm pack --ignore-scripts. В пустом Unity 6000.0.62f1 проекте подключить архив через UPM, установить UI и JsonSerialize (последний нужен самому скрипту отчёта). Положить ExternalStartupProbe.cs в Assets, InstallSmoke.cs в Assets/Editor и вызвать -executeMethod InstallSmoke.Build в batch mode. Затем запустить Build/Startup.exe -batchmode -nographics; результат записывается в Build/startup-result.json. В копиях manifest заменить абсолютные пути на собственные.

Для Git-проверки использовать тот же набор скриптов и Git URL с ?path=/src/SimplyLocalize на ревизию с этим изменением. Для прямой проверки CLI использовать команду из README.

## Ограничения и дальнейшая работа

Пакет не опубликован в OpenUPM и не отправлен на сервер. Другие версии Unity, WebGL/IL2CPP, большие наборы языков и графический интерфейс Inspector отдельными визуальными тестами здесь не проверялись. Внешнее редактирование JSON в Unity-проекте проверено; загрузка языковых пакетов вне проекта требует будущего файлового провайдера.

Полная переработка Coverage (явный запуск, прогресс/отмена, ссылки из отчёта), общий аудит всех public API и проверки шрифтов/сцен остаются дальнейшими задачами. При сбое настройки файлы защищает существующая транзакция; полное восстановление после аварийного завершения процесса не заявляется. Следующий основной этап журнала — безопасное переименование ключей и ссылок.
