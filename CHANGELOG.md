# Changelog

## [1.2.0] - 2024-07-23 
### Changed
* Added global localization template generation. Now there is only one .json file with translations and it is generated mainly from code. To work, just add the translations themselves to the created file.
### Removed
* Removed check for "None" key. Now this key is not required to be stored and can be easily replaced with any other or deleted.

## [1.1.0] - 2024-07-22 
### Changed
* Updated the logic for creating LocalizationKeysData asset.
* The way enum is serialized has been changed. Now enum values are implemented as strings. (Thanks for asset https://github.com/cathei)

### Added 
* Added the ability to quickly mark formatted keys with #F
* Added new assembly definition for generated keys