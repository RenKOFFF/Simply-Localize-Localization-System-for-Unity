# NS-DOC-004 — перенос репозитория и проверка локального пакета

Редакция **1.2**, 2026-11-16, Asia/Yekaterinburg. В редакции 1.1 добавлено закрытие MIG-001; в 1.2 удалены избыточные артефакты и исправлены инструкции воспроизведения. Исторические измерения не пересчитывались.

## 1. Идентичность проверенной среды

| Параметр | Значение |
|---|---|
| Night Shift | 1.1.0, протокол plugin-owned-v1 |
| Simply Localize package.json | 2.0.2 |
| Git HEAD | d2f77957ee30e06309a8a16ffa7c876929ad6384 |
| Describe | 2.0.2-5-gd2f7795-dirty |
| Пакет в рабочем дереве | `git diff -- src` пуст; изменений исходников пакета нет |
| Unity | 6000.0.62f1, Windows |
| Player | Windows x64 Release, BuildOptions.None |
| Проверка Player | -batchmode -nographics, Ryzen 5 5600 |
| Run ID | 20261116T121001Z_nightshift-1.1.0_d2f7795 |

Суффикс dirty относится к изменению структуры и документации всего репозитория. В build identity поле localizerWorkingTreeStatus пустое: оно проверяет только каталог пакета. Версия package.json не обозначает точное состояние релизного тега: HEAD на пять коммитов позже тега 2.0.2. Миграция не закоммичена и не отправлена в remote в рамках этого отчёта.

Runtime SHA-256 остался `568b7b2fdbaa081676cccbab3f6a4477673d65205e78d9f1788bf0badf642885`; хеш package.json — `8f2cfbed56c2df02a6394337465d958cd3e6a09939eaa96ae0d6ac24aefb39f6`. Runtime, публичные API, политики кэширования и загрузки плагина не изменялись.

## 2. Физическое расположение и Git

До переноса Unity-проект находился в `D:\Unity\Projects\SimplyLocalize-Lab`, а Git — внутри его `Assets/SimplyLocalize`. Скрипты обоих полигонов имели рабочие копии в Assets и исходные копии в Samples~. Пофайловое сравнение перед переносом не выявило различий между файлами Samples~ и соответствующими рабочими файлами.

Теперь корень репозитория — `D:\Unity\Projects\SimplyLocalize`:

```text
SimplyLocalize/
  .git/
  src/SimplyLocalize/                 # локальный UPM-пакет
    package.json
    Runtime/                         # исходные .meta сохранены
    Editor/
    Tests/
  playgrounds/SimplyLocalize-Lab/     # единственный Unity-проект
    Assets/HorrorLab/
    Assets/LocalizationLab/
    Assets/Editor/RepositoryMigrationChecks.cs
    Packages/
    ProjectSettings/
    README-LAB.md
  docs/playground-journal/
  docs/project-structure/0001-local-package-migration/
```

Папка .git перенесена целиком: история, текущая ветка и remote сохранены. Вложенного репозитория в Assets больше нет. Samples~ исключена из активной разработки. Прежние исходники LocalizationLab доступны в родительском коммите d2f7795; снимок HorrorLab до миграции — в [запуске 1.0.1](../../playground-journal/night-shift/runs/20261116T113518Z_nightshift-1.0.1_d2f7795/REPORT.md). Повторный ZIP этих же исходников удалён. Текущие рабочие исходники находятся непосредственно в Assets проекта.

Library, Builds, Logs, UserSettings, TestResults и Recovery физически перенесены вместе с проектом, но исключены из Git. Assets, все их .meta, Packages/manifest.json, Packages/packages-lock.json и ProjectSettings остаются версионируемыми. Новый путь зарегистрирован в Unity Hub, старая запись удалена из реестра Hub без удаления данных.

Резервная копия до переноса: `D:\Unity\MigrationBackups\SimplyLocalize-20261116`. Содержит repository-before.zip с .git, архивы активных HorrorLab/LocalizationLab, исходный файловый манифест и бывшую папку Samples~. Это локальный резерв, не дополнительный рабочий проект.

## 3. Подключение и разработка пакета

В проектном Packages/manifest.json:

```json
{
  "dependencies": {
    "com.renkoff.simply-localize": "file:../../../src/SimplyLocalize"
  },
  "testables": ["com.renkoff.simply-localize"]
}
```

