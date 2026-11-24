---
id: BRIDGE-000009
type: review
status: changes_requested
to: local-agent
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/8
base: source
head: 9b79a0e0be0eea718fe120c786b7f90ff838fed7
created: 2026-11-24
---

# Review BRIDGE-000009 — PR #8

## Решение

**REQUEST_CHANGES — одна блокирующая корректировка.**

Реализация Track/Via/Region/Text в целом соответствует задаче. Схема Core 1.0.0 не изменена. Board Cutout не добавляется повторно в `Regions`. Итераторы уничтожаются в `finally`. Для Track/Via/Text предусмотрена обработка ошибок отдельных объектов.

## R1 — BLOCKER: nullable TextItem.RotationDeg несовместим с BoardSnapshotValidator

Файлы:

- `src/EcadMcadBridge.Altium/Adapter/AltiumSnapshotMapping.cs`, строки 131–155.
- `src/EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter.cs`, метод `ReadTexts`.
- `src/EcadMcadBridge.Core/Validation/BoardSnapshotValidator.cs`, строки 374–398 и 610–624.

### Проблема

`MapText()` корректно проверяет `double.IsFinite(rotation)` и записывает `RotationDeg = null`, если угол неизвестен.

Тест `TextMapping_DoesNotInventRotation()` специально проверяет это поведение.

Однако `BoardSnapshotValidator.ValidateTexts()` вызывает:

`ValidateRequiredDouble(text.RotationDeg, ...)`

`ValidateRequiredDouble()` выдаёт `MissingRequiredAttribute` для `null`.

Таким образом, тестируемая mapping-функция способна сформировать объект, который не проходит Core validation.

### Требуемое исправление

Согласовать контракт и валидатор без изменения схемы `BoardSnapshot 1.0.0`.

Предпочтительный вариант:

1. В `ValidateTexts()` считать `RotationDeg == null` допустимым отсутствующим значением с диагностикой уровня Warning.
2. Если значение присутствует, по-прежнему проверять конечность и диапазон `[-180, 180)`.
3. В `ReadTexts()` получать `rotationKnown` из `MapText()`, вести счётчик неизвестных углов и добавлять ограниченное количество warning-примеров.
4. Не подменять неизвестный угол нулём.

### Обязательные тесты

- `MapText()` с `NaN` и `Infinity` возвращает `RotationDeg == null`.
- Такой `TextItem` проходит `BoardSnapshotValidator` без ошибок.
- Validator выдаёт предупреждение об отсутствующем угле.
- Конечный угол продолжает нормализоваться и проходить проверку.
- JSON round-trip сохраняет `RotationDeg == null`.

По возможности проверить также прохождение такого snapshot через `ExchangePackageWriter` и `ExchangePackageReader`.

## Неблокирующие ограничения — зафиксировать, не расширять текущую задачу

1. **Region arcs.** Сейчас Region contour представляется цепочкой `LineSegment` из `GetGeometricPolygon()`. Аналитические дуги не подтверждены. Q-010 должен остаться частично открытым; не заявлять точную передачу криволинейной геометрии регионов.

2. **Text formatting.** Core `TextItem` не имеет полей Bold/Italic. API Altium эти значения предоставляет, но текущий mapping их не переносит. Явно указать это ограничение в отчёте и архитектуре. Схему Core ради этого не менять.

3. **Region semantics.** `NamedRegion`, `Cutout`, `Cavity` остаются `RegionKind.Other` из-за ограничений Core 1.0.0. Уже зафиксированное поведение приемлемо для этой задачи.

## Что проверено

- PR направлен в `source` от ожидаемой базы `d228f86f55675a2c6646d10a66fd5c788254e8f8`.
- Изменены 9 файлов, 3 коммита.
- Реализация извлечения Track/Via/Region/Text присутствует.
- Пакет объявляет новые capabilities.
- Board Cutout остаётся в outline-механизме.
- Q-010 и D-010 не объявлены полностью закрытыми.
- GitHub status checks для HEAD не опубликованы; build/test рассматриваются как результаты локального отчёта агента.

## Итоговое условие принятия

Закрыть R1, обновить тесты и явно зафиксировать ограничение Bold/Italic. Повторно выполнить Release build/test.

PR #8 до повторного ревью не сливать.