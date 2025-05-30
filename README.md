# Simply Localize

A localization system for Unity, designed to simplify the management of translations and text handling for multiple languages in your project.

*Read in other languages: [Russian](README_RU.md).*

## Table of Contents

* [Features](#features)
* [Example](#example)
* [Installation](#installation)
* [Usage](#usage)
* [Component setup](#component-setup)

## Features

> [!NOTE]
> - **Convenient storage**: all translations are stored in a single JSON file and editor.
> - **Parameterization**: supports formatted texts with parameters.
> - **Custom editor**: allows adding keys both through the settings window and directly via localization components.
> - **Runtime language switching**: change the active language during gameplay.
> - **Image localization**: supports localization of images.
> - **Automatic key generation**: no need to add keys manually in the code.

> [!NOTE]
> - **Localization of app title**: The package does not provide the ability to localize the app title. It is recommended to use [Unity Localized App Title](https://github.com/yasirkula/UnityMobileLocalizedAppTitle.git) for this.

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

### Editor Usage

[![Unity-L76gtp-EYtd.png](https://i.postimg.cc/6pMYf4Vs/Unity-L76gtp-EYtd.png)](https://postimg.cc/sMZYVX0K)
[![Unity-ukki-Yoz-LL6.png](https://i.postimg.cc/Sxkvrpw9/Unity-ukki-Yoz-LL6.png)](https://postimg.cc/0bXnkh55)

### Code Usage

#### Basic Implementation
To set localized text with parameter:

```csharp
// Setting text by localization key
MyNameTextElement.TranslateByKey("MyNameIs");
// Setting dynamic parameter
MyNameTextElement.SetValue("Alex");

/* Result:
 * en: "My name is Alex"
 * ru: "Меня зовут Alex"
 */
```

> [!NOTE]
> 1. Method `TranslateByKey` is optional if key is set in Unity inspector
> 2. Method `TranslateByKey` can override existing keys

#### Language Switching
```csharp
Localization.SetLocalization("ru");
```
The change applies to all active UI elements using the localization system.

## Installation

1. Download and install the asset via the link: `Window > Package Manager > Add package from git URL`
```
https://github.com/RenKOFFF/SimplyLocalize.git
```

2. If you want to localize the app title on Android or IOS, install the third-party package `Unity Localized App Title`. You can add the package similar to the `SimplyLocalize` package or use one of the options from the original repository.
```
https://github.com/yasirkula/UnityMobileLocalizedAppTitle.git
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

### Component setup

Add one of the localization components to UI elements:

#### **`LocalizationText`**
- **Purpose**: For static text without dynamic changes
- **Usage**:
  1. Add the component to TextMeshPro or regular text
  2. Select the localization key from the list

#### **`FormattableLocalizationText`**
- **Purpose**: For dynamic text with parameters (e.g. "Health: {0}/{1}")
- **Features**:
  - Support for parameters in the string.Format style
  - Default values
- **Usage**:
  1. Add the component to the text object
  2. Enter the key with placeholders (e.g. "STATS_HEALTH_{0}_{1}")
  3. Set the parameters via code: `.SetValue(100, 200)`
  4. Set default value in editor (optional)

> [!IMPORTANT]
> - translation text must contain a fragment with the {n} parameter (e.g. "STATS_HEALTH_{0}_{1}")

#### **`LocalizationImage`**
- **Purpose**: For localized images
- **Usage**:
  1. Add component to Image
  2. Add sprite key from the list of keys in the editor window from the "Sprites" tab

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