Это фрагмент: остальные зависимости проекта сохранены. Путь file: считается от папки Packages. PackageInfo подтвердил source=Local и resolvedPath=`D:\Unity\Projects\SimplyLocalize\src\SimplyLocalize`. packages-lock.json обновлён Unity; зависимость TMP в этом Unity разрешилась как встроенный com.unity.textmeshpro 5.0.0, который зависит от uGUI 2.0.0. Манифест пакета с заявленной зависимостью TMP 3.0.9 не редактировался. Работа на других версиях Unity этим прогоном не подтверждается.

Новые исходники редактируются прямо в src/SimplyLocalize. Подключённый проект импортирует их и создаёт .meta рядом с исходниками; оба файла должны попадать в один коммит. Существующие .meta переносятся вместе с файлами, не генерируются заново. Нельзя оставлять вторую импортируемую копию пакета внутри Assets с теми же GUID.

Практическая проверка: во внешнем Editor/ был создан временный LocalPackageMetaProbe.cs без .meta. Первый импорт создал GUID e0e3719d36923c1428180b3f654b1812; после перезапуска Unity использовала тот же GUID. Временная проба удалена. Для повторения создайте уникальный тестовый .cs в Editor локального пакета, импортируйте его, запишите GUID из .meta, перезапустите проект и сравните GUID; затем удалите только собственную пробу вместе с её .meta. Финальные тесты и Player собирались без этой пробы.

HorrorBuildIdentity больше не вычисляет путь через физическое представление AssetDatabase-пути. Используется PackageInfo.FindForAssembly(typeof(LocalizationManager).Assembly).resolvedPath. Это адаптация инструмента полигона к UPM, не обход ошибки загрузки или памяти плагина.

## 4. Сохранность файлов и метаданных

До миграции зафиксированы SHA-256 **817 файлов**, включая **414 .meta** из Assets, Packages и ProjectSettings; .git исключена из пофайлового манифеста и сохранена отдельно в резерве.

Все 414 прежних .meta сохранены **побайтно**; пропавших исходных файлов после отображения старых путей на новые/архивные — **0**. Ненужный для импорта `.meta` бывшей папки Assets/SimplyLocalize сохранён как [former-repository-root.meta.txt](artifacts/former-repository-root.meta.txt). Прежние .meta документации и родительских папок пакета остаются в репозитории вне импортируемых корней. Это объясняет разницу между 414 исходными .meta и числом GUID активных Assets + пакета.

Проверяемые свидетельства:

- [migration-result.json](artifacts/migration-result.json): итог прежней сверки — 817 файлов, 414 метафайлов, утрат и изменений .meta нет; список намеренных изменений.
- [metadata-baseline.json](artifacts/metadata-baseline.json): единый список 414 путей, исходных GUID и SHA-256.
- [verify-metadata.ps1](artifacts/verify-metadata.ps1): повторная проверка наличия файлов и GUID. Запуск с -ExactBytes дополнительно проверяет байты, включая переводы строк; преобразование LF/CRLF при checkout может изменить SHA-256 без изменения GUID.

Полные дубли манифестов всех файлов и старый ZIP-зависимый скрипт удалены. Новый скрипт проверяет метафайлы, а не утверждает, что весь проект навсегда побайтно совпадает с состоянием миграции.

Намеренные изменения: манифест/lock проекта; документация и ссылки; версия/журнал стенда; Editor-сборщик build identity; сгенерированная build identity и удобные копии результатов Verification. Хеши прежних сцен и данных фикстуры сохранились. Новые проверочные Editor-файлы имеют созданные Unity .meta и включаются в Git.

## 5. Проверки и точные границы результата

| Проверка | Результат | Артефакт |
|---|---|---|
| Импорт локального пакета | Успешно | integration-final.json |
| Runtime и Editor-компиляция | Успешно | Успешная сборка в записи запуска 1.1.0 |
| GUID активных Assets и пакета | 356 уникальных, дубликатов нет | integration-final.json |
| Сцены проекта | 4 открыты; отсутствующих MonoBehaviour на объектах сцен нет | integration-final.json |
| GUID-ссылки в сериализованных данных | 928 проверены; 11 не разрешаются в прежних настройках URP, см. MIG-001 | integration-final.json |
| Окно LocalizationEditorWindow | Создано, CreateGUI выполнен без показа окна и захвата ввода | integration-final.json |
| Все EditMode-тесты | 270 / 270, skipped=0 | editmode.xml |
| Все PlayMode-тесты | 26 / 26, skipped=0 | playmode.xml |
| Данные/взаимодействия Night Shift в Editor | PASS: 9 визуальных вариантов, 16 случаев взаимодействия, 4 цели в сцене | editor-checks.txt |
| Release Player | Succeeded, 0 ошибок, 104438336 байт, 30.7365 с | build.txt |
| Player-проверки | 194 / 194, 18 комбинаций языков, 3 цикла мира | player.json |

