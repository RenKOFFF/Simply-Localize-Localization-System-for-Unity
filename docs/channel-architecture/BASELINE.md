# C0 — исходное состояние и независимые регрессии

Дата: 2026-11-17. База: `5f62636`, ветка `dev/localization-stability`.
Unity 6000.0.62f1 / Windows, открытый проект `playgrounds/SimplyLocalize-Lab`,
локальный UPM из `src/SimplyLocalize`. Unity CLI 1.0.1.

## Инвентаризация границ

| Существующий участок | Что нужно перенести/проверить | Этап |
|---|---|---|
| `Runtime/Core/LocalizationConfig.cs`, `LanguageProfile.cs` | Глобальные languages/default/fallback, loadTextData, Resources path, профили/прямые шрифты → определения каналов/наборов и лёгкие ссылки | C1 |
| `Localization.cs`, `LocalizationManager.cs` | Initialize/Shutdown, SetLanguage, Get/GetAsset, Reload, SetDataProvider, события → один runtime и фасад | C3–C4 |
| `PreparedLocalizationData.cs`, `LocalizationContext.cs` | Готовое состояние, atomic apply, лимиты, cancellation/drain → состояние каналов и владельцев | C2–C3 |
| `Core/Data/ILocalizationDataProvider.cs` и async/provider implementations | Загрузка целого языка или точных Resources-адресов → публичный контракт с объявленной единицей загрузки | C2 |
| `LocalizationResourceLease` и `LocalizationResourceDiagnostics` | Совместное владение, сессии, immutable snapshots; не обещать измерение RAM | C2/C5 |
| `Core/Pluralization/PluralRuleProvider.cs` | Глобальные overrides/cache, общий rule для неизвестного языка → статус поддержки и определённая изоляция | C1/C4 |
| `Core/TextProcessing`, `ProfileApplier`, `ProfileFontCache` | Язык найденного шаблона, overrides, шрифты, отсутствие накопления масштаба | C1/C4 |
| `Runtime/Components` | Глобальная привязка → канал/набор/экземпляр, простые defaults | C4 |
| `Editor/Windows`, `Editor/Data`, utilities | Setup, выбор данных, CSV, rename, Coverage и Diagnostics используют новую идентичность | C5 |
| JSON/text, `_meta.json`, asset tables, config/profile assets, ссылки компонентов | Сохранять полезные переводы; явно перенастроить; старые конфиги удалить после проверки ссылок | C6 |
| `Assets/LocalizationLab` | Минимальные примеры и старые игровые кнопки, не отдельный будущий продукт | C7 |
| `Assets/HorrorLab` | Night Shift, генераторы, bootstrap, игровые сервисы и probes → публичное применение без второго локализатора | C8 |

Пути runtime/editor в таблице относительно `src/SimplyLocalize`; Assets —
относительно проекта полигона. Старые сигнатуры перечислены как поверхность
изменения, не как обещание вечной совместимости. В C0 пакет не меняется.

## Зависимости тестов от демо

До C0:
- `PreparedResourcesTests` выбирал первый AudioClip из таблиц Night Shift.
- `LocalizationContextPlayModeTests` использовал Radio-таблицы Night Shift и
  текстуру `ScopedResourcesProbe`, физически также находящуюся в HorrorLab.
- `ResourceDiagnosticsTests` зависел от Radio-таблиц и их количества записей.
- `NightShiftHudRegressionTests` находился в общей PlayMode-сборке ядра.

После отделения:
- Три набора ресурсных тестов используют `Fixtures/Lifecycle` и известные ключи.
- Четыре коротких PCM WAV, картинка 2x2 и три таблицы независимы от сцен и игры.
  Аудио не preloaded импортёром: готовность обеспечивает реальный context-путь.
- `FixtureIsolationTests` проверяет импортированные зависимости таблиц, чтобы
  возврат ссылок на ресурсы игры не прошёл незамеченным.
- Новый PlayMode-сценарий проверяет разные языки двух контекстов, общий fallback,
  повторное переключение одного контекста и освобождение последним владельцем.
- Два HUD-теста перенесены с сохранением `.meta` в отдельную сборку
  `SimplyLocalize.Tests.NightShift` внутри HorrorLab. Они остаются в полном прогоне.
- В исходниках `Assets/SimplyLocalizeTests` больше нет ссылок на NightShift,
  HorrorLab, LocalizationLab или ScopedResourcesProbe. Это проверка границы
  исходников и ассетов, не отдельный чистый Player без демо.

## Что сохранить от маленького стенда

Удаление отложено до C7. Из существующих автоматических проверок:

| Сценарий LocalizationLab | Независимая регрессия | Что ещё нужно сохранить/проверить при C7 |
|---|---|---|
| Plural, дроби, named/indexed, escaping, selector | `FormattingTests`, `LocalizationRefreshTests.FormattableText_UpdatesEscapesSelectorsAndFractionalPlurals` | Наглядный пример параметров в новом демо |
| Fallback и язык формы | `LocalizationLoadingTests.Get_FormatsFallbackUsingItsSourceLanguage`, `PreparedLocalizationTests` | Видимость fallback в окне/демо |
| Reload при том же языке | `LocalizationLoadingTests`, `LocalizationRefreshTests.Reload_RefreshesActiveTextAndSpriteWithoutChangingLanguage` | Повторить через новый API |
| Смена провайдера, отказ сохраняет старый текст | `LocalizationLoadingTests`, `LocalizationRefreshTests.ProviderChange_RefreshesActiveComponents_AndFailureKeepsThemConsistent` | Сохранить пример пользовательского источника |
| TMP/legacy, overrides и повторное включение | `ProfileOverrideTests`, `ProfileApplierTests` | Визуальная проверка реального layout, не только значения свойств |
| Повторный Play и начальное состояние | `BootstrapLifecycleTests`, `PlaySessionResetTests` | Реальные входы с отключённым Domain Reload; reflection-reset не заменяет их |
| Реальные Button.onClick и контроллер сцены | `LocalizationLabChecks.Run()` в старом стенде | Перенести минимальный пользовательский сценарий в C7/C8 до удаления контроллера |

## Проверки

До изменений: **443 EditMode + 44 PlayMode passed**, 0 failed/skipped/inconclusive.
Отчёты локально: `TestResults/Channels-C0/baseline-editmode.json` и
`baseline-playmode.json`.

После изменений: **444 EditMode + 45 PlayMode passed**, 0 failed/skipped/inconclusive.
Из 45 PlayMode два — `SimplyLocalize.Tests.NightShift`, остальные 43 — ядро.
Новые `FixtureIsolationTests` и сценарий независимых контекстов прошли.
Компиляция без ошибок; `git diff --check` чистый. Отчёты локально:
`TestResults/Channels-C0/final-editmode.json` и `final-playmode.json`.
Изменённый при прогонах/сохранении динамический `HorrorLab/Data/Font.asset`
восстановлен из исходной ревизии; генерация атласа не относится к C0.
C0 выполнен, следующий этап — C1.

Этот шаг не проверяет реализацию новых каналов, миграцию данных, Player,
Addressables, WebGL/IL2CPP, полное покрытие языков или преимущество по памяти.
Необходимые фикстуры сохраняются в Git; сырые отчёты и одноразовый скрипт их
создания остаются в игнорируемом TestResults. Данные пользовательского
ProjectSettings не входят в коммит.
