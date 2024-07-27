# Changelog

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