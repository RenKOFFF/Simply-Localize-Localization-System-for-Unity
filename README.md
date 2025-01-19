# Simply Localize

A localization system for Unity, designed to simplify the management of translations and text handling for multiple languages in your project.

*Read in other languages: [Russian](README_RU.md).*

## Table of Contents

* [Features](#features)
* [Example](#example)
* [Installation](#installation)
* [Usage](#usage)

## Features

> [!NOTE]
> - **Convenient storage**: all translations are stored in a single JSON file and editor.
> - **Parameterization**: supports formatted texts with parameters.
> - **Custom editor**: allows adding keys both through the settings window and directly via localization components.
> - **Runtime language switching**: change the active language during gameplay.
> - **Image localization**: supports localization of images.
> - **Automatic key generation**: no need to add keys manually in the code.

## Example
### Localization File

```json
{
  "en": {
    "SelectLanguage": "Select Language",
    "En": "English",
    "Ru": "Russian",
    "Ja": "Japanese",
    "MyNameIs": "My name is {0}"
  },
  "ru": {
    "SelectLanguage": "Выберите язык",
    "En": "Английский",
    "Ru": "Русский",
    "Ja": "Японский",
    "MyNameIs": "Меня зовут {0}"
  },
  "ja": {
    "SelectLanguage": "言語を選択する",
    "En": "英語",
    "Ru": "ロシア語",
    "Ja": "日本語",
    "MyNameIs": "私の名前は {0}"
  }
}
```

### Usage in Code

```csharp
// Setting text with a parameter
MyNameTextElement.TranslateByKey("MyNameIs");
MyNameTextElement.SetValue("Alex");

// Result: "My name is Alex" (en)

// Changing the language at runtime
Localization.SetLocalization("ru");

// Result after switching language: "Меня зовут Alex"
```

## Installation

1. Download and install the asset via the link: `Window > Package Manager > Add package from git URL`
```
https://github.com/RenKOFFF/SimplyLocalize.git
```

## Usage

After installation, a new menu will appear in Unity:
**`Window -> SimplyLocalize -> Localization Settings`**.

In this window, you can:

- Add new languages and fonts for specific languages.
- Create and edit keys.
- Set translations for each language.
- Configure editor behavior:
    - Text-to-key conversion settings: replace spaces with slashes, underscores, or leave unchanged.
    - Enable/disable logging.

[![Unity-a-Jno-Nkr-PW9.gif](https://i.postimg.cc/BvWYbcmC/Unity-a-Jno-Nkr-PW9.gif)](https://postimg.cc/K1NrsLSK)

### Component Setup

Add one of the components to a text element:

- **`LocalizationText`** — for static strings.
- **`FormattableLocalizationText`** — for strings with parameters.
- **`LocalizationImage`** — for images.

### Changing Language and Setting a Default Language

Call the method to set the language:

```csharp
Localization.SetLocalization("ru"); // Set Russian language.
```

> [!TIP]
> - You can set the default language in the localization settings window and change it during gameplay if needed.
> - Setting a default language is optional. The key is to set the language before any localization scripts execute.

### Alternative Method for Adding Keys

Keys can also be added directly through text components.

[![Unity-srfu-Fb-Dd-Z7.png](https://i.postimg.cc/bvQ6tDMf/Unity-srfu-Fb-Dd-Z7.png)](https://postimg.cc/7CZMv6wK)

