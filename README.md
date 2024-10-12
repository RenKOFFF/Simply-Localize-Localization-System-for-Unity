# SimplyLocalAsset
A simple localization system for Unity.

## Features
* Keys are stored as enum, which eliminates the possibility of making a mistake when writing a key in a component
* Enum keys are generated, so there is no need to enter them into the code yourself.
* Easy editing of translations without having to dig around in different scripts/prefabs/components or any other places where translations are often stored, everything is stored in one file

### Example of generated file with localization
```json
{
   "Translations": {
      "en": {
         "SelectLanguage": "Select language",
         "Ru": "Russian",
         "En": "English",
         "Jp": "Japanese",
         "MyNameIs": "My name is {0}"
      },
      "jp": {
         "SelectLanguage": "言語を選択する",
         "Ru": "ロシア",
         "En": "英語",
         "Jp": "日本語",
         "MyNameIs": "私の名前は {0}"
      },
      "ru": {
         "SelectLanguage": "Выбери язык",
         "Ru": "Русский",
         "En": "Английский",
         "Jp": "Японский",
         "MyNameIs": "Меня зовут {0}"
      }
   }
}
```
### Example of use in code
```csharp
MyNameTextElement.TranslateByKey(LocalizationKey.MyNameIs);
MyNameTextElement.SetValue("Alex"); 

// output result: My name is Alex (en)
```

## Installation
1) Install newtonsoft json package > Add package by name <br>
```
com.unity.nuget.newtonsoft-json
```
2) Import this from Unity Package Manager. You can [download and import it from your hard drive](https://docs.unity3d.com/Manual/upm-ui-local.html), or [link to it from github directly](https://docs.unity3d.com/Manual/upm-ui-giturl.html).
```
https://github.com/RenKOFFF/SimplyLocalize.git
```
3) Initialize the package. Click Window -> SimplyLocalize -> Initialize Asset

## How to use
After installation, an additional tab "SimplyLocalize" will appear in the "Window" tab, which has 3 methods.

* Initialize Asset (1)
* Select Localization Keys List (2)
* Generate Keys (3)

In addition, the same tab will appear in the Create menu. From here you can create LocalizationData (4) and FontHolder (5)

1) Use (1) to create an asset in which you will enter all the keys. The asset is created only once when the project starts and is stored in resources.
   Use (2) to quickly find and select it.
2) In order to use the created keys, they must be compiled. Use (3) to generate and compile the keys. However, before generating, you must create configs with localization (4).
   * Configs store only two fields:
     * language code in i18n format.
     * font, to which the localized text is replaced during localization. You can leave it blank and then the font will not be replaced.

3) After creating the configs (4), you can generate keys (3).
4) At this stage, you can use the asset. Add the **LocalizationText(TMP/Legacy)** or **FormattableLocalizationText(TMP/Legacy)** component to the text depending on your needs
5) Before starting the game, call the Localization.SetLocalization() method or add the config you want to use by default to the generated asset with keys (1, 2).

### Example of adding a key
[![Unity-Gh-JXj6-XTKr.png](https://i.postimg.cc/J4b0hdG2/Unity-Gh-JXj6-XTKr.png)](https://postimg.cc/Ln82CDkt)
* You can move the keys in any convenient order, and also change the key if, for example, you made a typo, the serialized values ​​​​will not be lost.

### An example of an alternative way to add a key
[![Unity-UYR04t-Ener.png](https://i.postimg.cc/9M7Vnr9n/Unity-UYR04t-Ener.png)](https://postimg.cc/wygStx0X)
* You can add a new key directly in the localization component by entering it in the search.

### An example of using the component
[![An example of using the component](https://i.postimg.cc/mk9DCmVh/Unity-59-VYXzgt-JS.png)](https://postimg.cc/LgH2MBcM)

## Existing problems
### Error when opening project
#### Problem

When working on a project in a team, a situation arises when one person added a localizer, and another imported the project and/or switched from another git branch in which the localizer was not yet initialized.

#### Solution
Go to Packages/com.renkoff.simply-localize/Runtime/Data/Keys/Generated and delete the LocalizationKey.cs and LocalizationKeys.cs files + their meta files. Do not delete the SimplyLocalize.Generated.asmdef file!
