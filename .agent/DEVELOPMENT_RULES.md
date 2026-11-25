# DEVELOPMENT_RULES — ECAD-MCAD Bridge

Правила обязательны для человека и локального агента. Нарушение фиксируется в review как блокирующее.

## 1. Незыблемые ограничения

1. Не использовать DelphiScript.
2. Не использовать Legacy API Altium.
3. Не менять архитектуру без решения в `DECISIONS.md`.
4. SOLIDWORKS backend реализуется в рамках D-020; Top-прототип placement — D-022. Полноценный Add-in, Registry и установщик требуют отдельного решения; Bottom/Native/External placement вне scope.
5. Не добавлять API в категорию «подтверждено» без доказательства.
6. Не изменять ветку `source` напрямую; только через Pull Request.
7. Не коммитить проприетарные сборки Altium/SOLIDWORKS, лицензии и данные заказчиков.
8. Не обходить лицензирование и не автоматизировать CAD-системы способами, запрещёнными их лицензиями.

## 2. Стек и настройки проекта

- .NET SDK 10 (`global.json` фиксирует `10.0.x` при появлении кода).
- Target Framework по проектам:
  - `EcadMcadBridge.Altium` — `net10.0-windows` (совместимость подтверждена оператором, C-65);
  - `EcadMcadBridge.Core` — `net10.0`;
  - `EcadMcadBridge.Core.Tests` — `net10.0`;
  - `EcadMcadBridge.SolidWorks` — `net10.0-windows`, x64 (D-020);
  - `EcadMcadBridge.SolidWorks.Tests` — `net10.0-windows`, x64.
- Язык: C#, nullable reference types включены.
- `TreatWarningsAsErrors=true`, `AnalysisLevel` — последний доступный для SDK 10.
- Детерминированная сборка: `Deterministic=true`, `ContinuousIntegrationBuild=true` в CI.
- Платформа: `AnyCPU` для Core/Altium; `x64` для SOLIDWORKS backend и smoke.
- C# стиль: file-scoped namespaces, `var` только при очевидном типе, явные модификаторы доступа.

Совместимость AD26 + SDK 4.0.1 + .NET SDK 10 с `net10.0-windows` подтверждена оператором (C-65).
InteractiveBom под `net8.0-windows` — только референс и не определяет целевой TFM.

Если в будущем `net10.0-windows` перестанет загружаться в AD26, **нельзя молча менять TFM**.
Нужно остановиться, оформить `blocked` и запросить решение через `DECISIONS.md` (D-013).

## 3. Структура решения

```text
EcadMcadBridge.sln
src/
  EcadMcadBridge.Core/
  EcadMcadBridge.Altium/
  EcadMcadBridge.SolidWorks/      # STEP import backend, D-020
tools/
  EcadMcadBridge.SolidWorks.Smoke/
tests/
  EcadMcadBridge.Core.Tests/
  EcadMcadBridge.Altium.Tests/
  EcadMcadBridge.SolidWorks.Tests/
```

- `Directory.Build.props` — общие свойства сборки.
- `global.json` — версия SDK.
- `NuGet.config` — только при необходимости; источники пакетов фиксируются.
- `src/*/` — ни одного `PackageReference` на CAD SDK: SDK подключается как локальная ссылка.

## 4. Ссылки на Altium SDK

