# SimplyLocalAsset

A localization system for Unity designed to simplify translation management and text handling in your project.

*Read this in other languages: [Russian](README_RU.md).*

## Features

> [!NOTE]
> - **Enum-based keys**: Prevents errors when accessing strings.
> - **Automatic key generation**: No need to manually add them to the code.
> - **Convenient translation editing**: All translations are stored centrally.
> - **Flexible integration**: Supports formatted texts with parameters.
> - **Ease of use**: Configurable via an intuitive editor.
> - **Runtime language switching**: Change the active language during gameplay.

## Example Localization File

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

## Example Usage in Code

```csharp
// Setting text with a parameter
MyNameTextElement.TranslateByKey(LocalizationKey.MyNameIs);
MyNameTextElement.SetValue("Alex");

// Result: "My name is Alex" (en)

// Changing the language at runtime
Localization.SetLocalization("jp");

// Result after language change: "私の名前は Alex"
```

## Installation

1. Install `newtonsoft json` package: `Window > Package Manager > Add package by name`
```
com.unity.nuget.newtonsoft-json
```
2. Download the **preparation script** from the link: `Window > Package Manager > Add package from git URL`
```
https://github.com/RenKOFFF/SimplyLocalize.git?path=/Editor/Preparation
```
3. Wait for the script to generate all necessary files in the `Assets` folder.
4. Delete the preparation script.
5. Download and install the **main asset**: `Window > Package Manager > Add package from git URL` 
```
https://github.com/RenKOFFF/SimplyLocalize.git
```
## Usage

After installation, a new menu will appear in Unity:\
**`Window -> SimplyLocalize -> Localization Settings`**.

In this window, you can:

- Add new languages and language-specific fonts.
- Create and edit keys.
- Set translations for each language.


[![Unity-I04r-Rke-Er-J.png](https://i.postimg.cc/rFJRbwvv/Unity-I04r-Rke-Er-J.png)](https://postimg.cc/HVrL8dW2)
[![Unity-JMqpnnk6s-F.png](https://i.postimg.cc/sggGRRz1/Unity-JMqpnnk6s-F.png)](https://postimg.cc/hfNt96fq)
[![Unity-ZYMQ7g-Bkd4.png](https://i.postimg.cc/ZKZyVdD8/Unity-ZYMQ7g-Bkd4.png)](https://postimg.cc/SjTsKJ4R)


### Configuring Components

Add one of the components to your text element:

- **`LocalizationText`** — for static strings.
- **`FormattableLocalizationText`** — for strings with parameters.

[![Unity-AVOn-Det1d2.png](https://i.postimg.cc/cC7tdx6t/Unity-AVOn-Det1d2.png)](https://postimg.cc/gLJ2DPVG)

### Change Language and Setting the Default Language

Call the method to set the language:

```csharp
Localization.SetLocalization("ru"); // Set Russian language.
```

> [!TIP]
> - You can set the default language in the localization settings windows and change it during the game if necessary.
> - Setting the default language is not necessary. The main thing is to set the language before starting to execute all localization scripts.

### Alternative Key Addition

Keys can also be added directly through text components. Enter a new key in the search field to create it.

> [!WARNING]
> When adding a key in this way, the current key is reset to the first one. After adding, you must manually install the added key in the component.

[![Unity-36w-Z3-Hw-Z6-K.png](https://i.postimg.cc/KctMPNQc/Unity-36w-Z3-Hw-Z6-K.png)](https://postimg.cc/CdL5YbW9)

