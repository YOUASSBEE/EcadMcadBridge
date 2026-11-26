---
id: BRIDGE-000022
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T16:56:09+03:00
updated: 2026-11-26T16:56:09+03:00
base: source
branch: task/BRIDGE-000022-calibration-contract
commit: <pending>
pr: <pending>
related: [BRIDGE-000022, BRIDGE-000021, BRIDGE-000020]
---

# BRIDGE-000022 — D-024: окончательный контракт per-footprint калибровки

## 1. Итог

Принят **D-024** — окончательный контракт калибровки Bottom placement на уровне
конкретного footprint/body/STEP asset. Контракт заменяет исследовательский проект
D-023, но **не активирует production Bottom**: D-022 остаётся действующим для
production-поведения, SOLIDWORKS по-прежнему отклоняет Bottom и projection mismatch.

Production-код, transform engine, Exchange Package, catalog, API ledger, SOLIDWORKS
adapter и CAD-файлы не изменены. CAD smoke не запускался.

## 2. Изменённые файлы

| Файл | Изменение |
|---|---|
| `.agent/DECISIONS.md` | Добавлен D-024; O-010 закрыт как контракт, активация перенесена в criteria валидации D-024 §5 |
| `.agent/ARCHITECTURE.md` | §6.6.1 переименован в контракт D-023/D-024; добавлены ключ, поля, формула, fail-closed условия и статус catalog 1.2.0 |
| `.agent/DEVELOPMENT_RULES.md` | §4.5 заменён правилами D-024: identity, `e`, derived pivot, fail-closed, tolerances, версионирование |
| `.agent/PROJECT_CONTEXT.md` | stage/статус и Q-003 обновлены: D-024 принят, production Bottom не активирован |
| `AGENTS.md` | ограничения и текущий этап согласованы с D-024 |
| `README.md` | пользовательская формулировка согласована: Bottom research/contract, production Unsupported |
| `.agent/reports/BRIDGE-000022-report.md` | настоящий отчёт |

## 3. Принятые определения и формулы

### Идентичность

- Ключ записи: `(footprintSourceUniqueId, bodyUniqueId, bodyOrdinal, stepAssetSha256)`,
  упорядоченный кортеж. designator, имя модели и позиция на плате в ключ не входят.
- Omission любого поля ключа, дублирование полного ключа (даже при совпадающем
  содержимом) и несовпадение с catalog/STEP дают Unsupported без матрицы.
- `stepAssetSha256` — нижний hex SHA-256 точных байт STEP AP214 asset.
  Любой реэкспорт STEP требует повторной калибровки.
- Обязательные метаполя: `schemaVersion`, `calibrationId`, `libraryDocumentSha256`,
  Top/Bottom offsets IU+mm, representation/source/state provenance, `fixtureId`,
  `adVersion`, `measuredAtUtc`, `methodVersion`, `residualMm`.
- `calibrationId` — SHA-256 канонического JSON всех полей, кроме себя, в
  фиксированном порядке; derived `e` в хеш не входит. Кэш `e` при расхождении
  с вычисленным значением более `1e-9 mm` отвергается.

### Математика

```text
Board-local frame, мм; 1 IU = 0.00000254 mm
TopSurfaceZ    = +T/2
BottomSurfaceZ = -T/2        // T только из BoardSnapshot.Properties.Thickness.Mm

d_top          = Rz(-theta_top) * (ModelOrigin_top.XY - ComponentPosition_top.XY)
d_bottom_local = Rz(-theta_bottom) * (ModelOrigin_bottom.XY - ComponentPosition_bottom.XY)
d_bottom_board = (d_bottom_local.X, -d_bottom_local.Y)
e              = d_bottom_board - d_top

M_base = T(P_board + d_bottom_board, BottomSurfaceZ - StandoffHeight)
         * Rx(180) * T(-AssetOriginLocal)
M      = T(P_board + e) * Rz(theta) * T(-P_board - e) * M_base
```

- `theta` — quarter turn `{0,+90,180,-90}`; `Rz(+theta)` применяется в board frame
  вокруг `P_board + e`, а не как `Rx(180)*Rz*Rx(-180)`.