- Эталон — Altium SDK 4.0.1 из установленного Altium Developer:
  `...\Extensions\Altium Developer\SDK4.0\CSharp\`.
- Путь задаётся свойством `AltiumSdkDir` (MSBuild property) или переменной окружения;
  абсолютные пути в репозиторий не коммитятся.
- В репозитории не должно быть `Altium.SDK.dll`, `Altium.SDK.Interfaces.dll`,
  `Altium.Controls*.dll`, `DevExpress*.dll`.
- Все используемые сигнатуры фиксируются в `SDK_API_LEDGER.md` с указанием доказательства:
  исходный файл SDK, метаданные сборки или успешный smoke-тест.
- Если сигнатура не подтверждена, она попадает только в раздел «Требует проверки».
- Reflection и `dynamic` допустимы только как временный адаптер, с записью в ledger и планом замены.
- COM-контракт Add-in — по исходному InteractiveBom: `[assembly: ComVisible(true)]`,
  `PluginFactory : IPluginFactory` и `PluginServerModule` с `ClassInterfaceType.AutoDispatch` (D-017).
  `ClassInterfaceType.None` не является вариантом исходного проекта.

### 4.1 SOLIDWORKS interop

- Reference assemblies — локальные `SolidWorks.Interop.sldworks.dll` и
  `SolidWorks.Interop.swconst.dll` из `SolidWorksApiDir` (MSBuild property).
  Абсолютный путь не коммитится; DLL не коммитятся и не заменяются NuGet-пакетом.
- Backend (`EcadMcadBridge.SolidWorks`) не ссылается на Altium SDK.
- Диагностическое приложение подключается к уже запущенному SOLIDWORKS через ROT.
  COM-вызовы — в STA, последовательно, без `Task.Run`/`Parallel`.
- Не запускать и не завершать пользовательский процесс SOLIDWORKS. До работы снять
  baseline через `ISldWorks.GetDocuments()` и закрывать только документы, появившиеся
  после baseline; документы с `swFileLoadWarning_AlreadyOpen` защищать от закрытия.
- Временные настройки SOLIDWORKS (например, `swSaveAssemblyAsPartOptions`) менять
  только на время операции и безусловно восстанавливать в `finally`; неудача
  восстановления — Error.
- Existing/open target `.SLDPRT`/`.SLDASM` не перезаписывать и не удалять;
  `SaveAs3` считать успешным только при `true`, `Errors=0`, существующем файле
  ненулевого размера. Warning-биты декодировать по именам; known bit `128` не ошибка.
- Smoke-документы и временные копии сохраняются только в `%TEMP%`; CAD-бинарники
  в Git не добавляются.
- Add-in registration, Registry, установщик и UI — вне scope D-020.
- `AddComponent5(0,0,0)` — только API smoke. Для placement использовать
  рассчитанный Core transform и `SetTransformAndSolve3`/`GetTotalTransform`.
  Top-прототип `BRIDGE-000018` не доказывает Bottom/Native/External placement.

### 4.2 Placement rules (BRIDGE-000018)

- Запросы размещения строятся только из Exchange Package и Core
  `PlacementTransformBuilder`; production-код не содержит fixture designators,
  координат и footprint offsets.
- `ComponentPositionBoardMm` переводится в board-local frame по аналитическому
  bounding box только `ContourRole.Outer`: линии — Start/End; дуги — Start/End и
  кардинальные точки внутри фактического CW/CCW sweep. Центр окружности не является
  границей; inner/cutout contours не учитываются. Нет Outer contour или дуга
  некорректна — plan invalid, не `(0,0)`.
- Явный `--designators` требует **все** body entries компонента: отсутствие
  snapshot/catalog/import/STEP или unsupported body делает plan invalid и smoke
  `success=false`; частичное размещение компонента запрещено. Автоматический
  выбор включает только полностью поддержанные component instances.
- Model-origin offset передаётся как локальный
  `Rz(-ComponentRotationDeg) * deltaBoard`, не как board offset.
- `BoardSurfaceFrame` берётся только из `BoardSnapshot.Properties.Thickness`
  (`±T/2`); нельзя подставлять 1.6 мм, толщину STEP board solid или любое
  значение по умолчанию.
- `AssetOriginLocalMm = 0` для текущего STEP AP214 contract; placement/rotation
  не должны запекаться в asset повторно.
- `ModelStateSource = PlacedBodyEffective` нормализуется Core builder-ом;
  `ComponentRotationDeg` нельзя применять второй раз.
- `ModelRotationDegRaw` не игнорируется: отсутствие или ненулевое значение —
  диагностика Unsupported.
- Неподдерживаемые режимы (Bottom, projection mismatch, Native, External,
  NotAcquired, Unknown, неполные данные) не получают нулевую матрицу; они
  получают явную диагностику.
- SOLIDWORKS translation передаётся в метрах, rotation — в `ArrayData` как
  row-major `R^T`; Core 4×4 нельзя передавать напрямую.
- После установки transform читается обратно; translation residual ≤ 0.001 mm,
  rotation basis residual ≤ 1e-8. Save/reopen проверяет количество occurrences
  и transforms. Успех — только при `CleanupStatus = Verified`.

## 5. Работа с API и данными

- Чтение платы — только через документированные `PCB.IPCB_*` интерфейсы.
- Не парсить `.PcbDoc` вручную.
- Не использовать внутренние COM-интерфейсы и недокументированные DispId.
- Все единицы измерения указывать явно в имени (`Iu`, `Mm`, `Deg`); `Coord` допустим только
  как синоним IU в именах из SDK.
- Все линейные величины, полученные из Altium в IU, сохраняются парой: исходное целое `*Iu`
  и вычисленное `*Mm`. Это координаты, ширина дорожек, размеры падов, диаметры отверстий
  и переходных отверстий, радиусы дуг, размеры/толщина платы и высоты компонентов (D-015).
- Коэффициент: 1 IU = 0.00000254 mm. Контрольные значения:
  `1 000 000 IU = 2.54 mm`, `2 000 000 IU = 5.08 mm`, `10 000 000 IU = 25.4 mm`.
- Преобразование IU ↔ мм — только через `EDP.Utils` (C-57/C-58) или явную функцию Core с тестом.
- Тесты обязаны проверять контрольные значения и обратное преобразование.
- Потеря исходных IU при сериализации запрещена; неподтверждённые значения не заполняются догадками.
- Любая запись в плату — только после подтверждения пользователя и только в отдельной задаче.
- На этапе `BRIDGE-000001` запись в `.PcbDoc` запрещена.

## 6. Ошибки, логи, потоки

- Запрещено пустое `catch {}`. Допустим только осознанный fallback с записью в лог.
- Запрещены `.Result`, `.Wait()`, `Thread.Sleep` в UI-потоке.
- Длительные операции — асинхронно, с `CancellationToken`, без блокировки UI.
- Любое исключение на границе команды Add-in логируется и показывается пользователю кратко.
- Лог — в `%TEMP%` или `%LOCALAPPDATA%`, с ротацией и ограничением размера.
- В лог не попадают: полные пути заказчика, содержимое BOM/netlist, токены, лицензии.
- Для диагностики использовать коды `EMB-<AREA>-<NNN>`.

## 7. Тесты

- Core обязан иметь unit-тесты; покрытие критичных преобразований и валидации — обязательное.
- Тесты не требуют установленного Altium/SOLIDWORKS.
- Для каждого подтверждённого API, влияющего на данные, — минимум один тест преобразования
  или фикстура.
- Тестовые данные — синтетические; реальные проекты заказчиков не коммитятся.
- Команды:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

- Smoke-тест в CAD-хосте выполняется вручную по чек-листу задачи и фиксируется в report
  (версия CAD, build, шаги, результат, скриншот при необходимости).
- Падающий тест не отключается без записи в `DECISIONS.md` и указания причины.

## 8. Git и Pull Request

### 8.1 Ветки

- `source` — защищённая базовая ветка.
- `bootstrap/*` — bootstrap-документация.
- `task/BRIDGE-NNNNNN` — постановка (по необходимости).
- `agent/BRIDGE-NNNNNN-<slug>` — исполнение.
- `review/BRIDGE-NNNNNN` — ревью.
- `hotfix/*` — только по решению Оператора.

### 8.2 Коммиты

Conventional Commits:

```text
docs(BRIDGE-000001): add Altium skeleton specification
feat(altium): register export command
fix(core): reject NaN in package reader
test(core): cover coordinate rounding
chore(repo): pin .NET SDK
```

- Один коммит — одна логическая единица.
- В сообщении коммита указывать ID задачи, если он есть.
- Не смешивать форматирование и изменение поведения в одном коммите.

### 8.3 Pull Request

- База PR — `source`.
- В описании: цель, изменения, проверки, риски, ссылка на task.
- PR не мержится без review-файла или явного approve Оператора.
- Автор не мержит собственный PR без независимой проверки, кроме bootstrap-этапа по решению Оператора.
- После merge ветка удаляется; source остаётся линейным по возможности.

### 8.4 Git и runtime-данные

- Git — только задачи, отчёты, ревью, исходники, документация и синтетические фикстуры.
- Пользовательские `.PcbDoc`, STEP/STL/IGES, рабочие сборки и логи CAD в Git не коммитятся.
- Runtime-обмен идёт через локальный каталог вне Git (ARCHITECTURE, раздел 9).
- Подозрительный бинарный файл в diff — блокирующее замечание ревью.

## 9. Документация

При изменении кода обновляются:

- `SDK_API_LEDGER.md` — новые/изменённые API;
- `DECISIONS.md` — архитектурные решения и их последствия;
- `PROJECT_CONTEXT.md` — статус и открытые вопросы;
- report задачи — фактический результат, а не пересказ плана.

Документ без даты и статуса считается черновиком и не является источником истины.

## 10. Definition of Done

Задача считается выполненной, если:

- [ ] изменения соответствуют task и не расширяют scope самовольно;
- [ ] сборка проходит без предупреждений;
- [ ] тесты проходят;
- [ ] новые API добавлены в ledger с доказательством;
- [ ] архитектурные изменения проведены через `DECISIONS.md`;
- [ ] нет секретов, проприетарных DLL и данных заказчиков;
- [ ] в diff нет пользовательских PCB, STEP и рабочих сборок;
- [ ] report заполнен по форме из `ARCHITECTURE.md`, раздел 8.7;
- [ ] создан PR в `source`, указаны commit SHA и ссылка;
- [ ] smoke-тест в CAD выполнен или явно отмечен как невозможный с причиной.

## 11. Локальная среда (текущий стенд)

| Параметр | Значение |
|---|---|
| ОС | Windows |
| .NET SDK | 10.0.404 (также установлен 11.0.100; для проекта использовать 10.0.x) |
| Altium SDK | 4.0.1, найден в `Altium Developer\SDK4.0` |
| Altium Designer | 26 (точный build — Q-002); совместимость с .NET 10/`net10.0-windows` подтверждена оператором |
| SOLIDWORKS | 2026 SP3.2 / 34.3.2.0003; backend — `net10.0-windows`, x64 (D-020); Add-in host отдельно |
| Референсный Add-in | `InteractiveBom.zip`: исходный проект `InteractiveBom.sln` (shim/Impl/Panels/Tests), Release `net10.0-windows`; отдельная DLL `InteractiveBomPlug.dll` `net8.0-windows` — другой источник |

Пути установки в документации приведены как ориентир; в коде и скриптах использовать
настраиваемые свойства, а не жёстко прописанные каталоги.
