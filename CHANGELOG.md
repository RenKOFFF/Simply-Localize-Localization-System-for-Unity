# Changelog

## [1.3.4] - 2024-10-12
### Updated and changed
* Updated KeyGenerator. Now it also deletes .meta files. 
* Added error handler for situations when there is a conflict of LocalizationKey.cs files.
* Added asset update after adding a key via the LocalizationText component.
* Updated documentation.

## [1.3.3] - 2024-10-09
### Added
* Added the ability to change default values for Formattable components from the inspector.

## [1.3.2] - 2024-09-27
### Fixed
* Replaced the body of the SetValue method for FormattableLocalization components. Now it can accept multiple values.

## [1.3.1] - 2024-08-28
### Fixed
* Fixed a bug where Formattable components were not automatically updated when changing the language.

## [1.3.0] - 2024-08-20
### Changed
* The logic of constructing localization keys has been changed. Now the keys will be stored in the Assets folder, and not in Packages as before. This will help avoid errors and constant recompilations.

## [1.2.5] - 2024-08-19
### Added
* Added the ability to add multiple keys at once.

## [1.2.4] - 2024-07-27
### Added
* Added the ability to change the language at runtime.
* Added documentation in README.MD file.

### Changed
* A small restructuring of methods was made: TryGetKey() and TryGetTranslatedText() were moved from the LocalizationText components to the Localizaton class.

### Bug fixes
* Fixed a bug where when switching fonts it was impossible to return to the original font version.

## [1.2.3] - 2024-07-27 
### Added
* Added the ability to add a new key directly through the input field.

## [1.2.2] - 2024-07-27 
### Added
* Added search field to search for keys. Thanks to https://github.com/roboryantron for providing the search field asset - ***UnityEditorJunkie***.
* Added comments for generated keys.
* Added additional check for created keys. Now when creating a new key and when duplicating keys, the last key will receive the number 2 at the end.

## [1.2.1] - 2024-07-25 
### Changed
* Added the ability to set a default language in the LocalizationKeysData asset. 
* The LocalizationKeysData asset has been moved to the Resources folder for easy access.

## [1.2.0] - 2024-07-23 
### Changed
* Added global localization template generation. Now there is only one .json file with translations and it is generated mainly from code. To work, just add the translations themselves to the created file.
### Removed
* Removed check for "Sample" key. Now this key is not required to be stored and can be easily replaced with any other or deleted.

## [1.1.0] - 2024-07-22 
### Changed
* Updated the logic for creating LocalizationKeysData asset.
* The way enum is serialized has been changed. Now enum values are implemented as strings. (Thanks for asset https://github.com/cathei).

### Added 
* Added the ability to quickly mark formatted keys with #F.
* Added new assembly definition for generated keys.