- `e` — двумерный вектор в мм, `z=0`; знак не инвертируется; `e` не хранится как
  авторитетное поле и не является universal constant.
- Для R4-A/R4-B фикстуры: `d_top=(-4.314346,+1.740448)`,
  `d_bottom_local=(-0.943031,-1.740448)`, значит
  `d_bottom_board=(-0.943031,+1.740448)` и `e=(+3.371315,0) mm`. В отчёте
  BRIDGE-000016 указан только `e.x`, потому что на этой фикстуре `e.y=0`.
- Допуски: IU/mm consistency `1e-9`, rigid determinant `+1` с Core tolerance
  `1e-9`, SOLIDWORKS readback translation `≤ 0.001 mm`, rotation basis `≤ 1e-8`,
  независимый landmark residual `≤ 0.001 mm`.

### Применимость и версии

- Требуются полное совпадение ключа, `libraryDocumentSha256`, `schemaVersion`,
  `calibrationId`, catalog provenance; `SdkExport`, `Generic`, `Embedded`,
  `PlacedBodyEffective`, Bottom/Bottom, zero model state, `ModelRotationDegRaw=0`.
- Fail-closed без zero/identity/reflection fallback и без «первой подходящей
  записи» при отсутствии калибровки, дублировании, любом рассогласовании,
  некорректных числах, неизвестной версии или неоднозначном body.
- Catalog `1.2.0` зарезервирован D-024, но не реализован; writer остаётся на
  `1.1.0`. Record `schemaVersion="1.0.0"`. Будущий reader `1.2.0` принимает
  `1.0.0`; неизвестная версия отклоняется без downgrade. Неверсионированные
  research-записи D-023 недействительны.

### Критерии будущей валидации

До активации production требуются: минимум три записи для двух разных
footprint/body с хотя бы одним ненулевым rotation; измеренные Top/Bottom offsets;
независимая проверка знака и `e`; реальный Bottom STEP package; Top и Bottom
occurrences в SOLIDWORKS; save/reopen; transform readback; residual проверка;
отсутствие регрессии Top; diagnostic tests всех fail-closed условий.

## 4. Открытые вопросы и риски

1. **V-07:** стабильность `footprintSourceUniqueId` (и доступность стабильного
   `bodyUniqueId`) для компонентов после `LoadFromLibrary` не доказана; при
   отсутствии ID калибровка не создаётся.
2. **Y sign rule:** смена знака Y при переходе в board frame подтверждена только
   на одном library-relative Generic body; `e.y != 0` для произвольного footprint
   не проверен.
3. **Толщина платы:** в R4-B BoardProperties дал `2.00800208 mm`, STEP —
   `2.02800204 mm`. Контракт использует толщину из пакета, но реальное CAD-сопоставление
   Z для Bottom ещё требуется.
4. **AD version/provenance:** `adVersion` — provenance, а не ключ. Разные версии
   требуют либо повторного измерения, либо доказательства эквивалентности при том же
   STEP SHA.
5. **Объём доказательств:** R4-A/R4-B не подтверждают compound Bottom, projection
   mismatch, произвольные footprint и production-пакеты. Это остаётся вне scope
   до отдельного PR по реализации и smoke.

## 5. Проверки

- Согласованность `DECISIONS.md`, `ARCHITECTURE.md`, `DEVELOPMENT_RULES.md`,
  `PROJECT_CONTEXT.md`, `AGENTS.md`, `README.md` с D-022/D-023 проверена вручную:
  production Bottom остаётся Unsupported, D-022 не заменяется.
- `git diff --check`: без ошибок.
- Build и test suite **не запускались по указанию задачи**: production-код не менялся.
- SOLIDWORKS smoke **не выполнялся по указанию задачи**; это не доказательство
  Bottom runtime support.
- В Git не добавлены PCB, STEP, SLDPRT/SLDASM, proprietary DLL, лицензии,
  секреты или данные заказчика.

## 6. Commit SHA и Pull Request

- Commit (docs/report): `<pending>`
- PR: `<pending>`

## 7. История изменений

- 2026-11-26: создана ветка `task/BRIDGE-000022-calibration-contract` от `source`
  (`ae9e0f7…`); принят D-024 как контракт без production activation; обновлены
  связанные документы и настоящий отчёт.