Уникальные результаты миграции находятся в [artifacts](artifacts/). build.txt, editor-checks.txt, player.json, build identity и исходники сохранены только в [записи запуска 1.1.0](../../playground-journal/night-shift/runs/20261116T121001Z_nightshift-1.1.0_d2f7795/REPORT.md). Интеграционный проверяющий код — Assets/Editor/RepositoryMigrationChecks.cs; он отдельно записывает allProjectReferencesResolved и playgroundChecksPassed.

В ходе создания проверяющего кода исправлены неоднозначное имя PackageInfo и восстановление пустого SceneManagerSetup в batch mode. Это дефекты промежуточной версии инструмента проверки, а не локализатора. Рабочие логи этих попыток удалены; сохраняются итоговый integration-final.json, NUnit XML и результаты сборки в журнале запуска.

## 6. MIG-001 — прежние неразрешённые ссылки в настройках URP

Статус: **закрыт в стенде 1.1.1** последующей работой [NS-DOC-005](../0002-urp-reference-cleanup/REPORT.md). Относится к проекту/рендеринговой фикстуре, не к Simply Localize. При первоначальной миграции в Assets/Settings/DefaultVolumeProfile.asset найдены 4 неразрешённых GUID скриптов компонентов; в Assets/Settings/PC_Renderer.asset — 7 GUID ресурсов отладки probe volumes. Полный исходный перечень GUID записан в integration-final.json.

Файлы побайтно совпадают с исходным манифестом до переноса; соответствующие GUID отсутствуют среди исходных .meta проекта и пакета. Версии URP/Core не менялись. Поэтому это существовавшие в переносимых данных ссылки, а не потеря .meta при миграции. Поскольку отдельного полного AssetDatabase-прогона до переноса не было, отчёт не приписывает им измеренную прежнюю runtime-симптоматику.

При миграции ссылки не исправлялись. В последующей задаче подтверждено, что семь ресурсов относятся к устаревшему контейнеру URP, а профиль содержит девять неработающих subasset-записей: четыре с отсутствующим GUID и пять с null-скриптом. Они удалены штатным API Unity. Сохранены 19 рабочих эффектов, SSAO и метафайлы; четыре пары GPU-кадров совпали побайтно. Новый аудит: неразрешённых GUID 0, missing-script subassets 0. Доказательства находятся в NS-DOC-005; исходные артефакты миграции оставлены неизменными.

## 7. Воспроизведение и дальнейшая работа

Из корня репозитория, при закрытом проекте:

```powershell
unity test ./playgrounds/SimplyLocalize-Lab --mode EditMode --output ./playgrounds/SimplyLocalize-Lab/TestResults/editmode.xml
unity test ./playgrounds/SimplyLocalize-Lab --mode PlayMode --output ./playgrounds/SimplyLocalize-Lab/TestResults/playmode.xml
$env:NIGHTSHIFT_GIT = (Get-Command git).Source
unity run ./playgrounds/SimplyLocalize-Lab -- -executeMethod RepositoryMigrationChecks.VerifyAndBuild
```

При открытом проекте используйте CLI текущего Editor; второй экземпляр на том же проекте не запускайте. VerifyAndBuild вызывает генератор фикстуры: предназначен для этого личного стенда и может сохранять его сцены/данные. Обычная проверка структуры без сборки: RepositoryMigrationChecks.Verify.

Player запускается скрыто с -batchmode -nographics --nightshift-benchmark --nightshift-report <absolute.json> -logFile <absolute.log>. Подробный результат нового запуска, метрики и архив 40 файлов, определяющих source fingerprint: [Night Shift 1.1.0](../../playground-journal/night-shift/runs/20261116T121001Z_nightshift-1.1.0_d2f7795/REPORT.md).

Не проверены визуальное качество GPU-рендеринга, физическая задержка звука, другие версии Unity, гигабайтные наборы и многократная чистая установка на другой машине. Проект импортировался с перенесённой Library; проверки GUID и .meta сделаны отдельно по исходникам. Успешные тесты не закрывают ранее зарегистрированные SL-001–SL-012 и не подтверждают масштабирование локализатора.
