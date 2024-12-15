# SimplyLocalAsset

Система локализации для Unity, разработанная для упрощения управления переводами и обработки текста в вашем проекте.

*Читать на других языках: [English](README.md).*

## Возможности

> [!NOTE]
> - **Ключи на основе перечислений**: предотвращает ошибки при доступе к строкам.
> - **Автоматическая генерация ключей**: нет необходимости вручную добавлять их в код.
> - **Удобное редактирование переводов**: все переводы хранятся в одном файле и редакторе.
> - **Гибкая интеграция**: поддерживает форматированные тексты с параметрами.
> - **Простота использования**: настраивается с помощью интуитивно понятного редактора.
> - **Переключение языка во время выполнения**: изменение активного языка во время игры.

## Пример файла локализации

```json
{
  "en": {
    "SelectLanguage": "Select Language",
    "En": "English",
    "Ru": "Russian",
    "Jp": "Japanese",
    "MyNameIs": "My name is {0}"
  },
  "ru": {
    "SelectLanguage": "Выберите язык",
    "En": "Английский",
    "Ru": "Русский",
    "Jp": "Японский",
    "MyNameIs": "Меня зовут {0}"
  },
  "jp": {
    "SelectLanguage": "言語を選択する",
    "En": "英語",
    "Ru": "ロシア語",
    "Jp": "日本語",
    "MyNameIs": "私の名前は {0}"
  }
}
```

## Пример использования в коде

```csharp
// Установка текста с параметром
MyNameTextElement.TranslateByKey(LocalizationKey.MyNameIs);
MyNameTextElement.SetValue("Alex");

// Результат: "Меня зовут Алекс" (en)

// Изменение языка во время выполнения
Localization.SetLocalization("ru");

// Результат после смены языка: "Меня зовут Alex"
```

## Установка

1. Установите пакет `newtonsoft json`: `Window > Package Manager > Add package by name`
```
com.unity.nuget.newtonsoft-json
```
2. Загрузите **скрипт подготовки** по ссылке: `Window > Package Manager > Add package from git URL`
```
https://github.com/RenKOFFF/SimplyLocalize.git?path=/Editor/Preparation
```
3. Подождите, пока скрипт сгенерирует все необходимые файлы в папке `Assets`.
4. Удалите скрипт подготовки.
5. Загрузите и установите **основной ассет**: `Window > Package Manager > Add package from git URL`
```
https://github.com/RenKOFFF/SimplyLocalize.git
```
## Использование

После установки в Unity появится новое меню:\
**`Window -> SimplyLocalize -> Localization Settings`**.

В этом окне вы можете:

- Добавить новые языки и шрифты для определенных языков.
- Создать и редактировать ключи.
- Установить переводы для каждого языка.

  [![Unity-I04r-Rke-Er-J.png](https://i.postimg.cc/rFJRbwvv/Unity-I04r-Rke-Er-J.png)](https://postimg.cc/HVrL8dW2)
  [![Unity-JMqpnnk6s-F.png](https://i.postimg.cc/sggGRRz1/Unity-JMqpnnk6s-F.png)](https://postimg.cc/hfNt96fq)
  [![Unity-ZYMQ7g-Bkd4.png](https://i.postimg.cc/ZKZyVdD8/Unity-ZYMQ7g-Bkd4.png)](https://postimg.cc/SjTsKJ4R)

### Настройка компонентов

Добавьте один из компонентов в текстовый элемент:

- **`LocalizationText`** — для статических строк.
- **`FormattableLocalizationText`** — для строк с параметрами.

[![Unity-AVOn-Det1d2.png](https://i.postimg.cc/cC7tdx6t/Unity-AVOn-Det1d2.png)](https://postimg.cc/gLJ2DPVG)

### Изменение языка и установка языка по умолчанию

Вызовите метод для установки языка:

```csharp
Localization.SetLocalization("ru"); // Установить русский язык.
```

> [!TIP]
> - Вы можете установить язык по умолчанию в окнах настроек локализации и при необходимости изменить его во время игры.
> - Установка языка по умолчанию не обязательна. Главное - установить язык до начала выполнения всех скриптов локализации.

### Альтернативный способ добавления ключей

Ключи также можно добавлять напрямую через текстовые компоненты. Введите новый ключ в поле поиска, чтобы создать его.

> [!WARNING]
> При добавлении ключа таким способом текущий ключ сбрасывается до первого. После добавления необходимо вручную установить добавленный ключ в компоненте.

[![Unity-36w-Z3-Hw-Z6-K.png](https://i.postimg.cc/KctMPNQc/Unity-36w-Z3-Hw-Z6-K.png)](https://postimg.cc/CdL5YbW9)